using System.Collections.Generic;
using SimplyLocalize.Editor.Data;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>
    /// Walks the fallback chain (per-language → global) to find a usable translation
    /// or asset for a key when the target language has no value.
    /// Used by editor previews to show fallback content in grey instead of "(missing)".
    ///
    /// Runtime fallback resolution lives in LocalizationManager — this editor-side
    /// helper mirrors the same walking logic, but operates on the editor data cache
    /// rather than the runtime text caches.
    /// </summary>
    public static class FallbackResolver
    {
        public struct FallbackResult
        {
            /// <summary>The resolved translation value, or null if nothing was found.</summary>
            public string Value;

            /// <summary>Language code the value came from. Equals <c>languageCode</c> if
            /// the target language had its own translation; otherwise the fallback's code.</summary>
            public string FromLanguage;

            /// <summary>True if the value came from a fallback language, not the target.</summary>
            public bool IsFromFallback;
        }

        /// <summary>
        /// Resolves a translation for the given key, walking the fallback chain if the
        /// target language has no translation.
        ///
        /// Order:
        ///   1. Target language itself
        ///   2. Per-language fallback chain (profile.fallbackProfile → its fallback → ...)
        ///   3. Global fallback language (config.fallbackLanguage)
        ///
        /// Returns a result with Value=null if nothing was found at any level.
        /// </summary>
        public static FallbackResult ResolveTranslation(
            EditorLocalizationData data,
            LocalizationConfig config,
            string key,
            string languageCode)
        {
            var result = new FallbackResult();

            if (data == null || config == null || string.IsNullOrEmpty(key)
                || string.IsNullOrEmpty(languageCode))
                return result;

            // 1. Try target language directly
            string direct = data.GetTranslation(key, languageCode);

            if (!string.IsNullOrEmpty(direct))
            {
                result.Value = direct;
                result.FromLanguage = languageCode;
                result.IsFromFallback = false;
                return result;
            }

            // 2. Per-language fallback chain with cycle protection
            var profile = config.GetProfile(languageCode);

            if (profile != null)
            {
                var visited = new HashSet<string> { languageCode };
                var fb = profile.fallbackProfile;

                while (fb != null && visited.Add(fb.Code))
                {
                    string fbValue = data.GetTranslation(key, fb.Code);

                    if (!string.IsNullOrEmpty(fbValue))
                    {
                        result.Value = fbValue;
                        result.FromLanguage = fb.Code;
                        result.IsFromFallback = true;
                        return result;
                    }

                    fb = fb.fallbackProfile;
                }

                // 3. Global fallback (if not already visited through per-language chain)
                string globalCode = config.FallbackLanguageCode;

                if (!string.IsNullOrEmpty(globalCode) && visited.Add(globalCode))
                {
                    string globalValue = data.GetTranslation(key, globalCode);

                    if (!string.IsNullOrEmpty(globalValue))
                    {
                        result.Value = globalValue;
                        result.FromLanguage = globalCode;
                        result.IsFromFallback = true;
                        return result;
                    }
                }
            }

            return result;
        }
    }
}
