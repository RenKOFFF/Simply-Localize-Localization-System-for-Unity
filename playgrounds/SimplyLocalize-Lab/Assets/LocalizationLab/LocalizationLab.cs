using System;
using System.Collections.Generic;
using SimplyLocalize.Components;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace SimplyLocalize.Lab
{
    // The lab owns its manager; it does not modify the package's data or global settings.
    [DefaultExecutionOrder(-90)]
    public sealed class LocalizationLab : MonoBehaviour
    {
        public LocalizationProjectConfig config;
        public TMP_FontAsset uiFont;
        public Font legacyFont;
        public TMP_Text status;
        public TMP_Text diagnostic;
        public FormattableLocalizedText plural;
        public FormattableLocalizedText named;
        public LocalizedProfileOverride localOverride;
        public GameObject samples;
        public int Count { get; private set; } = 1;
        public int Events { get; private set; }
        public bool AlternateProvider { get; private set; }
        public int Revision { get; private set; } = 1;
        private LocalizationRuntime ownedManager;
        private readonly CancellationTokenSource lifetime = new();
        private bool previousRunInBackground;
        private string lastAction = "Ready";

        private async void Awake()
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            try { ownedManager = await Localization.InitializeAsync(config, cancellationToken: lifetime.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception error) { Debug.LogException(error, this); return; }
            Localization.OnLanguageChanged += LanguageChanged;
        }

        private void Start()
        {
            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                string action = button.gameObject.name;
                button.onClick.AddListener(() => Execute(action));
            }
            UpdateArguments();
            UpdateStatus();
        }

        private async void OnDestroy()
        {
            Localization.OnLanguageChanged -= LanguageChanged;
            lifetime.Cancel();
            if (ReferenceEquals(Localization.Runtime, ownedManager) && ownedManager != null)
                await Localization.ShutdownAsync();
            lifetime.Dispose();
            Application.runInBackground = previousRunInBackground;
        }

        private void LanguageChanged(string _) { Events++; }

        public async void Execute(string action)
        {
            try
            {
                switch (action)
                {
                    case "en": case "ru": case "pl": await Localization.SetLanguageAsync(action); break;
                    case "minus": Count = Math.Max(0, Count - 1); break;
                    case "plus": Count++; break;
                    case "next":
                        int[] values = { 0, 1, 2, 5, 11, 21, 22, 25, 101 };
                        Count = values[(Array.IndexOf(values, Count) + 1) % values.Length];
                        break;
                    case "reload": await Localization.ReloadAsync(); break;
                    case "provider":
                        bool next = !AlternateProvider;
                        var registry = new LocalizationSourceRegistry();
                        registry.Register("resources", next ? new RevisionProvider(() => Revision) : new ResourcesLocalizationSourceProvider());
                        await Localization.ReplaceProvidersAsync(registry);
                        AlternateProvider = next;
                        break;
                    case "revision": Revision++; break;
                    case "failure":
                        var before = Localization.Get("Lab/Welcome");
                        int beforeEvents = Events;
                        try
                        {
                            var brokenRegistry = new LocalizationSourceRegistry(); brokenRegistry.Register("resources", new BrokenProvider());
                            await Localization.ReplaceProvidersAsync(brokenRegistry);
                            throw new InvalidOperationException("The broken provider was unexpectedly accepted.");
                        }
                        catch (LabLoadException)
                        {
                            if (before != Localization.Get("Lab/Welcome") || beforeEvents != Events)
                                throw new InvalidOperationException("Failed load changed the visible data or events.");
                        }
                        break;
                    case "override": localOverride.enabled = !localOverride.enabled; break;
                    case "components": samples.SetActive(!samples.activeSelf); break;
                }
                UpdateArguments();
                lastAction = action == "failure" ? "Failed load: previous data and event count preserved" : action;
            }
            catch (Exception ex)
            {
                lastAction = "ERROR: " + ex.Message;
                Debug.LogException(ex, this);
            }
            UpdateStatus();
        }

        private void UpdateArguments()
        {
            plural.SetArgs(Count);
            named.SetParams(new Dictionary<string, object> { ["name"] = "Alex", ["count"] = Count });
        }

        private void UpdateStatus()
        {
            status.text = $"Language: {Localization.CurrentLanguage}     Count: {Count}     Events: {Events}\n"
                + $"Provider: {(AlternateProvider ? "Revision overlay" : "Resources")}     Source revision: {Revision}\n"
                + $"Local override: {(localOverride.enabled ? "ON" : "OFF")}     Samples: {(samples.activeSelf ? "ON" : "OFF")}";
            diagnostic.text = "Last action: " + lastAction;
        }

        public void CreateView()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 960);
            scaler.matchWidthOrHeight = 1f;
            var panel = Box("Panel", canvas.transform, new Color(0.035f, 0.055f, 0.09f));
            Stretch(panel);
            var layout = panel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 24, 24);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Text(panel, "SIMPLY LOCALIZE  /  TEST LAB", 30, 42, new Color(0.3f, 0.85f, 0.75f));
            Text(panel, "Switch languages, change counts and inspect the components in the Hierarchy.", 17, 28);
            var languages = Row(panel, 42);
            Button(languages, "en", "English"); Button(languages, "ru", "Русский"); Button(languages, "pl", "Polski");
            status = Text(panel, "Press Play to initialize localization", 18, 78);
            var actions = Row(panel, 38);
            Button(actions, "minus", "- 1"); Button(actions, "plus", "+ 1"); Button(actions, "next", "0 / 1 / 2 / 5 / 11 / 21...");
            Button(actions, "reload", "Reload"); Button(actions, "provider", "Switch provider");
            var extras = Row(panel, 38);
            Button(extras, "revision", "Bump source revision"); Button(extras, "failure", "Simulate load failure");
            Button(extras, "override", "Toggle local override"); Button(extras, "components", "Toggle samples");
            var body = Box("Samples", panel, new Color(0.065f, 0.095f, 0.14f));
            Height(body.gameObject, 320);
            var list = body.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            list.padding = new RectOffset(18, 18, 10, 10); list.spacing = 6;
            list.childControlHeight = list.childControlWidth = true;
            list.childForceExpandHeight = false;
            samples = body.gameObject;
            var welcome = Text(body, "Welcome", 24, 36);
            welcome.gameObject.AddComponent<LocalizedText>().Key = "Lab/Welcome";
            plural = Text(body, "Plural", 24, 36).gameObject.AddComponent<FormattableLocalizedText>();
            plural.Key = "Lab/Coins";
            named = Text(body, "Named", 21, 32).gameObject.AddComponent<FormattableLocalizedText>();
            named.Key = "Lab/Named";
            Text(body, "Fallback", 20, 32).gameObject.AddComponent<LocalizedText>().Key = "Lab/Fallback";
            Text(body, "Escapes", 20, 32).gameObject.AddComponent<LocalizedText>().Key = "Lab/Escapes";
            var overrideText = Text(body, "Profile override", 20, 38);
            overrideText.gameObject.AddComponent<LocalizedText>().Key = "Lab/Profile";
            localOverride = overrideText.gameObject.AddComponent<LocalizedProfileOverride>();
            var legacy = new GameObject("LegacyText", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            legacy.transform.SetParent(body, false); Height(legacy, 32);
            var legacyText = legacy.GetComponent<UnityEngine.UI.Text>();
            legacyText.font = legacyFont; legacyText.fontSize = 22; legacyText.color = Color.white;
            legacy.AddComponent<LocalizedText>().Key = "Lab/Welcome";
            diagnostic = Text(panel, "Last action: Ready", 17, 28, new Color(0.3f, 0.85f, 0.75f));
            Text(panel, "Fallback exists only in English. RU profile: x1.1. RU local override: x1.4.\nRevision overlay: bump source revision, then Reload to see the new value.", 17, 50);
        }

        private RectTransform Box(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false); go.GetComponent<UnityEngine.UI.Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Transform Row(Transform parent, float height)
        {
            var row = new GameObject("Controls", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup));
            row.transform.SetParent(parent, false); Height(row, height);
            var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return row.transform;
        }

        private TMP_Text Text(Transform parent, string value, float size, float height, Color? color = null)
        {
            var go = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false); Height(go, height);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = uiFont; text.fontSize = size; text.text = value;
            text.color = color ?? new Color(0.9f, 0.94f, 1f);
            text.raycastTarget = false; text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        private void Button(Transform row, string action, string label)
        {
            var rect = Box(action, row, new Color(0.13f, 0.2f, 0.29f));
            rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            var text = Text(rect, label, 17, 30);
            text.alignment = TextAlignmentOptions.Center; Stretch(text.rectTransform);
            text.margin = new Vector4(5, 0, 5, 0);
        }

        private static void Height(GameObject go, float height)
        {
            var element = go.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minHeight = element.preferredHeight = height;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private sealed class RevisionProvider : ILocalizationSourceProvider
        {
            private readonly ResourcesLocalizationSourceProvider source = new();
            private readonly Func<int> revision;
            public RevisionProvider(Func<int> revision) { this.revision = revision; }
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => source.GetCapabilities(kind);
            public async Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token)
            {
                var lease = await source.LoadAsync(request, token);
                if (request.Source.Kind != LocalizationSourceKind.TextTable) return lease;
                try
                {
                    var data = lease.Data.Text.ToDictionary(p => p.Key, p => p.Value);
                    if (data.ContainsKey("Lab/Welcome")) data["Lab/Welcome"] += " [revision " + revision() + "]";
                    return new LocalizationSourceLease(LocalizationSourceData.FromText(data), lease.ReleaseAsync);
                }
                catch { await lease.ReleaseAsync(); throw; }
            }
        }
        private sealed class LabLoadException : Exception { }
        private sealed class BrokenProvider : ILocalizationSourceProvider
        {
            public LocalizationProviderCapabilities GetCapabilities(LocalizationSourceKind kind) => new(LocalizationLoadUnit.Table, true, true);
            public Task<LocalizationSourceLease> LoadAsync(LocalizationSourceRequest request, CancellationToken token) => throw new LabLoadException();
        }
    }
}