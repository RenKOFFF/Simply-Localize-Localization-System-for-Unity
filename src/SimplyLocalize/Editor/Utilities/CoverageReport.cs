using System.Collections.Generic;
using System.Linq;

namespace SimplyLocalize.Editor.Utilities
{
    public struct LanguageCoverage
    {
        public string LanguageCode;
        public string DisplayName;
        public int TranslatedCount;
        public int TotalCount;
        public float Percentage;
        public List<string> MissingKeys;
        public int AvailableCount;
        public float AvailablePercentage;
        public List<string> UnavailableKeys;
        public int PresentCount;
        public List<string> EmptyKeys;
        public List<string> WhitespaceKeys;
    }

    public struct CoverageWarning
    {
        public enum Severity { Info, Warning, Error }
        public Severity Level;
        public string Message;
        public string Key;
        public string LanguageCode;
    }

    public sealed class CoverageReport
    {
        public readonly List<LanguageCoverage> TextCoverage = new();
        public readonly List<LanguageCoverage> AssetCoverage = new();
        public readonly List<CoverageWarning> Warnings = new();
        public bool IsComplete => !Warnings.Any(w => w.Level == CoverageWarning.Severity.Error);
    }

}
