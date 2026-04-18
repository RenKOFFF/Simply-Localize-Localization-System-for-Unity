using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Discovers all IAssetPreviewRenderer implementations in the editor domain
    /// and dispatches preview requests to the best match.
    ///
    /// Discovery happens lazily on first access, via TypeCache — no manual registration.
    /// Renderers are sorted by Priority (descending), so higher-priority user implementations
    /// automatically win over the built-in ones.
    /// </summary>
    public static class AssetPreviewRegistry
    {
        private static List<IAssetPreviewRenderer> _renderers;
        private static IAssetPreviewRenderer _fallback;

        /// <summary>
        /// Returns the highest-priority renderer that can handle the given asset,
        /// or the fallback renderer if no specialized one matches.
        /// Never returns null.
        /// </summary>
        public static IAssetPreviewRenderer GetRendererFor(Object asset)
        {
            EnsureDiscovered();

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i].CanRender(asset))
                    return _renderers[i];
            }

            return _fallback;
        }

        /// <summary>
        /// Forces re-discovery. Call after domain reload or when hot-adding types.
        /// Normally not needed — discovery is automatic on first access and on domain reload.
        /// </summary>
        public static void Invalidate()
        {
            _renderers = null;
            _fallback = null;
        }

        private static void EnsureDiscovered()
        {
            if (_renderers != null) return;

            _renderers = new List<IAssetPreviewRenderer>();
            IAssetPreviewRenderer fallbackCandidate = null;

            var types = TypeCache.GetTypesDerivedFrom<IAssetPreviewRenderer>();

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(System.Type.EmptyTypes) == null) continue;

                try
                {
                    var instance = (IAssetPreviewRenderer)System.Activator.CreateInstance(type);

                    // The DefaultAssetPreviewRenderer is stored separately as the fallback.
                    if (instance is DefaultAssetPreviewRenderer)
                    {
                        fallbackCandidate = instance;
                        continue;
                    }

                    _renderers.Add(instance);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning(
                        $"[SimplyLocalize] Failed to instantiate preview renderer '{type.FullName}': {e.Message}");
                }
            }

            // Higher priority first
            _renderers.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            _fallback = fallbackCandidate ?? new DefaultAssetPreviewRenderer();
        }
    }
}
