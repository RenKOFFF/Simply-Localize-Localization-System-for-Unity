using UnityEditor;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationKeysData))]
    internal class LocalizationKeysDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginDisabledGroup(true);
            
            DrawDefaultInspector();
            
            EditorGUI.EndDisabledGroup();
        }
    }
}