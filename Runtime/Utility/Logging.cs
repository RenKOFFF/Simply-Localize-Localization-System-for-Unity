using System;
using UnityEngine;

namespace SimplyLocalize
{
    public enum LogType{ Info, Warning, Error } 
    
    public static class Logging
    {
        public static void Log(string message, LogType type = LogType.Info)
        {
            if (Localization.EnableLogging == false) return;

#if !UNITY_EDITOR
            if (Localization.LoggingOnlyInEditor) return;
#endif
            
            var color = type switch
            {
                LogType.Info => Color.white,
                LogType.Warning => Color.yellow,
                LogType.Error => Color.red,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };


            Debug.Log($"<color={color}>[Simply Localize]</color>: {message}");
        }
    }
}