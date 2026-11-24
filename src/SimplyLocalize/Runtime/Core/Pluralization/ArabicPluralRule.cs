namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Plural rule for Arabic.
    /// Forms: zero (n=0), one (n=1), two (n=2), few (n%100 in 3..10), many (n%100 in 11..99), other.
    ///
    /// Usage in translation strings:
    ///   "{0} {0|لا عملات|عملة|عملتان|عملات|عملة|عملات}"
    ///    Form 0: zero  (0)
    ///    Form 1: one   (1)
    ///    Form 2: two   (2)
    ///    Form 3: few   (3-10, 103-110...)
    ///    Form 4: many  (11-99, 111-199...)
    ///    Form 5: other (100-102, 200-202...)
    /// </summary>
    public class ArabicPluralRule : IDecimalPluralRule
    {
        private static readonly CardinalPluralRule Rule = new(CardinalKind.Arabic);
        public int FormCount => 6;

        public int Evaluate(int count)
        {
            return Rule.Evaluate(count);
        }
        public int Evaluate(decimal count) => Rule.Evaluate(count);
    }
}
