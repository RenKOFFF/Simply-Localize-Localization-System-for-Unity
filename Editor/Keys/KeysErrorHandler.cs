#if UNITY_EDITOR

using System.Collections;
using SimplyLocalize.Runtime.Data.Keys.Generated;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Keys
{
    [InitializeOnLoad]
    public class KeysErrorHandler
    {
        static KeysErrorHandler()
        {
            Application.logMessageReceived += HandleLog;
            
            EditorCoroutineUtility.StartCoroutineOwnerless(TryFixError());
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (type is not (LogType.Error or LogType.Exception) ||
                !logString.Contains(nameof(LocalizationKey))) return;

            Debug.Log("There was an error importing the SimplyLocalize asset. We are attempting to fix the issue.");

            EditorCoroutineUtility.StartCoroutineOwnerless(TryFixError());
        }

        private static IEnumerator TryFixError()
        {
            KeyGenerator.RemoveOldFilesIfNewExists();
            
            yield return null;
            
            AssetDatabase.ImportAsset(KeyGenerator.OldFilePathAndName);
        }
    }
}

#endif