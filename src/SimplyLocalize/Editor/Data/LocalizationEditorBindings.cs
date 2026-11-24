using System;
using System.Linq;
using SimplyLocalize.Components;
using UnityEditor;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Serialized component binding commands. Validate before modifying all selected targets;
    /// Unity's normal property Undo and prefab override handling apply.</summary>
    public static class LocalizationEditorBindings
    {
        public static Type AssetType(Type componentType)
        {
            for (var type = componentType; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LocalizedAsset<>)) return type.GetGenericArguments()[0];
            return null;
        }
        public static void SetEntry(SerializedObject targets, LocalizationProjectConfig config, string channelId, string entryId)
        {
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            var reference = new LocalizationEntryReference(channelId, entryId);
            if (config.CreateEntryIndex().Resolve(reference, out var location) != LocalizationReferenceStatus.Found) throw new ArgumentException("Entry is not available in the selected channel.");
            ValidateTargets(targets);
            foreach (var target in targets.targetObjects)
                if ((AssetType(target.GetType()) == null ? LocalizationEntryKind.Text : LocalizationEntryKind.Asset) != location.Kind)
                    throw new ArgumentException("Entry kind does not match the component.");
            targets.Update(); targets.FindProperty("_key").stringValue = location.Key;
            targets.FindProperty("_channelId").stringValue = channelId; targets.FindProperty("_setId").stringValue = location.SetId;
            var entry = targets.FindProperty("_entry"); entry.FindPropertyRelative("_channelId").stringValue = channelId; entry.FindPropertyRelative("_entryId").stringValue = entryId;
            targets.ApplyModifiedProperties();
        }
        public static void ClearEntry(SerializedObject targets)
        {
            ValidateTargets(targets); targets.Update(); targets.FindProperty("_key").stringValue = "";
            targets.FindProperty("_channelId").stringValue = ""; targets.FindProperty("_setId").stringValue = "";
            var entry = targets.FindProperty("_entry"); entry.FindPropertyRelative("_channelId").stringValue = ""; entry.FindPropertyRelative("_entryId").stringValue = "";
            targets.ApplyModifiedProperties();
        }
        private static void ValidateTargets(SerializedObject targets)
        {
            if (targets == null || targets.targetObjects.Length == 0 || targets.targetObjects.Any(t => !(t is LocalizedComponentBase)))
                throw new ArgumentException("Select localization components.", nameof(targets));
        }
        public static EditorProjectContext ContextFor(LocalizedComponentBase component, out LocalizationEntryDefinition entry)
        {
            entry = null; var config = EditorProjectSelection.Config; if (config == null) return null;
            EditorProjectContext context;
            if (component.Entry.IsSet)
            {
                var status = EditorProjectSelection.EntryIndex.Resolve(component.Entry, out var location);
                if (status != LocalizationReferenceStatus.Found) throw new InvalidOperationException("Entry reference: " + status + ". Choose an available entry.");
                context = EditorProjectSelection.GetContext(component.Entry.ChannelId, location.SetId);
                entry = EditorProjectSelection.Entry(location.EntryId);
            }
            else
            {
                if (!string.IsNullOrEmpty(component.Entry.ChannelId) || !string.IsNullOrEmpty(component.Entry.EntryId)) throw new InvalidOperationException("Invalid entry identity. Choose an entry or clear the binding.");
                context = EditorProjectSelection.GetContext(string.IsNullOrEmpty(component.ChannelId) ? null : component.ChannelId, string.IsNullOrEmpty(component.SetId) ? null : component.SetId);
                entry = context.Set?.Entries.FirstOrDefault(e => e.Key == component.Key);
            }
            if (entry != null && entry.Kind != (AssetType(component.GetType()) == null ? LocalizationEntryKind.Text : LocalizationEntryKind.Asset))
                throw new InvalidOperationException("Entry kind does not match the component.");
            return context;
        }
    }
}
