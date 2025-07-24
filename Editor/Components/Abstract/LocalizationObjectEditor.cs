using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor
{
    [CanEditMultipleObjects]
    public class LocalizationObjectEditor<TLocalizationComponent, TTargetComponent, TLocalizationTarget> : UnityEditor.Editor 
        where TLocalizationComponent : LocalizationObject<TLocalizationTarget>
        where TTargetComponent : Component
        where TLocalizationTarget : Object
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideLocalizationTargetProp = serializedObject.FindProperty("_overrideLocalizationTarget");
            var localizationKeyProp = serializedObject.FindProperty("_keyObject");
            var localizationTargetProp = serializedObject.FindProperty("_localizationTarget");

            if (overrideLocalizationTargetProp == null || localizationTargetProp == null)
            {
                var implemented = serializedObject.targetObject is ICanOverrideLocalizationTarget<TTargetComponent>;
                if (implemented == false)
                {
                    Debug.LogError("If you want to use this editor, please implement ICanOverrideLocalizationTarget interface in your component.", serializedObject.targetObject);
                    return;
                }
                Debug.LogError("You implemented ICanOverrideLocalizationTarget interface, but not created backing fields. \n[_overrideLocalizationTarget] and [_localizationTarget]", serializedObject.targetObject);
                
                return;
            }
            
            EditorGUILayout.PropertyField(localizationKeyProp);
            EditorGUILayout.PropertyField(overrideLocalizationTargetProp);

            var hasImage = localizationTargetProp.propertyType != SerializedPropertyType.ObjectReference || localizationTargetProp.objectReferenceValue != null;

            if (overrideLocalizationTargetProp.boolValue)
            {
                var targetObject = (TLocalizationComponent)serializedObject.targetObject;

                if (targetObject != null)
                {
                    localizationTargetProp.objectReferenceValue ??= targetObject.GetComponent<TTargetComponent>();
                }
                
                EditorGUILayout.PropertyField(localizationTargetProp);
            }
            else
            {
                var targetObject = (TLocalizationComponent)serializedObject.targetObject;

                if (targetObject != null)
                {
                    localizationTargetProp.objectReferenceValue = targetObject.GetComponent<TTargetComponent>();
                }
                
                if (hasImage)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(localizationTargetProp);
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