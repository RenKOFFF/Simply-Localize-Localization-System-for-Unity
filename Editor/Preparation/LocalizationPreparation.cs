using System.IO;
using UnityEditor;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public class LocalizationPreparation
    {
        public static readonly string FolderName = "SimplyLocalizeData";
        
        public static readonly string LocalizationFolderPath = Path.Combine("Assets", FolderName);
        public static readonly string FileExtensionAsset = ".asset";
        
        public static readonly string LocalizationTemplateName = "Localization";
        public static readonly string TemplateExtension = ".json";
        
        public static readonly string LocalizationResourcesPath = Path.Combine("Assets", FolderName, "Resources");
        public static readonly string LocalizationResourcesDataPath = Path.Combine(LocalizationResourcesPath, LocalizationTemplateName);
        public static readonly string LocalizationTemplatePath = Path.ChangeExtension(LocalizationResourcesDataPath, TemplateExtension);
        
        
        static LocalizationPreparation()
        {
            PrepareFolders();
            LocalizeEditor.GetLocalizationKeysData();
        }
        
        public static void PrepareFolders()
        {
            if (!AssetDatabase.IsValidFolder(LocalizationFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", FolderName);
                AssetDatabase.Refresh();
            }
            
            if (!AssetDatabase.IsValidFolder(LocalizationResourcesPath))
            {
                AssetDatabase.CreateFolder(LocalizationFolderPath, "Resources");
                AssetDatabase.Refresh();
            }
        }
    }
}