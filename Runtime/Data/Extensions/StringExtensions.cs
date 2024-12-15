using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SimplyLocalize.Runtime.Data.Extensions
{
    public static class StringExtensions
    {
        public static string ToEnumName(this string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return "Empty";

            var cleanedString = Regex.Replace(str, @"\b\d+\w*", ""); 
            cleanedString = Regex.Replace(cleanedString, "[^a-zA-Z0-9 ]", "");

            var words = cleanedString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0) return "";
            if (words.Length == 1) return string.Join("", words.Select(w => 
                char.ToUpper(w[0]) + w[1..]));

            var pascalCaseName = string.Join("", words.Select(w => 
                char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")));

            return pascalCaseName;
        }
    }
}