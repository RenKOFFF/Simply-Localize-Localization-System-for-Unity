using System.Collections.Generic;

namespace SimplyLocalize.Data.KeyGeneration
{
    public static class LocalizationKeys
    {
        public static readonly Dictionary<LocalizationKey, string> Keys = new()
        {
            { LocalizationKey.None, LocalizationKey.None.ToString() },
        };
    }
}