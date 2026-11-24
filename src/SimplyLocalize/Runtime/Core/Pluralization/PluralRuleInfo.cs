namespace SimplyLocalize.Pluralization
{
    public enum PluralRuleSupport { BuiltIn, Custom, Default }

    /// <summary>Describes the actual selected rule; BuiltIn does not claim support for all linguistic features.</summary>
    public readonly struct PluralRuleInfo
    {
        public IPluralRule Rule { get; }
        public PluralRuleSupport Support { get; }
        public string MatchedLanguage { get; }
        internal PluralRuleInfo(IPluralRule rule, PluralRuleSupport support, string matchedLanguage)
        { Rule = rule; Support = support; MatchedLanguage = matchedLanguage; }
    }
}
