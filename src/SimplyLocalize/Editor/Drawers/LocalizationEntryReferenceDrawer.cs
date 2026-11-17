using System;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomPropertyDrawer(typeof(LocalizationEntryReference))]
    public sealed class LocalizationEntryReferenceDrawer : PropertyDrawer
    {
        private static float Step => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => (property.isExpanded ? 4 : 2) * Step - EditorGUIUtility.standardVerticalSpacing;
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using var scope = new EditorGUI.PropertyScope(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var channel = property.FindPropertyRelative("_channelId"); var entry = property.FindPropertyRelative("_entryId");
            bool mixed = EditorGUI.showMixedValue; EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            string display = "Choose entry", message = "Choose a channel, content set and entry. Stored IDs survive renames.";
            var config = EditorProjectSelection.Config;
            try
            {
                if (property.hasMultipleDifferentValues) display = "Mixed entries";
                else if (!string.IsNullOrEmpty(channel.stringValue) || !string.IsNullOrEmpty(entry.stringValue))
                {
                    display = "Unavailable entry";
                    if (config != null)
                    {
                        LocalizationEntryReference reference;
                        try { reference = new LocalizationEntryReference(channel.stringValue, entry.stringValue); }
                        catch (ArgumentException) { reference = default; }
                        var status = EditorProjectSelection.EntryIndex.Resolve(reference, out var location);
                        if (status == LocalizationReferenceStatus.Found)
                        {
                            var context = EditorProjectSelection.GetContext(channel.stringValue, location.SetId);
                            display = location.Key; message = context.Channel.Name + " / " + context.Set.Name + " · " + location.Kind;
                            var kind = LocalizationEditorFields.RequiredKind(property);
                            if (kind.HasValue && kind.Value != location.Kind) message = "Entry kind does not match this component. Choose another entry.";
                        }
                        else message = reference.IsSet ? "Entry reference: " + status + ". Choose an entry or None to clear it." : "Invalid identity. Choose an entry or None to clear it.";
                    }
                }
                if (config == null) message = EditorProjectSelection.Message;
            }
            catch (Exception failure) { message = failure.Message; }
            try
            {
                var foldout = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
                property.isExpanded = EditorGUI.Foldout(foldout, property.isExpanded, label, true);
                var button = new Rect(foldout.xMax + 2, row.y, Math.Max(0, row.xMax - foldout.xMax - 2), row.height);
                if (GUI.Button(button, new GUIContent(display, message), EditorStyles.popup))
                {
                    var assign = LocalizationEditorFields.CaptureAssignment(property, true);
                    var picker = ScriptableObject.CreateInstance<LocalizationReferenceSearchWindow>();
                    picker.Init(config, LocalizationEditorFields.RequiredKind(property), (c, e) =>
                    { try { assign(c, e); } catch (Exception failure) { EditorUtility.DisplayDialog("Simply Localize", failure.Message, "OK"); } });
                    SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)), picker);
                }
            }
            catch (Exception failure) { message = failure.Message; }
            finally { EditorGUI.showMixedValue = mixed; }
            row.y += Step; EditorGUI.LabelField(row, new GUIContent(message, message), EditorStyles.miniLabel);
            if (property.isExpanded)
                using (new EditorGUI.DisabledScope(true))
                {
                    row.y += Step; EditorGUI.PropertyField(row, channel, new GUIContent("Channel ID"));
                    row.y += Step; EditorGUI.PropertyField(row, entry, new GUIContent("Entry ID"));
                }
        }
    }
}
