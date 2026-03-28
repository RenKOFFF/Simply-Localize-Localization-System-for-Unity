namespace SimplyLocalize.Pluralization
{
    /// <summary>
    /// Plural rule for Slavic languages: Russian, Ukrainian, Polish, Croatian, Serbian, Czech, Slovak, Belarusian.
    /// Forms: one (n%10=1, n%100≠11), few (n%10 in 2..4, n%100 not in 12..14), many (other).
    ///
    /// Usage in translation strings:
    ///   "{0} {0|монету|монеты|монет}"
    ///    Form 0: one  → "монету"  (1, 21, 31, 101...)
    ///    Form 1: few  → "монеты"  (2, 3, 4, 22, 23...)
    ///    Form 2: many → "монет"   (0, 5-20, 25-30...)
    /// </summary>
    public class SlavicPluralRule : IPluralRule
    {
        public int FormCount => 3;

        public int Evaluate(int count)
        {
            int abs = count < 0 ? -count : count;
            int mod10 = abs % 10;
            int mod100 = abs % 100;

            if (mod10 == 1 && mod100 != 11)
                return 0;

            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
                return 1;

            return 2;
        }
    }
}
