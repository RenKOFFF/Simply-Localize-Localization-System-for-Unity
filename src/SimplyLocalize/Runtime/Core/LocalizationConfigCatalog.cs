using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Generated bootstrap index. Strings only: it never retains localized asset dependencies.</summary>
    public sealed class LocalizationConfigCatalog : ScriptableObject
    {
        public const string ResourcePath = "SimplyLocalize/GeneratedConfigCatalog";
        [Serializable]
        public sealed class Entry
        {
            public string resourcePath;
            public string configName;
            public bool autoInitialize;
        }
        [HideInInspector] public List<Entry> entries = new();

        internal static LocalizationConfig FindConfig(bool automaticOnly)
        {
#if UNITY_EDITOR
            // The Editor uses fresh asset metadata so unsaved setup changes and tests need no generated file.
            var entries = CollectEditorEntries();
#else
            var catalog = Resources.Load<LocalizationConfigCatalog>(ResourcePath);
            if (catalog == null)
            {
                Debug.LogError("[SimplyLocalize] Generated config catalog is missing. Rebuild the Player with the Simply Localize editor package, or initialize with an explicit config.");
                return null;
            }
            var entries = catalog.entries;
#endif
            var candidates = entries.Where(e => e != null && (!automaticOnly || e.autoInitialize)).ToList();
            if (candidates.Count == 0)
            {
                if (!automaticOnly) Debug.LogError("[SimplyLocalize] No LocalizationConfig found in Resources. Create one and place it in a Resources folder.");
                return null;
            }
            if (candidates.Count != 1)
            {
                var names = candidates.Select(e => e.configName).OrderBy(n => n, StringComparer.Ordinal);
                Debug.LogError($"[SimplyLocalize] Conflicting {(automaticOnly ? "auto-initialize " : "")}LocalizationConfigs in Resources: {string.Join(", ", names)}. Assign a config explicitly or enable autoInitialize on only one config.");
                return null;
            }
            var selected = candidates[0];
            var config = Resources.Load<LocalizationConfig>(selected.resourcePath);
            if (config == null)
                Debug.LogError($"[SimplyLocalize] Indexed config '{selected.resourcePath}' was not found. Rebuild the config catalog.");
            return config;
        }

#if UNITY_EDITOR
        public static List<Entry> CollectEditorEntries()
        {
            var result = new List<Entry>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:LocalizationConfig"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string resourcePath = GetResourcePath(path);
                if (resourcePath == null) continue;
                if (!paths.Add(resourcePath))
                    throw new InvalidOperationException("Ambiguous LocalizationConfig Resources path: " + resourcePath + ". Use unique paths across Resources folders.");
                var config = UnityEditor.AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path);
                if (config == null) continue;
                result.Add(new Entry { resourcePath = resourcePath, configName = config.name, autoInitialize = config.autoInitialize });
            }
            return result.OrderBy(e => e.resourcePath, StringComparer.Ordinal).ToList();
        }

        public static string GetResourcePath(string assetPath)
        {
            string path = assetPath.Replace('\\', '/');
            if (path.Split('/').Contains("Editor")) return null;
            int index = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
            if (index < 0) return null;
            string relative = path.Substring(index + "/Resources/".Length);
            int extension = relative.LastIndexOf('.');
            return extension < 0 ? relative : relative.Substring(0, extension);
        }
#endif
    }
}
