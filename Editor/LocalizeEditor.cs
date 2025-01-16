using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizeEditor
    {
        private static LocalizationKeysData _localizationKeysData;
        private static LocalizationConfig _localizationConfig;
        
        public static LocalizationKeysData GetLocalizationKeysData()
        {
            if (_localizationKeysData != null)
            {
                return _localizationKeysData;
            }

            return _localizationKeysData = GetData<LocalizationKeysData>();
        }
        
        public static LocalizationConfig GetLocalizationConfig()
        {
            if (_localizationConfig != null)
            {
                return _localizationConfig;
            }

            return _localizationConfig = GetData<LocalizationConfig>();
        }

        public static void GenerateLocalizationKeys()
        {
            GetLocalizationKeysData();
            
            _localizationKeysData.Keys = _localizationKeysData.Keys
                .GroupBy(x => x)
                .Select(x => x.First())
                .ToList();

            GenerateLocalizationJson(_localizationKeysData.Translations);
        }
        
        public static bool CanAddNewKey(string newKey)
        {
            var keys = GetLocalizationKeysData().Keys;
            
            return newKey != "" && 
                   keys.All(x => x != newKey.ToCorrectLocalizationKeyName()) && 
                   keys.Any(x => x != "<None>");
        }

        public static bool TryAddNewKey(string newKey, bool generateKeysAfterSuccess)
        {
            if (!CanAddNewKey(newKey))
            {
                return false;
            }
            
            GetLocalizationKeysData();
            
            var key = newKey.ToCorrectLocalizationKeyName();
            // if (_localizationKeysData.Keys.Any(x => x == key))
            // {
            //     Logging.Log($"Key {key} already exists", LogType.Error);
            //     return false;
            // }
            
            _localizationKeysData.Keys.Add(key);
            
            if (generateKeysAfterSuccess)
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
                Logging.Log($"No {nameof(LocalizationData)} found. " +
                            "Create one or more files in the \"Resources\" folder in editor window and try again.", LogType.Warning);
                
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

        private static T GetData<T>() where T : ScriptableObject
        {
            if (FindData(out T data))
            {
                return data;
            }
            
            return GenerateData<T>();
        }
        
        private static T GenerateData<T>() where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            var dataName = typeof(T).Name;
            
            var localizationDataPath = Path.Combine(LocalizationPreparation.LocalizationResourcesPath, dataName);
            var dataPath = Path.ChangeExtension(localizationDataPath, LocalizationPreparation.FileExtensionAsset);
        
            AssetDatabase.CreateAsset(so, dataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return so;
        }

        private static bool FindData<T>(out T data) where T : Object
        {
            var typeName = typeof(T).Name;
            var guids = AssetDatabase.FindAssets($"t:{typeName}");
            data = null;
        
            if (guids.Length == 0)
            {
                return false;
            }
            
            return guids.Length >= 1 && LoadData(guids, out data);
        }

        private static bool LoadData<T>(string[] guids, out T data) where T : Object
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
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