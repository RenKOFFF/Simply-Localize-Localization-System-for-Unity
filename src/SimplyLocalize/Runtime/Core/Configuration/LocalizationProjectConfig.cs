using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Lightweight project definitions. No active language, loaded assets or player preferences.</summary>
    [CreateAssetMenu(fileName = "SimplyLocalizeSettings", menuName = "SimplyLocalize/Project Configuration")]
    public sealed class LocalizationProjectConfig : ScriptableObject
    {
        public const string StartupResourcePath = "SimplyLocalizeSettings";
        [SerializeField] private bool _autoInitialize;
        [SerializeField] private bool _autoDetectLanguage;
        [SerializeField] private List<LocalizationLanguageDefinition> _languages = new();
        [SerializeField] private List<LocalizationChannelDefinition> _channels = new();
        [SerializeField] private List<LocalizationContentSet> _sets = new();
        [SerializeField, LocalizationId] private string _mainChannelId;
        public bool AutoInitialize { get => _autoInitialize; set => _autoInitialize = value; }
        public bool AutoDetectLanguage { get => _autoDetectLanguage; set => _autoDetectLanguage = value; }
        public IReadOnlyList<LocalizationLanguageDefinition> Languages => LocalizationIdentity.Read(_languages);
        public IReadOnlyList<LocalizationChannelDefinition> Channels => LocalizationIdentity.Read(_channels);
        public IReadOnlyList<LocalizationContentSet> Sets => LocalizationIdentity.Read(_sets);
        public string MainChannelId => _mainChannelId;
        public LocalizationChannelDefinition MainChannel => Channels.FirstOrDefault(c => c != null && c.Id == _mainChannelId);

        /// <summary>Creates a simple Main/Common definition in memory. The caller saves or destroys the asset.</summary>
        public static LocalizationProjectConfig Create(IEnumerable<string> languages)
        {
            var codes = (languages ?? throw new ArgumentNullException(nameof(languages)))
                .Select(LocalizationLanguageCode.Normalize).Distinct(StringComparer.Ordinal).ToArray();
            if (codes.Length == 0) throw new ArgumentException("Choose at least one language.", nameof(languages));
            var config = CreateInstance<LocalizationProjectConfig>();
            foreach (string code in codes) config.AddLanguage(code);
            var common = config.AddSet("Common");
            var main = config.AddChannel("Main", codes);
            config.SetMainChannel(main.Id); main.IncludeSet(common.Id, true);
            return config;
        }
        public LocalizationLanguageDefinition AddLanguage(string code, string displayName = null)
        {
            var language = new LocalizationLanguageDefinition(code, displayName);
            if (_languages.Any(l => l.Code == language.Code)) throw new ArgumentException("Language already exists: " + language.Code);
            _languages.Add(language); return language;
        }
        public bool RemoveLanguage(string code)
        {
            code = LocalizationLanguageCode.Normalize(code);
            if (_channels.Any(c => c.Languages.Contains(code)) || _sets.Any(s => s.Sources.Any(source => source.Language == code)))
                throw new InvalidOperationException("Remove the language from channels and sources before removing its definition.");
            return _languages.RemoveAll(l => l.Code == code) > 0;
        }
        public LocalizationChannelDefinition AddChannel(string name, IEnumerable<string> languages, string id = null)
        {
            var channel = new LocalizationChannelDefinition(name, languages, id);
            if (_channels.Any(c => c.Id == channel.Id)) throw new ArgumentException("Duplicate channel identity.");
            _channels.Add(channel); return channel;
        }
        public void SetMainChannel(string channelId)
        {
            if (!_channels.Any(c => c.Id == channelId)) throw new ArgumentException("Unknown channel identity.", nameof(channelId));
            _mainChannelId = channelId;
        }
        public bool RemoveChannel(string channelId)
        {
            if (channelId == _mainChannelId) throw new InvalidOperationException("Choose another Main channel first.");
            return _channels.RemoveAll(c => c.Id == channelId) > 0;
        }
        public LocalizationContentSet AddSet(string name, string id = null)
        {
            var set = new LocalizationContentSet(name, id);
            if (_sets.Any(s => s.Id == set.Id)) throw new ArgumentException("Duplicate content set identity.");
            _sets.Add(set); return set;
        }
        public bool RemoveSet(string setId)
        {
            if (_channels.Any(c => c.SetIds.Contains(setId))) throw new InvalidOperationException("Remove channel bindings before removing the set.");
            return _sets.RemoveAll(s => s.Id == setId) > 0;
        }
        public void MoveEntry(string entryId, string targetSetId)
        {
            var source = _sets.SingleOrDefault(s => s.Entries.Any(e => e.Id == entryId)) ?? throw new ArgumentException("Unknown entry identity.", nameof(entryId));
            var target = _sets.SingleOrDefault(s => s.Id == targetSetId) ?? throw new ArgumentException("Unknown target set.", nameof(targetSetId));
            if (source == target) return;
            target.Accept(source.Entries.Single(e => e.Id == entryId)); // Validate before changing the source.
            source.RemoveEntry(entryId);
        }
        public IReadOnlyList<LocalizationConfigurationIssue> Validate() => LocalizationProjectValidation.Validate(this);
        public void ValidateOrThrow()
        {
            var errors = Validate().Where(i => i.Severity == LocalizationIssueSeverity.Error).Select(i => i.Path + ": " + i.Message).ToArray();
            if (errors.Length != 0) throw new ArgumentException(string.Join("\n", errors), "config");
        }
        public LocalizationEntryIndex CreateEntryIndex() { ValidateOrThrow(); return new LocalizationEntryIndex(this); }
    }
}
