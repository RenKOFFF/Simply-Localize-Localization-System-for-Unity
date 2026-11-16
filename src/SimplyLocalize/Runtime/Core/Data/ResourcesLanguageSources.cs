using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SimplyLocalize.Data
{
    /// <summary>
    /// An immutable, ordered list of exact Resources addresses for one language.
    /// Addresses are relative to Resources, without file extensions. Empty lists intentionally load nothing.
    /// This descriptor contains strings only, so creating it does not load Unity objects or their dependencies.
    /// </summary>
    public sealed class ResourcesLanguageSources
    {
        public string LanguageCode { get; }
        public ReadOnlyCollection<string> TextFiles { get; }
        public ReadOnlyCollection<string> AssetTables { get; }

        public ResourcesLanguageSources(string languageCode, IEnumerable<string> textFiles = null,
            IEnumerable<string> assetTables = null)
        {
            if (!LocalizationConfig.IsLanguageCodeValid(languageCode))
                throw new ArgumentException("Invalid language code.", nameof(languageCode));
            LanguageCode = languageCode;
            TextFiles = CopyAddresses(textFiles, nameof(textFiles));
            AssetTables = CopyAddresses(assetTables, nameof(assetTables));
        }

        private static ReadOnlyCollection<string> CopyAddresses(IEnumerable<string> addresses, string parameter)
        {
            var copy = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (addresses != null)
                foreach (string address in addresses)
                {
                    if (string.IsNullOrEmpty(address) || !LocalizationConfig.IsResourcePathValid(address)
                        || address.EndsWith("/", StringComparison.Ordinal))
                        throw new ArgumentException("Expected an exact Resources address, not a root or folder: " + address, parameter);
                    if (!seen.Add(address)) throw new ArgumentException("Duplicate Resources address: " + address, parameter);
                    copy.Add(address);
                }
            return copy.AsReadOnly();
        }
    }
}
