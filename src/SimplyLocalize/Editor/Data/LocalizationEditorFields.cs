using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Components;
using UnityEditor;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Commands for fields in project-owned scripts. These only assign serialized values;
    /// they never create translations or change the running game's language.</summary>
    public static class LocalizationEditorFields
    {
        public static EditorProjectContext StringContext => EditorProjectSelection.GetContext();
        public static IReadOnlyList<LocalizationEntryDefinition> StringEntries =>
            StringContext?.Entries(LocalizationEntryKind.Text) ?? Array.Empty<LocalizationEntryDefinition>();

        public static void SetStringKey(SerializedProperty property, string key)
        {
            if (property == null || property.propertyType != SerializedPropertyType.String) throw new ArgumentException("Expected a string field.", nameof(property));
            key ??= "";
            if (key.Length > 0 && !StringEntries.Any(e => e.Key == key)) throw new ArgumentException("Choose a registered text key from Main's default set.", nameof(key));
            property.stringValue = key; property.serializedObject.ApplyModifiedProperties();
        }

        public static LocalizationEntryKind? RequiredKind(SerializedProperty property)
        {
            if (!IsComponentBinding(property)) return null;
            var kinds = property.serializedObject.targetObjects.Select(t => LocalizationEditorBindings.AssetType(t.GetType()) == null ? LocalizationEntryKind.Text : LocalizationEntryKind.Asset).Distinct().ToArray();
            if (kinds.Length != 1) throw new ArgumentException("Selected components require different entry kinds.");
            return kinds[0];
        }

        public static void SetReference(SerializedProperty property, string channelId, string entryId)
        {
            ValidateReferenceProperty(property);
            bool empty = string.IsNullOrEmpty(channelId) && string.IsNullOrEmpty(entryId);
            var config = EditorProjectSelection.Config;
            if (!empty)
            {
                if (config == null) throw new InvalidOperationException(EditorProjectSelection.Message);
                config.ValidateOrThrow();
                var reference = new LocalizationEntryReference(channelId, entryId);
                if (config.CreateEntryIndex().Resolve(reference, out var location) != LocalizationReferenceStatus.Found) throw new ArgumentException("Entry is not available in this channel.");
                var kind = RequiredKind(property);
                if (kind.HasValue && kind.Value != location.Kind) throw new ArgumentException("Entry kind does not match the component.");
            }
            if (IsComponentBinding(property))
            {
                // Preserve pending edits to other fields before the component command refreshes its SerializedObject.
                property.serializedObject.ApplyModifiedProperties();
                if (empty) LocalizationEditorBindings.ClearEntry(property.serializedObject);
                else LocalizationEditorBindings.SetEntry(property.serializedObject, config, channelId, entryId);
                return;
            }
            property.FindPropertyRelative("_channelId").stringValue = channelId ?? "";
            property.FindPropertyRelative("_entryId").stringValue = entryId ?? "";
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>A popup must not overwrite a field changed while it was open (including array reorder).
        /// Fresh SerializedObjects avoid retaining an Inspector's disposed property across UI events.</summary>
        internal static Action<string, string> CaptureAssignment(SerializedProperty property, bool reference)
        {
            property.serializedObject.ApplyModifiedProperties();
            var targets = property.serializedObject.targetObjects.ToArray(); string path = property.propertyPath;
            var config = EditorProjectSelection.Config;
            string[] before = targets.Select(t => Fingerprint(t, path, reference)).ToArray();
            return (first, second) =>
            {
                if (EditorProjectSelection.Config != config) throw new InvalidOperationException("Editor config changed. Open the picker again.");
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] == null || Fingerprint(targets[i], path, reference) != before[i]) throw new InvalidOperationException("The field changed while the picker was open. Open it again.");
                using var serialized = new SerializedObject(targets);
                var current = serialized.FindProperty(path) ?? throw new InvalidOperationException("The selected field no longer exists.");
                if (reference) SetReference(current, first, second); else SetStringKey(current, first);
            };
        }
        private static string Fingerprint(Object target, string path, bool reference)
        {
            using var serialized = new SerializedObject(target); var property = serialized.FindProperty(path);
            if (property == null) return null;
            if (!reference) return property.stringValue;
            return property.FindPropertyRelative("_channelId").stringValue + "\n" + property.FindPropertyRelative("_entryId").stringValue;
        }
        private static bool IsComponentBinding(SerializedProperty property) => property.propertyPath == "_entry" && property.serializedObject.targetObjects.All(t => t is LocalizedComponentBase);
        private static void ValidateReferenceProperty(SerializedProperty property)
        {
            if (property == null || property.type != nameof(LocalizationEntryReference)) throw new ArgumentException("Expected a LocalizationEntryReference field.", nameof(property));
        }
    }
}
