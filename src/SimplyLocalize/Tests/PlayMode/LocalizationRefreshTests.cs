using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Data;
using SimplyLocalize.TextProcessing;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    public class LocalizationRefreshTests
    {
        private readonly List<Object> _objects = new();
        private Provider _provider;
        private Text _text;
        private SpriteRenderer _renderer;
        private Sprite _first;
        private Sprite _second;

        [SetUp]
        public void SetUp()
        {
            Localization.Shutdown();
            var config = Track(ScriptableObject.CreateInstance<LocalizationConfig>());
            var profile = Track(ScriptableObject.CreateInstance<LanguageProfile>());
            profile.languageCode = "en";
            config.languages.Add(profile);
            var texture = Track(new Texture2D(2, 2));
            _first = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            _second = Track(Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero));
            _provider = MakeProvider("Before", _first);
            Localization.Initialize(config);
            Localization.SetDataProvider(_provider);
            Localization.SetLanguage("en");

            var textObject = Track(new GameObject("Localized text test", typeof(RectTransform)));
            textObject.SetActive(false);
            _text = textObject.AddComponent<Text>();
            textObject.AddComponent<LocalizedText>().Key = "label";
            textObject.SetActive(true);

            var spriteObject = Track(new GameObject("Localized sprite test"));
            spriteObject.SetActive(false);
            _renderer = spriteObject.AddComponent<SpriteRenderer>();
            spriteObject.AddComponent<LocalizedSprite>().Key = "icon";
            spriteObject.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Disable components before shutting down so their static subscriptions are removed.
            foreach (var value in _objects)
                if (value is GameObject gameObject) gameObject.SetActive(false);
            Localization.Shutdown();
            for (int i = _objects.Count - 1; i >= 0; i--) Object.Destroy(_objects[i]);
            _objects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reload_RefreshesActiveTextAndSpriteWithoutChangingLanguage()
        {
            yield return null;
            AssertTargets("Before", _first);
            _provider.Text = "After reload";
            _provider.Table = Table(_second);
            Localization.Reload();
            AssertTargets("After reload", _second);
            yield return null;
            AssertTargets("After reload", _second);

            _provider.Text = "Failed reload";
            _provider.FailAssets = true;
            Assert.Throws<InvalidOperationException>(() => Localization.Reload());
            yield return null;
            AssertTargets("After reload", _second);
            Assert.That(Localization.Get("label"), Is.EqualTo(_text.text));
        }

        [UnityTest]
        public IEnumerator ProviderChange_RefreshesActiveComponents_AndFailureKeepsThemConsistent()
        {
            yield return null;
            AssertTargets("Before", _first);
            var next = MakeProvider("After provider change", _second);
            next.FailAssets = true;
            Assert.Throws<InvalidOperationException>(() => Localization.SetDataProvider(next));
            AssertTargets("Before", _first);
            next.FailAssets = false;
            Localization.SetDataProvider(next);
            AssertTargets("After provider change", _second);
            yield return null;
            AssertTargets("After provider change", _second);
        }

        private void AssertTargets(string text, Sprite sprite)
        {
            Assert.That(Localization.CurrentLanguage, Is.EqualTo("en"));
            Assert.That(_text.text, Is.EqualTo(text));
            Assert.That(_renderer.sprite, Is.SameAs(sprite));
        }

        [UnityTest]
        public IEnumerator FormattableText_UpdatesEscapesSelectorsAndFractionalPlurals()
        {
            _provider.Text = "{{count}} {amount|coin|coins}: {0|Warrior|Mage}";
            Localization.Reload();
            var target = Track(new GameObject("Formattable text test", typeof(RectTransform)));
            target.SetActive(false);
            var text = target.AddComponent<Text>();
            var localized = target.AddComponent<FormattableLocalizedText>();
            localized.Key = "label";
            localized.SetArgs(new FormSelector(1));
            localized.SetParam("amount", 1.5m);
            target.SetActive(true);
            yield return null;
            Assert.That(text.text, Is.EqualTo("{count} coins: Mage"));
            localized.SetArgs(new FormSelector(0));
            localized.SetParam("amount", 1);
            Assert.That(text.text, Is.EqualTo("{count} coin: Warrior"));
            Localization.SetDataProvider(MakeProvider("New {{count}} {amount|coin|coins}: {0|Warrior|Mage}", _first));
            Assert.That(text.text, Is.EqualTo("New {count} coin: Warrior"));
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private LocalizationAssetTable Table(Sprite sprite)
        {
            var table = Track(ScriptableObject.CreateInstance<LocalizationAssetTable>());
            table.Set("icon", sprite);
            return table;
        }

        private Provider MakeProvider(string text, Sprite sprite) => new() { Text = text, Table = Table(sprite) };

        private sealed class Provider : ILocalizationDataProvider
        {
            public string Text;
            public LocalizationAssetTable Table;
            public bool FailAssets;
            public Dictionary<string, string> LoadTextData(string languageCode) => new() { ["label"] = Text };
            public bool HasTextData(string languageCode) => true;
            public List<LocalizationAssetTable> LoadAssetTables(string languageCode)
            {
                if (FailAssets) throw new InvalidOperationException("Simulated asset load failure");
                return new() { Table };
            }
        }
    }
}
