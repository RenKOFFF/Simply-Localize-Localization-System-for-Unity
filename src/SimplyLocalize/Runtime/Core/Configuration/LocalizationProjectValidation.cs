using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Pluralization;

namespace SimplyLocalize
{
    public enum LocalizationIssueSeverity { Warning, Error }

    public sealed class LocalizationConfigurationIssue
    {
        public LocalizationIssueSeverity Severity { get; }
        public string Code { get; }
        public string Path { get; }
        public string Message { get; }
        internal LocalizationConfigurationIssue(LocalizationIssueSeverity severity, string code, string path, string message)
        { Severity = severity; Code = code; Path = path; Message = message; }
    }

    internal static class LocalizationProjectValidation
    {
        internal static IReadOnlyList<LocalizationConfigurationIssue> Validate(LocalizationProjectConfig config)
        {
            var issues = new List<LocalizationConfigurationIssue>();
            void Error(string code, string path, string message) => issues.Add(new(LocalizationIssueSeverity.Error, code, path, message));
            void Warning(string code, string path, string message) => issues.Add(new(LocalizationIssueSeverity.Warning, code, path, message));
            var identities = new HashSet<string>(StringComparer.Ordinal);
            void Identity(string id, string path)
            {
                if (!LocalizationIdentity.IsValid(id)) Error("invalid-id", path, "Expected a non-empty canonical GUID identity.");
                else if (!identities.Add(id)) Error("duplicate-id", path, "Identity is already used: " + id);
            }
            void Name(string name, string path)
            {
                if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Any(char.IsControl))
                    Error("invalid-name", path, "Expected a non-empty name without surrounding whitespace or control characters.");
            }
            void Source(LocalizationSourceReference source, string path, bool appearance)
            {
                if (source == null) { Error("empty-source", path, "Source is empty."); return; }
                if (!LocalizationIdentity.IsProviderId(source.ProviderId))
                    Error("invalid-provider", path, "Use a provider identifier consisting of letters, digits, '.', '_' or '-'.");
                if (string.IsNullOrWhiteSpace(source.Address) || source.Address != source.Address.Trim() || source.Address.Any(char.IsControl))
                    Error("invalid-address", path, "Source address is empty or contains whitespace/control characters at its boundary.");
                else if (source.ProviderId == LocalizationSourceReference.ResourcesProvider &&
                    (!LocalizationConfig.IsResourcePathValid(source.Address) || source.Address.EndsWith("/", StringComparison.Ordinal)))
                    Error("invalid-address", path, "Resources requires an exact relative address, without traversal or a trailing '/'.");
                if (!Enum.IsDefined(typeof(LocalizationSourceKind), source.Kind) || (source.Kind == LocalizationSourceKind.Appearance) != appearance)
                    Error("invalid-source-kind", path, appearance ? "A language appearance must use an Appearance source." : "A content set accepts text/asset tables, not language appearance sources.");
            }

            var languages = new HashSet<string>(StringComparer.Ordinal);
            if (config.Languages.Count == 0) Error("no-languages", "languages", "Choose at least one language.");
            for (int i = 0; i < config.Languages.Count; i++)
            {
                var language = config.Languages[i]; string path = $"languages[{i}]";
                if (language == null) { Error("empty-language", path, "Language definition is empty."); continue; }
                if (!LocalizationLanguageCode.IsCanonical(language.Code)) Error("invalid-language", path, "Language code must be canonical: " + language.Code);
                else
                {
                    if (!languages.Add(language.Code)) Error("duplicate-language", path, "Language is already defined: " + language.Code);
                    if (PluralRuleProvider.GetRuleInfo(language.Code).Support == PluralRuleSupport.Default)
                        Warning("default-plural-rule", path, "No built-in/custom plural rule for " + language.Code + "; English-like fallback is used. Selecting a language does not guarantee its grammar or font coverage.");
                }
                Name(language.DisplayName, path + ".displayName");
                if (language.Appearance != null) Source(language.Appearance, path + ".appearance", true);
            }

            var sets = new Dictionary<string, LocalizationContentSet>(StringComparer.Ordinal);
            for (int i = 0; i < config.Sets.Count; i++)
            {
                var set = config.Sets[i]; string path = $"sets[{i}]";
                if (set == null) { Error("empty-set", path, "Content set is empty."); continue; }
                Identity(set.Id, path + ".id"); Name(set.Name, path + ".name");
                if (set.Id != null && !sets.ContainsKey(set.Id)) sets.Add(set.Id, set);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int e = 0; e < set.Entries.Count; e++)
                {
                    var entry = set.Entries[e]; string entryPath = path + $".entries[{e}]";
                    if (entry == null) { Error("empty-entry", entryPath, "Entry is empty."); continue; }
                    Identity(entry.Id, entryPath + ".id"); Name(entry.Key, entryPath + ".key");
                    if (!keys.Add(entry.Key ?? "")) Error("duplicate-key", entryPath, "Keys must be unique inside a set.");
                    if (!Enum.IsDefined(typeof(LocalizationEntryKind), entry.Kind)) Error("invalid-entry-kind", entryPath, "Unknown entry kind.");
                }
                var sourceLanguages = new HashSet<string>(StringComparer.Ordinal);
                for (int s = 0; s < set.Sources.Count; s++)
                {
                    var binding = set.Sources[s]; string sourcePath = path + $".sources[{s}]";
                    if (binding == null) { Error("empty-binding", sourcePath, "Language sources are empty."); continue; }
                    if (!languages.Contains(binding.Language ?? "")) Error("unknown-language", sourcePath, "Source language must be defined in the project.");
                    if (!sourceLanguages.Add(binding.Language ?? "")) Error("duplicate-source-language", sourcePath, "Combine this language's sources into a single binding.");
                    var addresses = new HashSet<string>(StringComparer.Ordinal);
                    for (int a = 0; a < binding.Sources.Count; a++)
                    {
                        var source = binding.Sources[a]; Source(source, sourcePath + $"[{a}]", false);
                        if (source != null && !addresses.Add(source.ProviderId + "\n" + source.Kind + "\n" + source.Address))
                            Error("duplicate-source", sourcePath, "Source address is repeated for this language.");
                    }
                }
                if (set.Entries.Count > 0 && set.Sources.Count == 0)
                    Warning("no-sources", path, "This set has entries but no language sources yet.");
            }

