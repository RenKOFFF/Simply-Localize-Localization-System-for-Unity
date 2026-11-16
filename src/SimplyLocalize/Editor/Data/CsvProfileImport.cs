using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    internal static class CsvProfileImport
    {
        internal static void Apply(CsvImportPlan plan, EditorLocalizationData data, LocalizationConfig config, Action<LocalizationConfig> saveConfig = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.resourcesBasePath != data.LoadedResourcesPath)
                throw new InvalidOperationException("Config resource path changed or does not match the loaded data. Reload and preview again.");
            var codes = config.GetLanguageCodes();
            if (!new HashSet<string>(codes, StringComparer.Ordinal).SetEquals(data.LoadedLanguages))
                throw new InvalidOperationException("Config languages changed or do not match the loaded data. Reload and preview again.");
            if (plan.NewLanguages.Count == 0) { plan.Apply(data, true); return; }
            string configPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(configPath) || !configPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the config under Assets before importing new languages.");
            if (!AssetDatabase.IsOpenForEdit(configPath)) throw new IOException("Config is not editable: " + configPath);
            EditorDataPath.Resolve(Application.dataPath, configPath.Substring("Assets/".Length));
            var paths = new Dictionary<string, string>();
            foreach (string code in plan.NewLanguages)
            {
                EditorDataPath.ValidateName(code, "language code");
                string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(Path.GetDirectoryName(configPath), "Profile_" + code + ".asset").Replace('\\', '/'));
                EditorDataPath.Resolve(Application.dataPath, path.Substring("Assets/".Length));
                if (File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path))
                    throw new IOException("Profile destination already exists. Refresh assets and preview again: " + path);
                paths.Add(code, path);
            }
            var originalLanguages = config.languages;
            var originalContents = originalLanguages == null ? null : new List<LanguageProfile>(originalLanguages);
            bool wasDirty = EditorUtility.IsDirty(config);
            string originalConfig = EditorJsonUtility.ToJson(config);
            byte[] originalConfigFile = EditorFileTransaction.Read(configPath);
            var created = new List<LanguageProfile>();
            EditorFileTransaction activeTransaction = null;
            try
            {
                plan.Apply(data, true, () =>
                {
                    foreach (var pair in paths)
                    {
                        var profile = ScriptableObject.CreateInstance<LanguageProfile>();
                        created.Add(profile);
                        profile.languageCode = profile.displayName = pair.Key;
                        activeTransaction.TouchExternal(pair.Value);
                        activeTransaction.TouchExternal(pair.Value + ".meta");
                        AssetDatabase.CreateAsset(profile, pair.Value);
                        if (AssetDatabase.LoadAssetAtPath<LanguageProfile>(pair.Value) != profile || !File.Exists(pair.Value))
                            throw new IOException("Failed to create language profile: " + pair.Value);
                        config.languages ??= new List<LanguageProfile>();
                        config.languages.Add(profile);
                    }
                    EditorUtility.SetDirty(config);
                    activeTransaction.TouchExternal(configPath);
                    if (saveConfig == null) AssetDatabase.SaveAssetIfDirty(config);
                    else saveConfig(config);
                    if (EditorUtility.IsDirty(config) || EditorFileTransaction.Equal(originalConfigFile, EditorFileTransaction.Read(configPath)))
                        throw new IOException("Failed to save language config: " + configPath);
                }, transaction =>
                {
                    activeTransaction = transaction;
                    transaction.TrackExternal(configPath);
                    foreach (string path in paths.Values)
                    {
                        transaction.TrackExternal(path);
                        transaction.TrackExternal(path + ".meta");
                    }
                });
            }
            catch
            {
                config.languages = originalLanguages;
                if (originalLanguages != null) { originalLanguages.Clear(); originalLanguages.AddRange(originalContents); }
                foreach (var profile in created)
                    if (profile != null && !AssetDatabase.Contains(profile)) UnityEngine.Object.DestroyImmediate(profile);
                AssetDatabase.Refresh();
                EditorJsonUtility.FromJsonOverwrite(originalConfig, config);
                // Restore dirty state without saving unrelated pending config edits.
                if (wasDirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                throw;
            }
        }
    }
}
