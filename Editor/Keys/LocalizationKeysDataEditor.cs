using SimplyLocalize.Runtime.Data.Extensions;
using SimplyLocalize.Runtime.Data.Keys;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Keys
{
    [CustomEditor(typeof(LocalizationKeysData))]
    public class LocalizationKeysDataEditor : UnityEditor.Editor
    {
        private string _newKeys = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Label("Add multiple keys (separated by commas or line breaks):");
            _newKeys = EditorGUILayout.TextArea(_newKeys);

            if (GUILayout.Button("Add Range"))
            {
                AddKeys();
            }
        }

        private void AddKeys()
        {
            var keysArray = _newKeys.Split(new[] { ',', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < keysArray.Length; i++)
            {
                keysArray[i] = keysArray[i].Trim();
            }

            var data = (LocalizationKeysData)target;

            foreach (var key in keysArray)
            {
                var keyName = key.ToPascalCase();
                data.Keys.Add(new EnumHolder { Name = keyName });
            }

            _newKeys = "";

            EditorUtility.SetDirty(target);
        }
    }
}