using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    internal static class EditorConfigSelection
    {
        internal static List<LocalizationConfig> Candidates() => AssetDatabase.FindAssets("t:LocalizationConfig")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<LocalizationConfig>).Where(c => c != null).Distinct().ToList();

        internal static LocalizationConfig Find(out string message)
        {
            try { LocalizationProjectSetup.ValidateStartupAddress(); }
            catch (Exception error) { message = error.Message; return null; }
            return Choose(Candidates(), AssetDatabase.LoadAssetAtPath<LocalizationConfig>(LocalizationProjectSetup.ConfigAssetPath), out message);
        }

        internal static LocalizationConfig Choose(IReadOnlyList<LocalizationConfig> candidates, LocalizationConfig startup, out string message)
        {
            message = null;
            if (startup != null) return startup;
            var valid = candidates.Where(c => c != null).Distinct().ToList();
            if (valid.Count == 1) return valid[0];
            if (valid.Count > 1) message = "Several localization configs found. Select the Config at the top of Simply Localize. Inspectors will use that selection.";
            return null;
        }
    }
}
