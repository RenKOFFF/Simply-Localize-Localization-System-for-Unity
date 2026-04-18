namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Defines a pluralization rule for a language group.
    /// Given a count, returns the index of the correct plural form.
    /// </summary>
    public interface IPluralRule
    {
        /// <summary>
        /// Returns the plural form index for the given count.
        /// Index 0 is always the first form listed in the translation string.
        /// </summary>
        int Evaluate(int count);

        /// <summary>
        /// Number of distinct plural forms this rule produces.
        /// Used for validation (e.g. Slavic = 3, Arabic = 6).
        /// </summary>
        int FormCount { get; }
    }
}
