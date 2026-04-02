using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    /// <summary>
    /// Per-language asset table. Stores key → asset mappings for one language.
    /// One SO file per language, placed in the language folder:
    ///   Localization/en/SpriteTable.asset
    ///   Localization/ru/SpriteTable.asset
    ///
    /// Runtime loads only the current language's table.
    /// Editor loads all for comparison/editing.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AssetTable",
        menuName = "SimplyLocalize/Asset Table")]
    public class LocalizationAssetTable : ScriptableObject
    {
        [SerializeField] private List<AssetEntry> _entries = new();

        /// <summary>
        /// Gets an asset by key, cast to T. Returns null if not found or wrong type.
        /// </summary>
        public T Get<T>(string key) where T : Object
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].key == key)
                    return _entries[i].asset as T;
            }

            return null;
        }

        /// <summary>
        /// Gets an untyped asset by key. Returns null if not found.
        /// </summary>
        public Object Get(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].key == key)
                    return _entries[i].asset;
            }

            return null;
        }

        /// <summary>
        /// Checks whether a key exists in this table.
        /// </summary>
        public bool HasKey(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].key == key)
                    return true;
            }

            return false;
        }

        /// <summary>All keys in this table.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                for (int i = 0; i < _entries.Count; i++)
                    yield return _entries[i].key;
            }
        }

        /// <summary>Number of entries.</summary>
        public int Count => _entries.Count;

        /// <summary>All entries for editor iteration.</summary>
        public IReadOnlyList<AssetEntry> Entries => _entries;

        // ──────────────────────────────────────────────
        //  Editor mutations
        // ──────────────────────────────────────────────

        /// <summary>Sets an asset for a key. Creates entry if missing.</summary>
        public void Set(string key, Object asset)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].key == key)
                {
                    _entries[i].asset = asset;
                    return;
                }
            }

            _entries.Add(new AssetEntry { key = key, asset = asset });
        }

        /// <summary>Removes a key from this table.</summary>
        public void Remove(string key)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].key == key)
                {
                    _entries.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>Renames a key, preserving the asset.</summary>
        public void RenameKey(string oldKey, string newKey)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].key == oldKey)
                {
                    _entries[i].key = newKey;
                    return;
                }
            }
        }

        /// <summary>Sorts entries alphabetically by key.</summary>
        public void Sort()
        {
            _entries.Sort((a, b) => string.Compare(a.key, b.key, StringComparison.Ordinal));
        }

        [Serializable]
        public class AssetEntry
        {
            public string key;
            public Object asset;
        }
    }
}