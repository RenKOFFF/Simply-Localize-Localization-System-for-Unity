using System;

namespace SimplyLocalize
{
    public static class StringExtensions
    {
        public static string ToCorrectLocalizationKeyName(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Empty";

            input = input.Trim();
            
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var finalKey = string.Join("/", parts);
            
            if (finalKey.EndsWith("/")) 
                finalKey = finalKey[..^1];
            
            return finalKey;
        }
    }
}