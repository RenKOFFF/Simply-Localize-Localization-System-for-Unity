using SimplyLocalize.Runtime.Data.Keys;
using UnityEditor;

namespace SimplyLocalize.Editor.Keys
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