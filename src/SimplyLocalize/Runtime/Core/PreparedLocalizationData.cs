using System;
using System.Collections.Generic;

namespace SimplyLocalize
{
    public enum LocalizationResolveStatus { NotPrepared, Missing, Found }

    /// <summary>A lookup result, including the language that actually supplied a fallback value.</summary>
    public readonly struct LocalizationResolveResult<T>
    {
        public LocalizationResolveStatus Status { get; }
        public T Value { get; }
        public string RequestedLanguage { get; }
        public string ResolvedLanguage { get; }
        public bool IsFallback => Status == LocalizationResolveStatus.Found && RequestedLanguage != ResolvedLanguage;
        internal LocalizationResolveResult(LocalizationResolveStatus status, T value, string requested, string resolved = null)
        { Status = status; Value = value; RequestedLanguage = requested; ResolvedLanguage = resolved; }
    }

    /// <summary>
    /// A detached language snapshot. Apply it only to the manager that prepared it.
    /// Dispose releases this handle's managed references; it does not unload Unity assets or dispose the provider.
    /// Applying copies its collections, so disposing a handle never invalidates an applied manager.
    /// Asset tables and their referenced Unity objects remain borrowed, and must not be mutated by their owner while in use.
    /// Preparation means table availability, not decoded AudioClip data or GPU/audio readiness.
    /// </summary>
    public sealed class PreparedLocalizationData : IDisposable
    {
        internal object Owner;
        internal readonly long Version;
        internal LanguageProfile Profile;
        internal string[] Chain;
        internal readonly string ResourcesPath;
        internal readonly bool LoadText;
        internal readonly Dictionary<string, Dictionary<string, string>> Text = new();
        internal readonly Dictionary<string, List<LocalizationAssetTable>> Assets = new();
        public string LanguageCode { get; }
        public bool IncludesAssets { get; }
        public bool IsDisposed { get; private set; }
        public int TextLanguageCount => Text.Count;
        public int AssetLanguageCount => Assets.Count;

        internal PreparedLocalizationData(object owner, long version, LanguageProfile profile, string[] chain,
            bool loadText, bool assets, string resourcesPath)
        {
            Owner = owner; Version = version; Profile = profile; Chain = chain;
            LanguageCode = profile.Code; LoadText = loadText; IncludesAssets = assets; ResourcesPath = resourcesPath;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Text.Clear(); Assets.Clear(); Profile = null; Chain = Array.Empty<string>(); Owner = null;
        }
    }
}
