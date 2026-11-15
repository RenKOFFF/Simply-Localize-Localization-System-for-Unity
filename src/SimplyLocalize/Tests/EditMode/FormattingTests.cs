using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using SimplyLocalize.Data;
using SimplyLocalize.Pluralization;
using SimplyLocalize.TextProcessing;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class FormattingTests
    {
        private static string Format(string text, string language, object value) =>
            TextFormatter.Format(TokenParser.Parse(text), language, new[] { value });

        [TestCase("{{value}}", "{value}")]
        [TestCase("a {{ b }} c", "a { b } c")]
        [TestCase("{{{{}}}}", "{{}}")]
        [TestCase("plain text", "plain text")]
        public void Manager_AllOverloadsDecodeEscapedBraces(string raw, string expected)
        {
            var config = ScriptableObject.CreateInstance<LocalizationConfig>();
            var en = ScriptableObject.CreateInstance<LanguageProfile>();
            try
            {
                en.languageCode = "en";
                config.languages.Add(en);
                var manager = new LocalizationManager(config);
                manager.SetDataProvider(new TextProvider(raw));
                manager.SetLanguage("en");
                Assert.That(manager.Get("key"), Is.EqualTo(expected));
                Assert.That(manager.Get("key", Array.Empty<object>()), Is.EqualTo(expected));
                Assert.That(manager.Get("key", new Dictionary<string, object>()), Is.EqualTo(expected));
                Assert.That(manager.Get("key", null, null), Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(en);
                Object.DestroyImmediate(config);
            }
        }

        [TestCase("{{{0}}}", "{Ada}")]
        [TestCase("{0|{{one}}|many}", "{one}")]
        [TestCase("x { } y", "x { } y")]
        [TestCase("x {missing } y", "x {missing } y")]
        [TestCase("{0|bad {1}|good}", "{0|bad {1}|good}")]
        [TestCase("{0|bad {1}|good} {0}", "{0|bad {1}|good} Ada")]
        [TestCase("x {unclosed", "x {unclosed")]
        [TestCase("x } y", "x } y")]
        [TestCase("{|one|other}", "{|one|other}")]
        public void Parser_EscapesAndMalformedTokensAreDeterministic(string raw, string expected)
        {
            object value = raw == "{0|{{one}}|many}" ? 1 : "Ada";
            Assert.That(Format(raw, "en", value), Is.EqualTo(expected));
        }

        [Test]
        public void Formatter_ReentrantParameterDoesNotCorruptOuterBuffer()
        {
            Assert.That(Format("before [{0}] after", "en", new NestedValue()), Is.EqualTo("before [inner 7] after"));
        }

        [TestCase("{0|{{|many}", "{")]
        [TestCase("{0|}}|many}", "}")]
        [TestCase("{0|{{one}}|many}", "{one}")]
        [TestCase("{0|one|many}}}", "one}")]
        [TestCase("{0|one|many}}", "one}")]
        [TestCase("{{{0|one|many}}}", "{one}")]
        public void Parser_FormEscapesPreserveLegacyOuterBraces(string raw, string expected)
        {
            Assert.That(Format(raw, "en", 1), Is.EqualTo(expected));
        }

        [Test]
        public void Formatter_ExceptionDoesNotPoisonNextCall()
        {
            Assert.Throws<InvalidOperationException>(() => Format("prefix {0}", "en", new ThrowingValue()));
            Assert.That(Format("next {0}", "en", 3), Is.EqualTo("next 3"));
        }

        // Explicit expected positions preserve the legacy prefix; extra categories go at the end.
        // Reference: Unicode CLDR 48 cardinal rules (no compact-number exponent).
        [TestCase("en", -1, 0)]
        [TestCase("es", 0, 1)]
        [TestCase("it", 0, 1)]
        [TestCase("ca", 0, 1)]
        [TestCase("gl", 0, 1)]
        [TestCase("fr", 0, 0)]
        [TestCase("fr", 1000000, 2)]
        [TestCase("pt-BR", 0, 0)]
        [TestCase("pt-PT", 0, 1)]
        [TestCase("PT_pt", 0, 1)]
        [TestCase("pt-Latn-PT", 0, 1)]
        [TestCase("pl", 21, 2)]
        [TestCase("pl", 22, 1)]
        [TestCase("pl", 12, 2)]
        [TestCase("cs", 21, 2)]
        [TestCase("sk", 22, 2)]
        [TestCase("sl", 2, 3)]
        [TestCase("sl", 3, 1)]
        [TestCase("sl", 101, 0)]
        [TestCase("ro", 0, 2)]
        [TestCase("ro", 20, 1)]
        [TestCase("ro", 101, 2)]
        [TestCase("sr", 21, 0)]
        [TestCase("bs", 11, 2)]
        [TestCase("hr", 22, 1)]
        [TestCase("ru", 21, 0)]
        [TestCase("uk", 11, 2)]
        [TestCase("be", 22, 1)]
        [TestCase("is", 21, 0)]
        [TestCase("is", 11, 1)]
        [TestCase("fi", 2, 1)]
        [TestCase("et", 0, 1)]
        [TestCase("tr", 2, 1)]
        [TestCase("hu", 0, 1)]
        [TestCase("ka", 2, 1)]
        [TestCase("ja", 42, 0)]
        [TestCase("ar", 0, 0)]
        [TestCase("ar", 2, 2)]
        [TestCase("ar", 103, 3)]
        [TestCase("ar", 100, 5)]
        [TestCase("pt-AO", 0, 1)]
        [TestCase("pt-CH", 0, 1)]
        [TestCase("oc", 1000000, 1)]
        [TestCase("pt-BR-x-pt", 0, 0)]
        [TestCase("ar", int.MinValue, 4)]
        [TestCase("fr", int.MinValue, 1)]
        [TestCase("unknown", 2, 1)]
        public void CardinalRules_RespectLanguageAndBoundaries(string language, int count, int expected)
        {
            Assert.That(PluralRuleProvider.GetRule(language).Evaluate(count), Is.EqualTo(expected));
        }

        [TestCase("en", "1.0", "other")]
        [TestCase("en", "1.5", "other")]
        [TestCase("ru", "1.5", "decimal")]
        [TestCase("ru", "1.0", "decimal")]
        [TestCase("pl", "2.5", "decimal")]
        [TestCase("cs", "2.5", "decimal")]
        [TestCase("sr", "1.1", "one")]
        [TestCase("sr", "1.2", "other")]
        [TestCase("sl", "1.5", "other")]
        [TestCase("fr", "1.5", "one")]
        [TestCase("da", "0.5", "one")]
        [TestCase("is", "2.1", "one")]
        [TestCase("be", "1.0", "one")]
        [TestCase("be", "1.5", "decimal")]
        [TestCase("sr", "1.10", "third")]
        [TestCase("es", "1.0", "one")]
        [TestCase("pt-PT", "1.0", "other")]
        [TestCase("fi", "1.0", "other")]
        public void Formatter_PreservesFractionalOperands(string language, string value, string expected)
        {
            var number = decimal.Parse(value, CultureInfo.InvariantCulture);
            Assert.That(Format("{0|one|other|third|decimal}", language, number), Is.EqualTo(expected));
        }

        [Test]
        public void Formatter_LargeIntegersDoNotWrapToInt32()
        {
            Assert.That(Format("{0|one|other}", "en", 4294967297L), Is.EqualTo("other"));
            Assert.That(Format("{0|one|few|many|other}", "ru", 4294967301L), Is.EqualTo("one"));
            Assert.That(Format("{0|zero|one|two|few|many|other}", "ar", long.MinValue), Is.EqualTo("few"));
            Assert.That(Format("{0|one|other}", "en", ulong.MaxValue), Is.EqualTo("other"));
        }

        [Test]
        public void Formatter_NumericStringsAreCultureIndependent_AndLegacyDefaultsRemain()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.That(Format("{0|one|other}", "en", "1.5"), Is.EqualTo("other"));
                Assert.That(Format("{0|one|other}", "en", "1"), Is.EqualTo("one"));
                Assert.That(Format("{0|one|other}", "fr", null), Is.EqualTo("one"));
                Assert.That(Format("{0|one|other}", "en", "not a number"), Is.EqualTo("other"));
                Assert.That(Format("{0|only}", "ar", 100), Is.EqualTo("only"));
                Assert.That(Format("{0|one|}", "en", 2), Is.Empty);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [TestCase("en")]
        [TestCase("ru")]
        [TestCase("ar")]
        [TestCase("ja")]
        public void ExplicitSelector_DoesNotUsePluralRule_AndWorksWithExistingTokens(string language)
        {
            var template = TokenParser.Parse("{name}: {0|Warrior|Mage|Rogue}, {gender|He|She}");
            var named = new Dictionary<string, object> { ["name"] = "Alex", ["gender"] = new FormSelector(1) };
            Assert.That(TextFormatter.Format(template, language, new object[] { new FormSelector(2) }, named),
                Is.EqualTo("Alex: Rogue, She"));
            Assert.That(Format("{0|first|last}", language, new FormSelector(-1)), Is.EqualTo("first"));
            Assert.That(Format("{0|first|last}", language, new FormSelector(int.MaxValue)), Is.EqualTo("last"));
            Assert.That(Format("{0}", language, new FormSelector(1)), Is.EqualTo("1"));
        }

        [Test]
        public void PlainNumericSelectorLikeArgumentsRemainPluralForCompatibility()
        {
            Assert.That(TextFormatter.Format(TokenParser.Parse("{gender|He|She}"), "en",
                new Dictionary<string, object> { ["gender"] = 1 }), Is.EqualTo("He"));
        }

        [Test]
        public void Formatter_BuiltInNumericTypesAndNonFiniteValuesAreSafe()
        {
            foreach (var value in new object[] { (sbyte)1, (byte)1, (short)1, (ushort)1, 1, 1u, 1L, 1UL, 1f, 1d, 1m })
                Assert.That(Format("{0|one|other}", "en", value), Is.EqualTo("one"));
            foreach (var value in new object[] { 1.5f, 1.5d, 1.5m, decimal.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
                Assert.That(Format("{0|one|other}", "en", value), Is.EqualTo("other"));
            Assert.That(Format("{0|zero|one|two|few|many|other}", "ar", 3.5m), Is.EqualTo("other"));
        }

        [Test]
        public void ShortLegacyFormsAndManuallyConstructedTokensRemainValid()
        {
            Assert.That(Format("{0|один|несколько|много}", "ru", 5), Is.EqualTo("много"));
            Assert.That(Format("{0|un|plusieurs}", "fr", 1000000), Is.EqualTo("plusieurs"));
            var template = new ParsedTemplate(new List<Token> { Token.MakeIndexed(0), Token.MakeText(" "), Token.MakeNamed("name") });
            Assert.That(TextFormatter.Format(template, "en", new object[] { 3 }), Is.EqualTo("3 {name}"));
        }

        [Test]
        public void ParsedTemplateCanBeReusedAcrossLanguagesAndThreads()
        {
            var template = TokenParser.Parse("{{coins}} {0|one|few|many|other}");
            System.Threading.Tasks.Parallel.For(0, 100, i =>
            {
                string language = i % 2 == 0 ? "ru" : "en";
                string expected = i % 2 == 0 ? "{coins} one" : "{coins} few";
                Assert.That(TextFormatter.Format(template, language, new object[] { 21 }), Is.EqualTo(expected));
            });
        }

        [Test]
        public void Parser_DeepMalformedInputIsLiteral_WithoutRecursion()
        {
            string text = "{0|" + new string('{', 10001) + "broken" + new string('}', 10001) + "|other}";
            Assert.That(Format(text, "en", 1), Is.EqualTo(text));
        }

        [Test]
        public void CustomInt32RuleStillWorks_WithoutOverflow()
        {
            var rule = new RecordingRule();
            PluralRuleProvider.RegisterRule("test-int32", rule);
            Assert.That(Format("{0|first|second}", "test-int32", long.MaxValue), Is.EqualTo("second"));
            Assert.That(rule.LastCount, Is.EqualTo(int.MaxValue));
            Format("{0|first|second}", "test-int32", -1.5m);
            Assert.That(rule.LastCount, Is.EqualTo(-1));
        }

        [Test]
        public void CustomRules_NormalizeCodesAndInvalidateRegionalLookups()
        {
            PluralRuleProvider.GetRule("test-custom-ZZ");
            var rule = new FixedRule(1);
            PluralRuleProvider.RegisterRule("TEST_CUSTOM", rule);
            Assert.That(PluralRuleProvider.GetRule("test-custom-ZZ"), Is.SameAs(rule));
            var region = new FixedRule(0);
            PluralRuleProvider.RegisterRule("test-custom-zz", region);
            Assert.That(PluralRuleProvider.GetRule("TEST_CUSTOM_ZZ"), Is.SameAs(region));
            Assert.That(Format("{0|first|second}", "test-custom-ZZ", 1), Is.EqualTo("first"));
        }

        [Test]
        public void CustomRules_RejectInvalidRegistration()
        {
            Assert.Throws<ArgumentException>(() => PluralRuleProvider.RegisterRule(" ", new FixedRule(0)));
            Assert.Throws<ArgumentNullException>(() => PluralRuleProvider.RegisterRule("test-invalid", null));
        }

        private sealed class NestedValue
        {
            public override string ToString() => Format("inner {0}", "en", 7);
        }
        private sealed class ThrowingValue
        {
            public override string ToString() => throw new InvalidOperationException("Parameter failure");
        }
        private sealed class FixedRule : IPluralRule
        {
            private readonly int _index;
            public FixedRule(int index) => _index = index;
            public int FormCount => 2;
            public int Evaluate(int count) => _index;
        }
        private sealed class RecordingRule : IPluralRule
        {
            public int LastCount;
            public int FormCount => 2;
            public int Evaluate(int count) { LastCount = count; return 1; }
        }
        private sealed class TextProvider : ILocalizationDataProvider
        {
            private readonly string _text;
            public TextProvider(string text) => _text = text;
            public Dictionary<string, string> LoadTextData(string languageCode) => new() { ["key"] = _text };
            public bool HasTextData(string languageCode) => true;
            public List<LocalizationAssetTable> LoadAssetTables(string languageCode) => new();
        }
    }
}
