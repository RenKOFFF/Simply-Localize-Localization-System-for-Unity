using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Shows only keys that contain Sprite assets, or keys with no assignments yet
    /// (so freshly-added keys remain visible until the user fills them in).
    /// </summary>
    public class SpriteAssetFilter : IAssetTypeFilter
    {
        public string DisplayName => "Sprites";
        public int Order => 10;
        public Type AcceptedFieldType => typeof(Sprite);

        public bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables)
        {
            bool anyAssigned = false;

            foreach (var table in tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;

                anyAssigned = true;
                if (asset is Sprite) return true;
            }

            return !anyAssigned;
        }
    }
}
