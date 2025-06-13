using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Editor
{
    public static class LocalizationMenuOptions
    {
        [MenuItem("CONTEXT/FormattableLocalizationText/Replace to Localization Text", false)]
        private static void ReplaceFormattableToBase(MenuCommand command)
        {
            var formattable = (FormattableLocalizationText)command.context;
            var gameObject = formattable.gameObject;

            var soFormattable = new SerializedObject(formattable);
            soFormattable.Update();
            var localizationKeyFormattable = soFormattable.FindProperty("_localizationKey");

            var valueFormattable = localizationKeyFormattable.FindPropertyRelative("_key");

            Undo.RegisterCompleteObjectUndo(gameObject, $"Replace {formattable.GetType().Name} with {nameof(LocalizationText)}");
            Undo.DestroyObjectImmediate(formattable);

            var localizationText = gameObject.AddComponent<LocalizationText>();

            var soLocalizationText = new SerializedObject(localizationText);
            var localizationKeyText = soLocalizationText.FindProperty("_localizationKey");

            var valueText = localizationKeyText.FindPropertyRelative("_key");

            valueText.stringValue = valueFormattable.stringValue;

            soLocalizationText.ApplyModifiedProperties();
            Selection.activeObject = localizationText;
        }

        [MenuItem("CONTEXT/LocalizationText/Replace to Formattable Localization Text", false)]
        private static void ReplaceBaseToFormattable(MenuCommand command)
        {
            var localizationText = (LocalizationText)command.context;
            var gameObject = localizationText.gameObject;

            var soLocalizationText = new SerializedObject(localizationText);
            soLocalizationText.Update();
            var localizationKeyText = soLocalizationText.FindProperty("_localizationKey");

            var valueText = localizationKeyText.FindPropertyRelative("_key");

            Undo.RegisterCompleteObjectUndo(gameObject, $"Replace {localizationText.GetType().Name} with {nameof(FormattableLocalizationText)}");
            Undo.DestroyObjectImmediate(localizationText);

            var formattableLocalizationText = gameObject.AddComponent<FormattableLocalizationText>();

            var soFormattable = new SerializedObject(formattableLocalizationText);
            var localizationKeyFormattable = soFormattable.FindProperty("_localizationKey");

            var valueFormattable = localizationKeyFormattable.FindPropertyRelative("_key");
            
            valueFormattable.stringValue = valueText.stringValue;

            soFormattable.ApplyModifiedProperties();
            Selection.activeObject = formattableLocalizationText;
        }


        [MenuItem("CONTEXT/FormattableLocalizationText/Replace to Localization Text", true)]
        public static bool ReplaceFormattableToBaseValidate()
        {
            return Selection.activeGameObject.TryGetComponent(out FormattableLocalizationText formattable) &&
                   formattable != null && formattable.GetType() == typeof(FormattableLocalizationText);
        }

        [MenuItem("CONTEXT/LocalizationText/Replace to Formattable Localization Text", true)]
        public static bool ReplaceBaseToFormattableValidate()
        {
            return Selection.activeGameObject.TryGetComponent(out LocalizationText text) && text != null &&
                   text.GetType() == typeof(LocalizationText) && text is not FormattableLocalizationText;
        }

        [MenuItem("GameObject/UI/Simply Localize/Legacy Text - LocalizationText", false, 10)]
        public static void CreateLocalizedTextGameObject(MenuCommand menuCommand)
        {
            var gameObject = new GameObject("Localized Text (Legacy)", typeof(RectTransform));

            gameObject.AddComponent<Text>();
            SetupGameObject(menuCommand, gameObject);
        }

        [MenuItem("GameObject/UI/Simply Localize/TMP Text - LocalizationText", false, 10)]
        public static void CreateLocalizedTMPGameObject(MenuCommand menuCommand)
        {
            var gameObject = new GameObject("Localized Text (TMP)", typeof(RectTransform));

            gameObject.AddComponent<TextMeshProUGUI>();
            SetupGameObject(menuCommand, gameObject);
        }

        private static void SetupGameObject(MenuCommand menuCommand, GameObject gameObject)
        {
            gameObject.AddComponent<LocalizationText>();

            var parent = menuCommand.context as GameObject;
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(gameObject, parent);
            }
            else
            {
                var canvas = GetOrCreateCanvas();
                GameObjectUtility.SetParentAndAlign(gameObject, canvas.gameObject);
            }

            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);
            Selection.activeObject = gameObject;
        }

        private static Canvas GetOrCreateCanvas()
        {
#if UNITY_6000_0_OR_NEWER
            var canvas = Object.FindFirstObjectByType<Canvas>();
#else
            var canvas = Object.FindObjectOfType<Canvas>();
#endif
            
            if (canvas == null)
            {
                var canvasGameObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvas = canvasGameObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            return canvas;
        }
    }
}