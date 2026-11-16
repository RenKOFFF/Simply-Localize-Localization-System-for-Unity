using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimplyLocalize
{
    /// <summary>Declare additional runtime assets whose lifetime follows a custom localization asset.
    /// Do not include shared configuration, profiles or objects owned outside localization contexts.</summary>
    public interface ILocalizationAssetDependencies
    {
        IEnumerable<UnityEngine.Object> GetLocalizationDependencies();
    }

    public enum LocalizationResourceOwnership { Borrowed, Owned }

    /// <summary>Limits apply to each prepared language/fallback set, not peak process RAM.
    /// Owned is an explicit promise that these Resources objects are used only through live contexts.
    /// Stop consumers and remove their references before closing an owned context.</summary>
    public sealed class LocalizationContextOptions
    {
        public LocalizationResourceOwnership Ownership { get; set; } = LocalizationResourceOwnership.Borrowed;
        public bool PrepareAudio { get; set; } = true;
        public int MaxSourceCharacters { get; set; } = 4000000;
        public int MaxTextEntries { get; set; } = 100000;
        public int MaxTextCharacters { get; set; } = 16000000;
        public int MaxAssetEntries { get; set; } = 4096;
        public int MaxTemplates { get; set; } = 1024;
        public double AudioTimeoutSeconds { get; set; } = 30;

        internal LocalizationContextOptions Copy()
        {
            if (!Enum.IsDefined(typeof(LocalizationResourceOwnership), Ownership)
                || MaxSourceCharacters < 1 || MaxTextEntries < 1 || MaxTextCharacters < 1 || MaxAssetEntries < 1 || MaxTemplates < 1
                || double.IsNaN(AudioTimeoutSeconds) || double.IsInfinity(AudioTimeoutSeconds) || AudioTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(LocalizationContextOptions), "Context limits must be positive and ownership must be defined.");
            return (LocalizationContextOptions)MemberwiseClone();
        }
    }
}
