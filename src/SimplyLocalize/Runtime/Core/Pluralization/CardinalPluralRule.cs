using System;

namespace SimplyLocalize.Pluralization
{
    internal enum CardinalKind
    {
        English, ExactlyOne, Danish, Icelandic, Russian, Belarusian, Polish,
        Czech, Slovenian, Serbian, Romanian, French, Italian, Spanish, Arabic, Other
    }

    /// <summary>
    /// CLDR 48 cardinal rules for ordinary numbers (compact exponent e = 0).
    /// Source: https://www.unicode.org/cldr/charts/48/supplemental/language_plural_rules.html
    /// Keeps historical positional prefixes; new categories are appended, never inserted.
    /// </summary>
    internal sealed class CardinalPluralRule : IDecimalPluralRule
    {
        private readonly CardinalKind _kind;
        public CardinalPluralRule(CardinalKind kind) => _kind = kind;

        public int FormCount => _kind switch
        {
            CardinalKind.Other => 1,
            CardinalKind.Arabic => 6,
            CardinalKind.Russian or CardinalKind.Belarusian or CardinalKind.Polish
                or CardinalKind.Czech or CardinalKind.Slovenian => 4,
            CardinalKind.Serbian or CardinalKind.Romanian or CardinalKind.French
                or CardinalKind.Italian or CardinalKind.Spanish => 3,
            _ => 2
        };

        public int Evaluate(int count) => Evaluate(new Operands(count, 0));
        public int Evaluate(decimal count) => Evaluate(new Operands(count, (decimal.GetBits(count)[3] >> 16) & 0xff));

        private int Evaluate(Operands o)
        {
            switch (_kind)
            {
                case CardinalKind.Other: return 0;
                case CardinalKind.English: return o.I == 1 && o.V == 0 ? 0 : 1;
                case CardinalKind.ExactlyOne: return o.N == 1 ? 0 : 1;
                case CardinalKind.Danish: return o.N == 1 || o.T != 0 && o.I <= 1 ? 0 : 1;
                case CardinalKind.Icelandic:
                    return o.T == 0 && IsOneEnding(o.I) || IsOneEnding(o.T) ? 0 : 1;
                case CardinalKind.Russian:
                    if (o.V != 0) return 3; // one, few, many, other
                    return SlavicInteger(o.I);
                case CardinalKind.Belarusian:
                    if (IsOneEnding(o.N)) return 0;
                    if (IsFewEnding(o.N)) return 1;
                    return o.N == o.I ? 2 : 3;
                case CardinalKind.Polish:
                    if (o.V != 0) return 3;
                    if (o.I == 1) return 0;
                    return IsFewEnding(o.I) ? 1 : 2;
                case CardinalKind.Czech: // one, few, other, many (fractional)
                    if (o.V != 0) return 3;
                    if (o.I == 1) return 0;
                    return o.I >= 2 && o.I <= 4 ? 1 : 2;
                case CardinalKind.Slovenian: // one, few, other, two
                    if (o.V != 0) return 1;
                    if (o.I % 100 == 1) return 0;
                    if (o.I % 100 == 2) return 3;
                    return o.I % 100 == 3 || o.I % 100 == 4 ? 1 : 2;
                case CardinalKind.Serbian: // one, few, other
                    if (o.V == 0 && IsOneEnding(o.I) || IsOneEnding(o.F)) return 0;
                    if (o.V == 0 && IsFewEnding(o.I) || IsFewEnding(o.F)) return 1;
                    return 2;
                case CardinalKind.Romanian: // one, other, few (legacy Romance prefix)
                    if (o.I == 1 && o.V == 0) return 0;
                    return o.V != 0 || o.N == 0 || o.N != 1 && InIntegerRange(o.N % 100, 1, 19) ? 2 : 1;
                case CardinalKind.French: // one, other, many (also Portuguese except pt-PT)
                    if (o.I <= 1) return 0;
                    return IsMillion(o) ? 2 : 1;
                case CardinalKind.Italian: // one, other, many (also Catalan, pt-PT)
                    if (o.I == 1 && o.V == 0) return 0;
                    return IsMillion(o) ? 2 : 1;
                case CardinalKind.Spanish:
                    if (o.N == 1) return 0;
                    return IsMillion(o) ? 2 : 1;
                case CardinalKind.Arabic:
                    if (o.N == 0) return 0;
                    if (o.N == 1) return 1;
                    if (o.N == 2) return 2;
                    if (InIntegerRange(o.N % 100, 3, 10)) return 3;
                    if (InIntegerRange(o.N % 100, 11, 99)) return 4;
                    return 5;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        private static bool IsMillion(Operands o) => o.V == 0 && o.I != 0 && o.I % 1000000 == 0;
        private static bool IsOneEnding(decimal n) => n % 10 == 1 && n % 100 != 11;
        private static bool IsFewEnding(decimal n) => InIntegerRange(n % 10, 2, 4) && !InIntegerRange(n % 100, 12, 14);
        private static bool InIntegerRange(decimal n, int min, int max) => n == decimal.Truncate(n) && n >= min && n <= max;
        private static int SlavicInteger(decimal n) => IsOneEnding(n) ? 0 : IsFewEnding(n) ? 1 : 2;

        private readonly struct Operands
        {
            public readonly decimal N, I, F, T;
            public readonly int V;
            public Operands(decimal number, int scale)
            {
                N = Math.Abs(number);
                I = decimal.Truncate(N);
                V = scale;
                decimal fraction = N - I;
                for (int i = 0; i < scale; i++) fraction *= 10;
                F = fraction;
                while (fraction != 0 && fraction % 10 == 0) fraction /= 10;
                T = fraction;
            }
        }
    }
}
