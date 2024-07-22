﻿#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Editor.Keys;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys;
using SimplyLocalize.Runtime.Data.Keys.Generated;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizeEditor
    {
        private static readonly string FolderName = "SimplyLocalizeData";

        private static readonly string LocalizationFolderPath = Path.Combine("Assets", FolderName);

        private static readonly string LocalizationDataPath =
            Path.Combine(LocalizationFolderPath, nameof(LocalizationKeysData));

        private static readonly string FileExtension = ".asset";

        private static readonly string DataPath = Path.ChangeExtension(LocalizationDataPath, FileExtension);

        private static readonly string LocalizationTemplateName = "Localization";
        private static readonly string LocalizationResourcesPath = Path.Combine("Assets", FolderName, "Resources");

        private static readonly string LocalizationResourcesDataPath =
            Path.Combine(LocalizationResourcesPath, LocalizationTemplateName);

        private static readonly string TemplateExtension = ".json";

        private static readonly string LocalizationTemplatePath =
            Path.ChangeExtension(LocalizationResourcesDataPath, TemplateExtension);


        private static LocalizationKeysData _localizationKeysData;

        [MenuItem("Window/SimplyLocalize/Create Localization Keys List", priority = 300, secondaryPriority = 1)]
        public static void GenerateLocalizationKeysData()
        {
            if (FindLocalizationData(out _localizationKeysData))
            {
                Debug.LogWarning($"{nameof(LocalizationKeysData)} already exists");
                SelectLocalizationKeysData();
                return;
            }

            _localizationKeysData = ScriptableObject.CreateInstance<LocalizationKeysData>();

            if (!AssetDatabase.IsValidFolder(LocalizationFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", FolderName);
            }

            AssetDatabase.CreateAsset(_localizationKeysData, DataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SelectLocalizationKeysData();

            Debug.Log($"{nameof(LocalizationKeysData)} created at " + DataPath);
        }

        [MenuItem("Window/SimplyLocalize/Select Localization Keys List", priority = 300, secondaryPriority = 2)]
        public static void SelectLocalizationKeysData()
        {
            if (_localizationKeysData != null || FindLocalizationData(out _localizationKeysData))
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = _localizationKeysData;
            }
        }

        [MenuItem("Window/SimplyLocalize/Generate Keys", priority = 3000, secondaryPriority = 3)]
        public static void GenerateLocalizationKeys()
        {
            if (_localizationKeysData == null && !FindLocalizationData(out _localizationKeysData))
            {
                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return;
            }

            KeyGenerator.SetEnums(_localizationKeysData.Keys);
            KeyGenerator.GenerateEnumKeys(nameof(LocalizationKey));
            KeyGenerator.GenerateDictionaryKeys(nameof(LocalizationKeys));

            GenerateLocalizationTemplate();
        }

        private static bool FindLocalizationData(out LocalizationKeysData data)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(LocalizationKeysData)}");
            data = null;

            if (guids.Length == 0)
            {
                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return false;
            }

            if (guids.Length == 1)
            {
                if (LoadLocalizationData(guids, out data)) return true;

                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return false;
            }

            if (guids.Length > 1)
            {
                if (LoadLocalizationData(guids, out data)) return true;

                Debug.LogError($"Multiple {nameof(LocalizationKeysData)} assets found. Delete one and try again.");
                return false;
            }

            return false;
        }

        private static bool LoadLocalizationData(string[] guids, out LocalizationKeysData data)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadAssetAtPath<LocalizationKeysData>(assetPath);
            if (asset == null)
            {
                data = null;
                return false;
            }

            data = asset;
            return true;
        }

        private static void GenerateLocalizationTemplate()
        {
            if (!AssetDatabase.IsValidFolder(LocalizationResourcesPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, "Resources");
            }

            var data = GetLocalizationData().ToArray();
            if (!data.Any())
            {
                Debug.LogWarning($"No {nameof(LocalizationData)} found. " +
                                 "Create one or more files in the \"Resources\" folder from the menu \"Create/Easy localize/New localization data\" and try again.");
                return;
            }

            if (!File.Exists(LocalizationTemplatePath))
            {
                var json = GetTemplateContent(data, null);
                WriteLocalization(json);
            }
            else
            {
                OverrideLocalization(data);
            }
        }

        private static IEnumerable<LocalizationData> GetLocalizationData() => Resources.LoadAll<LocalizationData>("");

        private static void WriteLocalization(string json)
        {
            File.WriteAllText(LocalizationTemplatePath, json);
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            var loadAssetAtPath = AssetDatabase.LoadAssetAtPath<TextAsset>(LocalizationTemplatePath);
            Selection.activeObject = loadAssetAtPath;
        }

        private static void OverrideLocalization(LocalizationData[] data)
        {
            var json = File.ReadAllText(LocalizationTemplatePath);

            var oldLocalizationData = JsonConvert.DeserializeObject<LocalizationDictionary>(json);

            var newJson = GetTemplateContent(data, oldLocalizationData);
            WriteLocalization(newJson);
        }

        private static string GetTemplateContent(LocalizationData[] data, LocalizationDictionary loadedData)
        {
            var langDict = data
                .ToDictionary(d => d.i18nLang, _ => new Dictionary<string, string>());

            foreach (var translations in langDict)
            {
                foreach (var enumHolder in _localizationKeysData.Keys)
                {
                    if (loadedData != null &&
                        loadedData.TryGetTranslating(translations.Key, enumHolder.Name, out var existTranslation))
                    {
                        translations.Value.Add(enumHolder.Name, existTranslation);
                    }
                    else
                    {
                        translations.Value.Add(enumHolder.Name, enumHolder.Name);
                    }
                }
            }

            var dictionary = new LocalizationDictionary
            {
                Translations = langDict
            };

            return JsonConvert.SerializeObject(dictionary, Formatting.Indented);
        }
    }
}
#endif