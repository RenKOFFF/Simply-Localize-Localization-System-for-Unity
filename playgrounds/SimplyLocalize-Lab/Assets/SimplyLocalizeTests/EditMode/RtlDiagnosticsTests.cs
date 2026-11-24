using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class RtlDiagnosticsTests
    {
        private string _root;
        private GameObject _canvas;
        private TMP_FontAsset _font;
        private LocalizationAppearance _profile;
        private ProfileApplier _applier;

        [TearDown]
        public void TearDown()
        {
            _applier?.Dispose();
            if (_canvas != null) Object.DestroyImmediate(_canvas);
            if (_font != null) Object.DestroyImmediate(_font);
            if (_profile != null) Object.DestroyImmediate(_profile);
            if (_root != null) AssetDatabase.DeleteAsset(_root);
        }

        [Test]
        public void NativeTMP_RendersMixedArabicGlyphsAndRichText_AndRestoresLayout()
        {
            // Exercise actual Arabic glyphs without distributing a third-party font with the package.
            string systemFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (!File.Exists(systemFont)) Assert.Ignore("This rendering diagnostic needs an installed Arial font with Arabic glyphs.");
            _root = "Assets/SimplyLocalizeRtlTest_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_root);
            string fontPath = _root + "/ArabicTest.ttf";
            File.Copy(systemFont, fontPath);
            AssetDatabase.ImportAsset(fontPath, ImportAssetOptions.ForceSynchronousImport);
            _font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(fontPath));
            const string plain = "مرحبا 123, ABC!\nالعالم";
            Assert.That(_font.TryAddCharacters(plain.Replace("\n", "")), Is.True);
            _canvas = new GameObject("RTL diagnostic", typeof(Canvas));
            var go = new GameObject("Mixed text", typeof(RectTransform));
            go.transform.SetParent(_canvas.transform);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = 24;
            text.rectTransform.sizeDelta = new Vector2(700, 300);
            text.alignment = TextAlignmentOptions.TopLeft;
            const string rich = "مرحبا 123, ABC!\n<b>العالم</b>";
            text.text = rich;
            _applier = new ProfileApplier();
            _applier.CacheOriginals(text);
            _profile = ScriptableObject.CreateInstance<LocalizationAppearance>();
            _profile.overrideLayout = _profile.overrideAlignment = true;
            _profile.textDirection = TextDirection.RTL;
            _profile.alignmentOverride = TextAlignmentOptions.TopRight;
            _applier.Apply(text, _profile);
            text.ForceMeshUpdate();
            Assert.That(text.text, Is.EqualTo(rich), "Localization must not reverse or rewrite source/rich-text tags.");
            Assert.That(text.isRightToLeftText, Is.True);
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.TopRight));
            var characters = text.textInfo.characterInfo.Take(text.textInfo.characterCount).ToArray();
            Assert.That(characters.Any(c => c.character == 'م' && c.isVisible), Is.True);
            Assert.That(characters.Any(c => c.character == '1' && c.isVisible), Is.True);
            Assert.That(characters.Any(c => c.character == 'A' && c.isVisible), Is.True);
            Assert.That(text.textInfo.lineCount, Is.GreaterThanOrEqualTo(2));
            _applier.Restore();
            text.ForceMeshUpdate();
            Assert.That(text.isRightToLeftText, Is.False);
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(text.text, Is.EqualTo(rich));
            // This deliberately does not assert Unicode BiDi ordering or Arabic shaping:
            // SimplyLocalize delegates layout to TMP and supplies neither algorithm.
        }
    }
}
