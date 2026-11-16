using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProfileApplierTests
    {
        private readonly List<Object> _objects = new();
        private readonly List<(ProfileApplier applier, TMP_Text text)> _appliers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _appliers) pair.applier.Apply(pair.text, null);
            _appliers.Clear();
            foreach (var obj in _objects) if (obj is GameObject) Object.DestroyImmediate(obj);
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null && _objects[i] is not GameObject) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void TMP_RepeatedProfileApplicationsAndResetAreIdempotent()
        {
            var text = MakeTMP();
            text.fontSize = 20;
            text.lineSpacing = 2;
            text.characterSpacing = 3;
            text.wordSpacing = 4;
            text.alignment = TextAlignmentOptions.TopLeft;
            var applier = Applier(text);
            var profile = Profile();
            profile.overrideTypography = profile.overrideSpacing = profile.overrideLayout = true;
            profile.fontSizeMultiplier = 1.5f;
            profile.lineSpacingAdjustment = 5;
            profile.characterSpacingAdjustment = 6;
            profile.wordSpacingAdjustment = 7;
            profile.textDirection = TextDirection.RTL;
            profile.overrideAlignment = true;
            profile.alignmentOverride = TextAlignmentOptions.BottomRight;
            for (int i = 0; i < 4; i++) applier.Apply(text, profile);
            Assert.That(text.fontSize, Is.EqualTo(30));
            Assert.That(text.lineSpacing, Is.EqualTo(7));
            Assert.That(text.characterSpacing, Is.EqualTo(9));
            Assert.That(text.wordSpacing, Is.EqualTo(11));
            Assert.That(text.isRightToLeftText, Is.True);
            applier.Apply(text, null);
            Assert.That(text.fontSize, Is.EqualTo(20));
            Assert.That(text.lineSpacing, Is.EqualTo(2));
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(text.isRightToLeftText, Is.False);
        }

        [Test]
        public void TMP_AutoSizeBoundsScaleAndRestoreWithoutDisablingAutoSize()
        {
            var text = MakeTMP();
            text.fontSizeMin = 10;
            text.fontSizeMax = 40;
            text.enableAutoSizing = true;
            var applier = Applier(text);
            var profile = Profile();
            profile.overrideTypography = true;
            profile.fontSizeMultiplier = 1.5f;
            applier.Apply(text, profile);
            applier.Apply(text, profile);
            Assert.That(text.enableAutoSizing, Is.True);
            Assert.That(text.fontSizeMin, Is.EqualTo(15));
            Assert.That(text.fontSizeMax, Is.EqualTo(60));
            applier.Apply(text, null);
            Assert.That(text.fontSizeMin, Is.EqualTo(10));
            Assert.That(text.fontSizeMax, Is.EqualTo(40));
        }

        [Test]
        public void Legacy_BestFitBoundsScaleAndRestore()
        {
            var text = MakeLegacy();
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 40;
            var applier = new ProfileApplier();
            applier.CacheOriginals(text);
            var profile = Profile();
            profile.overrideTypography = true;
            profile.fontSizeMultiplier = 1.5f;
            applier.Apply(text, profile);
            Assert.That(text.resizeTextForBestFit, Is.True);
            Assert.That(text.resizeTextMinSize, Is.EqualTo(15));
            Assert.That(text.resizeTextMaxSize, Is.EqualTo(60));
            applier.Apply(text, null);
            Assert.That(text.resizeTextMinSize, Is.EqualTo(10));
            Assert.That(text.resizeTextMaxSize, Is.EqualTo(40));
        }

        [TestCase(FontStyles.Bold | FontStyles.Italic, FontStyle.BoldAndItalic)]
        [TestCase(FontStyles.Bold | FontStyles.Underline, FontStyle.Bold)]
        [TestCase(FontStyles.Italic | FontStyles.Strikethrough, FontStyle.Italic)]
        public void Legacy_MapsSupportedStyleFlags(FontStyles style, FontStyle expected)
        {
            var text = MakeLegacy();
            var applier = new ProfileApplier();
            applier.CacheOriginals(text);
            var profile = Profile();
            profile.overrideTypography = true;
            profile.fontStyle = style;
            applier.Apply(text, profile);
            Assert.That(text.fontStyle, Is.EqualTo(expected));
        }

        [TestCase(TextAlignmentOptions.TopLeft, TextAnchor.UpperLeft)]
        [TestCase(TextAlignmentOptions.Top, TextAnchor.UpperCenter)]
        [TestCase(TextAlignmentOptions.TopRight, TextAnchor.UpperRight)]
        [TestCase(TextAlignmentOptions.Left, TextAnchor.MiddleLeft)]
        [TestCase(TextAlignmentOptions.Center, TextAnchor.MiddleCenter)]
        [TestCase(TextAlignmentOptions.Right, TextAnchor.MiddleRight)]
        [TestCase(TextAlignmentOptions.BottomLeft, TextAnchor.LowerLeft)]
        [TestCase(TextAlignmentOptions.Bottom, TextAnchor.LowerCenter)]
        [TestCase(TextAlignmentOptions.BottomRight, TextAnchor.LowerRight)]
        public void Legacy_MapsNineAlignmentPositions(TextAlignmentOptions alignment, TextAnchor expected)
        {
            var text = MakeLegacy();
            var applier = new ProfileApplier();
            applier.CacheOriginals(text);
            var profile = Profile();
            profile.overrideLayout = profile.overrideAlignment = true;
            profile.alignmentOverride = alignment;
            applier.Apply(text, profile);
            Assert.That(text.alignment, Is.EqualTo(expected));
        }

        [Test]
        public void FallbackFonts_AreIsolatedBetweenProfiles_AndSharedSourcesSurviveReset()
        {
            var primary = MakeFont("A ");
            var fallbackA = MakeFont("B");
            var fallbackB = MakeFont("C");
            var first = MakeTMP(primary);
            var second = MakeTMP(primary);
            var firstApplier = Applier(first);
            var secondApplier = Applier(second);
            var profile = Profile();
            profile.overrideFont = true;
            profile.primaryFont = primary;
            profile.fallbackFont = fallbackA;
            firstApplier.Apply(first, profile);
            var firstFont = first.font;
            profile.fallbackFont = fallbackB;
            secondApplier.Apply(second, profile);
            Assert.That(primary.fallbackFontAssetTable, Is.Empty);
            Assert.That(FindCharacter(firstFont, 'B'), Is.Not.Null);
            Assert.That(FindCharacter(firstFont, 'C'), Is.Null);
            Assert.That(FindCharacter(second.font, 'C'), Is.Not.Null);
            Assert.That(FindCharacter(second.font, 'B'), Is.Null);
            firstApplier.Apply(first, null);
            secondApplier.Apply(second, null);
            Assert.That(first.font, Is.SameAs(primary));
            Assert.That(firstFont == null, Is.True, "Transient fonts must be released.");
            Assert.That(primary.material != null, Is.True);
            Assert.That(primary.atlasTexture != null, Is.True);
            Assert.That(primary.HasCharacter('A'), Is.True);
        }

        [Test]
        public void FallbackOnlyProfile_UsesOriginalFontAndPreservesItsMaterialPreset()
        {
            var original = MakeFont("A ");
            var fallback = MakeFont("B");
            var text = MakeTMP(original);
            var preset = Track(new Material(original.material));
            text.fontSharedMaterial = preset;
            var applier = Applier(text);
            var profile = Profile();
            profile.overrideFont = true;
            profile.fallbackFont = fallback;
            applier.Apply(text, profile);
            Assert.That(text.font.HasCharacter('B', true), Is.True);
            Assert.That(text.fontSharedMaterial, Is.SameAs(preset));
            applier.Apply(text, null);
            Assert.That(text.font, Is.SameAs(original));
            Assert.That(text.fontSharedMaterial, Is.SameAs(preset));
            Assert.That(original.fallbackFontAssetTable, Is.Empty);
        }

        [Test]
        public void SharedFontVariant_IsReusedAndReleasedOnlyAfterLastUser()
        {
            var primary = MakeFont("A ");
            var fallback = MakeFont("B");
            var first = MakeTMP(primary);
            var second = MakeTMP(primary);
            var firstApplier = Applier(first);
            var secondApplier = Applier(second);
            var profile = Profile();
            profile.overrideFont = true;
            profile.primaryFont = primary;
            profile.fallbackFont = fallback;
            firstApplier.Apply(first, profile);
            var variant = first.font;
            for (int i = 0; i < 4; i++) firstApplier.Apply(first, profile);
            secondApplier.Apply(second, profile);
            Assert.That(first.font, Is.SameAs(variant));
            Assert.That(second.font, Is.SameAs(variant));
            firstApplier.Dispose();
            Assert.That(variant != null, Is.True);
            Assert.That(FindCharacter(second.font, 'B'), Is.Not.Null);
            secondApplier.Dispose();
            Assert.That(variant == null, Is.True);
            Assert.That(primary.material != null && primary.atlasTexture != null, Is.True);
        }

        [Test]
        public void CacheOriginals_KeepsTMPAndLegacyBaselinesSeparate()
        {
            var tmp = MakeTMP();
            tmp.fontSize = 20;
            var legacy = MakeLegacy();
            legacy.fontSize = 17;
            var applier = Applier(tmp);
            applier.CacheOriginals(legacy);
            var profile = Profile();
            profile.overrideTypography = true;
            profile.fontSizeMultiplier = 2;
            applier.Apply(legacy, profile);
            Assert.That(legacy.fontSize, Is.EqualTo(34));
            applier.Apply(tmp, profile);
            Assert.That(tmp.fontSize, Is.EqualTo(40));
        }

        [Test]
        public void AppearanceAppliesAndRestoresWithoutALegacyLanguageProfile()
        {
            var text = MakeTMP(); text.fontSize = 20; text.characterSpacing = 2; text.alignment = TextAlignmentOptions.TopLeft;
            var applier = Applier(text); var appearance = Track(ScriptableObject.CreateInstance<LocalizationAppearance>());
            appearance.overrideTypography = appearance.overrideSpacing = appearance.overrideLayout = true;
            appearance.fontSizeMultiplier = 1.5f; appearance.characterSpacingAdjustment = 3;
            appearance.textDirection = TextDirection.RTL; appearance.overrideAlignment = true; appearance.alignmentOverride = TextAlignmentOptions.Right;
            applier.Apply(text, appearance); applier.Apply(text, appearance);
            Assert.That(text.fontSize, Is.EqualTo(30)); Assert.That(text.characterSpacing, Is.EqualTo(5)); Assert.That(text.isRightToLeftText, Is.True);
            applier.Restore(); Assert.That(text.fontSize, Is.EqualTo(20)); Assert.That(text.characterSpacing, Is.EqualTo(2)); Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
        }
        [Test]
        public void AppearanceUsesTheSameFontVariantOwnershipForTwoConsumers()
        {
            var source = MakeFont("A"); var fallback = MakeFont("B"); var first = MakeTMP(source); var second = MakeTMP(source);
            var a = Applier(first); var b = Applier(second); var appearance = Track(ScriptableObject.CreateInstance<LocalizationAppearance>());
            appearance.overrideFont = true; appearance.primaryFont = source; appearance.fallbackFont = fallback;
            a.Apply(first, appearance); b.Apply(second, appearance); var variant = first.font;
            Assert.That(second.font, Is.SameAs(variant)); Assert.That(FindCharacter(second.font, 'B'), Is.Not.Null);
            a.Restore(); Assert.That(FindCharacter(second.font, 'B'), Is.Not.Null); b.Restore();
            Assert.That(variant == null, Is.True); Assert.That(source.fallbackFontAssetTable, Is.Empty); Assert.That(source.material != null && source.atlasTexture != null, Is.True);
        }
        [Test]
        public void ExistingSerializedProfileFieldsStillPopulateTheSharedAppearanceModel()
        {
            var profile = Profile();
            EditorJsonUtility.FromJsonOverwrite("{\"MonoBehaviour\":{\"languageCode\":\"ru\",\"overrideTypography\":true,\"fontSizeMultiplier\":1.5,\"overrideSpacing\":true,\"lineSpacingAdjustment\":4}}", profile);
            var text = MakeLegacy(); text.fontSize = 20; using var applier = new ProfileApplier(); applier.CacheOriginals(text);
            applier.Apply(text, profile); Assert.That(text.fontSize, Is.EqualTo(30)); Assert.That(profile.Code, Is.EqualTo("ru"));
            Assert.That(profile.lineSpacingAdjustment, Is.EqualTo(4)); applier.Restore(); Assert.That(text.fontSize, Is.EqualTo(20));
        }

        // Unlike HasCharacter(searchFallbacks: true), this excludes TMP's global/default fonts.
        private static TMP_Character FindCharacter(TMP_FontAsset font, char character) =>
            TMP_FontAssetUtilities.GetCharacterFromFontAsset(character, font, true,
                FontStyles.Normal, FontWeight.Regular, out _);

        private T Track<T>(T obj) where T : Object { _objects.Add(obj); return obj; }
        private LanguageProfile Profile() => Track(ScriptableObject.CreateInstance<LanguageProfile>());
        private Text MakeLegacy() => Track(new GameObject("Legacy profile test", typeof(RectTransform))).AddComponent<Text>();
        private TMP_Text MakeTMP(TMP_FontAsset font = null)
        {
            var go = Track(new GameObject("TMP profile test", typeof(RectTransform)));
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            return text;
        }
        private ProfileApplier Applier(TMP_Text text)
        {
            var applier = new ProfileApplier();
            applier.CacheOriginals(text);
            _appliers.Add((applier, text));
            return applier;
        }
        private TMP_FontAsset MakeFont(string characters)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            Assert.That(source, Is.Not.Null, "Import TMP Essential Resources to run font rendering tests.");
            var font = Track(TMP_FontAsset.CreateFontAsset(source));
            Assert.That(font, Is.Not.Null);
            Assert.That(font.TryAddCharacters(characters), Is.True);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            return font;
        }
    }
}
