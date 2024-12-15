using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SimplyLocalize.Editor.Preparation;
using SimplyLocalize.Runtime.Data;
using SimplyLocalize.Runtime.Data.Keys;
using SimplyLocalize.Runtime.Data.Serializable;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizeEditor
    {
        private static LocalizationKeysData _localizationKeysData;
        
        public static LocalizationKeysData GetLocalizationKeysData()
        {
            if (_localizationKeysData != null || FindLocalizationKeysData(out _localizationKeysData))
            {
                return _localizationKeysData;
            }

            return GenerateLocalizationKeysData();
        }

        public static void GenerateLocalizationKeys()
        {
            GetLocalizationKeysData();
            
            _localizationKeysData.Keys = _localizationKeysData.Keys
                .GroupBy(x => x)
                .Select(x => x.First())
                .ToList();
            
            KeyGenerator.SetEnums(_localizationKeysData.Keys);
            KeyGenerator.GenerateKeys();

            GenerateLocalizationJson(_localizationKeysData.Translations);
        }

        public static bool TryAddNewKey(string newEnumKey)
        {
            GetLocalizationKeysData();

            if (!_localizationKeysData.TryAddNewKey(newEnumKey))
                return false;
            
            GenerateLocalizationKeys();

            EditorUtility.SetDirty(_localizationKeysData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            return true;
        }

        public static void GenerateLocalizationJson(SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> oldLocalizationData)
        {
            var data = GetLocalizationsData();
            if (!data.Any())
            {
                Debug.LogWarning($"No {nameof(LocalizationData)} found. " +
                                 "Create one or more files in the \"Resources\" folder from the menu " +
                                 "\"Create/SimplyLocalize/New localization data\" and try again.");
                return;
            }

            if (!File.Exists(LocalizationPreparation.LocalizationTemplatePath))
            {
                var json = GetLocalizationsAsJson(data, oldLocalizationData);
                WriteLocalization(json);
            }
            else
            {
                OverrideLocalization(data, oldLocalizationData);
            }
        }

        public static SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> GetAllLocalizationsDictionary()
        {
            if (File.Exists(LocalizationPreparation.LocalizationTemplatePath) == false)
            {
                return null;
            }
            
            var json = File.ReadAllText(LocalizationPreparation.LocalizationTemplatePath);
            var deserializeTranslating = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(json);
            
            var result = new SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>>();

            foreach (var localization in deserializeTranslating)
            {
                var valueDictionary = new SerializableSerializableDictionary<string, string>();

                foreach (var pair in localization.Value)
                {
                    valueDictionary.Add(pair.Key, pair.Value);
                }
                
                result.Add(localization.Key, valueDictionary);
            }

            return result;
        }

        private static LocalizationKeysData GenerateLocalizationKeysData()
        {
            if (FindLocalizationKeysData(out _localizationKeysData))
            {
                return _localizationKeysData;
            }
        
            _localizationKeysData = ScriptableObject.CreateInstance<LocalizationKeysData>();
            
            var localizationDataPath = Path.Combine(LocalizationPreparation.LocalizationResourcesPath, nameof(LocalizationKeysData));
            var dataPath = Path.ChangeExtension(localizationDataPath, LocalizationPreparation.FileExtensionAsset);
        
            AssetDatabase.CreateAsset(_localizationKeysData, dataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        
            Debug.Log($"{nameof(LocalizationKeysData)} created at " + dataPath);
            
            return _localizationKeysData;
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

        private static LocalizationData[] GetLocalizationsData()
        {
            return Resources.LoadAll<LocalizationData>("");
        }

        private static void WriteLocalization(string json)
        {
            File.WriteAllText(LocalizationPreparation.LocalizationTemplatePath, json);
            AssetDatabase.Refresh();

            var loadAssetAtPath = AssetDatabase.LoadAssetAtPath<TextAsset>(LocalizationPreparation.LocalizationTemplatePath);
            if (loadAssetAtPath == null)
                return;

            Selection.activeObject = loadAssetAtPath;

            EditorUtility.SetDirty(loadAssetAtPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void OverrideLocalization(LocalizationData[] data, SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> oldLocalizationData)
        {
            var newJson = GetLocalizationsAsJson(data, oldLocalizationData);
            WriteLocalization(newJson);
        }

        private static string GetLocalizationsAsJson(LocalizationData[] data, SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>> existingLocalization)
        {
            var langDict = new SerializableSerializableDictionary<string, SerializableSerializableDictionary<string, string>>();

            foreach (var language in data)
            {
                langDict.Add(language.i18nLang, new SerializableSerializableDictionary<string, string>());
            }

            foreach (var language in langDict)
            {
                foreach (var key in GetLocalizationKeysData().Keys)
                {
                    if (existingLocalization != null && existingLocalization.TryGetValue(language.Key, out var currentLocalization) && currentLocalization.TryGetValue(key, out var existTranslation))
                    {
                        language.Value.Add(key, existTranslation);
                    }
                    else
                    {
                        language.Value.Add(key, key);
                    }
                }
            }
            
            _localizationKeysData.Translations = langDict;

            var dictionary = langDict
                .ToDictionary<KeyValuePair<string, SerializableSerializableDictionary<string, string>>, string, Dictionary<string, string>>(
                    language => language.Key, 
                    language => language.Value);

            return JsonConvert.SerializeObject(dictionary, Formatting.Indented);
        }
    }
}