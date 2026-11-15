using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Data;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Pluralization;
using SimplyLocalize.TextProcessing;

namespace SimplyLocalize.Editor.Utilities
{
    public struct LanguageCoverage
    {
        public string LanguageCode;
        public string DisplayName;
        public int TranslatedCount;
        public int TotalCount;
        public float Percentage;
        public List<string> MissingKeys;
        public int AvailableCount;
        public float AvailablePercentage;
        public List<string> UnavailableKeys;
    }

    public struct CoverageWarning
    {
        public enum Severity { Info, Warning, Error }
        public Severity Level;
        public string Message;
        public string Key;
        public string LanguageCode;
    }

    public sealed class CoverageReport
    {
        public readonly List<LanguageCoverage> TextCoverage = new();
        public readonly List<LanguageCoverage> AssetCoverage = new();
        public readonly List<CoverageWarning> Warnings = new();
        public bool IsComplete => !Warnings.Any(w => w.Level == CoverageWarning.Severity.Error);
    }

    /// <summary>Read-only diagnostics using runtime parsing, resource discovery and fallback semantics.</summary>
    public static class CoverageAnalyzer
    {
        public static List<LanguageCoverage> ComputeCoverage(EditorLocalizationData data, LocalizationConfig config)
        {
            if (data == null || config == null) return new();
            return Compute(data.AllKeys, config, (key, code) => !string.IsNullOrEmpty(data.GetTranslation(key, code)));
        }

        public static List<CoverageWarning> ComputeWarnings(EditorLocalizationData data, LocalizationConfig config)
        {
            var warnings = new List<CoverageWarning>();
            if (config == null || data == null)
            {
                Add(warnings, CoverageWarning.Severity.Error, "Coverage requires a configuration and loaded data.");
                return warnings;
            }
            foreach (var error in config.GetValidationErrors()) Add(warnings, CoverageWarning.Severity.Error, error);
            var profiles = Profiles(config).ToList();
            if (profiles.Count == 0) Add(warnings, CoverageWarning.Severity.Warning, "No languages configured; localization has not been checked.");
            if (data.KeyCount == 0) Add(warnings, CoverageWarning.Severity.Warning, "No text keys found; localization has not been checked.");
            string reference = config.DefaultLanguageCode ?? profiles.FirstOrDefault()?.Code;
            foreach (var key in data.AllKeys)
            {
                string referenceValue = reference != null ? data.GetTranslation(key, reference) : null;
                var referenceParams = ExtractParams(referenceValue);
                foreach (var profile in profiles)
                {
                    string value = data.GetTranslation(key, profile.Code);
                    if (string.IsNullOrEmpty(value))
                    {
                        var fallback = FallbackResolver.ResolveTranslation(data, config, key, profile.Code);
                        Add(warnings, CoverageWarning.Severity.Warning,
                            $"Missing direct translation for '{key}' in {profile.Code}" +
                            (fallback.Value != null ? $"; available via {fallback.FromLanguage}." : "; no fallback available."), key, profile.Code);
                        continue;
                    }
                    foreach (var token in TokenParser.Parse(value).Tokens)
                    {
                        if (token.Type == TokenType.Text) continue;
                        if (token.Type == TokenType.IndexedParam && token.Index < 0)
                            Add(warnings, CoverageWarning.Severity.Error, $"Invalid parameter index in '{key}' ({profile.Code}): {token.Index}.", key, profile.Code);
                        if (token.HasForms)
                        {
                            int required = PluralRuleProvider.GetRule(profile.Code).FormCount;
                            if (token.Forms.Length < required)
                                Add(warnings, CoverageWarning.Severity.Warning,
                                    $"'{key}' ({profile.Code}): {token.Forms.Length} forms, plural rule expects {required}. Numeric values may use the last form; an explicit FormSelector may intentionally use fewer forms.", key, profile.Code);
                        }
                    }
                    if (profile.Code == reference || string.IsNullOrEmpty(referenceValue)) continue;
                    var parameters = ExtractParams(value);
                    var missing = referenceParams.Except(parameters).ToArray();
                    var extra = parameters.Except(referenceParams).ToArray();
                    if (missing.Length > 0) Add(warnings, CoverageWarning.Severity.Error,
                        $"Missing params in '{key}' ({profile.Code}): {string.Join(", ", missing)}; reference: {reference}.", key, profile.Code);
                    if (extra.Length > 0) Add(warnings, CoverageWarning.Severity.Info,
                        $"Extra params in '{key}' ({profile.Code}): {string.Join(", ", extra)}; may be intentional.", key, profile.Code);
                }
            }
            return warnings;
        }

