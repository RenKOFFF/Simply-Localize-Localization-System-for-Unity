using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    /// <summary>Shared, reference-counted font variants. Never changes the source font's fallback list.</summary>
    internal static class ProfileFontCache
    {
        internal sealed class Entry
        {
            public TMP_FontAsset Source, Fallback, Font;
            public int Users;
        }

        private static readonly Dictionary<(TMP_FontAsset, TMP_FontAsset), Entry> Fonts = new();

        internal static Entry Acquire(TMP_FontAsset source, TMP_FontAsset fallback)
        {
            var key = (source, fallback);
            if (!Fonts.TryGetValue(key, out var entry))
            {
                var font = Object.Instantiate(source);
#if UNITY_6000_6_OR_NEWER
                font.name = source.name + " (SimplyLocalize " + fallback.GetEntityId() + ")";
#else
                font.name = source.name + " (SimplyLocalize " + fallback.GetInstanceID() + ")";
#endif
                font.hideFlags = HideFlags.HideAndDontSave;
                // The variant borrows atlas textures/materials. Only the source can grow its atlas.
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                // Search the source for newly added glyphs and its existing fallbacks first.
                font.fallbackFontAssetTable = new List<TMP_FontAsset> { source, fallback };
                entry = new Entry { Source = source, Fallback = fallback, Font = font };
                Fonts.Add(key, entry);
            }
            entry.Users++;
            return entry;
        }

        internal static void Release(Entry entry)
        {
            if (entry == null || --entry.Users > 0) return;
            Fonts.Remove((entry.Source, entry.Fallback));
            if (entry.Font == null) return;
            // TMP_FontAsset.OnDestroy destroys its material and atlas textures. These belong
            // to the source, so detach them before disposing only our transient font object.
            entry.Font.material = null;
            entry.Font.atlasTextures = Array.Empty<Texture2D>();
            if (Application.isPlaying) Object.Destroy(entry.Font);
            else Object.DestroyImmediate(entry.Font);
        }
    }
}
