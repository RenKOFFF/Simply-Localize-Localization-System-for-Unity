using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    /// <summary>Identity-based choices. Only routing metadata is read, never translations or assets.</summary>
    public sealed class LocalizationReferenceSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private LocalizationProjectConfig config;
        private LocalizationEntryKind? kind;
        private Action<string, string> select;
        public void Init(LocalizationProjectConfig project, LocalizationEntryKind? requiredKind, Action<string, string> onSelect)
        { config = project; kind = requiredKind; select = onSelect; hideFlags = HideFlags.HideAndDontSave; }
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Localization entry"), 0),
                new SearchTreeEntry(new GUIContent("None")) { level = 1, userData = default(LocalizationEntryReference) } };
            if (config == null) return tree;
            try { config.ValidateOrThrow(); }
            catch (Exception failure)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("Fix the project config: " + failure.Message), 1));
                return tree; // Clearing an old reference remains possible even with an invalid config.
            }
            var sets = config.Sets.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (var channel in config.Channels)
            {
                string channelName = channel.Name;
                if (config.Channels.Count(c => c.Name == channelName) > 1) channelName += " (" + channel.Id.Substring(0, 8) + ")";
                tree.Add(new SearchTreeGroupEntry(new GUIContent(channelName), 1));
                foreach (string setId in channel.SetIds)
                {
                    var set = sets[setId]; string setName = set.Name;
                    if (channel.SetIds.Count(id => sets[id].Name == setName) > 1) setName += " (" + set.Id.Substring(0, 8) + ")";
                    tree.Add(new SearchTreeGroupEntry(new GUIContent(setName), 2));
                    foreach (var entry in set.Entries.Where(e => !kind.HasValue || e.Kind == kind.Value).OrderBy(e => e.Key, StringComparer.Ordinal))
                        tree.Add(new SearchTreeEntry(new GUIContent(entry.Key + (kind.HasValue ? "" : "  · " + entry.Kind), channel.Name + " / " + set.Name + " / " + entry.Key))
                        { level = 3, userData = new LocalizationEntryReference(channel.Id, entry.Id) });
                }
            }
            return tree;
        }
        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (!(entry.userData is LocalizationEntryReference reference)) return false;
            select?.Invoke(reference.ChannelId, reference.EntryId); return true;
        }
    }
}
