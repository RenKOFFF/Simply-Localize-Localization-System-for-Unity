using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public static class GameViewLanguageDropdown
    {
        public const string EnabledPrefKey = "SimplyLocalize_Dropdown_Enabled";
        public const string PositionPrefKey = "SimplyLocalize_Dropdown_Position";

        public enum Position { Left, Center, Right }

        private const string OverlayName = "SimplyLocalize_LangDropdown";
        private const double RefreshInterval = 0.25;
        private static readonly Type GameViewType;
        private static readonly Dictionary<EditorWindow, VisualElement> Overlays =
            new Dictionary<EditorWindow, VisualElement>();
        private static readonly List<EditorWindow> StaleWindows = new List<EditorWindow>();
        private static bool _inPlayMode;
        private static double _nextRefresh;

        internal static bool ShouldShow => _inPlayMode && Application.isPlaying &&
            EditorApplication.isPlayingOrWillChangePlaymode && EditorPrefs.GetBool(EnabledPrefKey, true);

        private const float DropdownWidth = 150;
        private const float DropdownHeight = 20;
        private const float Padding = 6;
        private const float TopOffset = 22; // Clear the Game View toolbar

        static GameViewLanguageDropdown()
        {
            GameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            _inPlayMode = EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            _inPlayMode = state == PlayModeStateChange.EnteredPlayMode;
            _nextRefresh = 0;
            if (!_inPlayMode)
                RemoveOverlays();
        }

        private static void Tick()
        {
            if (!ShouldShow)
            {
                if (Overlays.Count > 0)
                    RemoveOverlays();
                _nextRefresh = 0;
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            if (GameViewType == null) return;

            StaleWindows.Clear();
            foreach (var entry in Overlays)
            {
                if (entry.Key == null || entry.Value.parent != entry.Key.rootVisualElement)
                {
                    entry.Value.RemoveFromHierarchy();
                    StaleWindows.Add(entry.Key);
                }
            }
            foreach (var window in StaleWindows)
                Overlays.Remove(window);
            StaleWindows.Clear();

            // GetWindow would create and focus a window; discovery must have no such side effects.
            foreach (var obj in Resources.FindObjectsOfTypeAll(GameViewType))
            {
                var window = obj as EditorWindow;
                if (window == null) continue;
                EnsureOverlay(window);
            }
        }

        private static void EnsureOverlay(EditorWindow window)
        {
            var root = window.rootVisualElement;
            var existing = root.Query<VisualElement>(OverlayName).ToList();
            if (!Overlays.TryGetValue(window, out var overlay) || overlay.parent != root ||
                existing.Count != 1 || existing[0] != overlay || overlay.Q<IMGUIContainer>() == null)
            {
                overlay?.RemoveFromHierarchy();
                RemoveElements(root);
                overlay = new VisualElement { name = OverlayName, pickingMode = PickingMode.Ignore };
                overlay.style.position = UnityEngine.UIElements.Position.Absolute;
                overlay.style.left = 0;
                overlay.style.right = 0;
                overlay.style.top = TopOffset;
                overlay.style.flexDirection = FlexDirection.Row;

                var container = new IMGUIContainer(DrawDropdown);
                container.style.width = DropdownWidth + Padding * 2;
                container.style.height = DropdownHeight + Padding * 2;
                container.style.flexShrink = 0;
                overlay.Add(container);
                root.Add(overlay);
                Overlays[window] = overlay;
            }

            var pos = (Position)EditorPrefs.GetInt(PositionPrefKey, (int)Position.Right);
            overlay.style.justifyContent = pos == Position.Left ? Justify.FlexStart :
                pos == Position.Center ? Justify.Center : Justify.FlexEnd;
            overlay.Q<IMGUIContainer>().MarkDirtyRepaint();
        }

        private static void RemoveElements(VisualElement root)
        {
            foreach (var element in root.Query<VisualElement>(OverlayName).ToList())
                element.RemoveFromHierarchy();
        }

        private static void RemoveOverlays()
        {
            foreach (var overlay in Overlays.Values)
                overlay.RemoveFromHierarchy();
            Overlays.Clear();
            StaleWindows.Clear();

            // Include untracked elements left by a rebuilt visual tree or an earlier reload.
            if (GameViewType == null) return;
            foreach (var obj in Resources.FindObjectsOfTypeAll(GameViewType))
                if (obj is EditorWindow window && window != null)
                    RemoveElements(window.rootVisualElement);
        }

        private static void BeforeAssemblyReload()
        {
            RemoveOverlays();
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        }

        private static void DrawDropdown()
        {
            if (!ShouldShow || !Localization.IsInitialized) return;

            var bgRect = new Rect(0, 0, DropdownWidth + Padding * 2, DropdownHeight + Padding * 2);
            EditorGUI.DrawRect(bgRect, new Color(0, 0, 0, 0.35f));
            DrawLanguagePopup(new Rect(Padding, Padding, DropdownWidth, DropdownHeight));
        }

        internal static void DrawLanguagePopup(Rect rect)
        {
            if (!ShouldShow || !Localization.IsInitialized) return;

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0) return;

            var names = new string[languages.Count];
            int currentIndex = -1;

            for (int i = 0; i < languages.Count; i++)
            {
                var p = languages[i];
                names[i] = p != null ? p.displayName : "(null)";
                if (p != null && Localization.CurrentLanguage == p.Code)
                    currentIndex = i;
            }

            int newIndex = EditorGUI.Popup(rect, currentIndex, names);

            if (newIndex != currentIndex && newIndex >= 0 && newIndex < languages.Count && languages[newIndex] != null)
                Localization.SetLanguage(languages[newIndex]);
        }
    }
}
