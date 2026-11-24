using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Pluralization;
using SimplyLocalize.TextProcessing;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>Read-only coverage of registered entries in one channel/set. Failed reads never become missing translations.</summary>
    public static class ProjectCoverageAnalyzer
    {
        public static CoverageReport Analyze(LocalizationProjectConfig config, string channelId = null, string setId = null,
            ILocalizationEditorTextReader textReader = null, ILocalizationEditorAssetReader assetReader = null, LocalizationPluralRules pluralRules = null)
        {
            var report = new CoverageReport(); LocalizationChannelDefinition channel;
            void Error(string message) => Warn(report, CoverageWarning.Severity.Error, message);
            try
            {
                if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
                channel = config.Channels.Single(c => c.Id == (channelId ?? config.MainChannelId)); setId ??= channel.DefaultSetId;
                if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available in this channel.");
            }
            catch (Exception failure) { Error(failure.Message); return report; }
            pluralRules ??= new LocalizationPluralRules();
            try
            {
                var data = EditorProjectTextData.Load(config, channel.Id, setId, textReader);
                foreach (string code in data.Languages)
                {
                    var row = Row(config, code, data.Entries, id => data.TryGetDirect(id, code, out _),
                        id => data.TryGetDirect(id, code, out var cell) && !string.IsNullOrEmpty(cell.Value), id => data.TryResolve(id, code, out _));
                    row.WhitespaceKeys = data.Entries.Where(p => data.TryGetDirect(p.Key, code, out var cell) && !string.IsNullOrEmpty(cell.Value)
                        && string.IsNullOrWhiteSpace(cell.Value)).Select(p => p.Value).OrderBy(k => k, StringComparer.Ordinal).ToList(); report.TextCoverage.Add(row);
                    var rule = pluralRules.GetRuleInfo(code);
                    if (rule.Support == PluralRuleSupport.Default) Warn(report, CoverageWarning.Severity.Warning,
                        "General plural rule for " + code + "; review forms or supply a custom rule.", language: code);
                    foreach (var entry in data.Entries)
                    {
                        bool present = data.TryGetDirect(entry.Key, code, out var cell); string value = cell?.Value;
                        if (string.IsNullOrEmpty(value))
                        {
                            string fallback = data.TryResolve(entry.Key, code, out var resolved) ? "; available via " + resolved.Language : "; no fallback available";
                            Warn(report, CoverageWarning.Severity.Warning, (present ? "Empty entry" : "No entry") + " for '" + entry.Value + "' (" + code + ")" + fallback + ".", entry.Value, code); continue;
                        }
                        if (string.IsNullOrWhiteSpace(value)) Warn(report, CoverageWarning.Severity.Warning,
                            "Whitespace-only translation for '" + entry.Value + "' (" + code + "); runtime uses it without fallback.", entry.Value, code);
                        foreach (var token in TokenParser.Parse(value).Tokens)
                        {
                            if (token.Type == TokenType.Text) continue;
                            if (token.Type == TokenType.IndexedParam && token.Index < 0) Warn(report, CoverageWarning.Severity.Error, "Invalid parameter index in '" + entry.Value + "'.", entry.Value, code);
                            if (token.HasForms && token.Forms.Length < rule.Rule.FormCount) Warn(report, CoverageWarning.Severity.Warning,
                                "'" + entry.Value + "' (" + code + "): " + token.Forms.Length + " forms; rule expects " + rule.Rule.FormCount + ". Explicit FormSelector may intentionally use fewer forms.", entry.Value, code);
                        }
                        if (code == channel.DefaultLanguage || !data.TryGetDirect(entry.Key, channel.DefaultLanguage, out var reference) || string.IsNullOrEmpty(reference.Value)) continue;
                        var expected = Parameters(reference.Value); var actual = Parameters(value);
                        var missing = expected.Except(actual).ToArray(); var extra = actual.Except(expected).ToArray();
                        if (missing.Length > 0) Warn(report, CoverageWarning.Severity.Error, "Missing params in '" + entry.Value + "' (" + code + "): " + string.Join(", ", missing) + "; reference: " + channel.DefaultLanguage + ".", entry.Value, code);
                        if (extra.Length > 0) Warn(report, CoverageWarning.Severity.Info, "Extra params in '" + entry.Value + "' (" + code + "): " + string.Join(", ", extra) + "; may be intentional.", entry.Value, code);
                    }
                    foreach (var extra in data.GetUnregistered(code)) Warn(report, CoverageWarning.Severity.Info,
                        "Unregistered text key '" + extra.Key + "' (" + code + ") at " + extra.Source.Address + "; ignored by runtime.", extra.Key, code);
                }
            }
            catch (Exception failure) { report.TextCoverage.Clear(); Error("Cannot check text: " + failure.Message); }
            try
            {
                var data = EditorProjectAssetData.Load(config, channel.Id, setId, assetReader);
                foreach (string code in data.Languages)
                {
                    var row = Row(config, code, data.Entries, id => data.TryGetDirect(id, code, out _),
                        id => data.TryGetDirect(id, code, out var cell) && cell.Asset != null, id => data.TryResolve(id, code, out _)); report.AssetCoverage.Add(row);
                    foreach (string key in row.MissingKeys) Warn(report, CoverageWarning.Severity.Warning,
                        "Missing direct asset for '" + key + "' (" + code + ")" + (row.UnavailableKeys.Contains(key) ? "; no fallback available." : "; available via fallback."), key, code);
                    foreach (var extra in data.GetUnregistered(code)) Warn(report, CoverageWarning.Severity.Info,
                        "Unregistered asset key '" + extra.Key + "' (" + code + ") at " + extra.Source.Address + "; ignored by runtime.", extra.Key, code);
                }
            }
            catch (Exception failure) { report.AssetCoverage.Clear(); Error("Cannot check assets: " + failure.Message); }
            return report;
        }
        private static LanguageCoverage Row(LocalizationProjectConfig config, string code, IReadOnlyDictionary<string, string> entries,
            Func<string, bool> present, Func<string, bool> direct, Func<string, bool> available)
        {
            var sorted = entries.OrderBy(p => p.Value, StringComparer.Ordinal).ToArray();
            var missing = sorted.Where(p => !direct(p.Key)).Select(p => p.Value).ToList();
            var unavailable = sorted.Where(p => !available(p.Key)).Select(p => p.Value).ToList();
            return new LanguageCoverage
            {
                LanguageCode = code, DisplayName = config.Languages.Single(l => l.Code == code).DisplayName,
                TotalCount = entries.Count, PresentCount = sorted.Count(p => present(p.Key)), TranslatedCount = entries.Count - missing.Count,
                AvailableCount = entries.Count - unavailable.Count, MissingKeys = missing, UnavailableKeys = unavailable,
                EmptyKeys = sorted.Where(p => present(p.Key) && !direct(p.Key)).Select(p => p.Value).ToList(), WhitespaceKeys = new(),
                Percentage = entries.Count == 0 ? 0 : (float)(entries.Count - missing.Count) / entries.Count,
                AvailablePercentage = entries.Count == 0 ? 0 : (float)(entries.Count - unavailable.Count) / entries.Count
            };
        }
        private static HashSet<string> Parameters(string value)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in TokenParser.Parse(value).Tokens)
                if (token.Type == TokenType.IndexedParam) result.Add("{" + token.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                else if (token.Type == TokenType.NamedParam) result.Add("{" + token.Name + "}");
            return result;
        }
        private static void Warn(CoverageReport report, CoverageWarning.Severity severity, string message, string key = null, string language = null) =>
            report.Warnings.Add(new CoverageWarning { Level = severity, Message = message, Key = key, LanguageCode = language });
    }
}
