using System;
using System.IO;
using System.Linq;

namespace SimplyLocalize.Editor.Data
{
    internal static class EditorDataPath
    {
        internal static void ValidateName(string name, string label)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith(".")
                || name == "." || name == ".." || name.Any(char.IsControl)
                || name.IndexOfAny("/\\:*?\"<>|".ToCharArray()) >= 0)
                throw new FormatException($"Invalid {label}: '{name}'. Expected a single safe name, not a path.");
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem)
                || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '0' && stem[3] <= '9')
                throw new FormatException($"Reserved {label}: '{name}'.");
        }

        internal static string Resolve(string root, string relative)
        {
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("No localization base path. Load a configured localization folder before saving.");
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Localization path escapes the base directory: " + path);
            // A junction/symlink inside the root can otherwise redirect a lexically safe path.
            for (string current = path; current != null && current.Length >= fullRoot.Length; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Localization write paths cannot traverse a symbolic link or junction: " + current);
            return path;
        }
    }
}
