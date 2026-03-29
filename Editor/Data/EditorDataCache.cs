using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>
    /// Shared lazily loaded localization data for inspectors and editor tools.
    /// Auto-invalidates on asset changes, undo/redo, and manual calls.
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
            EditorApplication.projectChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
            AssemblyReloadEvents.afterAssemblyReload += MarkDirty;
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

        public static List<string> AllKeys
        {
            get
            {
                EnsureLoaded();
                return _sortedKeys;
            }
        }

        public static void Invalidate() => MarkDirty();

        private static void MarkDirty()
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
                _dirty = false;
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
