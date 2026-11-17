using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Isolated project/ScriptableObject drafts saved together with rollback and localization Undo.
    /// Uses Unity serialization for native assets, never a handwritten YAML writer. Only editable Assets paths are writable.</summary>
    public sealed class LocalizationProjectAssetEdit : IDisposable
    {
        private sealed class AssetDraft
        {
            internal ScriptableObject Original, Draft;
            internal string Path, Json, State;
            internal byte[] Bytes;
            internal bool Touched;
        }
        private readonly LocalizationProjectConfig original;
        private readonly string configPath, configJson;
        private readonly byte[] configBytes;
        private readonly Dictionary<string, AssetDraft> assets = new(StringComparer.OrdinalIgnoreCase);
        private bool disposed, committed;
        public LocalizationProjectConfig Draft { get; }
        public LocalizationProjectAssetEdit(LocalizationProjectConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            configPath = AssetDatabase.GetAssetPath(config); LocalizationProjectChange.ValidateWritable(configPath);
            if (EditorUtility.IsDirty(config)) throw new InvalidOperationException("Save existing config edits before opening an asset edit.");
            original = config; configJson = Json(config); configBytes = File.ReadAllBytes(configPath);
            Draft = Clone(config);
        }
        public T Edit<T>(T asset) where T : ScriptableObject
        {
            EnsureOpen(); if (asset == null) throw new ArgumentNullException(nameof(asset));
            string path = AssetDatabase.GetAssetPath(asset); LocalizationProjectChange.ValidateWritable(path);
            if (path == configPath) throw new ArgumentException("Use Draft to edit the project config.", nameof(asset));
            if (!AssetDatabase.IsMainAsset(asset)) throw new ArgumentException("Edit the main ScriptableObject asset, not a sub-asset.", nameof(asset));
            if (assets.TryGetValue(path, out var existing)) return (T)existing.Draft;
            if (EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Save existing asset edits first: " + path);
            var draft = new AssetDraft { Original = asset, Draft = Clone(asset), Path = path, Json = Json(asset), State = State(asset), Bytes = File.ReadAllBytes(path) };
            assets.Add(path, draft); return (T)draft.Draft;
        }
        public T Create<T>(string path) where T : ScriptableObject
        {
            EnsureOpen(); LocalizationProjectChange.ValidateWritable(path);
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .asset path.", nameof(path));
            if (path == configPath || assets.ContainsKey(path) || File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta"))
                throw new IOException("Destination already exists: " + path);
            ValidateNewAddress(path);
            var value = ScriptableObject.CreateInstance<T>(); value.hideFlags = HideFlags.HideAndDontSave; value.name = Path.GetFileNameWithoutExtension(path);
            assets.Add(path, new AssetDraft { Draft = value, Path = path }); return value;
        }
        public LocalizationProjectChange Commit()
        {
            EnsureOpen(); Draft.ValidateOrThrow(); string targetConfig = Json(Draft);
            if (original == null || AssetDatabase.GetAssetPath(original) != configPath || Json(original) != configJson)
                throw new InvalidOperationException("Config changed. Reload before saving.");
            foreach (var asset in assets.Values.Where(a => a.Bytes != null))
                if (asset.Original == null || AssetDatabase.GetAssetPath(asset.Original) != asset.Path || State(asset.Original) != asset.State)
                    throw new InvalidOperationException("Asset changed. Reload before saving: " + asset.Path);
            var modified = assets.Values.Where(a => a.Bytes == null || State(a.Draft) != a.State).ToArray();
            foreach (var asset in modified)
            {
                if (asset.Bytes == null) ValidateNewAddress(asset.Path);
                using var serialized = new SerializedObject(asset.Draft); var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                        && !EditorUtility.IsPersistent(property.objectReferenceValue) && !assets.Values.Any(a => a.Draft == property.objectReferenceValue))
                        throw new InvalidOperationException("Save referenced assets before assigning them: " + asset.Path + " (" + property.propertyPath + ")");
            }
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in modified.Where(a => a.Bytes == null))
            {
                metadata.Add(asset.Path + ".meta");
                for (string directory = Path.GetDirectoryName(asset.Path).Replace('\\', '/'); directory != "Assets"; directory = Path.GetDirectoryName(directory).Replace('\\', '/'))
                    if (!Directory.Exists(directory)) metadata.Add(directory + ".meta");
            }
            using var transaction = new EditorFileTransaction();
            foreach (var asset in assets.Values)
                if (modified.Contains(asset)) transaction.TrackExternalSnapshot(asset.Path, asset.Bytes);
                else transaction.Add(asset.Path, asset.Bytes, asset.Bytes);
            foreach (string meta in metadata) transaction.TrackExternalSnapshot(meta, null);
            bool changeConfig = configJson != targetConfig, configTouched = false;
            if (changeConfig) transaction.TrackExternalSnapshot(configPath, configBytes); else transaction.Add(configPath, configBytes, configBytes);
            LocalizationProjectChange change = null;
            try
            {
                transaction.Execute(() =>
                {
                    if (metadata.Count > 0)
                    {
                        foreach (string meta in metadata.Where(m => !modified.Any(a => a.Path + ".meta" == m))) transaction.TouchExternal(meta);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    }
                    foreach (var asset in modified)
                    {
                        transaction.TouchExternal(asset.Path); asset.Touched = true;
                        if (asset.Bytes == null)
                        {
                            transaction.TouchExternal(asset.Path + ".meta");
                            asset.Original = ScriptableObject.CreateInstance(asset.Draft.GetType());
                            AssetDatabase.CreateAsset(asset.Original, asset.Path);
                        }
                    }
                    // Every new target now has a GUID; references between drafts can be remapped safely.
                    foreach (var asset in modified)
                    {
                        ApplyDraft(asset);
                        EditorUtility.SetDirty(asset.Original); AssetDatabase.SaveAssetIfDirty(asset.Original);
                        if (!File.Exists(asset.Path) || EditorUtility.IsDirty(asset.Original)) throw new IOException("Could not save asset: " + asset.Path);
                    }
                    if (changeConfig)
                    {
                        transaction.TouchExternal(configPath); configTouched = true;
                        EditorJsonUtility.FromJsonOverwrite(targetConfig, original); EditorUtility.SetDirty(original); AssetDatabase.SaveAssetIfDirty(original);
                        if (EditorUtility.IsDirty(original)) throw new IOException("Could not save project config.");
                    }
                    // Capture history before backups are released: a failed snapshot must still roll back the save.
                    var changes = modified.Select(a => new LocalizationProjectChange.FileChange(a.Path, a.Bytes, File.ReadAllBytes(a.Path))).ToArray();
                    change = LocalizationProjectChange.CaptureApplied(original, configPath, configJson, targetConfig, configBytes, changes);
                });
            }
            catch
            {
                foreach (var asset in modified.Where(a => a.Touched))
                {
                    if (asset.Bytes != null && asset.Original != null) { EditorJsonUtility.FromJsonOverwrite(asset.Json, asset.Original); EditorUtility.ClearDirty(asset.Original); }
                    else if (asset.Original != null && !AssetDatabase.Contains(asset.Original)) Object.DestroyImmediate(asset.Original);
                    asset.Touched = false;
                }
                if (configTouched) { EditorJsonUtility.FromJsonOverwrite(configJson, original); EditorUtility.ClearDirty(original); }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); throw;
            }
            committed = true; AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); return change;
        }
        private static string Json(ScriptableObject asset)
        { var json = JObject.Parse(EditorJsonUtility.ToJson(asset)); json.Remove("m_ObjectHideFlags"); return json.ToString(Newtonsoft.Json.Formatting.None); }
        private static T Clone<T>(T asset) where T : ScriptableObject
        { var copy = (T)ScriptableObject.CreateInstance(asset.GetType()); EditorUtility.CopySerialized(asset, copy); copy.hideFlags = HideFlags.HideAndDontSave; return copy; }
        private void EnsureOpen() { if (disposed || committed) throw new InvalidOperationException("This edit is closed. Open a fresh edit."); }
        private void ApplyDraft(AssetDraft asset)
        {
            var copy = Clone(asset.Draft);
            try
            {
                using var serialized = new SerializedObject(copy); var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                    {
                        var target = assets.Values.FirstOrDefault(a => a.Draft == property.objectReferenceValue);
                        if (target != null) property.objectReferenceValue = target.Original;
                    }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var flags = asset.Original.hideFlags; EditorUtility.CopySerialized(copy, asset.Original); asset.Original.hideFlags = flags;
            }
            finally { Object.DestroyImmediate(copy); }
        }
        private static string State(ScriptableObject asset)
        {
            // EditorJsonUtility can serialize transient references as zero. Include live reference IDs
            // so such assignments cannot disappear from change detection before validation/remapping.
            var references = new List<string>();
            using var serialized = new SerializedObject(asset); var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    references.Add(property.propertyPath + "=" + property.objectReferenceInstanceIDValue);
            return Json(asset) + "\n" + string.Join("\n", references);
        }
        private void ValidateNewAddress(string path)
        {
            string address = ResourcesEditorTextReader.ResourceAddress(path); if (address == null) return;
            if (assets.Keys.Any(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase) && string.Equals(ResourcesEditorTextReader.ResourceAddress(p), address, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Resources address is already staged: " + address);
            if (AssetDatabase.GetAllAssetPaths().Any(p => !AssetDatabase.IsValidFolder(p) && string.Equals(ResourcesEditorTextReader.ResourceAddress(p), address, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Resources address already exists: " + address);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var asset in assets.Values) if (asset.Draft != null) Object.DestroyImmediate(asset.Draft);
            if (Draft != null) Object.DestroyImmediate(Draft);
        }
    }
}
