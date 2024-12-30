using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public class LocalizationPreparation
    {
        public static readonly string FolderName = "SimplyLocalizeData";
        
        public static readonly string LocalizationFolderPath = Path.Combine("Assets", FolderName);
        public static readonly string FileExtensionAsset = ".asset";
        public static readonly string FileExtensionCs = ".cs";
        
        public static readonly string LocalizationTemplateName = "Localization";
        public static readonly string TemplateExtension = ".json";
        
        public static readonly string LocalizationResourcesPath = Path.Combine("Assets", FolderName, "Resources");
        public static readonly string LocalizationResourcesDataPath = Path.Combine(LocalizationResourcesPath, LocalizationTemplateName);
        public static readonly string LocalizationTemplatePath = Path.ChangeExtension(LocalizationResourcesDataPath, TemplateExtension);
        
        public static readonly string GeneratedFolderName = "Generated";
        public static readonly string GeneratedFolderPath = Path.Combine(LocalizationFolderPath, GeneratedFolderName);
        
        public static readonly string AssemblyReferenceName = "SimplyLocalize.Generated";
        public static readonly string AssemblyReferenceExtension = ".asmref";
        
        public static readonly string AssemblyReferenceDataPath = Path.Combine(GeneratedFolderPath, AssemblyReferenceName);
        public static readonly string AssemblyReferenceDataPathWithExtension = Path.ChangeExtension(AssemblyReferenceDataPath, AssemblyReferenceExtension);
        
        static LocalizationPreparation()
        {
            CreateEnums();
        }
        
        public static void PrepareFolders()
        {
            if (!AssetDatabase.IsValidFolder(LocalizationFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", FolderName);
                AssetDatabase.Refresh();
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFolderPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, GeneratedFolderName);
                AssetDatabase.Refresh();
            }
            
            if (!AssetDatabase.IsValidFolder(LocalizationResourcesPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, "Resources");
                AssetDatabase.Refresh();
            }
        }

        private static void CreateEnums()
        {
            const string keysName = "LocalizationKey";
            const string dictionaryKeysName = "LocalizationKeys";

            var newKeysPath = KeyGenerator.GetDataPath(keysName);
            var newDictionaryKeysPath = KeyGenerator.GetDataPath(dictionaryKeysName);

            PrepareFolders();
            
            if (!File.Exists(newKeysPath) || !File.Exists(newDictionaryKeysPath))
            {
                KeyGenerator.GenerateKeys();
            }

            CreateAssemblyReferences();
        }
        
        private static void CreateAssemblyReferences()
        {
            if (File.Exists(AssemblyReferenceDataPathWithExtension))
            {
                return;
            }
            
            var assemblyReferenceJson = new AssemblyDefinitionReferenceJson
            {
                reference = AssemblyReferenceName
            };
            
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