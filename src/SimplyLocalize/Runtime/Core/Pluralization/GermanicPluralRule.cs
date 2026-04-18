namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Plural rule for Germanic languages: English, German, Dutch, Swedish, Danish, Norwegian.
    /// Forms: one (n = 1), other.
    ///
    /// Usage in translation strings:
    ///   "{0} {0|coin|coins}"
    ///    Form 0: one   → "coin"
    ///    Form 1: other → "coins"
    /// </summary>
    public class GermanicPluralRule : IPluralRule
    {
        public int FormCount => 2;

        public int Evaluate(int count)
        {
            return count == 1 ? 0 : 1;
        }
    }
}
