using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    public enum LogLevel { Info, Warning, Error }

    /// <summary>
    /// Colored logging utility for SimplyLocalize.
    ///
    /// - Editor and standalone builds: Unity Console with rich text colors.
    /// - WebGL builds: browser console with CSS %c styling via JS plugin.
    ///
    /// Usage:
    ///   LocalizationLogger.Log("Loaded {0} keys for {1}", LogLevel.Info, null,
    ///       ("247", Color.green), ("Russian", Color.yellow));
    /// </summary>
    public static class LocalizationLogger
    {
        internal static bool Enabled { get; set; }
        internal static bool EnabledInBuild { get; set; }

        private static readonly Color InfoColor = new(0f, 0.6f, 1f, 1f);
        private static readonly Color WarningColor = new(1f, 0.5f, 0f, 1f);
        private static readonly Color ErrorColor = new(1f, 0f, 0f, 1f);

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SimplyLocalize_ConsoleLog(
            string level, string prefixColor, string message, string argsJson);
#endif

        // ──────────────────────────────────────────────
        //  Simple API (internal)
        // ──────────────────────────────────────────────

        internal static void Log(string message)
        {
            if (!ShouldLog()) return;
            OutputLog(message, LogLevel.Info, null, null);
        }

        internal static void LogWarning(string message)
        {
            OutputLog(message, LogLevel.Warning, null, null);
        }

        internal static void LogError(string message)
        {
            OutputLog(message, LogLevel.Error, null, null);
        }

        // ──────────────────────────────────────────────
        //  Rich API (public)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Logs a formatted message with colored arguments.
        /// </summary>
        public static void Log(string message, LogLevel level, Object context = null,
            params (string arg, Color color)[] args)
        {
            if (level == LogLevel.Info && !ShouldLog()) return;
            OutputLog(message, level, context, args);
        }

        /// <summary>
        /// Shorthand for Info level with colored args.
        /// </summary>
        public static void LogInfo(string message, params (string arg, Color color)[] args)
        {
            Log(message, LogLevel.Info, null, args);
        }

        // ──────────────────────────────────────────────
        //  Output
        // ──────────────────────────────────────────────

        private static void OutputLog(string message, LogLevel level, Object context,
            (string arg, Color color)[] args)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            OutputWebGL(message, level, args);
#else
            OutputUnity(message, level, context, args);
#endif
        }

        /// <summary>
        /// Unity Editor and standalone builds: rich text in Console.
        /// </summary>
        private static void OutputUnity(string message, LogLevel level, Object context,
            (string arg, Color color)[] args)
        {
            string prefixHex = ToHex(GetLevelColor(level));
            string formatted;

            if (args != null && args.Length > 0)
            {
                var fmtArgs = new object[args.Length];

                for (int i = 0; i < args.Length; i++)
                {
                    var (arg, color) = args[i];
                    fmtArgs[i] = $"<color=#{ToHex(color)}>{arg}</color>";
                }

                try { formatted = string.Format(message, fmtArgs); }
                catch (FormatException) { formatted = message; }
            }
            else
            {
                formatted = message;
            }

            string final = $"<color=#{prefixHex}>[SimplyLocalize]</color> {formatted}";

            switch (level)
            {
                case LogLevel.Info:
                    Debug.Log(final, context);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(final, context);
                    break;
                case LogLevel.Error:
                    Debug.LogError(final, context);
                    break;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// WebGL builds: calls JS plugin for browser console with CSS colors.
        /// </summary>
        private static void OutputWebGL(string message, LogLevel level,
            (string arg, Color color)[] args)
        {
            string prefixColor = ToCssColor(GetLevelColor(level));
            string levelStr = level.ToString();
            string argsJson = "[]";

            if (args != null && args.Length > 0)
            {
                // Build JSON array: [{"text":"value","color":"#FF0000"}, ...]
                var sb = new System.Text.StringBuilder("[");

                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"text\":\"");
                    sb.Append(EscapeJson(args[i].arg));
                    sb.Append("\",\"color\":\"");
                    sb.Append(ToCssColor(args[i].color));
                    sb.Append("\"}");
                }

                sb.Append(']');
                argsJson = sb.ToString();
            }

            SimplyLocalize_ConsoleLog(levelStr, prefixColor, message, argsJson);
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string ToCssColor(Color c) =>
            $"#{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";
#endif

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private static Color GetLevelColor(LogLevel level) => level switch
        {
            LogLevel.Info => InfoColor,
            LogLevel.Warning => WarningColor,
            LogLevel.Error => ErrorColor,
            _ => InfoColor
        };

        private static bool ShouldLog()
        {
#if UNITY_EDITOR
            return Enabled;
#else
            return Enabled && EnabledInBuild;
#endif
        }

        private static string ToHex(Color c) =>
            $"{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";
    }
}