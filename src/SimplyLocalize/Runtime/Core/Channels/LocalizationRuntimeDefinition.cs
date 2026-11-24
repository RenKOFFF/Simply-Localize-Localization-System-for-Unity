using System;
using System.Collections.Generic;
using System.Linq;

namespace SimplyLocalize
{
    // Private copies: authoring edits cannot change an in-flight runtime.
    internal sealed class LocalizationRuntimeDefinition
    {
        internal sealed class Channel
        {
            internal readonly string Id, Name, DefaultLanguage, FallbackLanguage, DefaultSetId;
            internal readonly LocalizationChannelMode Mode;
            internal readonly string[] Languages, SetIds, StartupSetIds;
            internal readonly Dictionary<string, string> Fallbacks;
            internal Channel(LocalizationChannelDefinition source)
            {
                Id = source.Id; Name = source.Name; Mode = source.Mode;
                DefaultLanguage = source.DefaultLanguage; FallbackLanguage = source.FallbackLanguage; DefaultSetId = source.DefaultSetId;
                Languages = source.Languages.ToArray(); SetIds = source.SetIds.ToArray(); StartupSetIds = source.StartupSetIds.ToArray();
                Fallbacks = source.Fallbacks.ToDictionary(f => f.Language, f => f.Fallback, StringComparer.Ordinal);
            }
            internal string[] Chain(string requested)
            {
                string language = Languages.Contains(requested) ? requested : DefaultLanguage;
                var result = new List<string>();
                while (!string.IsNullOrEmpty(language) && !result.Contains(language))
                {
                    result.Add(language);
                    language = Fallbacks.TryGetValue(language, out var next) ? next : language == FallbackLanguage ? null : FallbackLanguage;
                }
                return result.ToArray();
            }
        }
        internal sealed class Set
        {
            internal readonly string Id;
            internal readonly Dictionary<string, LocalizationEntryKind> Keys;
            internal readonly Dictionary<string, LocalizationSourceReference[]> Sources;
            internal Set(LocalizationContentSet source)
            {
                Id = source.Id; Keys = source.Entries.ToDictionary(e => e.Key, e => e.Kind, StringComparer.Ordinal);
                Sources = source.Sources.ToDictionary(s => s.Language, s => s.Sources.Select(Copy).ToArray(), StringComparer.Ordinal);
            }
        }
        internal readonly string MainId;
        internal readonly Dictionary<string, Channel> Channels;
        internal readonly Dictionary<string, Set> Sets;
        internal readonly Dictionary<string, LocalizationSourceReference> Appearances;
        internal readonly LocalizationEntryIndex Index;
        internal LocalizationRuntimeDefinition(LocalizationProjectConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.ValidateOrThrow(); MainId = source.MainChannelId; Index = source.CreateEntryIndex();
            Channels = source.Channels.ToDictionary(c => c.Id, c => new Channel(c), StringComparer.Ordinal);
            Sets = source.Sets.ToDictionary(s => s.Id, s => new Set(s), StringComparer.Ordinal);
            Appearances = source.Languages.Where(l => l.Appearance != null).ToDictionary(l => l.Code, l => Copy(l.Appearance), StringComparer.Ordinal);
        }
        private static LocalizationSourceReference Copy(LocalizationSourceReference source) => new(source.ProviderId, source.Address, source.Kind);
    }
}
