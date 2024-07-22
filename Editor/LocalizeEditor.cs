#if UNITY_EDITOR
using System.IO;
using SimplyLocalize.Editor.Keys;
using SimplyLocalize.Runtime.Data.Keys;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public static class LocalizeEditor
    {
        private static readonly string FolderName = "SimplyLocalizeData";
        
        private static readonly string DefaultLocalizationFolderPath = Path.Combine("Assets", FolderName);
        private static readonly string LocalizationDataPath = Path.Combine(DefaultLocalizationFolderPath, nameof(LocalizationKeysData));
        private static readonly string FileExtension = ".asset";
        
        private static readonly string DataPath = Path.ChangeExtension(LocalizationDataPath, FileExtension);

        private static LocalizationKeysData _localizationKeysData;

        [MenuItem("SimplyLocalize/Create localization keys data", priority = 0)]
        public static void GenerateLocalizationKeysData()
        {
            if (FindLocalizationData(out _localizationKeysData))
            {
                Debug.LogWarning($"{nameof(LocalizationKeysData)} already exists");
                SelectLocalizationKeysData();
                return;
            }
            
            _localizationKeysData = ScriptableObject.CreateInstance<LocalizationKeysData>();
            
            if (!AssetDatabase.IsValidFolder(DefaultLocalizationFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", FolderName);
            }
            
            AssetDatabase.CreateAsset(_localizationKeysData, DataPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            SelectLocalizationKeysData();

            Debug.Log($"{nameof(LocalizationKeysData)} created at " + DataPath);
        }

        [MenuItem("SimplyLocalize/Select localization keys data", priority = 1)]
        public static void SelectLocalizationKeysData()
        {
            if (_localizationKeysData != null || FindLocalizationData(out _localizationKeysData))
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = _localizationKeysData;
            }
        }

        [MenuItem("SimplyLocalize/Generate Keys", priority = 2)]
        public static void GenerateLocalizationKeys()
        {
            if (_localizationKeysData == null && !FindLocalizationData(out _localizationKeysData))
            {
                Debug.LogWarning($"No {nameof(LocalizationKeysData)} asset found. Create one and try again.");
                return;
            }
            
            KeyGenerator.SetEnums(_localizationKeysData.Keys);
            KeyGenerator.GenerateEnumKeys("LocalizationKey");
            KeyGenerator.GenerateDictionaryKeys("LocalizationKeys");
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
    }
}
#endif
