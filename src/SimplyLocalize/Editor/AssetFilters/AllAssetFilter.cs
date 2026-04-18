using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Built-in filter that shows every key regardless of its asset type.
    /// Accepts any UnityEngine.Object in fields.
    /// </summary>
    public class AllAssetFilter : IAssetTypeFilter
    {
        public string DisplayName => "All";
        public int Order => 0;
        public Type AcceptedFieldType => typeof(UnityEngine.Object);

        public bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables)
        {
            return true;
        }
    }
}
