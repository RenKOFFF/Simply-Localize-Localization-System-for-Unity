namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Plural rule for East Asian and similar languages: Japanese, Chinese, Korean, Vietnamese, Thai, Indonesian, Malay.
    /// These languages have no grammatical plural forms — always returns form 0.
    ///
    /// Usage in translation strings:
    ///   "{0}個のコイン"  — no plural token needed, just use {0} directly
    ///
    /// If a plural token is used, only one form is needed:
    ///   "{0} {0|コイン}"
    ///    Form 0: other → "コイン" (always)
    /// </summary>
    public class EastAsianPluralRule : IPluralRule
    {
        public int FormCount => 1;

        public int Evaluate(int count)
        {
            return 0;
        }
    }
}
