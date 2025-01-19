using UnityEditor;
using UnityEngine.UI;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationImage), true)]
    [CanEditMultipleObjects]
    public class LocalizationImageEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideImageProp = serializedObject.FindProperty("_overrideImage");
            var localizationKeyProp = serializedObject.FindProperty("_keyObject");
            var imageProp = serializedObject.FindProperty("_image");

            EditorGUILayout.PropertyField(localizationKeyProp);
            EditorGUILayout.PropertyField(overrideImageProp);

            var hasImage = imageProp.propertyType != SerializedPropertyType.ObjectReference || imageProp.objectReferenceValue != null;

            if (overrideImageProp.boolValue)
            {
                var targetObject = (LocalizationImage)serializedObject.targetObject;

                if (targetObject != null)
                {
                    imageProp.objectReferenceValue ??= targetObject.GetComponent<Image>();
                }
                
                EditorGUILayout.PropertyField(imageProp);
            }
            else
            {
                var targetObject = (LocalizationImage)serializedObject.targetObject;

                if (targetObject != null)
                {
                    imageProp.objectReferenceValue = targetObject.GetComponent<Image>();
                }
                
                if (hasImage)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(imageProp);
                    EditorGUI.EndDisabledGroup();
                }
            }
            
            if (!hasImage)
            {
                EditorGUILayout.HelpBox("You must assign an Image component.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}