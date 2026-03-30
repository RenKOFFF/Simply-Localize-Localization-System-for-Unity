using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>
    /// Scans all scenes and prefabs in the project for localized components
    /// and replaces old key references with new ones.
    /// Used after renaming keys in the TranslationsTab.
    /// </summary>
    public static class KeyReferenceUpdater
    {
        /// <summary>
        /// Updates all references from oldKey to newKey across the project.
        /// Returns the number of components updated.
        /// </summary>
        public static int UpdateReferences(string oldKey, string newKey)
        {
            int updated = 0;

            // Update prefabs
            updated += UpdatePrefabs(oldKey, newKey);

            // Update open scenes
            updated += UpdateOpenScenes(oldKey, newKey);

            // Update scene assets (not currently open)
            updated += UpdateSceneAssets(oldKey, newKey);

            return updated;
        }

        /// <summary>
        /// Updates references for multiple key renames at once.
        /// </summary>
        public static int UpdateReferences(Dictionary<string, string> oldToNewKeys)
        {
            int total = 0;
            foreach (var kvp in oldToNewKeys)
                total += UpdateReferences(kvp.Key, kvp.Value);
            return total;
        }

        private static int UpdatePrefabs(string oldKey, string newKey)
        {
            int updated = 0;

            var guids = AssetDatabase.FindAssets("t:Prefab");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var components = prefab.GetComponentsInChildren<LocalizedComponentBase>(true);
                bool modified = false;

                foreach (var comp in components)
                {
                    var so = new SerializedObject(comp);
                    var keyProp = so.FindProperty("_key");

                    if (keyProp != null && keyProp.stringValue == oldKey)
                    {
                        keyProp.stringValue = newKey;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        modified = true;
                        updated++;
                    }
                }

                if (modified)
                {
                    EditorUtility.SetDirty(prefab);
                    PrefabUtility.SavePrefabAsset(prefab);
                }
            }

            return updated;
        }

        private static int UpdateOpenScenes(string oldKey, string newKey)
        {
            int updated = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                bool sceneModified = false;

                foreach (var root in roots)
                {
                    var components = root.GetComponentsInChildren<LocalizedComponentBase>(true);

                    foreach (var comp in components)
                    {
                        var so = new SerializedObject(comp);
                        var keyProp = so.FindProperty("_key");

                        if (keyProp != null && keyProp.stringValue == oldKey)
                        {
                            keyProp.stringValue = newKey;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            sceneModified = true;
                            updated++;
                        }
                    }
                }

                if (sceneModified)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            return updated;
        }

        private static int UpdateSceneAssets(string oldKey, string newKey)
        {
            int updated = 0;

            var sceneGuids = AssetDatabase.FindAssets("t:Scene");

            foreach (var guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Skip currently open scenes
                bool isOpen = false;

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).path == path)
                    {
                        isOpen = true;
                        break;
                    }
                }

                if (isOpen) continue;

                // Quick text check — avoid opening scenes that don't contain the key
                string sceneText = File.ReadAllText(path);
                if (!sceneText.Contains(oldKey)) continue;

                // Open scene additively, update, save, close
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

                var roots = scene.GetRootGameObjects();
                bool modified = false;

                foreach (var root in roots)
                {
                    var components = root.GetComponentsInChildren<LocalizedComponentBase>(true);

                    foreach (var comp in components)
                    {
                        var so = new SerializedObject(comp);
                        var keyProp = so.FindProperty("_key");

                        if (keyProp != null && keyProp.stringValue == oldKey)
                        {
                            keyProp.stringValue = newKey;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            modified = true;
                            updated++;
                        }
                    }
                }

                if (modified)
                    EditorSceneManager.SaveScene(scene);

                EditorSceneManager.CloseScene(scene, true);
            }

            return updated;
        }
    }
}