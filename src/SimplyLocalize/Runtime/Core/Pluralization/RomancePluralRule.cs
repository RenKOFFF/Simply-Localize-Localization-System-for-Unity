namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Plural rule for Romance languages: French, Spanish, Italian, Portuguese.
    /// Forms: one (n = 0 or n = 1), other.
    ///
    /// Usage in translation strings:
    ///   "{0} {0|pièce|pièces}"
    ///    Form 0: one   → "pièce"  (0, 1)
    ///    Form 1: other → "pièces" (2, 3, 4...)
    /// </summary>
    public class RomancePluralRule : IPluralRule
    {
        public int FormCount => 2;

        public int Evaluate(int count)
        {
            int abs = count < 0 ? -count : count;
            return abs <= 1 ? 0 : 1;
        }
    }
}
