namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// French-style cardinal rule (also Portuguese except European Portuguese).
    /// Forms: one, other, many. The optional many form is appended to preserve two-form data.
    ///
    /// Usage in translation strings:
    ///   "{0} {0|pièce|pièces}"
    ///    Form 0: one   → "pièce"  (0, 1)
    ///    Form 1: other → "pièces" (2, 3, 4...)
    /// </summary>
    public class RomancePluralRule : IDecimalPluralRule
    {
        private static readonly CardinalPluralRule Rule = new(CardinalKind.French);
        public int FormCount => Rule.FormCount;

        public int Evaluate(int count)
        {
            return Rule.Evaluate(count);
        }
        public int Evaluate(decimal count) => Rule.Evaluate(count);
    }
}
