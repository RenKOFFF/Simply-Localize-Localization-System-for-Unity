using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>Updates serialized localization references. Literal strings in source code are not rewritten.</summary>
    public static class KeyReferenceUpdater
    {
        public static int UpdateReferences(string oldKey, string newKey) =>
            UpdateReferences(new Dictionary<string, string> { [oldKey] = newKey });

        /// <summary>Mappings are simultaneous: A → B and B → C never turn A into C.</summary>
        public static int UpdateReferences(Dictionary<string, string> oldToNewKeys) => UpdateReferences(oldToNewKeys, null);

        /// <summary>Optionally restrict asset discovery to Unity project folders. Returns changed object count.</summary>
        public static int UpdateReferences(Dictionary<string, string> oldToNewKeys, string[] searchRoots)
        {
            var plan = ReferenceRenamePlan.Prepare(oldToNewKeys, ReferenceKind.All, searchRoots);
            using var transaction = new EditorFileTransaction();
            try
            {
                plan.Enlist(transaction);
                transaction.Execute(() => plan.Apply(transaction, plan.FileCount == 0));
            }
            catch { plan.Restore(); throw; }
            plan.Refresh();
            return plan.Count;
        }
    }

    internal enum ReferenceKind { All, Text, Asset }

    internal sealed class ReferenceRenamePlan
    {
        private sealed class Change
        {
            internal int Index;
            internal string Property, Before, After;
        }
        private sealed class Job
        {
            internal string Path, DiskPath;
            internal byte[] Before;
            internal Scene LiveScene;
            internal bool WasDirty, WasOpenUnloaded;
            internal readonly List<Change> Changes = new();
            internal readonly Dictionary<Object, (string json, bool dirty)> Memory = new();
            internal readonly Dictionary<GameObject, PropertyModification[]> Overrides = new();
        }
        private readonly List<Job> _jobs = new();
        private bool _applied;
        internal int Count => _jobs.Sum(j => j.Changes.Select(c => c.Index).Distinct().Count());
        internal int FileCount => _jobs.Count(j => j.DiskPath != null);

        internal static Dictionary<string, string> Validate(Dictionary<string, string> mappings)
        {
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
            if (mappings.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
                throw new ArgumentException("Localization keys cannot be empty.");
            if (mappings.Values.Distinct(StringComparer.Ordinal).Count() != mappings.Count)
                throw new ArgumentException("Several keys cannot be renamed to the same key.");
            return mappings.Where(p => p.Key != p.Value).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }

        internal static ReferenceRenamePlan Prepare(Dictionary<string, string> mappings, ReferenceKind kind, string[] roots = null)
        {
            mappings = Validate(mappings);
            var plan = new ReferenceRenamePlan();
            if (mappings.Count == 0) return plan;
            bool Included(string path) => roots == null || roots.Any(root => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal));
            var open = new HashSet<string>();
            var unloaded = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) { unloaded.Add(scene.path); continue; }
                if (EditorSceneManager.IsPreviewScene(scene)) continue;
                open.Add(scene.path);
                if (scene.path.Length == 0 || Included(scene.path))
                    plan.Add(new Job { Path = scene.path, LiveScene = scene, WasDirty = scene.isDirty }, mappings, kind);
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && Included(stage.assetPath))
                plan.Add(new Job { Path = stage.assetPath, LiveScene = stage.scene, WasDirty = stage.scene.isDirty }, mappings, kind);
            foreach (string path in AssetDatabase.GetAllAssetPaths().OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!(path.StartsWith("Assets/") || path.StartsWith("Packages/")) || !Included(path) || open.Contains(path)) continue;
                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".prefab" || extension == ".unity" || extension == ".asset")
                    plan.Add(new Job { Path = path, WasOpenUnloaded = unloaded.Contains(path) }, mappings, kind);
            }
            if (plan._jobs.Any(j => !j.LiveScene.IsValid() && j.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                && Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).path.Length == 0))
                throw new InvalidOperationException("Save untitled scenes before renaming references in closed scenes.");
            return plan;
        }

        private void Add(Job job, Dictionary<string, string> mappings, ReferenceKind kind)
        {
            WithObjects(job, false, (objects, _) =>
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    var target = objects[i];
                    if (!target) continue;
                    using var serialized = new SerializedObject(target);
                    var property = serialized.GetIterator();
                    var managedReferences = new HashSet<long>();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        // SerializeReference graphs can contain shared nodes and cycles.
                        enterChildren = property.propertyType != SerializedPropertyType.ManagedReference
                            || managedReferences.Add(property.managedReferenceId);
                        if (property.propertyType != SerializedPropertyType.String || !mappings.TryGetValue(property.stringValue, out string replacement)
                            || !IsKey(target, property.propertyPath, kind)) continue;
                        // Inherited values follow their definition; do not create redundant prefab overrides.
                        if (PrefabUtility.GetCorrespondingObjectFromSource(target) != null && !property.prefabOverride) continue;
                        job.Changes.Add(new Change { Index = i, Property = property.propertyPath, Before = property.stringValue, After = replacement });
                        if (job.LiveScene.IsValid() || target is ScriptableObject)
                        {
                            job.Memory[target] = (EditorJsonUtility.ToJson(target), EditorUtility.IsDirty(target));
                            if (PrefabUtility.IsPartOfPrefabInstance(target))
                            {
                                var root = PrefabUtility.GetNearestPrefabInstanceRoot(target);
                                if (!job.Overrides.ContainsKey(root)) job.Overrides[root] = PrefabUtility.GetPropertyModifications(root);
                            }
                        }
                    }
                }
                if (job.Changes.Count > 0 && job.Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && AssetDatabase.LoadAllAssetsAtPath(job.Path).Any(EditorUtility.IsDirty))
                    throw new InvalidOperationException("Save pending asset changes before renaming: " + job.Path);
            });
            if (job.Changes.Count == 0) return;
            if (job.Path.Length > 0)
            {
                if (!job.Path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("A localization reference belongs to a package: " + job.Path + ". Update the package reference, or turn off automatic reference updates and migrate references manually.");
                if (job.LiveScene.IsValid() && job.WasDirty)
                    throw new InvalidOperationException("Save pending scene changes before renaming: " + job.Path);
                if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == job.Path)
                    throw new InvalidOperationException("Close Prefab Mode before renaming references in: " + job.Path);
                job.DiskPath = AssetDiskPath(job.Path);
                job.Before = EditorFileTransaction.Read(job.DiskPath) ?? throw new IOException("Asset file is missing: " + job.Path);
            }
            _jobs.Add(job);
        }

        internal static string AssetDiskPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Only project assets can be saved: " + path);
            return EditorDataPath.Resolve(Application.dataPath, path.Substring(7));
        }

        internal void Enlist(EditorFileTransaction transaction)
        {
            foreach (var job in _jobs)
            {
                if (job.DiskPath != null)
                {
                    if (!EditorFileTransaction.Equal(job.Before, EditorFileTransaction.Read(job.DiskPath)))
                        throw new InvalidOperationException("Asset changed during rename. Try again: " + job.Path);
                    if (!AssetDatabase.IsOpenForEdit(job.Path)) throw new IOException("Asset is not open for editing: " + job.Path);
                    transaction.TrackExternal(job.DiskPath);
                }
                foreach (var pair in job.Memory)
                    if (!pair.Key || EditorJsonUtility.ToJson(pair.Key) != pair.Value.json)
                        throw new InvalidOperationException("An object changed during rename. Try again: " + job.Path);
            }
        }

        internal void Apply(EditorFileTransaction transaction, bool undo = false)
        {
            _applied = true;
            foreach (var job in _jobs)
            {
                if (job.DiskPath != null) transaction.TouchExternal(job.DiskPath);
                WithObjects(job, true, (objects, save) =>
                {
                    foreach (var group in job.Changes.GroupBy(c => c.Index))
                    {
                        if (group.Key >= objects.Count || !objects[group.Key]) throw new InvalidOperationException("Object changed during rename: " + job.Path);
                        var target = objects[group.Key];
                        if (undo) Undo.RecordObject(target, "Rename localization references");
                        using var serialized = new SerializedObject(target);
                        foreach (var change in group)
                        {
                            var property = serialized.FindProperty(change.Property);
                            if (property == null || property.propertyType != SerializedPropertyType.String || property.stringValue != change.Before)
                                throw new InvalidOperationException("Reference changed during rename: " + job.Path + " / " + change.Property);
                            property.stringValue = change.After;
                        }
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                        EditorUtility.SetDirty(target);
                    }
                    save();
                });
            }
            if (undo) Undo.FlushUndoRecordObjects();
        }

        internal void Refresh()
        {
            foreach (var job in _jobs.Where(j => j.DiskPath != null)) AssetDatabase.ImportAsset(job.Path, ImportAssetOptions.ForceUpdate);
            EditorDataCache.Invalidate();
        }

        internal void Restore()
        {
            if (!_applied) return;
            Refresh();
            foreach (var job in _jobs)
            {
                foreach (var pair in job.Memory)
                {
                    if (!pair.Key) continue;
                    EditorJsonUtility.FromJsonOverwrite(pair.Value.json, pair.Key);
                    if (pair.Value.dirty) EditorUtility.SetDirty(pair.Key); else EditorUtility.ClearDirty(pair.Key);
                }
                foreach (var pair in job.Overrides)
                    if (pair.Key) PrefabUtility.SetPropertyModifications(pair.Key, pair.Value);
                if (!job.LiveScene.IsValid()) continue;
                // Unity exposes no public API to clear scene dirtiness without saving it.
                // Keep restored objects dirty for review; the original on-disk scene is already restored.
                EditorSceneManager.MarkSceneDirty(job.LiveScene);
            }
        }

        private static List<Object> Components(GameObject root) => root.GetComponentsInChildren<MonoBehaviour>(true).Cast<Object>().ToList();
        private static void WithObjects(Job job, bool writing, Action<List<Object>, Action> action)
        {
            if (job.LiveScene.IsValid())
            {
                if (!job.LiveScene.isLoaded) throw new InvalidOperationException("Scene was closed during rename: " + job.Path);
                action(job.LiveScene.GetRootGameObjects().SelectMany(Components).ToList(), () =>
                {
                    EditorSceneManager.MarkSceneDirty(job.LiveScene);
                    if (job.Path.Length > 0 && !EditorSceneManager.SaveScene(job.LiveScene)) throw new IOException("Could not save scene: " + job.Path);
                });
            }
            else if (job.Path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = PrefabUtility.LoadPrefabContents(job.Path);
                try
                {
                    action(Components(root), () =>
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, job.Path, out bool success);
                        if (!success) throw new IOException("Could not save prefab: " + job.Path);
                    });
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            else if (job.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var active = SceneManager.GetActiveScene();
                var scene = writing ? EditorSceneManager.OpenScene(job.Path, OpenSceneMode.Additive) : EditorSceneManager.OpenPreviewScene(job.Path);
                try
                {
                    action(scene.GetRootGameObjects().SelectMany(Components).ToList(), () =>
                    {
                        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save scene: " + job.Path);
                    });
                }
                finally
                {
                    if (writing) EditorSceneManager.CloseScene(scene, !job.WasOpenUnloaded); else EditorSceneManager.ClosePreviewScene(scene);
                    if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                }
            }
            else
            {
                var objects = AssetDatabase.LoadAllAssetsAtPath(job.Path).OfType<ScriptableObject>().Cast<Object>().ToList();
                action(objects, () =>
                {
                    foreach (int index in job.Changes.Select(c => c.Index).Distinct())
                    {
                        AssetDatabase.SaveAssetIfDirty(objects[index]);
                        if (EditorUtility.IsDirty(objects[index])) throw new IOException("Could not save asset: " + job.Path);
                    }
                });
            }
        }

        private static bool IsKey(Object target, string path, ReferenceKind kind)
        {
            if (target is LocalizedComponentBase && path == "_key")
            {
                bool asset = false;
                for (var type = target.GetType(); type != null; type = type.BaseType)
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LocalizedAsset<>)) asset = true;
                return kind == ReferenceKind.All || (kind == ReferenceKind.Asset) == asset;
            }
            if (kind == ReferenceKind.Asset) return false;
            object value = target;
            FieldInfo field = null;
            foreach (string segment in path.Replace(".Array.data[", "[").Split('.'))
            {
                if (value == null) return false;
                int bracket = segment.IndexOf('[');
                string name = bracket < 0 ? segment : segment.Substring(0, bracket);
                field = null;
                for (var type = value.GetType(); type != null && field == null; type = type.BaseType)
                    field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) return false;
                value = field.GetValue(value);
                if (bracket >= 0)
                {
                    if (!(value is IList list) || !int.TryParse(segment.Substring(bracket + 1).TrimEnd(']'), out int index) || index >= list.Count) return false;
                    value = list[index];
                }
            }
            return field != null && (field.IsDefined(typeof(LocalizationKeyAttribute), true) || field.IsDefined(typeof(LocalizationPreviewAttribute), true));
        }
    }
}
