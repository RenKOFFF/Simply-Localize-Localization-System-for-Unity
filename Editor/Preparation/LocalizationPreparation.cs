using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public class LocalizationPreparation
    {
        public static readonly string AppTitleLocalizationPackageURL = "https://github.com/yasirkula/UnityMobileLocalizedAppTitle.git";
        
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
            var keysData = LocalizeEditor.LocalizationKeysData;
            var config = LocalizeEditor.LocalizationConfig;

#if UNITY_ANDROID || UNITY_IOS
            if (config is { ShowAppLocalizationGitPackagePopup: true })
                CheckLocalizedAppTitlePackage(AppTitleLocalizationPackageURL);
#endif
        }

        private static void PrepareFolders()
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

#if UNITY_ANDROID || UNITY_IOS
        private static void CheckLocalizedAppTitlePackage(string gitUrl)
        {
            var request = Client.List(true);
            EditorApplication.update += ProcessRequest;
            return;

            void ProcessRequest()
            {
                if (!request.IsCompleted) return;
                
                if (request.Status == StatusCode.Success)
                {
                    bool packageFound = false;
                    foreach (var package in request.Result)
                    {
                        if (package.source == PackageSource.Git && package.packageId.Split('@').Last() == gitUrl)
                        {
                            packageFound = true;
                            break;
                        }
                    }

                    if (!packageFound)
                    {
                        TryAddLocalizedAppTitlePackage();
                    }
                    
                    var config = LocalizeEditor.GetLocalizationConfig();
                    config.ShowAppLocalizationGitPackagePopup = false;
                        
                    EditorUtility.SetDirty(config);
                }

                EditorApplication.update -= ProcessRequest;
            }
        }

        public static void TryAddLocalizedAppTitlePackage()
        {
            const string title = "Install \"Unity Localized App Title\" package";
            const string message = "\"Unity Localized App Title\" package not found. If you are creating an app for Android or iOS and want to localize the app title, install a \"Unity Localized App Title\" package.\n";
            const string messageAdditional = "If you already installed the package or don't want to localize the app title, skip this step. You can always install the package at any time.";
            
            AddRequest request;
            
            if (EditorUtility.DisplayDialog(title, message + messageAdditional, "Install", "Ignore"))
            {
                request = Client.Add(AppTitleLocalizationPackageURL);
                EditorApplication.update += Progress;
            }

            return;

            void Progress()
            {
                if (request.IsCompleted)
                {
                    switch (request.Status)
                    {
                        case StatusCode.Success:
                            Logging.Log("Package installed: {0}", args: (request.Result.packageId, Color.green));
                            break;
                        case >= StatusCode.Failure:
                            Logging.Log(request.Error.message, LogType.Warning);
                            break;
                    }

                    EditorApplication.update -= Progress;
                }
            }
        }
#endif
    }
}