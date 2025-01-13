using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    public enum LogType{ Info, Warning, Error } 
    
    public static class Logging
    {
        private static readonly Color InfoColor = new(0f, .6f, 1, 1f);
        private static readonly Color WarningColor = new(1f, 0.5f, 0f, 1f);
        private static readonly Color ErrorColor = new(1f, 0f, 0f, 1f);
        
        public static void Log(string message, LogType type = LogType.Info, Object context = null)
        {
            if (Localization.EnableLogging == false) return;

#if !UNITY_EDITOR
            if (Localization.LoggingOnlyInEditor) return;
#endif
            
            var color = type switch
            {
                LogType.Info => InfoColor,
                LogType.Warning => WarningColor,
                LogType.Error => ErrorColor,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
            
            int r = (int)(color.r * 255), g = (int)(color.g * 255), b = (int)(color.b * 255);
            var hexColor = r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
            
            Debug.Log($"<color=#{hexColor}>[Simply Localize]</color>: {message}", context);
        }
    }
}