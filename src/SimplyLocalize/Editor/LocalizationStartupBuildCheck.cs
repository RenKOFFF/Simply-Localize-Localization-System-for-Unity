using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    // Explicit runtime/bootstrap configs may live outside Resources. Only the reserved startup
    // address is mandatory to validate here; no runtime/provider initialization belongs in a build hook.
    internal sealed class LocalizationStartupBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                LocalizationProjectAuthoring.ValidateStartupAddress(LocalizationProjectAuthoring.ConfigAssetPath);
                var asset = AssetDatabase.LoadMainAssetAtPath(LocalizationProjectAuthoring.ConfigAssetPath);
                if (asset != null)
                {
                    if (!(asset is LocalizationProjectConfig config))
                        throw new InvalidOperationException("The startup asset must be a LocalizationProjectConfig (Project Configuration): "
                            + LocalizationProjectAuthoring.ConfigAssetPath + ". Set up the project explicitly in Simply Localize; no automatic migration is performed.");
                    config.ValidateOrThrow();
                }
            }
            catch (Exception error) { throw new BuildFailedException(error.Message); }
            foreach (var config in LocalizationProjectAuthoring.FindConfigs())
            {
                string path = AssetDatabase.GetAssetPath(config);
                if (config.AutoInitialize && path != LocalizationProjectAuthoring.ConfigAssetPath)
                    Debug.LogWarning("[SimplyLocalize] Auto-initialize config is outside the startup path: " + path
                        + ". Open Simply Localize > Settings and use Use for automatic startup, or disable auto-initialize and pass the config explicitly.", config);
            }
        }
    }
}
