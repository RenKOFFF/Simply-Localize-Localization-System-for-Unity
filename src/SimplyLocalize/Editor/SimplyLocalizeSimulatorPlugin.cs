using UnityEditor;
using UnityEditor.DeviceSimulation;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor
{
    public sealed class SimplyLocalizeSimulatorPlugin : DeviceSimulatorPlugin
    {
        private IMGUIContainer _container;
        private double _nextRefresh;

        public override string title => "Simply Localize";

        public override void OnCreate()
        {
            EditorApplication.update -= Refresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnDestroy;
            EditorApplication.update += Refresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnDestroy;
        }

        public override VisualElement OnCreateUI()
        {
            _container?.RemoveFromHierarchy();
            _container = new IMGUIContainer(DrawGUI) { name = "SimplyLocalize_SimulatorLanguage" };
            _container.style.marginLeft = 4;
            _container.style.marginRight = 4;
            _nextRefresh = 0;
            Refresh();
            return _container;
        }

        public override void OnDestroy()
        {
            EditorApplication.update -= Refresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnDestroy;
            _container?.RemoveFromHierarchy();
            _container = null;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            _nextRefresh = 0;
            // Hide immediately on exit, regardless of the order of event subscribers.
            if (_container != null && state != PlayModeStateChange.EnteredPlayMode)
                _container.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            if (_container == null || EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.25;
            _container.style.display = GameViewLanguageDropdown.ShouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            if (GameViewLanguageDropdown.ShouldShow)
                _container.MarkDirtyRepaint();
        }

        private static void DrawGUI()
        {
            if (!GameViewLanguageDropdown.ShouldShow) return;
            if (!Localization.IsInitialized)
            {
                EditorGUILayout.HelpBox("Localization has not been initialized.", MessageType.Info);
                return;
            }

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0)
            {
                EditorGUILayout.HelpBox("No localization languages are available.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Language");
            GameViewLanguageDropdown.DrawLanguagePopup(EditorGUILayout.GetControlRect());
        }
    }
}