            if (config.Channels.Count == 0) Error("no-channels", "channels", "Configure the Main channel.");
            var channelIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Channels.Count; i++)
            {
                var channel = config.Channels[i]; string path = $"channels[{i}]";
                if (channel == null) { Error("empty-channel", path, "Channel is empty."); continue; }
                Identity(channel.Id, path + ".id"); Name(channel.Name, path + ".name"); channelIds.Add(channel.Id ?? "");
                if (!Enum.IsDefined(typeof(LocalizationChannelMode), channel.Mode)) Error("invalid-channel-mode", path, "Choose Independent or FollowMain.");
                if (channel.Id == config.MainChannelId && channel.Mode != LocalizationChannelMode.Independent)
                    Error("main-follows-itself", path, "Main must be independent.");
                var available = new HashSet<string>(StringComparer.Ordinal);
                foreach (string language in channel.Languages)
                {
                    if (!languages.Contains(language ?? "")) Error("unknown-language", path, "Channel language must be defined in the project: " + language);
                    if (!available.Add(language ?? "")) Error("duplicate-channel-language", path, "Channel repeats language: " + language);
                }
                if (available.Count == 0) Error("no-channel-languages", path, "Choose at least one channel language.");
                if (string.IsNullOrEmpty(channel.DefaultLanguage) || !available.Contains(channel.DefaultLanguage))
                    Error("invalid-default", path, "Default language must be selected in this channel.");
                if (!string.IsNullOrEmpty(channel.FallbackLanguage) && !available.Contains(channel.FallbackLanguage))
                    Error("invalid-fallback", path, "Fallback language must be selected in this channel.");
                var fallback = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var edge in channel.Fallbacks)
                {
                    if (edge == null) { Error("empty-fallback", path, "Fallback rule is empty."); continue; }
                    if (!available.Contains(edge.Language ?? "") || !available.Contains(edge.Fallback ?? ""))
                        Error("invalid-fallback", path, "Both fallback languages must be selected in this channel.");
                    if (edge.Language != null)
                    {
                        if (fallback.ContainsKey(edge.Language)) Error("duplicate-fallback", path, "Language has multiple fallback rules: " + edge.Language);
                        else fallback.Add(edge.Language, edge.Fallback);
                    }
                }
                foreach (string start in available)
                {
                    var visited = new HashSet<string>(StringComparer.Ordinal); string current = start;
                    while (!string.IsNullOrEmpty(current) && available.Contains(current))
                    {
                        if (!visited.Add(current)) { Error("fallback-cycle", path, "Fallback cycle reachable from " + start); break; }
                        current = fallback.TryGetValue(current, out string next) ? next : current == channel.FallbackLanguage ? null : channel.FallbackLanguage;
                    }
                }
                var bound = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in channel.SetIds)
                {
                    if (id == null || !sets.ContainsKey(id)) Error("unknown-set", path, "Channel refers to a missing content set.");
                    if (!bound.Add(id ?? "")) Error("duplicate-set-binding", path, "Content set is bound more than once.");
                }
                var startup = new HashSet<string>(StringComparer.Ordinal);
                if (bound.Count > 0 && (string.IsNullOrEmpty(channel.DefaultSetId) || !bound.Contains(channel.DefaultSetId)))
                    Error("invalid-default-set", path, "Choose a default content set from this channel's sets.");
                if (bound.Count == 0 && !string.IsNullOrEmpty(channel.DefaultSetId))
                    Error("invalid-default-set", path, "Default set refers to a removed channel binding.");
                foreach (string id in channel.StartupSetIds)
                {
                    if (id == null || !bound.Contains(id)) Error("invalid-startup-set", path, "Startup set must belong to this channel.");
                    if (!startup.Add(id ?? "")) Error("duplicate-startup-set", path, "Startup set is repeated.");
                }
            }
            if (string.IsNullOrEmpty(config.MainChannelId) || !channelIds.Contains(config.MainChannelId))
                Error("missing-main", "mainChannelId", "Select an existing Main channel.");
            return issues.AsReadOnly();
        }
    }
}
