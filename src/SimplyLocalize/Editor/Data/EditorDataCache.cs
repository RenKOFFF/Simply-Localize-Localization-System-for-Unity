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
        private static LocalizationConfig _selectedConfig;
        private static string _selectionMessage;

        /// <summary>Explicit editor context, shared by the window and inspectors; does not change runtime startup.</summary>
        public static void SelectConfig(LocalizationConfig config)
        {
            if (ReferenceEquals(_selectedConfig, config)) return;
            _selectedConfig = config; MarkDirty();
        }

        public static string SelectionMessage { get { EnsureLoaded(); return _selectionMessage; } }

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
            if (!_dirty && (_config != null || ReferenceEquals(_config, null)))
                return;

            _selectionMessage = null;
            _config = _selectedConfig != null ? _selectedConfig : EditorConfigSelection.Find(out _selectionMessage);

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

    }
}
