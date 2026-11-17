using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor
{
    /// <summary>Project configuration setup used by the window and Editor automation. No dialogs or game runtime changes.</summary>
    public static class LocalizationProjectAuthoring
    {
        public const string ConfigAssetPath = "Assets/SimplyLocalize/Resources/SimplyLocalizeSettings.asset";
        public sealed class Result
        {
            public LocalizationProjectConfig Config { get; internal set; }
            public LocalizationProjectChange Change { get; internal set; }
        }
        public static Result Configure(IEnumerable<string> languages, string defaultLanguage, string fallbackLanguage = null,
            IReadOnlyDictionary<string, string> displayNames = null)
        {
            var codes = (languages ?? throw new ArgumentNullException(nameof(languages))).Select(LocalizationLanguageCode.Normalize).ToArray();
            if (codes.Length == 0 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length) throw new ArgumentException("Select distinct language codes.");
            defaultLanguage = LocalizationLanguageCode.Normalize(defaultLanguage);
            fallbackLanguage = string.IsNullOrEmpty(fallbackLanguage) ? null : LocalizationLanguageCode.Normalize(fallbackLanguage);
            if (!codes.Contains(defaultLanguage) || fallbackLanguage != null && !codes.Contains(fallbackLanguage)) throw new ArgumentException("Choose defaults from the selected languages.");
            var config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(ConfigAssetPath);
            if (config == null) return new Result { Config = Create(codes, defaultLanguage, fallbackLanguage, displayNames: displayNames) };
            ValidateStartupAddress(ConfigAssetPath);
            return new Result { Config = config, Change = AddLanguages(config, codes, displayNames, true, defaultLanguage, fallbackLanguage) };
        }
        public static IReadOnlyList<LocalizationProjectConfig> FindConfigs() => AssetDatabase.FindAssets("t:LocalizationProjectConfig")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>).Where(c => c != null).ToArray();

        /// <summary>Creates Main/Common and empty exact text sources. An occupied path/address is an error, never an implicit migration.</summary>
        public static LocalizationProjectConfig Create(IEnumerable<string> languages, string defaultLanguage, string fallbackLanguage = null,
            string assetPath = ConfigAssetPath, bool automaticStartup = true, IReadOnlyDictionary<string, string> displayNames = null)
        {
            var codes = (languages ?? throw new ArgumentNullException(nameof(languages))).Select(LocalizationLanguageCode.Normalize).ToArray();
            if (codes.Length == 0 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length) throw new ArgumentException("Select distinct language codes.", nameof(languages));
            defaultLanguage = LocalizationLanguageCode.Normalize(defaultLanguage);
            fallbackLanguage = string.IsNullOrEmpty(fallbackLanguage) ? null : LocalizationLanguageCode.Normalize(fallbackLanguage);
            if (!codes.Contains(defaultLanguage) || fallbackLanguage != null && !codes.Contains(fallbackLanguage))
                throw new ArgumentException("Choose default and fallback from the selected languages.");
            LocalizationProjectChange.ValidateWritable(assetPath); Vacant(assetPath);
            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .asset config path.");
            if (automaticStartup && ResourcesEditorTextReader.ResourceAddress(assetPath) != LocalizationProjectConfig.StartupResourcePath)
                throw new ArgumentException("Automatic startup requires the reserved Resources address; disable it for an explicit bootstrap config.");
            if (automaticStartup) ValidateStartupAddress();
            string resourceRoot = ResourceRoot(assetPath);
            var files = codes.ToDictionary(code => code, code => resourceRoot + "/SimplyLocalize/Translations/" + code + "/Common.json", StringComparer.Ordinal);
            foreach (string file in files.Values) { LocalizationProjectChange.ValidateWritable(file); Vacant(file); ValidateNewAddress(file); }
            // Validate caller-supplied names before allocating an unsaved Unity object.
            if (displayNames != null)
                foreach (string code in codes)
                    if (displayNames.TryGetValue(code, out var displayName)) _ = new LocalizationLanguageDefinition(code, displayName);
            var config = LocalizationProjectConfig.Create(codes);
            config.name = Path.GetFileNameWithoutExtension(assetPath); config.AutoInitialize = automaticStartup;
            config.MainChannel.DefaultLanguage = defaultLanguage; config.MainChannel.FallbackLanguage = fallbackLanguage;
            foreach (var language in config.Languages)
            {
                ApplyCatalogName(language);
                if (displayNames != null && displayNames.TryGetValue(language.Code, out var displayName)) language.DisplayName = displayName;
            }
            foreach (var pair in files) config.Sets[0].SetSources(pair.Key, new LocalizationSourceReference("resources", ResourcesEditorTextReader.ResourceAddress(pair.Value), LocalizationSourceKind.TextTable));
            config.ValidateOrThrow();
            var ownedMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assetPath + ".meta" };
            foreach (string file in files.Values) ownedMetadata.Add(file + ".meta");
            foreach (string file in files.Values.Concat(new[] { assetPath }))
                for (string directory = Path.GetDirectoryName(file).Replace('\\', '/'); directory != "Assets"; directory = Path.GetDirectoryName(directory).Replace('\\', '/'))
                    if (!Directory.Exists(directory))
                    {
                        if (File.Exists(directory + ".meta")) { Object.DestroyImmediate(config); throw new IOException("An orphan folder .meta occupies " + directory); }
                        ownedMetadata.Add(directory + ".meta");
                    }
            using var transaction = new EditorFileTransaction();
            transaction.TrackExternalSnapshot(assetPath, null);
            foreach (string meta in ownedMetadata) transaction.TrackExternalSnapshot(meta, null);
            foreach (string file in files.Values) transaction.Add(file, null, new UTF8Encoding(false).GetBytes("{\n  \"translations\": {}\n}\n"));
            try
            {
                transaction.Execute(() =>
                {
                    foreach (string meta in ownedMetadata.Where(m => m != assetPath + ".meta")) transaction.TouchExternal(meta);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    transaction.TouchExternal(assetPath); transaction.TouchExternal(assetPath + ".meta"); AssetDatabase.CreateAsset(config, assetPath);
                    EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
                    if (!File.Exists(assetPath) || EditorUtility.IsDirty(config)) throw new IOException("Could not save project config: " + assetPath);
                });
                return config;
            }
            catch
            {
                AssetDatabase.Refresh();
                if (config != null && !AssetDatabase.Contains(config)) Object.DestroyImmediate(config);
                throw;
            }
        }

        /// <summary>Adds a project language and makes it available in Main. Independent channels stay explicit.
        /// A simple Resources project also gets a new empty Main text source; external backends keep source setup explicit.</summary>
        public static LocalizationProjectChange AddLanguage(LocalizationProjectConfig config, string language, string displayName = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            language = LocalizationLanguageCode.Normalize(language);
            if (config.Languages.Any(l => l.Code == language)) return null;
            return AddLanguages(config, new[] { language }, string.IsNullOrWhiteSpace(displayName) ? null : new Dictionary<string, string> { [language] = displayName });
        }
        private static LocalizationProjectChange AddLanguages(LocalizationProjectConfig config, string[] languages,
            IReadOnlyDictionary<string, string> names = null, bool changeDefaults = false, string defaultLanguage = null, string fallbackLanguage = null)
        {
            var common = config.Sets.FirstOrDefault(s => s.Id == config.MainChannel.DefaultSetId);
            bool createText = common != null && (common.Sources.Count == 0 || common.Sources.SelectMany(s => s.Sources).Any(s => s.Kind == LocalizationSourceKind.TextTable))
                && common.Sources.SelectMany(s => s.Sources).Where(s => s.Kind == LocalizationSourceKind.TextTable)
                .All(s => s.ProviderId == "resources") && (common.Entries.Count == 0 || common.Entries.Any(e => e.Kind == LocalizationEntryKind.Text));
            using var edit = new LocalizationProjectEdit(config, createText ? new[] { common.Id } : Array.Empty<string>());
            foreach (string language in languages)
            {
                if (!edit.Draft.Languages.Any(l => l.Code == language))
                {
                    var definition = edit.Draft.AddLanguage(language); ApplyCatalogName(definition);
                    if (names != null && names.TryGetValue(language, out var displayName)) definition.DisplayName = displayName;
                }
                var draftCommon = createText ? edit.Draft.Sets.Single(s => s.Id == common.Id) : null;
                if (createText && !draftCommon.Sources.Any(s => s.Language == language && s.Sources.Any(r => r.Kind == LocalizationSourceKind.TextTable)))
                {
                    string path = ResourceRoot(AssetDatabase.GetAssetPath(config)) + "/SimplyLocalize/Translations/" + language + "/Common.json";
                    if (File.Exists(path)) edit.RegisterTextSource(common.Id, language, path);
                    else edit.CreateTextSource(common.Id, language, path);
                }
            }
            edit.Draft.MainChannel.SetLanguages(edit.Draft.MainChannel.Languages.Concat(languages));
            if (changeDefaults) { edit.Draft.MainChannel.DefaultLanguage = defaultLanguage; edit.Draft.MainChannel.FallbackLanguage = fallbackLanguage; }
            return edit.Commit();
        }
        public static void ValidateStartupAddress(string allowedPath = null)
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if (!AssetDatabase.IsValidFolder(path) && path != allowedPath && string.Equals(ResourcesEditorTextReader.ResourceAddress(path), LocalizationProjectConfig.StartupResourcePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Startup address is already occupied: " + path + ". Select or explicitly relocate the project config; no data was overwritten.");
        }
        /// <summary>Relocates this project config, retaining its GUID, pending settings and source addresses.
        /// This explicit asset move does not migrate old config types or move translation data.</summary>
        public static void UseForAutomaticStartup(LocalizationProjectConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            string source = AssetDatabase.GetAssetPath(config);
            LocalizationProjectChange.ValidateWritable(source); LocalizationProjectChange.ValidateWritable(ConfigAssetPath);
            config.ValidateOrThrow(); ValidateStartupAddress(source);
            if (source != ConfigAssetPath) Vacant(ConfigAssetPath);
            string before = EditorJsonUtility.ToJson(config); bool dirty = EditorUtility.IsDirty(config), moved = false;
            // Native Unity saving/moving can stall on a locked file; probe before calling it.
            foreach (string path in new[] { source, source + ".meta" })
            {
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("File is read-only: " + path);
                using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            try
            {
                if (source != ConfigAssetPath)
                {
                    EnsureFolders(Path.GetDirectoryName(ConfigAssetPath).Replace('\\', '/'));
                    string failure = AssetDatabase.MoveAsset(source, ConfigAssetPath);
                    if (!string.IsNullOrEmpty(failure)) throw new IOException(failure);
                    moved = true; EditorJsonUtility.FromJsonOverwrite(before, config);
                }
                using var transaction = new EditorFileTransaction(); transaction.TrackExternal(ConfigAssetPath);
                transaction.Execute(() =>
                {
                    transaction.TouchExternal(ConfigAssetPath);
                    config.AutoInitialize = true; config.name = Path.GetFileNameWithoutExtension(ConfigAssetPath);
                    EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
                    if (EditorUtility.IsDirty(config)) throw new IOException("Could not save startup config.");
                });
            }
            catch (Exception failure)
            {
                if (moved)
                {
                    string rollback = AssetDatabase.MoveAsset(ConfigAssetPath, source);
                    if (!string.IsNullOrEmpty(rollback)) throw new AggregateException(failure, new IOException("Could not restore config path: " + rollback));
                }
                EditorJsonUtility.FromJsonOverwrite(before, config);
                if (dirty) EditorUtility.SetDirty(config); else EditorUtility.ClearDirty(config);
                throw;
            }
        }
        private static void EnsureFolders(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolders(parent);
            if (Directory.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            else
            {
                if (File.Exists(path + ".meta")) throw new IOException("An orphan folder .meta occupies " + path);
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create folder: " + path);
            }
        }
        private static void ApplyCatalogName(LocalizationLanguageDefinition language)
        {
            var catalog = LanguageCatalog.Find(language.Code); if (catalog == null) return;
            language.DisplayName = catalog.NativeName; language.SystemLanguage = catalog.SystemLanguage;
        }
        private static string ResourceRoot(string configPath)
        {
            string parent = Path.GetDirectoryName(configPath).Replace('\\', '/');
            return parent.EndsWith("/Resources", StringComparison.Ordinal) ? parent : parent + "/Resources";
        }
        private static void Vacant(string path)
        { if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta")) throw new IOException("Destination already exists: " + path); }
        private static void ValidateNewAddress(string path)
        {
            string address = ResourcesEditorTextReader.ResourceAddress(path);
            if (!LocalizationResourceAddress.IsValid(address)) throw new ArgumentException("Invalid Resources destination: " + path);
            if (AssetDatabase.GetAllAssetPaths().Any(p => !AssetDatabase.IsValidFolder(p) && string.Equals(ResourcesEditorTextReader.ResourceAddress(p), address, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Resources address already exists: " + address);
        }
    }
}
