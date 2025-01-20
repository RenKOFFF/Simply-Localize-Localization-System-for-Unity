using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor
{
    [InitializeOnLoad]
    public static class GameViewLocalizationDropdown
    {
        private const string _GAME_VIEW_DROPDOWN_NAME = "GameViewLocalizationDropdownTree";

        static GameViewLocalizationDropdown()
        {
            EditorApplication.update += AddDropdownToGameView;
        }

        private static void AddDropdownToGameView()
        {
            var gameView = GetGameView();
            if (gameView == null) return;

            var root = gameView.rootVisualElement;
            var gameViewElement = root.Q<VisualElement>(_GAME_VIEW_DROPDOWN_NAME);

            if (!LocalizeEditor.GetLocalizationConfig().ShowLanguagePopup)
            {
                RemoveElementIfExist(gameViewElement, root);
                return;
            }

            var currentLanguage = Localization.CurrentLanguage ?? LocalizeEditor.GetLocalizationKeysData().DefaultLocalizationData?.i18nLang;
            var choices = LocalizeEditor.GetLanguages().Select(x => x.i18nLang).ToList();
            var index = choices.IndexOf(currentLanguage);

            if (gameViewElement != null)
            {
                var existingDropdown = gameViewElement.Q<DropdownField>("LanguageDropdown");
                if (existingDropdown == null || index == -1) return;
                
                existingDropdown.UnregisterValueChangedCallback(ChangeLanguage);
                
                existingDropdown.index = index;
                existingDropdown.choices = choices;
                
                existingDropdown.RegisterValueChangedCallback(ChangeLanguage);

                return;
            }

            var uss = Resources.Load<VisualTreeAsset>(_GAME_VIEW_DROPDOWN_NAME);
            if (uss == null)
            {
                return;
            }
            
            var container = new VisualElement();
            uss.CloneTree(container);
            container.name = _GAME_VIEW_DROPDOWN_NAME;
            

            if (string.IsNullOrEmpty(currentLanguage) || index == -1)
            {
                return;
            }
            
            var dropdown = container.Q<DropdownField>("LanguageDropdown");
            dropdown.choices = choices;
            dropdown.index = index;
            
            dropdown.RegisterValueChangedCallback(ChangeLanguage);

            container.Add(dropdown);
            root.Add(container);
        }

        private static void ChangeLanguage(ChangeEvent<string> evt)
        {
            if (Localization.CanTranslateInEditor() == false)
            {
                Logging.Log($"Cannot change language in editor.", LogType.Warning);
                return;
            }
                
            var language = evt.newValue;
            Localization.SetLocalization(language);
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