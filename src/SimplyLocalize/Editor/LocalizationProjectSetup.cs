using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    /// <summary>Editor-only project setup. No dialogs: usable from the Inspector, scripts and -executeMethod.</summary>
    public static class LocalizationProjectSetup
    {
        public const string ConfigAssetPath = "Assets/SimplyLocalize/Resources/SimplyLocalizeSettings.asset";

        public sealed class Result
        {
            public LocalizationConfig Config { get; internal set; }
            public IReadOnlyList<string> CreatedPaths { get; internal set; }
            public IReadOnlyList<string> ValidationErrors { get; internal set; }
        }

        /// <summary>Creates or updates the startup config. Existing translations/profiles are never overwritten.
        /// Existing startup/logging flags are preserved; supplied default/fallback codes are applied explicitly.</summary>
        public static Result Configure(string[] languageCodes, string defaultLanguage, string fallbackLanguage = null, bool createAlongsideExistingConfigs = false)
        {
            ValidateCodes(languageCodes);
            if (!languageCodes.Contains(defaultLanguage)) throw new ArgumentException("Default language must be included in languageCodes.");
            if (fallbackLanguage != null && !languageCodes.Contains(fallbackLanguage))
                throw new ArgumentException("Fallback language must be included in languageCodes.");
            ValidateStartupAddress();
            var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(ConfigAssetPath);
            if (config == null)
            {
                EnsureVacant(ConfigAssetPath);
                var alternatives = AssetDatabase.FindAssets("t:LocalizationConfig");
                if (alternatives.Length != 0 && !createAlongsideExistingConfigs)
                    throw new InvalidOperationException("Existing localization configs found. Select one and use Use for automatic startup; no new config was created.");
                config = ScriptableObject.CreateInstance<LocalizationConfig>();
                config.autoInitialize = true;
                config.resourcesBasePath = "SimplyLocalize/Translations";
            }
            return Apply(config, languageCodes, defaultLanguage, fallbackLanguage, !AssetDatabase.Contains(config));
        }

        /// <summary>Adds profiles and empty translation files to a saved config without changing its defaults.</summary>
        public static Result AddLanguages(LocalizationConfig config, params string[] languageCodes)
        {
            ValidateCodes(languageCodes);
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!AssetDatabase.Contains(config)) throw new ArgumentException("Save the config before adding languages.");
            return Apply(config, languageCodes, null, null, false);
        }

        private static void ValidateCodes(string[] codes)
        {
            if (codes == null || codes.Length == 0) throw new ArgumentException("At least one language code is required.");
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes)
            {
                EditorDataPath.ValidateName(code, "language code");
                if (string.IsNullOrWhiteSpace(code) || code != code.Trim() || !unique.Add(code))
                    throw new ArgumentException("Invalid or duplicate language code: " + code);
            }
        }

        private static Result Apply(LocalizationConfig config, string[] codes, string defaultCode, string fallbackCode, bool createConfig)
        {
            string path = createConfig ? ConfigAssetPath : AssetDatabase.GetAssetPath(config);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Config must be editable under Assets.");
            EditorDataPath.Resolve(Application.dataPath, path.Substring(7));
            if (!createConfig && !AssetDatabase.IsOpenForEdit(path)) throw new IOException("Config is not editable: " + path);
            var errors = config.GetValidationErrors();
            if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors));
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string resourceRoot = parent.EndsWith("/Resources", StringComparison.Ordinal) ? parent : parent + "/Resources";
            string profilesRoot = parent.EndsWith("/Resources", StringComparison.Ordinal) ? parent.Substring(0, parent.Length - 10) + "/Languages" : parent + "/Languages";
            var newProfiles = new Dictionary<string, string>();
            var files = new List<string>();
            foreach (string code in codes)
            {
                var existing = config.languages?.FirstOrDefault(p => p != null && string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.Code != code) throw new ArgumentException("Language already uses different casing: " + existing.Code);
                if (existing == null)
                {
                    string profilePath = profilesRoot + "/" + code + ".asset";
                    EnsureVacant(profilePath);
                    newProfiles.Add(code, profilePath);
                }
                if (!config.loadTextData) continue;
                string textAddress = (config.resourcesBasePath.Length == 0 ? "" : config.resourcesBasePath.TrimEnd('/') + "/") + code + "/text/";
                // Respect existing external-editor files, including data in other Resources roots.
                if (AssetDatabase.GetAllAssetPaths().Any(p => ResourceAddress(p)?.StartsWith(textAddress, StringComparison.Ordinal) == true)) continue;
                string file = resourceRoot + "/" + textAddress + "main.json";
                if (File.Exists(file)) continue;
                EnsureVacant(file);
                files.Add(file);
            }
            if (!createConfig && newProfiles.Count == 0 && files.Count == 0 &&
                (defaultCode == null || config.DefaultLanguageCode == defaultCode) &&
                (fallbackCode == null || config.FallbackLanguageCode == fallbackCode)) return MakeResult(config, Array.Empty<string>());

            string before = EditorJsonUtility.ToJson(config);
            bool wasDirty = EditorUtility.IsDirty(config);
            var originalLanguages = config.languages;
            var created = new List<string>();
            var temporaryProfiles = new List<LanguageProfile>();
            using var transaction = new EditorFileTransaction();
            transaction.TrackExternal(path);
            if (createConfig) transaction.TrackExternal(path + ".meta");
            foreach (var profilePath in newProfiles.Values)
            {
                transaction.TrackExternal(profilePath); transaction.TrackExternal(profilePath + ".meta");
            }
            foreach (var file in files) transaction.Add(file, null, Encoding.UTF8.GetBytes("{\"translations\":{}}\n"));
            try
            {
                transaction.Execute(() =>
                {
                    config.languages = new List<LanguageProfile>(config.languages ?? new List<LanguageProfile>());
                    foreach (var pair in newProfiles)
                    {
                        EnsureFolders(Path.GetDirectoryName(pair.Value).Replace('\\', '/'));
                        var profile = ScriptableObject.CreateInstance<LanguageProfile>();
                        temporaryProfiles.Add(profile);
                        profile.languageCode = pair.Key;
                        profile.displayName = pair.Key;
                        profile.systemLanguage = pair.Key switch { "en" => SystemLanguage.English, "ru" => SystemLanguage.Russian, "pl" => SystemLanguage.Polish, _ => SystemLanguage.Unknown };
                        transaction.TouchExternal(pair.Value); transaction.TouchExternal(pair.Value + ".meta");
                        AssetDatabase.CreateAsset(profile, pair.Value);
                        if (!File.Exists(pair.Value) || AssetDatabase.LoadAssetAtPath<LanguageProfile>(pair.Value) != profile)
                            throw new IOException("Could not create profile: " + pair.Value);
                        config.languages.Add(profile); created.Add(pair.Value);
                    }
                    if (defaultCode != null) config.defaultLanguage = config.GetProfile(defaultCode);
                    if (fallbackCode != null) config.fallbackLanguage = config.GetProfile(fallbackCode);
                    EnsureFolders(parent);
                    transaction.TouchExternal(path);
                    if (createConfig)
                    {
                        transaction.TouchExternal(path + ".meta"); AssetDatabase.CreateAsset(config, path); created.Add(path);
                    }
                    else { EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); }
                    if (!File.Exists(path) || EditorUtility.IsDirty(config)) throw new IOException("Could not save config: " + path);
                });
                created.AddRange(files);
                AssetDatabase.Refresh();
                EditorDataCache.Invalidate();
                return MakeResult(config, created.ToArray());
            }
            catch
            {
                AssetDatabase.Refresh();
                foreach (var profile in temporaryProfiles) if (profile != null && !AssetDatabase.Contains(profile)) UnityEngine.Object.DestroyImmediate(profile);
                if (createConfig) { if (config != null && !AssetDatabase.Contains(config)) UnityEngine.Object.DestroyImmediate(config); }
                else
                {
                    EditorJsonUtility.FromJsonOverwrite(before, config);
                    config.languages = originalLanguages;
                    if (wasDirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                }
                throw;
            }
        }

        private static Result MakeResult(LocalizationConfig config, IReadOnlyList<string> created) =>
            new Result { Config = config, CreatedPaths = created, ValidationErrors = config.GetValidationErrors().AsReadOnly() };

        /// <summary>Moves a saved config to the reserved startup address, retaining its GUID and data.</summary>
        public static void UseForAutomaticStartup(LocalizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            string source = AssetDatabase.GetAssetPath(config);
            if (!source.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Select a saved project config under Assets.");
            ValidateStartupAddress(source);
            if (source != ConfigAssetPath) EnsureVacant(ConfigAssetPath);
            string before = EditorJsonUtility.ToJson(config);
            bool dirty = EditorUtility.IsDirty(config);
            bool moved = false;
            // Probe before invoking Unity's native move/save (locked files can stall native saves).
            using (var probe = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
            if (!AssetDatabase.IsOpenForEdit(source)) throw new IOException("Config is not editable: " + source);
            try
            {
                if (source != ConfigAssetPath)
                {
                    EnsureFolders(Path.GetDirectoryName(ConfigAssetPath).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(source, ConfigAssetPath);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                    moved = true;
                    // MoveAsset may reimport the object from disk; retain pending Inspector edits.
                    EditorJsonUtility.FromJsonOverwrite(before, config);
                }
                using var transaction = new EditorFileTransaction();
                transaction.TrackExternal(ConfigAssetPath);
                transaction.Execute(() =>
                {
                    config.autoInitialize = true;
                    transaction.TouchExternal(ConfigAssetPath);
                    EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
                    if (EditorUtility.IsDirty(config)) throw new IOException("Could not save startup config.");
                });
                EditorDataCache.Invalidate();
            }
            catch (Exception failure)
            {
                if (moved)
                {
                    string error = AssetDatabase.MoveAsset(ConfigAssetPath, source);
                    if (!string.IsNullOrEmpty(error)) throw new AggregateException(failure, new IOException("Could not restore config path: " + error));
                }
                // A rollback move can reimport too; restore pending edits after the final move.
                EditorJsonUtility.FromJsonOverwrite(before, config);
                if (dirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                throw;
            }
        }

        internal static string ResourceAddress(string path)
        {
            if (path.Split('/').Contains("Editor")) return null;
            int start = path.LastIndexOf("/Resources/", StringComparison.Ordinal);
            return start < 0 ? null : Path.ChangeExtension(path.Substring(start + 11), null);
        }

        internal static void ValidateStartupAddress(string allowedSource = null)
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if (string.Equals(ResourceAddress(path), LocalizationConfig.StartupResourcePath, StringComparison.OrdinalIgnoreCase) && path != allowedSource &&
                    (path != ConfigAssetPath || AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path) == null))
                    throw new InvalidOperationException("Reserved startup Resources address is occupied: " + path + ". Use " + ConfigAssetPath + ".");
        }

        private static void EnsureVacant(string path)
        {
            EditorDataPath.Resolve(Application.dataPath, path.Substring(7));
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta"))
                throw new IOException("Destination already exists; nothing was overwritten: " + path);
        }

        private static void EnsureFolders(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolders(parent);
            // Transaction staging may already have created a physical directory.
            if (Directory.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            else AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Batch entry: -executeMethod SimplyLocalize.Editor.LocalizationProjectSetup.ConfigureFromCommandLine
        /// -slLanguages en,ru -slDefault en [-slFallback en]. No dialogs; failures propagate to batch mode.</summary>
        public static void ConfigureFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Read(string option, bool required)
            {
                int index = Array.IndexOf(args, option);
                if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("-")) return args[index + 1];
                if (required) throw new ArgumentException("Missing argument: " + option);
                return null;
            }
            var result = Configure(Read("-slLanguages", true).Split(','), Read("-slDefault", true), Read("-slFallback", false));
            Debug.Log("[SimplyLocalize] Setup complete: " + AssetDatabase.GetAssetPath(result.Config) + "; created " + result.CreatedPaths.Count + " files.");
        }
    }

    internal sealed class LocalizationStartupBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            try { LocalizationProjectSetup.ValidateStartupAddress(); }
            catch (Exception error) { throw new BuildFailedException(error.Message); }
            foreach (string guid in AssetDatabase.FindAssets("t:LocalizationConfig"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path);
                if (config.autoInitialize && path != LocalizationProjectSetup.ConfigAssetPath)
                    Debug.LogWarning("[SimplyLocalize] Auto-initialize config is outside the startup path: " + path + ". Use its Inspector to move it, or disable auto-initialize and pass the config explicitly.", config);
            }
        }
    }
}
