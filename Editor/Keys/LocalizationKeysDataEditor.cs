using UnityEditor;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationKeysData))]
    public class LocalizationKeysDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginDisabledGroup(true);
            
            DrawDefaultInspector();
            
            EditorGUI.EndDisabledGroup();
        }
    }
}