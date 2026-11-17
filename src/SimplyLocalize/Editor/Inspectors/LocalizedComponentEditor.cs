using System;
using System.Linq;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.AssetPreviews;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Inspectors
{
    /// <summary>Common binding UI for text and asset consumers. Content authoring stays in the main window.</summary>
    public abstract class LocalizedComponentEditor : UnityEditor.Editor
    {
        private string channel, set, fingerprint, error;
        private bool showPreview, showIdentity, showLookup;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var component = (LocalizedComponentBase)target;
            var config = EditorProjectSelection.Config;
            var selectedConfig = (LocalizationProjectConfig)EditorGUILayout.ObjectField(new GUIContent("Editor config", "Shared Editor preview selection. Does not change the game's runtime."), config, typeof(LocalizationProjectConfig), false);
            if (selectedConfig != config) { Try(() => EditorProjectSelection.Select(selectedConfig)); fingerprint = null; config = EditorProjectSelection.Config; }
            if (config == null) EditorGUILayout.HelpBox(EditorProjectSelection.Message ?? "Set up localization in Simply Localize.", MessageType.Info);
            EditorProjectContext bound = null; LocalizationEntryDefinition entry = null;
            if (config != null)
            {
                try { bound = LocalizationEditorBindings.ContextFor(component, out entry); }
                catch (Exception failure) { EditorGUILayout.HelpBox(failure.Message, MessageType.Warning); }
                try { DrawBinding(component, config, bound, entry); }
                catch (Exception failure) { EditorGUILayout.HelpBox(failure.Message, MessageType.Error); }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            DrawComponentProperties();
            var parameters = serializedObject.FindProperty("_defaultNamedParams");
            if (parameters != null)
            {
                EditorGUILayout.PropertyField(parameters, new GUIContent("Parameters"), true);
                using (new EditorGUI.DisabledScope(bound == null || entry == null || targets.Length != 1))
                    if (GUILayout.Button("Add parameters from default template"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        Try(() => { if (bound.Text.TryResolve(entry.Id, bound.Channel.DefaultLanguage, out var cell)) LocalizationEditorParameters.AddMissing(serializedObject, cell.Value); });
                    }
            }
            showPreview = EditorGUILayout.Foldout(showPreview, "Saved translations preview", true);
            if (showPreview && bound != null && entry != null)
            {
                if (targets.Length != 1) EditorGUILayout.HelpBox("Preview shows the first selected component.", MessageType.Info);
                if (GUILayout.Button("Reload preview from disk")) EditorProjectSelection.Invalidate();
                try { DrawPreview(bound, entry, parameters); }
                catch (Exception failure) { EditorGUILayout.HelpBox(failure.Message, MessageType.Error); }
            }
            showIdentity = EditorGUILayout.Foldout(showIdentity, "Stored identity", true);
            if (showIdentity)
                using (new EditorGUI.DisabledScope(true))
                {
                    var reference = serializedObject.FindProperty("_entry");
                    EditorGUILayout.PropertyField(reference.FindPropertyRelative("_channelId"), new GUIContent("Channel ID"));
                    EditorGUILayout.PropertyField(reference.FindPropertyRelative("_entryId"), new GUIContent("Entry ID"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_key"), new GUIContent("Stored key"));
                }
            showLookup = EditorGUILayout.Foldout(showLookup, "String lookup", true);
            if (showLookup)
            {
                EditorGUILayout.HelpBox("An entry ID takes priority. String lookup uses the channel's default set when Set ID is empty.", MessageType.Info);
                if (component.Entry.IsSet && GUILayout.Button("Use string lookup instead"))
                {
                    var reference = serializedObject.FindProperty("_entry"); reference.FindPropertyRelative("_channelId").stringValue = ""; reference.FindPropertyRelative("_entryId").stringValue = "";
                }
                using (new EditorGUI.DisabledScope(component.Entry.IsSet))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_key"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_channelId"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_setId"));
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
        /// <summary>Override to draw integration-specific settings without duplicating the common binding UI.</summary>
        protected virtual void DrawComponentProperties() =>
            DrawPropertiesExcluding(serializedObject, "m_Script", "_entry", "_key", "_channelId", "_setId", "_defaultNamedParams");

        private void DrawBinding(LocalizedComponentBase component, LocalizationProjectConfig config, EditorProjectContext bound, LocalizationEntryDefinition entry)
        {
            _ = EditorProjectSelection.EntryIndex;
            string current = config.GetInstanceID() + ":" + component.Entry.ChannelId + ":" + component.Entry.EntryId + ":" + component.ChannelId + ":" + component.SetId + ":" + component.Key;
            if (current != fingerprint) { fingerprint = current; channel = bound?.ChannelId ?? config.MainChannelId; set = bound?.SetId; }
            var channels = config.Channels.ToArray(); int c = Array.FindIndex(channels, x => x.Id == channel); if (c < 0) c = 0;
            int next = EditorGUILayout.Popup("Channel", c, channels.Select(x => x.Name).ToArray());
            if (next != c) set = null; channel = channels[next].Id;
            var sets = config.Sets.Where(s => channels[next].SetIds.Contains(s.Id)).ToArray();
            if (sets.Length > 0)
            {
                int s = Array.FindIndex(sets, x => x.Id == set); if (s < 0) s = Math.Max(0, Array.FindIndex(sets, x => x.Id == channels[next].DefaultSetId));
                set = sets[EditorGUILayout.Popup("Content set", s, sets.Select(x => x.Name).ToArray())].Id;
                bool same = bound != null && channel == bound.ChannelId && set == bound.SetId;
                if (!same && entry != null) EditorGUILayout.HelpBox("Choose an entry to change the component's binding.", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Entry");
                    string display = serializedObject.FindProperty("_entry").hasMultipleDifferentValues ? "Mixed entries" : same && entry != null ? entry.Key : "Choose entry";
                    if (GUILayout.Button(display, EditorStyles.popup))
                    {
                        var choices = EditorProjectSelection.GetContext(channel, set).Entries(LocalizationEditorBindings.AssetType(component.GetType()) == null ? LocalizationEntryKind.Text : LocalizationEntryKind.Asset);
                        var search = CreateInstance<KeySearchWindow>(); var entries = choices.ToArray(); string selectedChannel = channel;
                        search.Init(entries.Select(e => e.Key).ToList(), key => Try(() =>
                        {
                            if (EditorProjectSelection.Config != config) throw new InvalidOperationException("Editor config changed. Open the entry picker again.");
                            if (string.IsNullOrEmpty(key)) LocalizationEditorBindings.ClearEntry(serializedObject);
                            else LocalizationEditorBindings.SetEntry(serializedObject, config, selectedChannel, entries.Single(e => e.Key == key).Id);
                            fingerprint = null; Repaint();
                        }));
                        SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)), search);
                    }
                }
                if (entry == null && !string.IsNullOrEmpty(component.Key)) EditorGUILayout.HelpBox("Key is not registered in this channel/set. Add it in Simply Localize or choose an existing entry.", MessageType.Warning);
            }
            else EditorGUILayout.HelpBox("Connect a content set to this channel in Simply Localize.", MessageType.Info);
            if (GUILayout.Button("Clear binding")) { LocalizationEditorBindings.ClearEntry(serializedObject); fingerprint = null; }
        }
        private void DrawPreview(EditorProjectContext context, LocalizationEntryDefinition entry, SerializedProperty parameters)
        {
            foreach (string language in context.Channel.Languages)
                if (entry.Kind == LocalizationEntryKind.Text)
                {
                    bool found = context.Text.TryResolve(entry.Id, language, out var cell);
                    string value = found ? cell.Value : "(missing)";
                    if (found && parameters != null) value = LocalizationEditorParameters.Format(value, cell.Language, parameters);
                    string caption = language + (found && cell.Language != language ? " → " + cell.Language : "");
                    EditorGUILayout.LabelField(caption, value, EditorStyles.wordWrappedLabel);
                }
                else
                {
                    Type type = LocalizationEditorBindings.AssetType(target.GetType()) ?? typeof(Object);
                    bool found = context.Assets.TryResolve(entry.Id, language, out var cell, type);
                    string caption = language + (found && cell.Language != language ? " → " + cell.Language : "");
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(caption, found ? cell.Asset : null, type, false);
                    if (found) AssetPreviewRegistry.GetRendererFor(cell.Asset).DrawPreview(GUILayoutUtility.GetRect(80, 100, GUILayout.ExpandWidth(true)), cell.Asset);
                }
            if (parameters != null) EditorGUILayout.HelpBox("Built-in formatting preview, using the resolved translation's language. Runtime parameter values may differ.", MessageType.None);
        }
        private void Try(Action operation) { try { operation(); error = null; } catch (Exception failure) { error = failure.Message; } }
    }
}
