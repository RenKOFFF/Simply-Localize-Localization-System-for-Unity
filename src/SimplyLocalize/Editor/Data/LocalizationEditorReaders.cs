using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Project-side Editor integrations, independent from runtime providers. Register from
    /// InitializeOnLoad. Readers and returned Unity assets remain owned by the registering code.</summary>
    public static class LocalizationEditorReaders
    {
        private sealed class Registration : IDisposable
        {
            internal string Id;
            internal ILocalizationEditorTextReader Text;
            internal ILocalizationEditorAssetReader Assets;
            internal Func<LocalizationSourceReference, LocalizationAppearance> Appearance;
            public void Dispose()
            {
                if (!readers.TryGetValue(Id, out var current) || !ReferenceEquals(this, current)) return;
                readers.Remove(Id); Notify();
            }
        }
        private sealed class TextDispatch : ILocalizationEditorTextReader
        { public LocalizationEditorTextSource Read(LocalizationSourceReference source) => ReadText(source); }
        private sealed class AssetDispatch : ILocalizationEditorAssetReader
        { public LocalizationEditorAssetSource Read(LocalizationSourceReference source) => ReadAssets(source); }
        private static readonly Dictionary<string, Registration> readers = new(StringComparer.Ordinal);
        private static ResourcesEditorTextReader resourcesText;
        private static ResourcesEditorAssetReader resourcesAssets;
        public static ILocalizationEditorTextReader Text { get; } = new TextDispatch();
        public static ILocalizationEditorAssetReader Assets { get; } = new AssetDispatch();
        public static event Action Changed;
        static LocalizationEditorReaders() { EditorApplication.projectChanged += Invalidate; }
        /// <summary>Rebuilds Resources path indexes and invalidates shared previews. Does not write or unload assets.</summary>
        public static void Invalidate() { ResetResources(); Notify(); }
        internal static void ResetResources() { resourcesText = null; resourcesAssets = null; }
        public static IDisposable Register(string providerId, ILocalizationEditorTextReader text = null,
            ILocalizationEditorAssetReader assets = null, Func<LocalizationSourceReference, LocalizationAppearance> appearance = null)
        {
            if (string.IsNullOrWhiteSpace(providerId) || providerId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                || string.Equals(providerId, "resources", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a non-reserved provider identity.", nameof(providerId));
            if (text == null && assets == null && appearance == null) throw new ArgumentException("Supply at least one Editor reader.");
            if (readers.ContainsKey(providerId)) throw new InvalidOperationException("Editor readers already registered for " + providerId);
            var registration = new Registration { Id = providerId, Text = text, Assets = assets, Appearance = appearance };
            readers.Add(providerId, registration); Notify(); return registration;
        }
        public static LocalizationEditorTextSource ReadText(LocalizationSourceReference source)
        {
            Require(source, LocalizationSourceKind.TextTable);
            var reader = source.ProviderId == "resources" ? resourcesText ??= new ResourcesEditorTextReader() : Find(source).Text;
            return reader?.Read(source) ?? throw new InvalidOperationException("No Editor text reader/data for " + source.ProviderId);
        }
        public static LocalizationEditorAssetSource ReadAssets(LocalizationSourceReference source)
        {
            Require(source, LocalizationSourceKind.AssetTable);
            var reader = source.ProviderId == "resources" ? resourcesAssets ??= new ResourcesEditorAssetReader() : Find(source).Assets;
            return reader?.Read(source) ?? throw new InvalidOperationException("No Editor asset reader/data for " + source.ProviderId);
        }
        public static LocalizationAppearance ReadAppearance(LocalizationSourceReference source)
        {
            Require(source, LocalizationSourceKind.Appearance);
            var result = source.ProviderId == "resources"
                ? AssetDatabase.LoadAssetAtPath<LocalizationAppearance>((resourcesText ??= new ResourcesEditorTextReader()).ResolveSourceAssetPath(source))
                : Find(source).Appearance?.Invoke(source);
            return result != null ? result : throw new InvalidOperationException("No Editor appearance reader/data for " + source.ProviderId);
        }
        private static void Require(LocalizationSourceReference source, LocalizationSourceKind kind)
        { if (source == null || source.Kind != kind) throw new ArgumentException("Expected source kind " + kind); }
        private static Registration Find(LocalizationSourceReference source) => readers.TryGetValue(source.ProviderId, out var reader) ? reader
            : throw new InvalidOperationException("Register an Editor reader for provider '" + source.ProviderId + "'.");
        private static void Notify()
        { if (Changed != null) foreach (Action observer in Changed.GetInvocationList()) try { observer(); } catch (Exception error) { Debug.LogException(error); } }
    }
}
