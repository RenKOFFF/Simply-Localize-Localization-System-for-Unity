using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor
{
    /// <summary>
    /// Draws a compact language dropdown directly on the Game View window.
    /// Attaches via GUI callback injection — no separate floating window.
    /// </summary>
    [InitializeOnLoad]
    public static class GameViewLanguageDropdown
    {
        public const string EnabledPrefKey = "SimplyLocalize_Dropdown_Enabled";
        public const string PositionPrefKey = "SimplyLocalize_Dropdown_Position";

        public enum Position { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

        private static bool _registered;
        private static Type _gameViewType;
        private static EditorWindow _gameView;

        static GameViewLanguageDropdown()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= TrackGameView;
            EditorApplication.update += TrackGameView;

            _gameViewType = typeof(UnityEditor.Editor).Assembly
                .GetType("UnityEditor.GameView");
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                _registered = false;
        }

        private static void TrackGameView()
        {
            if (!Application.isPlaying || !EditorPrefs.GetBool(EnabledPrefKey, true))
            {
                if (_registered)
                {
                    UnregisterGUI();
                    _registered = false;
                }

                return;
            }

            if (!_registered)
            {
                RegisterGUI();
                _registered = true;
            }

            if (_gameView != null)
                _gameView.Repaint();
        }

        private static void RegisterGUI()
        {
            if (_gameViewType == null) return;

            _gameView = EditorWindow.GetWindow(_gameViewType, false, null, false);

            if (_gameView != null)
            {
                // Remove first to avoid duplicates
                _gameView.rootVisualElement.UnregisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(OnGeometryChanged);

                // Use IMGUI container injected into the Game View's root
                var container = new UnityEngine.UIElements.IMGUIContainer(DrawDropdownGUI);
                container.name = "SimplyLocalize_LanguageDropdown";
                container.style.position = UnityEngine.UIElements.Position.Absolute;
                container.style.width = new UnityEngine.UIElements.Length(100, UnityEngine.UIElements.LengthUnit.Percent);
                container.style.height = new UnityEngine.UIElements.Length(100, UnityEngine.UIElements.LengthUnit.Percent);
                container.pickingMode = UnityEngine.UIElements.PickingMode.Ignore;

                // Remove any existing overlay
                RemoveExistingOverlay(_gameView);

                _gameView.rootVisualElement.Add(container);
            }
        }

        private static void UnregisterGUI()
        {
            if (_gameView != null)
                RemoveExistingOverlay(_gameView);
        }

        private static void RemoveExistingOverlay(EditorWindow window)
        {
            var existing = window.rootVisualElement.Q("SimplyLocalize_LanguageDropdown");
            existing?.RemoveFromHierarchy();
        }

        private static void OnGeometryChanged(UnityEngine.UIElements.GeometryChangedEvent evt) { }

        private static void DrawDropdownGUI()
        {
            if (!Application.isPlaying || !Localization.IsInitialized) return;

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0) return;

            var pos = (Position)EditorPrefs.GetInt(PositionPrefKey, (int)Position.TopRight);

            float dropdownWidth = 140;
            float dropdownHeight = 20;
            float padding = 6;

            // Get the visible rect
            Rect viewRect = new Rect(0, 0, Screen.width, Screen.height);

            float x, y;

            switch (pos)
            {
                case Position.TopLeft:
                    x = padding;
                    y = padding;
                    break;
                case Position.TopCenter:
                    x = (viewRect.width - dropdownWidth) / 2;
                    y = padding;
                    break;
                case Position.BottomLeft:
                    x = padding;
                    y = viewRect.height - dropdownHeight - padding - 20;
                    break;
                case Position.BottomCenter:
                    x = (viewRect.width - dropdownWidth) / 2;
                    y = viewRect.height - dropdownHeight - padding - 20;
                    break;
                case Position.BottomRight:
                    x = viewRect.width - dropdownWidth - padding;
                    y = viewRect.height - dropdownHeight - padding - 20;
                    break;
                default: // TopRight
                    x = viewRect.width - dropdownWidth - padding;
                    y = padding;
                    break;
            }

            // Build names array
            var names = new string[languages.Count];
            int currentIndex = 0;

            for (int i = 0; i < languages.Count; i++)
            {
                var p = languages[i];
                names[i] = p != null ? $"{p.displayName}" : "(null)";

                if (p != null && Localization.CurrentLanguage == p.Code)
                    currentIndex = i;
            }

            // Draw background
            var bgRect = new Rect(x - 4, y - 2, dropdownWidth + 8, dropdownHeight + 4);
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Draw popup
            var popupRect = new Rect(x, y, dropdownWidth, dropdownHeight);

            var style = new GUIStyle(EditorStyles.popup)
            {
                fontSize = 11,
                fixedHeight = dropdownHeight
            };

            int newIndex = EditorGUI.Popup(popupRect, currentIndex, names, style);

            if (newIndex != currentIndex && languages[newIndex] != null)
                Localization.SetLanguage(languages[newIndex]);
        }
    }
}
