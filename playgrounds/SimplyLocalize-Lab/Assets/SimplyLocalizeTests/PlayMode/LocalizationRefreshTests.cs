using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SimplyLocalize.Components;
using static SimplyLocalize.Tests.ComponentSourceFixture;
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
        private ComponentSourceFixture _provider;
        private Text _text;
        private SpriteRenderer _renderer;
        private Sprite _first;
        private Sprite _second;

        [SetUp]
        public void SetUp()
        {
            Completed(Localization.ShutdownAsync());
            var texture = Track(new Texture2D(2, 2));
            _first = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
            _second = Track(Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero));
            _provider = MakeProvider("Before", _first);
            var config = Track(_provider.CreateConfig("en"));
            Completed(Localization.InitializeAsync(config, providers: _provider.Registry()));



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
            Completed(Localization.ShutdownAsync());
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
            _provider.Icon = _second;
            Completed(Localization.ReloadAsync());
            AssertTargets("After reload", _second);
            yield return null;
            AssertTargets("After reload", _second);

            _provider.Text = "Failed reload";
            _provider.FailAssets = true;
            Assert.Throws<InvalidOperationException>(() => Completed(Localization.ReloadAsync()));
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
            Assert.Throws<InvalidOperationException>(() => Completed(Localization.ReplaceProvidersAsync(next.Registry())));
            AssertTargets("Before", _first);
            next.FailAssets = false;
            Completed(Localization.ReplaceProvidersAsync(next.Registry()));
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
            Completed(Localization.ReloadAsync());
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
            Completed(Localization.ReplaceProvidersAsync(MakeProvider("New {{count}} {amount|coin|coins}: {0|Warrior|Mage}", _first).Registry()));
            Assert.That(text.text, Is.EqualTo("New {count} coin: Warrior"));
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private ComponentSourceFixture MakeProvider(string text, Sprite sprite) => new() { Text = text, Icon = sprite };
    }
}