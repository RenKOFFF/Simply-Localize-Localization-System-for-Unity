using System;
using System.Linq;

namespace SimplyLocalize.Editor
{
    public static class LocalizationStringExtensions
    {
        public static readonly char[] WrongChars = { '\\', '\t', '\n', '\r', '\f', '\v', '\0', ';', '.', '!', '?', '{', '}' };
        
        public static string ToCorrectLocalizationKeyName(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Empty";
            
            input = input.Trim();
            input = input.Where(c => !WrongChars.Contains(c)).Aggregate("", (current, c) => current + c);
            
            if (string.IsNullOrWhiteSpace(input)) return "Empty";
            
            var finalKey = Localization.LocalizationConfig.SpaceIsGroupSeparator switch
            {
                LocalizationConfig.SpaceUsage.GroupSeparator => AsGroupSeparator(input),
                LocalizationConfig.SpaceUsage.Underline => AsUnderlineSeparator(input),
                LocalizationConfig.SpaceUsage.Original => input,
                _ => throw new ArgumentOutOfRangeException()
            };

            return finalKey;
        }

        private static string AsUnderlineSeparator(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var finalKey = string.Join("_", parts);
            
            return finalKey;
        }

        private static string AsGroupSeparator(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var finalKey = string.Join("/", parts);
            
            if (finalKey.EndsWith("/")) 
                finalKey = finalKey[..^1];
            return finalKey;
        }
    }
}