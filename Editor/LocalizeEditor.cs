#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Editor.Keys;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys;
using SimplyLocalize.Runtime.Data.Keys.Generated;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizeEditor
    {
        private static readonly string FolderName = "SimplyLocalizeData";
        
        private static readonly string LocalizationFolderPath = Path.Combine("Assets", FolderName);
        private static readonly string FileExtension = ".asset";
        
        private static readonly string LocalizationTemplateName = "Localization";
        private static readonly string TemplateExtension = ".json";
        
        private static readonly string LocalizationResourcesPath = Path.Combine("Assets", FolderName, "Resources");
        private static readonly string LocalizationResourcesDataPath = Path.Combine(LocalizationResourcesPath, LocalizationTemplateName);
        private static readonly string LocalizationTemplatePath = Path.ChangeExtension(LocalizationResourcesDataPath, TemplateExtension);
        
        private static readonly string GeneratedFolderName = "Generated";
        private static readonly string GeneratedFolderPath = Path.Combine(LocalizationFolderPath, GeneratedFolderName);
        
        private static readonly string AssemblyReferenceName = "SimplyLocalize.Generated";
        private static readonly string AssemblyReferenceExtension = ".asmref";
        
        private static readonly string AssemblyReferenceDataPath = Path.Combine(GeneratedFolderPath, AssemblyReferenceName);
        private static readonly string AssemblyReferenceDataPathWithExtension = Path.ChangeExtension(AssemblyReferenceDataPath, AssemblyReferenceExtension);

        private static readonly string AssemblyReferenceGuid = "GUID:f576fac433856444e817119d3cfb1a11";

        private static LocalizationKeysData _localizationKeysData;

        public static string NewKeysPath => GeneratedFolderPath;

        [MenuItem("Window/SimplyLocalize/Initialize Asset", priority = 300, secondaryPriority = 1)]
        public static void InitializeAssets()
        {
            GenerateLocalizationKeysData();
            
            CreateAssemblyReferences();
            GenerateLocalizationKeys();
        }

        [MenuItem("Window/SimplyLocalize/Select Localization Keys List", priority = 300, secondaryPriority = 2)]
        public static void SelectLocalizationKeysData()
        {
            if (_localizationKeysData != null || FindLocalizationKeysData(out _localizationKeysData))
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = _localizationKeysData;
            }
        }

        [MenuItem("Window/SimplyLocalize/Generate Keys", priority = 3000, secondaryPriority = 3)]
        public static void GenerateLocalizationKeys()
        {
            if (_localizationKeysData == null && !FindLocalizationKeysData(out _localizationKeysData))
            {
                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return;
            }

            KeyGenerator.UpdateGenerationPath();
            
            KeyGenerator.SetEnums(_localizationKeysData.Keys);
            KeyGenerator.GenerateEnumKeys();
            KeyGenerator.GenerateDictionaryKeys();

            GenerateLocalizationTemplate();
        }

        public static bool TryAddNewKey(string newEnumKey)
        {
            if (_localizationKeysData == null && !FindLocalizationKeysData(out _localizationKeysData))
                return false;

            if (!_localizationKeysData.TryAddNewKey(newEnumKey))
                return false;
            
            GenerateLocalizationKeys();
            
            return true;
        }

        private static void GenerateLocalizationKeysData()
        {
            if (FindLocalizationKeysData(out _localizationKeysData))
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
            
            if (!AssetDatabase.IsValidFolder(LocalizationResourcesPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, "Resources");
            }
            
            var localizationDataPath = Path.Combine(LocalizationResourcesPath, nameof(LocalizationKeysData));
            var dataPath = Path.ChangeExtension(localizationDataPath, FileExtension);

            AssetDatabase.CreateAsset(_localizationKeysData, dataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SelectLocalizationKeysData();

            Debug.Log($"{nameof(LocalizationKeysData)} created at " + dataPath);
        }

        private static bool FindLocalizationKeysData(out LocalizationKeysData data)
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
                if (LoadLocalizationKeysData(guids, out data)) return true;

                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return false;
            }

            if (guids.Length > 1)
            {
                if (LoadLocalizationKeysData(guids, out data)) return true;

                Debug.LogError($"Multiple {nameof(LocalizationKeysData)} assets found. Delete one and try again.");
                return false;
            }

            return false;
        }

        private static bool LoadLocalizationKeysData(string[] guids, out LocalizationKeysData data)
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
            var data = GetLocalizationData();
            if (!data.Any())
            {
                Debug.LogWarning($"No {nameof(LocalizationData)} found. " +
                                 "Create one or more files in the \"Resources\" folder from the menu " +
                                 "\"Create/SimplyLocalize/New localization data\" and try again.");
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

        private static LocalizationData[] GetLocalizationData()
        {
            return Resources.LoadAll<LocalizationData>("");
        }

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

            var oldLocalizationData = JsonConvert.DeserializeObject<AllLocalizationsDictionary>(json);

            var newJson = GetTemplateContent(data, oldLocalizationData);
            WriteLocalization(newJson);
        }

        private static string GetTemplateContent(LocalizationData[] data, AllLocalizationsDictionary loadedData)
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

            var dictionary = new AllLocalizationsDictionary
            {
                Translations = langDict
            };

            return JsonConvert.SerializeObject(dictionary, Formatting.Indented);
        }
        
        private static void CreateAssemblyReferences()
        {
            var assemblyReferenceJson = new AssemblyDefinitionReferenceJson
            {
                reference = AssemblyReferenceGuid
            };

            if (!AssetDatabase.IsValidFolder(GeneratedFolderPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, GeneratedFolderName);
            }
            
            var json = JsonConvert.SerializeObject(assemblyReferenceJson, Formatting.Indented);
            File.WriteAllText(AssemblyReferenceDataPathWithExtension, json);

            AssetDatabase.Refresh();
            
            Debug.Log($"{nameof(AssemblyDefinitionReferenceAsset)} created at " + AssemblyReferenceDataPathWithExtension);
        }
        
        [System.Serializable]
        private class AssemblyDefinitionReferenceJson
        {
            public string reference;
        }
    }
}
#endif