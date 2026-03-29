using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>
    /// Shared, lazily loaded localization data for use by inspectors and other editor tools.
    /// Caches data and auto-refreshes when assets change.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorDataCache
    {
        private static EditorLocalizationData _data;
        private static LocalizationConfig _config;
        private static List<string> _sortedKeys;
        private static bool _dirty = true;

        static EditorDataCache()
        {
            EditorApplication.projectChanged += () => _dirty = true;
        }

        public static LocalizationConfig Config
        {
            get
            {
                EnsureLoaded();
                return _config;
            }
        }

        public static EditorLocalizationData Data
        {
            get
            {
                EnsureLoaded();
                return _data;
            }
        }

        /// <summary>Sorted list of all translation keys.</summary>
        public static List<string> AllKeys
        {
            get
            {
                EnsureLoaded();
                return _sortedKeys;
            }
        }

        /// <summary>Force reload on next access.</summary>
        public static void Invalidate()
        {
            _dirty = true;
        }

        private static void EnsureLoaded()
        {
            if (!_dirty && _data != null && _config != null)
                return;

            _config = FindConfig();

            if (_config == null)
            {
                _data = null;
                _sortedKeys = new List<string>();
                return;
            }

            _data = new EditorLocalizationData();
            _data.Load(_config);
            _sortedKeys = _data.AllKeys.OrderBy(k => k).ToList();
            _dirty = false;
        }

        private static LocalizationConfig FindConfig()
        {
            var guids = AssetDatabase.FindAssets("t:LocalizationConfig");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path);
            }

            return null;
        }
    }
}
