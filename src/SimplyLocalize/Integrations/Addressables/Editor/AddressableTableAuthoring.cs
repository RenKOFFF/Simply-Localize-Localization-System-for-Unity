using System;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Windows;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Integrations.Editor
{
    [InitializeOnLoad]
    public sealed class AddressableTableAuthoring : ILocalizationEditorTableWriter
    {
        private const string GroupName = "Simply Localize Tables";

        static AddressableTableAuthoring()
        {
            LocalizationEditorReaders.Register(AddressableTableProvider.Id, assets: new AddressableTableAuthoring());
            ProjectSettingsView.OptionalSourceControls += AddControls;
        }

        private static void AddControls(VisualElement parent, LocalizationProjectConfig config, string setId, Func<string> language, Action<Action> run)
        {
            var root = new Foldout { text = "Addressables" };
            parent.Add(root);
            root.Add(new Button(() =>
            {
                string path = EditorUtility.SaveFilePanelInProject("Create Addressables table", "ChapterVoice", "asset", "Save outside Resources.");
                if (!string.IsNullOrEmpty(path)) run(() => CreateTable(config, setId, language(), path));
            }) { text = "Create Addressables table", name = "createAddressableTable_" + setId });
            var existing = new UnityEditor.UIElements.ObjectField("Existing table") { objectType = typeof(LocalizationAssetTable), allowSceneObjects = false };
            var connect = new Button(() => run(() => ConnectTable(config, setId, language(), (LocalizationAssetTable)existing.value)))
                { text = "Connect with Addressables" };
            connect.SetEnabled(false);
            existing.RegisterValueChangedCallback(e => connect.SetEnabled(e.newValue != null));
            root.Add(existing);
            root.Add(connect);
            root.Add(new Button(() => run(() =>
            {
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
            })) { text = "Build Addressables content", tooltip = "Build the project's Addressables catalog and bundles for the active platform." });
        }

        public static void CreateTable(LocalizationProjectConfig config, string setId, string language, string path)
        {
            ValidatePath(path);
            if (File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path) || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\', '/')))
                throw new IOException("Choose a new file in an existing project folder.");
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>();
            LocalizationProjectChange change;
            try
            {
                AssetDatabase.CreateAsset(table, path);
                change = Connect(config, setId, language, table);
            }
            catch
            {
                if (AssetDatabase.Contains(table)) AssetDatabase.DeleteAsset(path);
                else UnityEngine.Object.DestroyImmediate(table);
                throw;
            }
            LocalizationProjectUndo.Record(change, "Connect Addressables table");
        }

        private static void ValidatePath(string path)
        {
            EditorDataPath.ValidateNewAssetPath(path);
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.Split('/').Any(p =>
                string.Equals(p, "Resources", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Editor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "StreamingAssets", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Choose an .asset file outside Resources, Editor and StreamingAssets.");
        }

        public static void ConnectTable(LocalizationProjectConfig config, string setId, string language, LocalizationAssetTable table) =>
            LocalizationProjectUndo.Record(Connect(config, setId, language, table), "Connect Addressables table");

        private static LocalizationProjectChange Connect(LocalizationProjectConfig config, string setId, string language, LocalizationAssetTable table)
        {
            if (table == null || !AssetDatabase.IsMainAsset(table)) throw new ArgumentException("Select a saved localization table.");
            string path = AssetDatabase.GetAssetPath(table);
            ValidatePath(path);
            using var edit = new LocalizationProjectAssetEdit(config);
            var set = edit.Draft.Sets.Single(s => s.Id == setId);
            language = LocalizationLanguageCode.Normalize(language);
            if (!edit.Draft.Languages.Any(l => l.Code == language)) throw new ArgumentException("Unknown project language.");
            var sources = set.Sources.FirstOrDefault(s => s.Language == language)?.Sources ?? Array.Empty<LocalizationSourceReference>();
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (sources.Any(s => s.ProviderId == AddressableTableProvider.Id && s.Address == guid))
                throw new InvalidOperationException("Table is already connected for this language.");
            var existing = settings.FindAssetEntry(guid, true);
            var group = existing?.parentGroup ?? settings.FindGroup(GroupName);
            bool createdGroup = group == null;
            bool addedEntry = false;
            LocalizationProjectChange change;
            try
            {
                if (createdGroup)
                {
                    group = settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                    group.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                }
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null || !schema.IncludeGUIDInCatalog || !schema.IncludeInBuild)
                    throw new InvalidOperationException(group.Name + " must build bundled content and include GUIDs in the catalog.");
                if (existing == null)
                {
                    if (settings.CreateOrMoveEntry(guid, group) == null) throw new InvalidOperationException("Could not register " + path);
                    addedEntry = true;
                }
                AssetDatabase.SaveAssetIfDirty(group);
                AssetDatabase.SaveAssetIfDirty(schema);
                AssetDatabase.SaveAssetIfDirty(settings);
                var source = new LocalizationSourceReference(AddressableTableProvider.Id, guid, LocalizationSourceKind.AssetTable);
                set.SetSources(language, sources.Append(source).ToArray());
                change = edit.Commit();
            }
            catch
            {
                if (addedEntry) settings.RemoveAssetEntry(guid);
                if (createdGroup && group != null && group.entries.Count == 0) settings.RemoveGroup(group);
                if (group != null) AssetDatabase.SaveAssetIfDirty(group);
                AssetDatabase.SaveAssetIfDirty(settings);
                throw;
            }
            return change;
        }

        public string ResolveTablePath(LocalizationSourceReference source)
        {
            var entry = AddressableAssetSettingsDefaultObject.Settings?.FindAssetEntry(source.Address, true);
            var schema = entry?.parentGroup.GetSchema<BundledAssetGroupSchema>();
            if (schema == null || !schema.IncludeInBuild || !schema.IncludeGUIDInCatalog)
                throw new InvalidOperationException("Missing Addressables catalog entry for table " + source.Address);
            return entry.AssetPath;
        }

        public LocalizationEditorAssetSource Read(LocalizationSourceReference source)
        {
            string path = ResolveTablePath(source);
            var table = AssetDatabase.LoadAssetAtPath<LocalizationAssetTable>(path);
            if (!table) throw new InvalidOperationException("Expected a localization asset table at " + path);
            return ResourcesEditorAssetReader.ReadTable(path, table);
        }
    }
}
