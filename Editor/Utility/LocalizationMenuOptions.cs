using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Editor
{
    public static class LocalizationMenuOptions
    {
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