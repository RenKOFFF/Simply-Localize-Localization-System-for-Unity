using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Discovers all IAssetTypeFilter implementations and auto-generates filters
    /// for asset types found in the localization tables.
    ///
    /// Custom IAssetTypeFilter implementations (e.g. SpriteAssetFilter) provide nice display names
    /// and override auto-generated filters for matching types.
    /// Types without a custom filter get one auto-generated via ObjectNames.NicifyVariableName.
    /// </summary>
    public static class AssetTypeFilterRegistry
    {
        private static List<IAssetTypeFilter> _customFilters;
        private static List<IAssetTypeFilter> _allFilters;

        /// <summary>
        /// Returns the current filter list. If RefreshFromTables hasn't been called yet,
        /// returns only custom filters discovered via TypeCache.
        /// </summary>
        public static IReadOnlyList<IAssetTypeFilter> GetAllFilters()
        {
            if (_allFilters == null)
            {
                EnsureCustomFiltersDiscovered();
                _allFilters = new List<IAssetTypeFilter>(_customFilters);

                if (_allFilters.Count == 0)
                    _allFilters.Add(new AllAssetFilter());
            }

            return _allFilters;
        }

        /// <summary>
        /// Rebuilds the filter list based on actual asset types found in the given tables.
        /// Custom IAssetTypeFilter implementations override auto-generated ones for matching types.
        /// Call this whenever tables change (tab build, project refresh, etc).
        /// </summary>
        public static IReadOnlyList<IAssetTypeFilter> RefreshFromTables(
            IReadOnlyDictionary<string, LocalizationAssetTable> tables)
        {
            EnsureCustomFiltersDiscovered();

            // Collect all distinct types from all tables
            var types = new HashSet<Type>();

            if (tables != null)
            {
                foreach (var table in tables.Values)
                {
                    if (table == null) continue;
                    types.UnionWith(table.GetContainedTypes());
                }
            }

            // Build a lookup: AcceptedFieldType → custom filter
            var customByType = new Dictionary<Type, IAssetTypeFilter>();

            foreach (var cf in _customFilters)
            {
                if (cf is AllAssetFilter) continue;
                if (cf.AcceptedFieldType != null && cf.AcceptedFieldType != typeof(UnityEngine.Object))
                    customByType[cf.AcceptedFieldType] = cf;
            }

            // Build final list
            _allFilters = new List<IAssetTypeFilter>();

            // "All" filter always first
            var allFilter = _customFilters.FirstOrDefault(f => f is AllAssetFilter)
                            ?? new AllAssetFilter();
            _allFilters.Add(allFilter);

            // For each discovered type: prefer custom filter, otherwise auto-generate
            int autoOrder = 10;

            foreach (var type in types.OrderBy(t => t.Name))
            {
                if (customByType.TryGetValue(type, out var custom))
                {
                    _allFilters.Add(custom);
                }
                else
                {
                    string displayName = ObjectNames.NicifyVariableName(type.Name);
                    _allFilters.Add(new AutoAssetTypeFilter(type, displayName, autoOrder));
                }

                autoOrder += 10;
            }

            return _allFilters;
        }

        /// <summary>Forces re-discovery on next access.</summary>
        public static void Invalidate()
        {
            _customFilters = null;
            _allFilters = null;
        }

        private static void EnsureCustomFiltersDiscovered()
        {
            if (_customFilters != null) return;

            _customFilters = new List<IAssetTypeFilter>();

            var types = TypeCache.GetTypesDerivedFrom<IAssetTypeFilter>();

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type == typeof(AutoAssetTypeFilter)) continue; // skip our internal class
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    var instance = (IAssetTypeFilter)Activator.CreateInstance(type);
                    _customFilters.Add(instance);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[SimplyLocalize] Failed to instantiate asset filter '{type.FullName}': {e.Message}");
                }
            }

            _customFilters.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }

    /// <summary>
    /// Auto-generated filter for a type discovered from asset tables at runtime.
    /// Used when no custom IAssetTypeFilter exists for a given asset type.
    /// </summary>
    internal class AutoAssetTypeFilter : IAssetTypeFilter
    {
        public string DisplayName { get; }
        public int Order { get; }
        public Type AcceptedFieldType { get; }

        public AutoAssetTypeFilter(Type type, string displayName, int order)
        {
            AcceptedFieldType = type;
            DisplayName = displayName;
            Order = order;
        }

        public bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables)
        {
            bool anyAssigned = false;

            foreach (var table in tables.Values)
            {
                if (table == null) continue;
                var asset = table.Get(key);
                if (asset == null) continue;

                anyAssigned = true;
                if (AcceptedFieldType.IsInstanceOfType(asset)) return true;
            }

            // Unassigned keys visible under every filter
            return !anyAssigned;
        }
    }
}
