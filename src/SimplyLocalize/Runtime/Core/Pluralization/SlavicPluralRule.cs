namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Cardinal rule for Russian and Ukrainian. Other Slavic languages have separate rules.
    /// Forms: one (n%10=1, n%100≠11), few (n%10 in 2..4, n%100 not in 12..14), many (other).
    ///
    /// Usage in translation strings:
    ///   "{0} {0|монету|монеты|монет}"
    ///    Form 0: one  → "монету"  (1, 21, 31, 101...)
    ///    Form 1: few  → "монеты"  (2, 3, 4, 22, 23...)
    ///    Form 2: many → "монет"   (0, 5-20, 25-30...)
    /// </summary>
    public class SlavicPluralRule : IDecimalPluralRule
    {
        private static readonly CardinalPluralRule Rule = new(CardinalKind.Russian);
        /// <summary>one, few, many, other. Optional fractional other is appended after the legacy three forms.</summary>
        public int FormCount => Rule.FormCount;

        public int Evaluate(int count)
        {
            return Rule.Evaluate(count);
        }
        public int Evaluate(decimal count) => Rule.Evaluate(count);
    }
}
