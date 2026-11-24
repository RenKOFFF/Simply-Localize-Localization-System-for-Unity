using System;
using System.Collections.Generic;
using SimplyLocalize.Editor.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Window-owned unsaved entry values. Store in a serialized EditorWindow field;
    /// never save this in Assets, Resources, or a player's preferences.</summary>
    [Serializable]
    public sealed class ProjectViewDraft : IDisposable
    {
        public enum DraftKind { Text, Asset, Appearance }
        [Serializable] public sealed class Value
        {
            [SerializeField] internal string Language, Text, Destination;
            [SerializeField] internal bool Removed, HadAsset;
            [SerializeField] internal Object Asset;
        }
        [SerializeField] internal DraftKind Kind;
        [SerializeField] internal LocalizationProjectConfig Config;
        [SerializeField] internal string Channel, Set, Entry, Key, Description, Language;
        [SerializeField] internal List<Value> Values = new();
        [SerializeField] internal LocalizationDraftBaseline Baseline;
        [SerializeField] internal bool Active;
        // A separate snapshot owns this clone. Dispose when replaced or discarded, not on assembly reload.
        [SerializeField] internal LocalizationAppearance Appearance;
        [SerializeField] internal List<Object> AppearanceReferences = new();
        public bool IsPending => Active && Baseline != null;
        public void Dispose()
        { if (Appearance != null) Object.DestroyImmediate(Appearance); Appearance = null; Baseline = null; Active = false; }
        internal ProjectViewDraft Copy()
        {
            var copy = (ProjectViewDraft)MemberwiseClone(); copy.Values = Values.ConvertAll(v => new Value
                { Language = v.Language, Text = v.Text, Destination = v.Destination, Removed = v.Removed, HadAsset = v.HadAsset, Asset = v.Asset });
            if (Appearance != null)
            {
                copy.Appearance = (LocalizationAppearance)ScriptableObject.CreateInstance(Appearance.GetType());
                UnityEditor.EditorUtility.CopySerialized(Appearance, copy.Appearance); copy.Appearance.hideFlags = HideFlags.HideAndDontSave;
            }
            return copy;
        }
        internal void ValidateValues()
        {
            if (Values.Exists(v => v.HadAsset && v.Asset == null) || AppearanceReferences.Exists(o => o == null))
                throw new InvalidOperationException("An asset assigned in this draft was deleted or unloaded. The missing reference will not be saved as an empty value.");
        }
        internal void ValidateRoute(LocalizationProjectConfig config, DraftKind kind, string channel = null, string set = null)
        {
            if (Config != config || Kind != kind || channel != null && Channel != channel || set != null && Set != set)
                throw new InvalidOperationException("This draft belongs to another project, channel or content set.");
            if (!IsPending) throw new InvalidOperationException("The draft no longer has its original baselines.");
        }
    }
}
