using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a Sprite on Image (UI) or SpriteRenderer (world-space).
    ///
    /// Supports two modes:
    ///   1. Direct entries: drag sprites per language in the Inspector
    ///   2. Path-based: set a key, sprites loaded from Resources/{lang}/images/
    ///
    /// Replaces the old LocalizedImage component with full LocalizedAsset support.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Sprite")]
    public class LocalizedSprite : LocalizedAsset<Sprite>
    {
        private Image _image;
        private SpriteRenderer _spriteRenderer;

        protected override void ApplyAsset(Sprite asset)
        {
            CacheTargets();

            if (_image != null) _image.sprite = asset;
            else if (_spriteRenderer != null) _spriteRenderer.sprite = asset;
        }

        protected override Sprite ReadCurrentAsset()
        {
            CacheTargets();

            if (_image != null) return _image.sprite;
            if (_spriteRenderer != null) return _spriteRenderer.sprite;
            return null;
        }

        protected override Object LoadFromProvider(string languageCode)
        {
            return Localization.GetAsset<Sprite>(_key);
        }

        private void CacheTargets()
        {
            if (_image == null && _spriteRenderer == null)
            {
                _image = GetComponent<Image>();
                if (_image == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }
    }
}