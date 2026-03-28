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
    public class ArabicPluralRule : IPluralRule
    {
        public int FormCount => 6;

        public int Evaluate(int count)
        {
            int abs = count < 0 ? -count : count;

            if (abs == 0) return 0;
            if (abs == 1) return 1;
            if (abs == 2) return 2;

            int mod100 = abs % 100;

            if (mod100 >= 3 && mod100 <= 10) return 3;
            if (mod100 >= 11 && mod100 <= 99) return 4;

            return 5;
        }
    }
}
