using System;
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

        private static bool _registered;
        private static Type _gameViewType;
        private static EditorWindow _gameView;

        private const float DropdownWidth = 150;
        private const float DropdownHeight = 20;
        private const float Padding = 6;
        private const float TopOffset = 22; // Clear the Game View toolbar

        static GameViewLanguageDropdown()
        {
            _gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Tick;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                RemoveOverlay();
                _registered = false;
            }
        }

        private static void Tick()
        {
            bool shouldShow = Application.isPlaying && EditorPrefs.GetBool(EnabledPrefKey, true);

            if (shouldShow && !_registered)
            {
                InjectOverlay();
                _registered = true;
            }
            else if (!shouldShow && _registered)
            {
                RemoveOverlay();
                _registered = false;
            }

            if (_registered && _gameView != null)
                _gameView.Repaint();
        }

        private static void InjectOverlay()
        {
            if (_gameViewType == null) return;
            _gameView = EditorWindow.GetWindow(_gameViewType, false, null, false);
            if (_gameView == null) return;

            RemoveOverlay();

            var pos = (Position)EditorPrefs.GetInt(PositionPrefKey, (int)Position.Right);

            var container = new IMGUIContainer(DrawDropdown);
            container.name = "SimplyLocalize_LangDropdown";
            container.style.position = UnityEngine.UIElements.Position.Absolute;
            container.style.width = DropdownWidth + Padding * 2;
            container.style.height = DropdownHeight + Padding * 2;
            container.style.top = TopOffset;

            switch (pos)
            {
                case Position.Left:
                    container.style.left = 0;
                    break;
                case Position.Center:
                    container.style.alignSelf = Align.Center;
                    container.style.left = new StyleLength(StyleKeyword.Auto);
                    container.style.right = new StyleLength(StyleKeyword.Auto);
                    break;
                default:
                    container.style.right = 0;
                    break;
            }

            _gameView.rootVisualElement.Add(container);
        }

        private static void RemoveOverlay()
        {
            if (_gameView == null) return;
            _gameView.rootVisualElement.Q("SimplyLocalize_LangDropdown")?.RemoveFromHierarchy();
        }

        private static void DrawDropdown()
        {
            if (!Application.isPlaying || !Localization.IsInitialized) return;

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0) return;

            var names = new string[languages.Count];
            int currentIndex = 0;

            for (int i = 0; i < languages.Count; i++)
            {
                var p = languages[i];
                names[i] = p != null ? p.displayName : "(null)";
                if (p != null && Localization.CurrentLanguage == p.Code)
                    currentIndex = i;
            }

            var bgRect = new Rect(0, 0, DropdownWidth + Padding * 2, DropdownHeight + Padding * 2);
            EditorGUI.DrawRect(bgRect, new Color(0, 0, 0, 0.35f));

            var rect = new Rect(Padding, Padding, DropdownWidth, DropdownHeight);
            int newIndex = EditorGUI.Popup(rect, currentIndex, names);

            if (newIndex != currentIndex && languages[newIndex] != null)
                Localization.SetLanguage(languages[newIndex]);
        }
    }
}