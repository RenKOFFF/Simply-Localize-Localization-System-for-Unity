using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using SimplyLocalize.Components;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static SimplyLocalize.Tests.ComponentSourceFixture;

namespace SimplyLocalize.Tests
{
    public class ChannelComponentTests
    {
        private readonly List<Object> objects = new();
        private readonly List<LocalizationRuntime> runtimes = new();
        private T Track<T>(T value) where T : Object { objects.Add(value); return value; }
        private LocalizationRuntime Runtime(ComponentSourceFixture source, LocalizationProjectConfig config, string language = "en")
        {
            var runtime = new LocalizationRuntime(config, source.Registry()); runtimes.Add(runtime);
            Completed(runtime.InitializeAsync(language)); return runtime;
        }
        private LocalizedText Label()
        {
            var go = Track(new GameObject("Channel label", typeof(RectTransform))); go.SetActive(false);
            var text = go.AddComponent<Text>(); text.text = "Original"; text.fontSize = 20;
            var localized = go.AddComponent<LocalizedText>(); localized.Key = "label"; return localized;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var value in objects) if (value is GameObject go) go.SetActive(false);
            foreach (var runtime in runtimes) Completed(runtime.ReleaseAsync()); runtimes.Clear();
            Completed(Localization.ShutdownAsync());
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]);
            objects.Clear(); yield return null;
        }
        [Test] public void ExplicitRuntimeBindingIgnoresGlobalChangesAndReenableRestoresIt()
        {
            var firstSource = new ComponentSourceFixture { Text = "First" };
            var secondSource = new ComponentSourceFixture { Text = "Second" };
            var config = Track(firstSource.CreateConfig("en", "ru"));
            var first = Runtime(firstSource, config); var second = Runtime(secondSource, config);
            var localized = Label(); localized.Bind(first); localized.gameObject.SetActive(true);
            var text = localized.GetComponent<Text>(); Assert.That(text.text, Is.EqualTo("First"));
            Completed(Localization.InitializeAsync(config, providers: secondSource.Registry()));
            Assert.That(text.text, Is.EqualTo("First"));
            localized.Bind(second); Assert.That(text.text, Is.EqualTo("Second"));
            localized.enabled = false; Assert.That(text.text, Is.EqualTo("Original"));
            secondSource.Text = "Updated"; Completed(second.ReloadAsync());
            localized.enabled = true; Assert.That(text.text, Is.EqualTo("Updated"));
            localized.Bind(null); Assert.That(text.text, Is.EqualTo("Original"));
            localized.UseDefaultRuntime(); Assert.That(text.text, Is.EqualTo("Second"));
            Completed(Localization.ShutdownAsync()); Assert.That(text.text, Is.EqualTo("Original"));
        }
        [Test] public void OversizedSerializedParameterIsIgnoredAndSetArgRejectsItBeforeAllocation()
        {
            var source = new ComponentSourceFixture { Text = "{0}" };
            var runtime = Runtime(source, Track(source.CreateConfig("en")));
            var go = Track(new GameObject("Defaults", typeof(RectTransform)));
            go.SetActive(false);
            var text = go.AddComponent<Text>();
            var label = go.AddComponent<FormattableLocalizedText>();
            var defaults = new List<FormattableLocalizedText.NamedParam>
            {
                new() { name = "0", value = "valid" },
                new() { name = int.MaxValue.ToString(), value = "ignored" }
            };
            typeof(FormattableLocalizedText).GetField("_defaultNamedParams", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(label, defaults);
            label.Key = "label";
            label.Bind(runtime);
            go.SetActive(true);
            Assert.That(text.text, Is.EqualTo("valid"));
            Assert.DoesNotThrow(() => label.SetArg(FormattableLocalizedText.MaxParameterIndex, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => label.SetArg(FormattableLocalizedText.MaxParameterIndex + 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => label.SetArg(int.MaxValue, 2));
            Assert.That(text.text, Is.EqualTo("valid"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FormattedLabelKeepsArgumentsWhenTheLanguageChanges(bool useTmp)
        {
            var source = new ComponentSourceFixture { Text = "{name}: {0} {0|coin|coins}" };
            var config = Track(source.CreateConfig("en", "ru", "pl"));
            var runtime = Runtime(source, config);
            var go = Track(new GameObject("Coins", typeof(RectTransform)));
            go.SetActive(false);
            if (useTmp) go.AddComponent<TMPro.TextMeshProUGUI>().font = TMPro.TMP_Settings.defaultFontAsset;
            else go.AddComponent<Text>();
            var label = go.AddComponent<FormattableLocalizedText>();
            label.Key = "label";
            label.Bind(runtime);
            label.SetParam("name", "Alex");
            label.SetArgs(1);
            go.SetActive(true);
            string Displayed() => useTmp ? go.GetComponent<TMPro.TMP_Text>().text : go.GetComponent<Text>().text;
            Assert.That(Displayed(), Is.EqualTo("Alex: 1 coin"));
            source.Text = "{name}: {0} {0|монета|монеты|монет}";
            Completed(runtime.SwitchLanguageAsync("ru"));
            Assert.That(Displayed(), Is.EqualTo("Alex: 1 монета"));
            label.SetArgs(2);
            Assert.That(Displayed(), Is.EqualTo("Alex: 2 монеты"));
            label.SetArgs(5);
            Assert.That(Displayed(), Is.EqualTo("Alex: 5 монет"));
            source.Text = "{name}: {0} {0|moneta|monety|monet}";
            Completed(runtime.SwitchLanguageAsync("pl"));
            Assert.That(Displayed(), Is.EqualTo("Alex: 5 monet"));
            source.Text = "{name}: {0} {0|coin|coins}";
            Completed(runtime.SwitchLanguageAsync("en"));
            Assert.That(Displayed(), Is.EqualTo("Alex: 5 coins"));
        }

        [Test] public void ComponentCanBindBeforeInitializationAndStableEntryTakesPriorityOverKey()
        {
            var source = new ComponentSourceFixture();
            var config = Track(source.CreateConfig("en"));
            var runtime = new LocalizationRuntime(config, source.Registry()); runtimes.Add(runtime);
            var localized = Label(); localized.Entry = new LocalizationEntryReference(config.MainChannelId, config.Sets[0].Entries[0].Id);
            localized.Key = "unrelated"; localized.Bind(runtime); localized.gameObject.SetActive(true);
            Assert.That(localized.GetComponent<Text>().text, Is.EqualTo("Original"));
            Completed(runtime.InitializeAsync());
            Assert.That(localized.GetComponent<Text>().text, Is.EqualTo("Text"));
            int loads = source.Loads;
            localized.Refresh(); localized.enabled = false; localized.enabled = true;
            Assert.That(source.Loads, Is.EqualTo(loads), "Binding and ready refresh must not load providers.");
            Completed(runtime.ReleaseAsync());
            Assert.That(localized.BoundRuntime, Is.Null); Assert.That(localized.GetComponent<Text>().text, Is.EqualTo("Original"));
        }
        [Test] public void IndependentChannelBindingUpdatesOnlyItsConsumers()
        {
            var source = new ComponentSourceFixture { Text = "Initial" }; var config = Track(source.CreateConfig("en", "ru"));
            var other = config.AddChannel("Other", new[] { "en", "ru" }); other.IncludeSet(config.Sets[0].Id, true);
            var runtime = Runtime(source, config);
            var main = Label(); main.Bind(runtime); main.gameObject.SetActive(true);
            var independent = Label(); independent.Bind(runtime, other.Id); independent.gameObject.SetActive(true);
            source.Text = "Other changed"; Completed(runtime.SwitchLanguageAsync(other.Id, "ru"));
            Assert.That(main.GetComponent<Text>().text, Is.EqualTo("Initial"));
            Assert.That(independent.GetComponent<Text>().text, Is.EqualTo("Other changed"));
        }
        [Test] public void FallbackTextUsesActualLanguageAppearanceAndOverride()
        {
            var source = new ComponentSourceFixture();
            var english = Track(ScriptableObject.CreateInstance<LocalizationAppearance>());
            english.overrideTypography = true; english.fontSizeMultiplier = 1.5f;
            var russian = Track(ScriptableObject.CreateInstance<LocalizationAppearance>());
            russian.overrideTypography = true; russian.fontSizeMultiplier = 2;
            source.Appearances.Add("en", english); source.Appearances.Add("ru", russian);
            var config = Track(source.CreateConfig("en", "ru"));
            config.Sets[0].SetSources("ru"); config.MainChannel.SetFallback("ru", "en");
            var runtime = Runtime(source, config, "ru"); var localized = Label();
            var overrides = localized.gameObject.AddComponent<LocalizedProfileOverride>();
            ((List<LocalizedProfileOverride.OverrideEntry>)overrides.Entries).Add(new() {
                languageCode = "en", overrideTypography = true, fontSizeMultiplier = 1.25f });
            localized.Bind(runtime); localized.gameObject.SetActive(true);
            Assert.That(localized.GetComponent<Text>().fontSize, Is.EqualTo(25));
            overrides.enabled = false; Assert.That(localized.GetComponent<Text>().fontSize, Is.EqualTo(30));
            Completed(runtime.ReleaseAsync()); Assert.That(localized.GetComponent<Text>().fontSize, Is.EqualTo(20));
        }
        [Test] public void CustomAssetComponentDetachesTargetBeforeLastSourceCleanup()
        {
            var asset = Track(new Texture2D(1, 1)); var original = Track(new Texture2D(1, 1));
            var source = new ComponentSourceFixture { Icon = asset }; var config = Track(source.CreateConfig("en"));
            var runtime = Runtime(source, config);
            var go = Track(new GameObject("External custom component")); go.SetActive(false);
            var component = go.AddComponent<CustomTextureConsumer>(); component.Value = original;
            component.Key = "icon"; component.Bind(runtime); go.SetActive(true);
            Assert.That(component.Value, Is.SameAs(asset));
            bool checkedRelease = false;
            source.OnRelease = () => { Assert.That(component.Value, Is.SameAs(original)); checkedRelease = true; };
            Completed(runtime.ReleaseAsync()); Completed(component.ReleaseCompletion);
            Assert.That(checkedRelease, Is.True); Assert.That(source.Releases, Is.EqualTo(source.Loads));
        }
        [UnityTest] public IEnumerator AudioDisableStopsPlaybackAndRestoresOriginalClip()
        {
            var original = Track(AudioClip.Create("Original", 44100, 1, 44100, false));
            var clip = Track(AudioClip.Create("Localized", 44100, 1, 44100, false));
            var source = new ComponentSourceFixture { Icon = clip }; var config = Track(source.CreateConfig("en"));
            var runtime = Runtime(source, config);
            var go = Track(new GameObject("Localized audio")); go.SetActive(false);
            var audio = go.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.clip = original;
            var localized = go.AddComponent<LocalizedAudioClip>(); localized.Key = "icon"; localized.Bind(runtime);
            typeof(LocalizedAudioClip).GetField("_autoPlay", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(localized, true);
            go.SetActive(true); yield return null;
            Assert.That(audio.clip, Is.SameAs(clip));
            localized.enabled = false; Assert.That(audio.clip, Is.SameAs(original)); Assert.That(audio.isPlaying, Is.False);
        }
        [Test] public void LocalizedEventUsesBoundIndependentChannel()
        {
            var source = new ComponentSourceFixture(); var config = Track(source.CreateConfig("en", "ru"));
            var other = config.AddChannel("Other", new[] { "en", "ru" });
            var runtime = Runtime(source, config);
            var go = Track(new GameObject("Bound event")); go.SetActive(false);
            var localized = go.AddComponent<LocalizedEvent>(); localized.Bind(runtime, other.Id);
            int english = 0, russian = 0;
            var en = new UnityEvent(); en.AddListener(() => english++);
            var ru = new UnityEvent(); ru.AddListener(() => russian++);
            typeof(LocalizedEvent).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(localized,
                new List<LocalizedEvent.LanguageEventEntry> { new() { languageCode = "en", onLanguageSet = en }, new() { languageCode = "ru", onLanguageSet = ru } });
            go.SetActive(true); Assert.That(english, Is.EqualTo(1));
            Completed(runtime.SwitchLanguageAsync("ru")); Assert.That(russian, Is.Zero);
            Completed(runtime.SwitchLanguageAsync(other.Id, "ru")); Assert.That(russian, Is.EqualTo(1));
            localized.enabled = false; Completed(runtime.SwitchLanguageAsync(other.Id, "en")); Assert.That(english, Is.EqualTo(1));
        }
    }
    // Lives in the consumer test assembly, exercising the public extension API without internals.
    public sealed class CustomTextureConsumer : LocalizedAsset<Texture2D>
    {
        public Texture2D Value;
        protected override void ApplyAsset(Texture2D asset) => Value = asset;
        protected override Texture2D ReadCurrentAsset() => Value;
    }
}
