using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetFilters
{
    /// <summary>
    /// Discovers all IAssetTypeFilter implementations and exposes them sorted by Order.
    /// The Assets tab's filter dropdown is built from this list.
    /// </summary>
    public static class AssetTypeFilterRegistry
    {
        private static List<IAssetTypeFilter> _filters;

        /// <summary>Returns all discovered filters sorted by Order (ascending).</summary>
        public static IReadOnlyList<IAssetTypeFilter> GetAllFilters()
        {
            EnsureDiscovered();
            return _filters;
        }

        /// <summary>Forces re-discovery. Normally handled automatically by domain reload.</summary>
        public static void Invalidate() => _filters = null;

        private static void EnsureDiscovered()
        {
            if (_filters != null) return;

            _filters = new List<IAssetTypeFilter>();

            var types = TypeCache.GetTypesDerivedFrom<IAssetTypeFilter>();

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(System.Type.EmptyTypes) == null) continue;

                try
                {
                    var instance = (IAssetTypeFilter)System.Activator.CreateInstance(type);
                    _filters.Add(instance);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning(
                        $"[SimplyLocalize] Failed to instantiate asset filter '{type.FullName}': {e.Message}");
                }
            }

            _filters.Sort((a, b) => a.Order.CompareTo(b.Order));

            if (_filters.Count == 0)
            {
                // Safety net — if even the built-in AllAssetFilter is missing, inject one
                _filters.Add(new AllAssetFilter());
            }
        }
    }
}
