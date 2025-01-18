using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public static class GameViewDropdown
    {
        private const string _GAME_VIEW_DROPDOWN_NAME = "GameViewDropdown";

        static GameViewDropdown()
        {
            EditorApplication.update += AddDropdownToGameView;
        }

        private static void AddDropdownToGameView()
        {
            var gameView = GetGameView();
            if (gameView == null) return;

            var root = gameView.rootVisualElement;
            var gameViewDropdown = root.Q<VisualElement>(_GAME_VIEW_DROPDOWN_NAME);

            if (!Application.isPlaying || !LocalizeEditor.GetLocalizationConfig().ShowLanguagePopup)
            {
                RemoveElementIfExist(gameViewDropdown, root);
                return;
            }

            if (gameViewDropdown != null) return;

            var container = new VisualElement
            {
                name = _GAME_VIEW_DROPDOWN_NAME,
                style =
                {
                    position = Position.Absolute,
                    top = 30,
                    left = 10,
                    width = 200,
                    backgroundColor = new Color(0, 0, 0, 0.5f),
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 5,
                    paddingBottom = 5,
                    borderBottomLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopLeftRadius = 5,
                    borderTopRightRadius = 5
                }
            };
            
            var currentLanguage = Localization.CurrentLanguage ?? LocalizeEditor.GetLocalizationKeysData().DefaultLocalizationData?.i18nLang;

            if (string.IsNullOrEmpty(currentLanguage))
            {
                return;
            }

            var choices = LocalizeEditor.GetLanguages().Select(x => x.i18nLang).ToList();
            var index = choices.IndexOf(currentLanguage);

            if (index == -1)
            {
                return;
            }
            
            var dropdown = new PopupField<string>("Language", choices, index);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var language = evt.newValue;
                Localization.SetLocalization(language);
            });

            container.Add(dropdown);
            root.Add(container);
        }

        private static void RemoveElementIfExist(VisualElement gameViewDropdown, VisualElement root)
        {
            if (gameViewDropdown != null && root.Contains(gameViewDropdown))
            {
                root.Remove(gameViewDropdown);
            }
        }

        private static EditorWindow GetGameView()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var type = assembly.GetType("UnityEditor.GameView");

            var windows = Resources.FindObjectsOfTypeAll(type);
            if (windows.Length > 0)
            {
                return windows[0] as EditorWindow;
            }

            return null;
        }
    }
}