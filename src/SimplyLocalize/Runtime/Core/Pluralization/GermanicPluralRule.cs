namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// English-style cardinal rule (also German, Dutch, Swedish, Finnish, Estonian).
    /// Forms: one (absolute integer 1 without fractional digits), other.
    ///
    /// Usage in translation strings:
    ///   "{0} {0|coin|coins}"
    ///    Form 0: one   → "coin"
    ///    Form 1: other → "coins"
    /// </summary>
    public class GermanicPluralRule : IDecimalPluralRule
    {
        private static readonly CardinalPluralRule Rule = new(CardinalKind.English);
        public int FormCount => 2;

        public int Evaluate(int count)
        {
            return Rule.Evaluate(count);
        }
        public int Evaluate(decimal count) => Rule.Evaluate(count);
    }
}
