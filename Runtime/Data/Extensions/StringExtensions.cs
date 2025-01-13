using System;

namespace SimplyLocalize
{
    public static class StringExtensions
    {
        public static string ToCorrectName(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Empty";

            input = input.Trim();
            
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("/", parts);
        }
    }
}