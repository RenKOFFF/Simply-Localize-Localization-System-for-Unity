using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>
    /// Internal logging utility. Logging can be enabled/disabled via LocalizationConfig.
    /// Warnings and errors are always shown regardless of the setting.
    /// </summary>
    internal static class LocalizationLogger
    {
        internal static bool Enabled { get; set; }

        private const string Prefix = "[SimplyLocalize] ";

        internal static void Log(string message)
        {
            if (Enabled)
                Debug.Log(Prefix + message);
        }

        internal static void LogWarning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void LogError(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
