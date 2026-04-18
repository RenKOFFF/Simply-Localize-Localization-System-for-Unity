using System;
using System.Collections.Generic;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Filters which asset keys are shown in the Assets tab, and constrains which
    /// asset types can be dragged into fields when this filter is active.
    ///
    /// Implement this interface to add filters for custom asset types.
    /// Discovery is automatic via TypeCache.
    ///
    /// Built-in implementations:
    ///   - AllAssetFilter       (shows every key, accepts any Object)
    ///   - SpriteAssetFilter    (shows sprite keys, accepts Sprite)
    ///   - AudioClipAssetFilter (shows audio keys, accepts AudioClip)
    ///
    /// Example for Material filter:
    /// <code>
    /// public class MaterialAssetFilter : IAssetTypeFilter
    /// {
    ///     public string DisplayName =&gt; "Materials";
    ///     public int Order =&gt; 30;
    ///     public Type AcceptedFieldType =&gt; typeof(Material);
    ///     public bool MatchesKey(string key, IReadOnlyDictionary&lt;string, LocalizationAssetTable&gt; tables)
    ///     {
    ///         bool anyAssigned = false;
    ///         foreach (var t in tables.Values)
    ///         {
    ///             var a = t.Get(key);
    ///             if (a == null) continue;
    ///             anyAssigned = true;
    ///             if (a is Material) return true;
    ///         }
    ///         return !anyAssigned;
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IAssetTypeFilter
    {
        /// <summary>Label shown in the filter dropdown (e.g. "Sprites", "Audio Clips").</summary>
        string DisplayName { get; }

        /// <summary>Sort order in the dropdown. Lower values appear first. "All" uses 0.</summary>
        int Order { get; }

        /// <summary>
        /// The type passed to ObjectField so Unity's asset picker shows only matching assets.
        /// Use typeof(Object) for "accept anything".
        /// </summary>
        Type AcceptedFieldType { get; }

        /// <summary>
        /// Decides whether the given key should be visible under this filter.
        /// Implementations MUST include keys that have no assigned assets in any language
        /// so newly-created keys remain visible until the user fills them in.
        /// </summary>
        bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables);
    }
}
