namespace SimplyLocalize
{
    /// <summary>Validation shared by exact runtime sources and Editor folder discovery.</summary>
    public static class LocalizationResourceAddress
    {
        public static bool IsValid(string path, bool allowFolder = false)
        {
            if (path == null || path != path.Trim() || path.StartsWith("/")
                || path.IndexOfAny(new[] { '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return false;
            foreach (char c in path) if (char.IsControl(c)) return false;
            if (path.Length == 0) return allowFolder;
            if (!allowFolder && path.EndsWith("/")) return false;
            foreach (var part in path.TrimEnd('/').Split('/'))
                if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }
    }
}
