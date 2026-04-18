using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Shows only keys that contain AudioClip assets, or keys with no assignments yet.
    /// </summary>
    public class AudioClipAssetFilter : IAssetTypeFilter
    {
        public string DisplayName => "Audio Clips";
        public int Order => 20;
        public Type AcceptedFieldType => typeof(AudioClip);

        public bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables)
        {
            bool anyAssigned = false;

            foreach (var table in tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;

                anyAssigned = true;
                if (asset is AudioClip) return true;
            }

            return !anyAssigned;
        }
    }
}
