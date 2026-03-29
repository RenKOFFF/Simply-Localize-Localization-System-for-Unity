using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    /// <summary>
    /// Shows a language switcher dropdown overlay in the Game View during Play Mode.
    /// Position is configurable: Left, Right, or Center.
    ///
    /// Enable/disable via Window → SimplyLocalize → Toggle Game View Dropdown.
    /// </summary>
    [InitializeOnLoad]
    public static class GameViewLanguageDropdown
    {
        private const string EnabledPrefKey = "SimplyLocalize_GameViewDropdown_Enabled";
        private const string PositionPrefKey = "SimplyLocalize_GameViewDropdown_Position";

        public enum DropdownPosition { Left, Center, Right }

        private static bool _enabled;
        private static DropdownPosition _position;
        private static bool _isExpanded;

        static GameViewLanguageDropdown()
        {
            _enabled = EditorPrefs.GetBool(EnabledPrefKey, true);
            _position = (DropdownPosition)EditorPrefs.GetInt(PositionPrefKey, (int)DropdownPosition.Right);

#if UNITY_2021_1_OR_NEWER
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
        }

        [MenuItem("Window/SimplyLocalize/Toggle Game View Dropdown")]
        public static void ToggleEnabled()
        {
            _enabled = !_enabled;
            EditorPrefs.SetBool(EnabledPrefKey, _enabled);
        }

        [MenuItem("Window/SimplyLocalize/Dropdown Position/Left")]
        private static void SetPositionLeft() => SetPosition(DropdownPosition.Left);

        [MenuItem("Window/SimplyLocalize/Dropdown Position/Center")]
        private static void SetPositionCenter() => SetPosition(DropdownPosition.Center);

        [MenuItem("Window/SimplyLocalize/Dropdown Position/Right")]
        private static void SetPositionRight() => SetPosition(DropdownPosition.Right);

        private static void SetPosition(DropdownPosition pos)
        {
            _position = pos;
            EditorPrefs.SetInt(PositionPrefKey, (int)pos);
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.update += RepaintGameView;
            else if (state == PlayModeStateChange.ExitingPlayMode)
                EditorApplication.update -= RepaintGameView;
        }

        private static double _lastRepaint;

        private static void RepaintGameView()
        {
            if (!_enabled || !Application.isPlaying) return;

            // Throttle repaints
            if (EditorApplication.timeSinceStartup - _lastRepaint < 0.5) return;

            _lastRepaint = EditorApplication.timeSinceStartup;

            // Force repaint of Game View by hooking into GUI
            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");

            if (gameViewType != null)
            {
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                gameView?.Repaint();
            }
        }

        /// <summary>
        /// Draw the dropdown. Call this from a custom OnGUI or use the PlayModeOverlay approach.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterOverlay()
        {
#if UNITY_2021_1_OR_NEWER
            EditorApplication.update -= CheckAndDrawOverlay;
            EditorApplication.update += CheckAndDrawOverlay;
#endif
        }

        private static void CheckAndDrawOverlay()
        {
            // The overlay is drawn via HandleUtility in Play mode
            // We use a simpler approach: a floating EditorWindow
        }

        /// <summary>
        /// Draws the language dropdown as an IMGUI overlay.
        /// Can be called from any OnGUI context (e.g. a custom EditorWindow overlay).
        /// </summary>
        public static void DrawDropdown(Rect viewRect)
        {
            if (!_enabled || !Application.isPlaying || !Localization.IsInitialized)
                return;

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0) return;

            float buttonWidth = _isExpanded ? 120 : 100;
            float buttonHeight = 22;
            float padding = 8;
            float totalHeight = _isExpanded
                ? buttonHeight + (languages.Count * (buttonHeight + 2)) + padding
                : buttonHeight + padding;

            // Position based on setting
            float x;

            switch (_position)
            {
                case DropdownPosition.Left:
                    x = padding;
                    break;
                case DropdownPosition.Center:
                    x = (viewRect.width - buttonWidth) / 2;
                    break;
                default: // Right
                    x = viewRect.width - buttonWidth - padding;
                    break;
            }

            float y = padding;

            // Main button
            string currentLang = Localization.CurrentProfile != null
                ? $"{Localization.CurrentProfile.displayName}"
                : Localization.CurrentLanguage ?? "?";

            var mainRect = new Rect(x, y, buttonWidth, buttonHeight);

            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(mainRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = Color.white }
            };

            if (GUI.Button(mainRect, currentLang, labelStyle))
                _isExpanded = !_isExpanded;

            // Expanded list
            if (_isExpanded)
            {
                float listY = y + buttonHeight + 2;

                for (int i = 0; i < languages.Count; i++)
                {
                    var profile = languages[i];
                    if (profile == null) continue;

                    var langRect = new Rect(x, listY, buttonWidth, buttonHeight);
                    bool isCurrent = Localization.CurrentLanguage == profile.Code;

                    GUI.color = isCurrent
                        ? new Color(0.2f, 0.5f, 0.9f, 0.8f)
                        : new Color(0, 0, 0, 0.6f);

                    GUI.DrawTexture(langRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;

                    if (GUI.Button(langRect, $"{profile.displayName} ({profile.Code})", labelStyle))
                    {
                        Localization.SetLanguage(profile);
                        _isExpanded = false;
                    }

                    listY += buttonHeight + 2;
                }
            }
        }
    }

    /// <summary>
    /// Floating overlay window that draws the language dropdown in Play Mode.
    /// Auto-opens when entering Play Mode, auto-closes when exiting.
    /// </summary>
    public class LanguageDropdownOverlay : EditorWindow
    {
        private static LanguageDropdownOverlay _instance;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            bool enabled = EditorPrefs.GetBool("SimplyLocalize_GameViewDropdown_Enabled", true);

            if (!enabled) return;

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (_instance == null)
                {
                    _instance = CreateInstance<LanguageDropdownOverlay>();
                    _instance.titleContent = new GUIContent("Lang");
                    _instance.ShowUtility();
                    _instance.minSize = new Vector2(130, 30);
                    _instance.maxSize = new Vector2(130, 400);

                    PositionWindow(_instance);
                }
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (_instance != null)
                {
                    _instance.Close();
                    _instance = null;
                }
            }
        }

        private static void PositionWindow(EditorWindow window)
        {
            var pos = (GameViewLanguageDropdown.DropdownPosition)
                EditorPrefs.GetInt("SimplyLocalize_GameViewDropdown_Position",
                    (int)GameViewLanguageDropdown.DropdownPosition.Right);

            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");

            if (gameViewType != null)
            {
                var gameView = GetWindow(gameViewType, false, null, false);

                if (gameView != null)
                {
                    var gvRect = gameView.position;
                    float x;

                    switch (pos)
                    {
                        case GameViewLanguageDropdown.DropdownPosition.Left:
                            x = gvRect.x + 10;
                            break;
                        case GameViewLanguageDropdown.DropdownPosition.Center:
                            x = gvRect.x + (gvRect.width - 130) / 2;
                            break;
                        default:
                            x = gvRect.x + gvRect.width - 140;
                            break;
                    }

                    window.position = new Rect(x, gvRect.y + 30, 130, 30);
                }
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !Localization.IsInitialized)
            {
                EditorGUILayout.LabelField("Not playing", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var languages = Localization.AvailableLanguages;
            if (languages == null || languages.Count == 0) return;

            // Build list for popup
            var names = new string[languages.Count];
            int currentIndex = 0;

            for (int i = 0; i < languages.Count; i++)
            {
                var p = languages[i];

                if (p == null)
                {
                    names[i] = "(null)";
                    continue;
                }

                names[i] = $"{p.displayName} ({p.Code})";

                if (Localization.CurrentLanguage == p.Code)
                    currentIndex = i;
            }

            int newIndex = EditorGUILayout.Popup(currentIndex, names);

            if (newIndex != currentIndex && languages[newIndex] != null)
            {
                Localization.SetLanguage(languages[newIndex]);
            }

            // Resize window height to fit
            float h = EditorGUIUtility.singleLineHeight + 8;
            minSize = new Vector2(130, h);
            maxSize = new Vector2(130, h);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                Close();
                _instance = null;
            }
            else
            {
                Repaint();
            }
        }
    }
}