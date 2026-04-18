using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Generic base for localized asset components.
    /// Assets are resolved from centralized asset tables (LocalizationAssetTable).
    /// Falls back to the data provider (path-based loading) if not found in tables.
    ///
    /// To create a custom localized asset:
    ///   public class LocalizedMaterial : LocalizedAsset&lt;Material&gt;
    ///   {
    ///       protected override void ApplyAsset(Material asset)
    ///           => GetComponent&lt;Renderer&gt;().sharedMaterial = asset;
    ///
    ///       protected override Material ReadCurrentAsset()
    ///           => GetComponent&lt;Renderer&gt;()?.sharedMaterial;
    ///   }
    /// </summary>
    public abstract class LocalizedAsset<T> : LocalizedComponentBase where T : Object
    {
        private T _originalAsset;
        private bool _originalCached;

        /// <summary>
        /// Apply the resolved asset to the target component.
        /// Called on language change. Asset may be null if nothing was found.
        /// </summary>
        protected abstract void ApplyAsset(T asset);

        /// <summary>
        /// Read the current asset value from the target component.
        /// Used to cache the original value before any localization is applied.
        /// </summary>
        protected abstract T ReadCurrentAsset();

        /// <summary>
        /// Optional: load from the legacy data provider path.
        /// Override for backward compatibility with folder-based sprites/audio.
        /// Return null to skip provider fallback.
        /// </summary>
        protected virtual Object LoadFromProvider(string languageCode) => null;

        protected override void OnEnable()
        {
            CacheOriginal();
            base.OnEnable();
        }

        public override void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
                return;

            CacheOriginal();

            // 1. Try centralized asset tables
            T asset = Localization.GetAsset<T>(_key);

            // 2. Fallback to provider (path-based)
            if (asset == null)
            {
                var providerResult = LoadFromProvider(Localization.CurrentLanguage);
                asset = providerResult as T;
            }

            // 3. Fallback to original if nothing found
            if (asset == null)
                asset = _originalAsset;

            ApplyAsset(asset);
        }

        private void CacheOriginal()
        {
            if (_originalCached) return;
            _originalAsset = ReadCurrentAsset();
            _originalCached = true;
        }
    }
}