        /// <summary>
        /// Loads all runtime files/tables, including nested folders and multiple Resources roots.
        /// Visible editor values overlay the snapshot. Does not change file ownership or live state.
        /// </summary>
        public static CoverageReport Analyze(LocalizationConfig config, EditorLocalizationData editorData = null,
            ILocalizationDataProvider provider = null)
        {
            var report = new CoverageReport();
            if (config == null)
            {
                Add(report.Warnings, CoverageWarning.Severity.Error, "No configuration assigned.");
                return report;
            }
            var errors = config.GetValidationErrors();
            if (errors.Count > 0)
            {
                foreach (var error in errors) Add(report.Warnings, CoverageWarning.Severity.Error, error);
                return report;
            }
            provider ??= new ResourcesDataProvider(config.resourcesBasePath);
            var data = new EditorLocalizationData();
            var tables = new Dictionary<string, List<LocalizationAssetTable>>();
            var assetKeys = new HashSet<string>();
            var codes = new HashSet<string>();
            foreach (var profile in Profiles(config)) foreach (var code in Chain(config, profile)) codes.Add(code);
            foreach (var code in codes)
            {
                try
                {
                    var translations = provider.LoadTextData(code) ?? throw new InvalidOperationException("Provider returned null text data.");
                    foreach (var pair in translations)
                    {
                        data.AddKey(pair.Key, "coverage");
                        data.SetTranslation(pair.Key, code, pair.Value);
                    }
                }
                catch (Exception error)
                {
                    Add(report.Warnings, CoverageWarning.Severity.Error, $"Cannot check text for '{code}': {error.Message}", language: code);
                }
                try
                {
                    tables[code] = provider.LoadAssetTables(code) ?? throw new InvalidOperationException("Provider returned null asset tables.");
                    var seen = new HashSet<string>();
                    foreach (var table in tables[code])
                    {
                        if (table == null) continue;
                        foreach (var key in table.Keys)
                        {
                            if (string.IsNullOrEmpty(key)) continue;
                            if (!seen.Add(key)) Add(report.Warnings, CoverageWarning.Severity.Warning,
                                $"Duplicate asset key '{key}' in '{code}' (table '{table.name}'); resolution depends on table order and requested type.", key, code);
                            assetKeys.Add(key);
                        }
                    }
                }
                catch (Exception error)
                {
                    Add(report.Warnings, CoverageWarning.Severity.Error, $"Cannot check assets for '{code}': {error.Message}", language: code);
                }
            }
            if (editorData != null)
                foreach (var language in editorData.Translations)
                    foreach (var pair in language.Value)
                    {
                        data.AddKey(pair.Key, editorData.GetFileForKey(pair.Key) ?? "coverage");
                        data.SetTranslation(pair.Key, language.Key, pair.Value);
                    }
            report.TextCoverage.AddRange(ComputeCoverage(data, config));
            report.Warnings.AddRange(ComputeWarnings(data, config));
            report.AssetCoverage.AddRange(Compute(assetKeys, config, (key, code) =>
                tables.TryGetValue(code, out var list) && list.Any(table => table != null && table.Get(key) != null)));
            return report;
        }

        private static IEnumerable<LanguageProfile> Profiles(LocalizationConfig config) =>
            (config.languages ?? new List<LanguageProfile>()).Where(p => p != null && !string.IsNullOrEmpty(p.Code)).Distinct();

        private static IEnumerable<string> Chain(LocalizationConfig config, LanguageProfile profile)
        {
            var seen = new HashSet<string>();
            for (var current = profile; current != null && seen.Add(current.Code); current = current.fallbackProfile)
                yield return current.Code;
            if (config.fallbackLanguage != null && seen.Add(config.fallbackLanguage.Code)) yield return config.fallbackLanguage.Code;
        }

        private static List<LanguageCoverage> Compute(IEnumerable<string> keys, LocalizationConfig config, Func<string, string, bool> hasValue)
        {
            var allKeys = keys.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var results = new List<LanguageCoverage>();
            foreach (var profile in Profiles(config))
            {
                var chain = Chain(config, profile).ToList();
                var missing = allKeys.Where(key => !hasValue(key, profile.Code)).ToList();
                var unavailable = allKeys.Where(key => !chain.Any(code => hasValue(key, code))).ToList();
                results.Add(new LanguageCoverage
                {
                    LanguageCode = profile.Code, DisplayName = profile.displayName,
                    TotalCount = allKeys.Count, TranslatedCount = allKeys.Count - missing.Count,
                    MissingKeys = missing, UnavailableKeys = unavailable, AvailableCount = allKeys.Count - unavailable.Count,
                    Percentage = allKeys.Count == 0 ? 0 : (float)(allKeys.Count - missing.Count) / allKeys.Count,
                    AvailablePercentage = allKeys.Count == 0 ? 0 : (float)(allKeys.Count - unavailable.Count) / allKeys.Count
                });
            }
            return results;
        }

        private static HashSet<string> ExtractParams(string text)
        {
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            var parsed = TokenParser.Parse(text);
            if (parsed != null)
                foreach (var token in parsed.Tokens)
                    if (token.Type == TokenType.IndexedParam) parameters.Add(token.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else if (token.Type == TokenType.NamedParam) parameters.Add(token.Name);
            return parameters;
        }

        private static void Add(List<CoverageWarning> warnings, CoverageWarning.Severity severity, string message,
            string key = null, string language = null) =>
            warnings.Add(new CoverageWarning { Level = severity, Message = message, Key = key, LanguageCode = language });
    }
}
