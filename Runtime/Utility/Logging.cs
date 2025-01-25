using System;
using System.Collections.Generic;
using System.Linq;
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
        
        public static void Log(string message, LogType type = LogType.Info, Object context = null, params (string arg, Color color)[] args)
        {
            if (Localization.LocalizationConfig.EnableLogging == false) return;

#if !UNITY_EDITOR
            if (Localization.LocalizationConfig.EnableLoggingInBuild == false) return;
#endif
            
            var color = type switch
            {
                LogType.Info => InfoColor,
                LogType.Warning => WarningColor,
                LogType.Error => ErrorColor,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
            
            var hexColor = HexColor(color);
            
            if (args.Length > 0)
            {
                var argsList = new object[args.Length];

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var argString = $"<color=#{HexColor(arg.color)}>{arg.arg}</color>";
                    
                    argsList[i] = argString;
                }

                message = string.Format(message, argsList);
            }

            var m = $"<color=#{hexColor}>[Simply Localize]</color>: {message}";

            switch (type)
            {
                case LogType.Info:
                    Debug.Log(m, context);
                    break;
                
                case LogType.Warning:
                    Debug.LogWarning(m, context);
                    break;
                
                case LogType.Error:
                    Debug.LogError(m, context);
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static string HexColor(Color color)
        {
            int r = (int)(color.r * 255), g = (int)(color.g * 255), b = (int)(color.b * 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }
    }
}