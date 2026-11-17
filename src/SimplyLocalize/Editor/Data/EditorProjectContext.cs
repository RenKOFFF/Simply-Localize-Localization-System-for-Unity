using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>One Editor selection. No runtime initialization, language changes, or eager content loads.</summary>
    public sealed class EditorProjectContext
    {
        public LocalizationProjectConfig Config { get; }
        public string ChannelId { get; }
        public string SetId { get; }
        public LocalizationChannelDefinition Channel => Config.Channels.Single(c => c.Id == ChannelId);
        public LocalizationContentSet Set => string.IsNullOrEmpty(SetId) ? null : Config.Sets.Single(s => s.Id == SetId);
        private EditorProjectTextData text;
        private EditorProjectAssetData assets;
        private long revision = -1;
        public EditorProjectContext(LocalizationProjectConfig config, string channelId = null, string setId = null)
        {
            Config = config != null ? config : throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            ChannelId = channelId ?? config.MainChannelId;
            var channel = config.Channels.SingleOrDefault(c => c.Id == ChannelId) ?? throw new ArgumentException("Unknown channel.", nameof(channelId));
            SetId = string.IsNullOrEmpty(setId) ? channel.DefaultSetId : setId;
            if (!string.IsNullOrEmpty(SetId) && !channel.SetIds.Contains(SetId)) throw new ArgumentException("Set is not available in this channel.", nameof(setId));
        }
        public IReadOnlyList<LocalizationEntryDefinition> Entries(LocalizationEntryKind kind) =>
            Array.AsReadOnly((Set?.Entries.Where(e => e.Kind == kind) ?? Enumerable.Empty<LocalizationEntryDefinition>()).OrderBy(e => e.Key, StringComparer.Ordinal).ToArray());
        public EditorProjectTextData Text { get { EnsureFresh(); return text ??= EditorProjectTextData.Load(Config, ChannelId, SetId, LocalizationEditorReaders.Text); } }
        public EditorProjectAssetData Assets { get { EnsureFresh(); return assets ??= EditorProjectAssetData.Load(Config, ChannelId, SetId, LocalizationEditorReaders.Assets); } }
        public void Invalidate() { text = null; assets = null; }
        private void EnsureFresh() { if (revision != EditorProjectSelection.Revision) { Invalidate(); revision = EditorProjectSelection.Revision; } }
    }

    /// <summary>Window/Inspector selection shared only inside this Editor session. Explicit component
    /// bindings obtain their own context without changing this selection or the running game.</summary>
    [InitializeOnLoad]
    public static class EditorProjectSelection
    {
        private const string Prefix = "SimplyLocalize.ProjectEditor.";
        private static readonly Dictionary<string, EditorProjectContext> contexts = new(StringComparer.Ordinal);
        private static readonly Queue<string> contextOrder = new();
        private static LocalizationEntryIndex index;
        private static Dictionary<string, LocalizationEntryDefinition> entries;
        private static LocalizationProjectConfig config;
        private static bool dirty = true;
        private static string message;
        internal static long Revision { get; private set; }
        public static string Message { get { EnsureConfig(); return message; } }
        public static LocalizationProjectConfig Config { get { EnsureConfig(); return config; } }
        internal static LocalizationEntryIndex EntryIndex
        {
            get
            {
                if (Config == null) throw new InvalidOperationException(Message);
                if (index == null)
                {
                    index = config.CreateEntryIndex();
                    entries = config.Sets.SelectMany(s => s.Entries).ToDictionary(e => e.Id, StringComparer.Ordinal);
                }
                return index;
            }
        }
        internal static LocalizationEntryDefinition Entry(string id) { _ = EntryIndex; return entries[id]; }
        static EditorProjectSelection()
        {
            Undo.undoRedoPerformed += Invalidate;
            LocalizationProjectUndo.Changed += Invalidate;
            LocalizationEditorReaders.Changed += Invalidate;
        }
        public static void Select(LocalizationProjectConfig selected, string channelId = null, string setId = null)
        {
            var context = selected == null ? null : new EditorProjectContext(selected, channelId, setId);
            string path = selected == null ? "" : AssetDatabase.GetAssetPath(selected);
            if (selected != null && string.IsNullOrEmpty(path)) throw new ArgumentException("Save the project config before selecting it.");
            SessionState.SetString(Prefix + "Config", selected == null ? "" : AssetDatabase.AssetPathToGUID(path));
            SessionState.SetString(Prefix + "Channel", context?.ChannelId ?? ""); SessionState.SetString(Prefix + "Set", context?.SetId ?? "");
            Invalidate();
        }
        public static EditorProjectContext Current
        {
            get
            {
                if (Config == null) return null;
                string channel = SessionState.GetString(Prefix + "Channel", "");
                if (!config.Channels.Any(c => c.Id == channel)) channel = config.MainChannelId;
                string set = SessionState.GetString(Prefix + "Set", "");
                if (!config.Channels.Single(c => c.Id == channel).SetIds.Contains(set)) set = null;
                return GetContext(channel, set);
            }
        }
        public static EditorProjectContext GetContext(string channelId = null, string setId = null)
        {
            if (Config == null) return null;
            channelId ??= config.MainChannelId;
            if (string.IsNullOrEmpty(setId)) setId = config.Channels.FirstOrDefault(c => c.Id == channelId)?.DefaultSetId;
            string key = channelId + ":" + (setId ?? "");
            if (!contexts.TryGetValue(key, out var context))
            {
                context = new EditorProjectContext(config, channelId, setId);
                // Browsing many sets must not retain every opened asset preview for the session.
                if (contexts.Count >= 16) { string oldest = contextOrder.Dequeue(); contexts[oldest].Invalidate(); contexts.Remove(oldest); }
                contexts.Add(key, context); contextOrder.Enqueue(key);
            }
            return context;
        }
        public static void Invalidate()
        {
            Revision++; LocalizationEditorReaders.ResetResources();
            foreach (var context in contexts.Values) context.Invalidate();
            contexts.Clear(); contextOrder.Clear(); index = null; entries = null; dirty = true;
        }
        private static void EnsureConfig()
        {
            if (!dirty && (config != null || ReferenceEquals(config, null))) return;
            dirty = false; config = null; message = null;
            string selected = SessionState.GetString(Prefix + "Config", "");
            if (selected.Length > 0)
            {
                config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(AssetDatabase.GUIDToAssetPath(selected));
                if (config == null) message = "The selected project config is no longer available. Select a config in Simply Localize.";
                return;
            }
            try
            {
                LocalizationProjectAuthoring.ValidateStartupAddress(LocalizationProjectAuthoring.ConfigAssetPath);
                config = AssetDatabase.LoadAssetAtPath<LocalizationProjectConfig>(LocalizationProjectAuthoring.ConfigAssetPath);
                if (config != null) return;
                var candidates = LocalizationProjectAuthoring.FindConfigs();
                if (candidates.Count == 1) config = candidates[0];
                else message = candidates.Count > 1 ? "Select a project config in Simply Localize." : "Set up localization in Simply Localize first.";
            }
            catch (Exception error) { message = error.Message; }
        }
    }
}
