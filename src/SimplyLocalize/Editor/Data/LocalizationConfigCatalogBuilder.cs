using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    public sealed class LocalizationConfigCatalogBuilder : IPreprocessBuildWithReport
    {
        public const string AssetPath = "Assets/SimplyLocalizeGenerated/Resources/SimplyLocalize/GeneratedConfigCatalog.asset";
        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            try { Generate(AssetPath); }
            catch (Exception error) { throw new BuildFailedException("Simply Localize config catalog: " + error.Message); }
        }

        [MenuItem("Tools/Simply Localize/Rebuild Bootstrap Catalog")]
        public static void Rebuild() => Generate(AssetPath);

        internal static void Generate(string assetPath)
        {
            var entries = LocalizationConfigCatalog.CollectEditorEntries();
            foreach (string guid in AssetDatabase.FindAssets("t:LocalizationConfigCatalog"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (LocalizationConfigCatalog.GetResourcePath(assetPath) == LocalizationConfigCatalog.ResourcePath
                    && path != assetPath && LocalizationConfigCatalog.GetResourcePath(path) == LocalizationConfigCatalog.ResourcePath)
                    throw new InvalidOperationException("Duplicate generated config catalog: " + path);
            }
            var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null && !(existing is LocalizationConfigCatalog) || existing == null && File.Exists(assetPath))
                throw new IOException("Refusing to overwrite an unrelated file: " + assetPath);
            var catalog = existing as LocalizationConfigCatalog;
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                AssetDatabase.Refresh();
                catalog = ScriptableObject.CreateInstance<LocalizationConfigCatalog>();
                catalog.entries = entries;
                AssetDatabase.CreateAsset(catalog, assetPath);
            }
            else
            {
                catalog.entries = entries;
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssetIfDirty(catalog);
            if (EditorUtility.IsDirty(catalog) || !File.Exists(assetPath)) throw new IOException("Could not save config catalog: " + assetPath);
        }
    }
}
