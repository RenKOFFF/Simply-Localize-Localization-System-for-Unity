using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Declare additional runtime assets whose lifetime follows a custom localization asset.
    /// Do not include shared configuration or objects owned outside the localization source lifetime.</summary>
    public interface ILocalizationAssetDependencies
    {
        IEnumerable<UnityEngine.Object> GetLocalizationDependencies();
    }

    public enum LocalizationResourceOwnership { Borrowed, Owned }

    /// <summary>Limits apply to each prepared language/fallback set, not peak process RAM.
    /// Owned is an explicit promise that these Resources objects are used only through retained localization sources.
    /// Stop consumers and remove their references before releasing their last source owner.</summary>
    public sealed class LocalizationRuntimeOptions
    {
        public string DiagnosticName { get; set; }
        public LocalizationResourceOwnership Ownership { get; set; } = LocalizationResourceOwnership.Borrowed;
        public bool PrepareAudio { get; set; } = true;
        public int MaxSourceCharacters { get; set; } = 4000000;
        public int MaxTextEntries { get; set; } = 100000;
        public int MaxTextCharacters { get; set; } = 16000000;
        public int MaxAssetEntries { get; set; } = 4096;
        public int MaxTemplates { get; set; } = 1024;
        public double AudioTimeoutSeconds { get; set; } = 30;

        internal LocalizationRuntimeOptions Copy()
        {
            if (!Enum.IsDefined(typeof(LocalizationResourceOwnership), Ownership)
                || MaxSourceCharacters < 1 || MaxTextEntries < 1 || MaxTextCharacters < 1 || MaxAssetEntries < 1 || MaxTemplates < 1
                || double.IsNaN(AudioTimeoutSeconds) || double.IsInfinity(AudioTimeoutSeconds) || AudioTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(LocalizationRuntimeOptions), "Runtime limits must be positive and ownership must be defined.");
            return (LocalizationRuntimeOptions)MemberwiseClone();
        }
    }
}
