using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Data;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class ProfileOverrideTests
    {
        private readonly List<Object> _objects = new();
        private readonly List<TMP_FontAsset> _borrowedAtlasFonts = new();
        private LanguageProfile _en, _ru;

        [SetUp]
        public void SetUp()
        {
            var config = Track(ScriptableObject.CreateInstance<LocalizationConfig>());
            _en = Track(ScriptableObject.CreateInstance<LanguageProfile>());
            _ru = Track(ScriptableObject.CreateInstance<LanguageProfile>());
            _en.languageCode = "en";
            _ru.languageCode = "ru";
            _en.overrideTypography = _en.overrideSpacing = true;
            _en.fontSizeMultiplier = 1.8f;
            _en.lineSpacingAdjustment = 10;
            config.languages.Add(_en);
            config.languages.Add(_ru);
            Localization.Initialize(config);
            Localization.SetDataProvider(new Provider());
            Localization.SetLanguage("en");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var obj in _objects) if (obj is GameObject go) go.SetActive(false);
            Localization.Shutdown();
            foreach (var obj in _objects) if (obj is GameObject) Object.Destroy(obj);
            yield return null;
            foreach (var font in _borrowedAtlasFonts)
                if (font != null) font.atlasTextures = System.Array.Empty<Texture2D>();
            _borrowedAtlasFonts.Clear();
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null && _objects[i] is not GameObject) Object.Destroy(_objects[i]);
            _objects.Clear();
            yield return null;
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        public void Overrides_AreStableAcrossReloadLanguageSwitchAndReenable(bool tmp, int owner)
        {
            var go = MakeText(tmp, owner, out var local);
            go.SetActive(true);
            for (int i = 0; i < 3; i++)
            {
                Localization.Reload();
                AssertSize(go, tmp, 30);
                if (tmp) Assert.That(go.GetComponent<TMP_Text>().lineSpacing, Is.EqualTo(5));
            }
            Localization.SetLanguage("ru");
            AssertSize(go, tmp, 20);
            Localization.SetLanguage("en");
            AssertSize(go, tmp, 30);
            go.SetActive(false);
            go.SetActive(true);
            AssertSize(go, tmp, 30);
            local.enabled = false;
            AssertSize(go, tmp, owner == 0 ? 20 : 36);
            local.enabled = true;
            AssertSize(go, tmp, 30);
        }

        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        public void EmptyTranslationKeyStillAppliesProfile(bool tmp, int owner)
        {
            var go = MakeText(tmp, owner, out _);
            go.GetComponent<LocalizedComponentBase>().Key = "";
            go.SetActive(true);
            AssertSize(go, tmp, 30);
        }

        [Test]
        public void FontAndLayoutOverridesWithUnsetValuesRestoreOriginals()
        {
            var go = MakeText(true, 1, out var local);
            var text = go.GetComponent<TMP_Text>();
            text.alignment = TextAlignmentOptions.TopLeft;
            var originalFont = text.font;
            var originalMaterial = text.fontSharedMaterial;
            var entry = local.Entries[0];
            entry.overrideFont = entry.overrideLayout = true;
            entry.overrideAlignment = true;
            entry.tmpAlignmentOverride = TextAlignmentOptions.BottomRight;
            entry.textDirection = TextDirection.RTL;
            go.SetActive(true);
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.BottomRight));
            entry.overrideAlignment = false;
            Localization.Reload();
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(text.font, Is.SameAs(originalFont));
            Assert.That(text.fontSharedMaterial, Is.SameAs(originalMaterial));
        }

        [Test]
        public void LocalFontOverride_PreservesOriginalPresetUnlessMaterialIsExplicit()
        {
            var go = MakeText(true, 1, out var local);
            var text = go.GetComponent<TMP_Text>();
            var originalPreset = Track(new Material(text.font.material));
            var overridePreset = Track(new Material(text.font.material));
            text.fontSharedMaterial = originalPreset;
            var entry = local.Entries[0];
            entry.overrideFont = true;
            entry.tmpFont = text.font;
            go.SetActive(true);
            Assert.That(text.fontSharedMaterial, Is.SameAs(originalPreset));
            entry.tmpMaterial = overridePreset;
            Localization.Reload();
            Assert.That(text.fontSharedMaterial, Is.SameAs(overridePreset));
            entry.tmpMaterial = null;
            Localization.Reload();
            Assert.That(text.fontSharedMaterial, Is.SameAs(originalPreset));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LocalTypography_ScalesAutoSizeBoundsFromOriginals(bool tmp)
        {
            var go = MakeText(tmp, 1, out _);
            var text = go.GetComponent<TMP_Text>();
            var legacy = go.GetComponent<Text>();
            if (tmp)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 10;
                text.fontSizeMax = 40;
            }
            else
            {
                legacy.resizeTextForBestFit = true;
                legacy.resizeTextMinSize = 10;
                legacy.resizeTextMaxSize = 40;
            }
            go.SetActive(true);
            for (int i = 0; i < 3; i++) Localization.Reload();
            Assert.That(tmp ? text.fontSizeMin : legacy.resizeTextMinSize, Is.EqualTo(15));
            Assert.That(tmp ? text.fontSizeMax : legacy.resizeTextMaxSize, Is.EqualTo(60));
            Localization.SetLanguage("ru");
            Assert.That(tmp ? text.fontSizeMin : legacy.resizeTextMinSize, Is.EqualTo(10));
            Assert.That(tmp ? text.fontSizeMax : legacy.resizeTextMaxSize, Is.EqualTo(40));
        }

        [Test]
        public void UnrelatedLocalizedComponentAndNullEntryDoNotSuppressStandaloneProfile()
        {
            var go = MakeText(true, 0, out var local);
            go.AddComponent<LocalizedSprite>();
            ((List<LocalizedProfileOverride.OverrideEntry>)local.Entries).Insert(0, null);
            go.SetActive(true);
            AssertSize(go, true, 30);
            Localization.SetLanguage("ru");
            AssertSize(go, true, 20);
        }

        [UnityTest]
        public IEnumerator FallbackGlyphs_RenderAfterLanguageSwitch_AndSourcesSurviveDisable()
        {
            var primary = MakeStaticFont("A ");
            var fallbackB = MakeStaticFont("B");
            var fallbackC = MakeStaticFont("C");
            _en.overrideFont = _ru.overrideFont = true;
            _en.primaryFont = _ru.primaryFont = primary;
            _en.fallbackFont = fallbackB;
            _ru.fallbackFont = fallbackC;
            var canvas = Track(new GameObject("Profile canvas", typeof(Canvas)));
            var go = MakeText(true, 0, out _);
            go.transform.SetParent(canvas.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.rectTransform.sizeDelta = new Vector2(400, 100);
            go.SetActive(true);
            text.text = "AB";
            text.ForceMeshUpdate();
            Assert.That(text.textInfo.characterCount, Is.EqualTo(2));
            Assert.That(text.textInfo.characterInfo[1].fontAsset, Is.SameAs(fallbackB));
            var previousVariant = text.font;
            Localization.SetLanguage("ru");
            text.text = "AC";
            text.ForceMeshUpdate();
            Assert.That(text.textInfo.characterInfo[1].fontAsset, Is.SameAs(fallbackC));
            var currentVariant = text.font;
            go.SetActive(false);
            yield return null;
            Assert.That(previousVariant == null && currentVariant == null, Is.True);
            Assert.That(primary.fallbackFontAssetTable, Is.Empty);
            Assert.That(primary.material != null && primary.atlasTexture != null, Is.True);
            Assert.That(TMP_Settings.defaultFontAsset.atlasTexture != null, Is.True);
        }

        private TMP_FontAsset MakeStaticFont(string characters)
        {
            var source = TMP_Settings.defaultFontAsset;
            Assert.That(source, Is.Not.Null, "Import TMP Essential Resources to run font rendering tests.");
            var font = Track(Object.Instantiate(source));
            _borrowedAtlasFonts.Add(font);
            font.material = new Material(source.material);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            font.characterTable.RemoveAll(c => characters.IndexOf((char)c.unicode) < 0);
            font.ReadFontAssetDefinition();
            return font;
        }

        [Test]
        public void OverrideAddedAtRuntimeIsAppliedAndDisabledOverrideIsIgnored()
        {
            var go = MakeText(false, 1, out var local);
            Object.DestroyImmediate(local);
            go.SetActive(true);
            AssertSize(go, false, 36);
            local = go.AddComponent<LocalizedProfileOverride>();
            AddEntry(local);
            Localization.Reload();
            AssertSize(go, false, 30);
            local.enabled = false;
            AssertSize(go, false, 36);
        }

        private GameObject MakeText(bool tmp, int owner, out LocalizedProfileOverride local)
        {
            var go = Track(new GameObject("Profile override test", typeof(RectTransform)));
            go.SetActive(false);
            if (tmp)
            {
                var text = go.AddComponent<TextMeshProUGUI>();
                text.font = TMP_Settings.defaultFontAsset;
                text.fontSize = 20;
                text.lineSpacing = 2;
            }
            else go.AddComponent<Text>().fontSize = 20;
            if (owner == 1) go.AddComponent<LocalizedText>().Key = "label";
            if (owner == 2) go.AddComponent<FormattableLocalizedText>().Key = "label";
            local = go.AddComponent<LocalizedProfileOverride>();
            AddEntry(local);
            return go;
        }
        private void AddEntry(LocalizedProfileOverride local) => ((List<LocalizedProfileOverride.OverrideEntry>)local.Entries).Add(
            new LocalizedProfileOverride.OverrideEntry
            {
                profile = _en, overrideTypography = true, fontSizeMultiplier = 1.5f,
                overrideSpacing = true, lineSpacingAdjustment = 3
            });
        private void AssertSize(GameObject go, bool tmp, float size) =>
            Assert.That(tmp ? go.GetComponent<TMP_Text>().fontSize : go.GetComponent<Text>().fontSize, Is.EqualTo(size));
        private T Track<T>(T obj) where T : Object { _objects.Add(obj); return obj; }
        private sealed class Provider : ILocalizationDataProvider
        {
            public Dictionary<string, string> LoadTextData(string languageCode) => new() { ["label"] = "Text" };
            public bool HasTextData(string languageCode) => true;
            public List<LocalizationAssetTable> LoadAssetTables(string languageCode) => new();
        }
    }
}
