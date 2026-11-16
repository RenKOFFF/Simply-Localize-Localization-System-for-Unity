using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorLabChecks
    {
        public static string Data()
        {
            var definition = AssetDatabase.LoadAssetAtPath<HorrorWorldDefinition>(HorrorLabSetup.Root + "/Data/Precinct.asset");
            var text = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(HorrorLabSetup.Root + "/Data/TextConfig.asset");
            var global = Localization.Runtime;
            int checkedVariants = 0;
            foreach (var locale in definition.visualConfig.languages)
            {
                var manager = new LocalizationManager(definition.visualConfig);

                manager.SetLanguage(locale.Code);
                foreach (var id in definition.visualKeys)
                {
                    var variant = manager.GetAsset<HorrorVisualVariant>(id);
                    if (variant == null || variant.texture == null || variant.contentLanguage == null) throw new InvalidOperationException("Visual metadata missing: " + id);
                    if (locale.Code == "pl" && id == "Signs/Warning" && variant.contentLanguage.Code != definition.visualConfig.FallbackLanguageCode)
                        throw new InvalidOperationException("Fallback source language was lost.");
                    checkedVariants++;
                }

            }
            foreach (var locale in text.languages)
            {
                var manager = new LocalizationManager(text); manager.SetLanguage(locale.Code);
                foreach (var id in new[] { "UI/VisualLanguage", "UI/Auto", "UI/Always", "UI/LoadingWorld", "UI/HeavyLocked", "UI/SubtitleLine" })
                    if (!manager.HasKey(id) || manager.Get(id) == id) throw new InvalidOperationException("Missing UI translation: " + locale.Code + "/" + id);

            }
            if (!ReferenceEquals(global, Localization.Runtime)) throw new InvalidOperationException("Global localization was replaced.");
            return "PASS: " + checkedVariants + " visual resolutions, fallback provenance, menu translations and global-manager isolation. Async integration and memory checks run in the standalone Player.";
        }
    }
}
