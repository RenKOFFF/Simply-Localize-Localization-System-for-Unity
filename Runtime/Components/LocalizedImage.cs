using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a sprite on an Image (UI) or SpriteRenderer (world-space) component.
    ///
    /// Sprites are loaded from the data provider by key and language.
    /// Falls back to the fallback language if a sprite is missing for the current language.
    /// If no sprite is found at all, the original sprite is preserved.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Image")]
    public class LocalizedImage : LocalizedComponentBase
    {
        private Image _image;
        private SpriteRenderer _spriteRenderer;
        private Sprite _originalSprite;
        private bool _originalCached;

        protected override void OnEnable()
        {
            CacheComponents();
            CacheOriginal();
            base.OnEnable();
        }

        public override void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
                return;

            CacheComponents();
            CacheOriginal();

            var sprite = Localization.GetSprite(_key);

            if (sprite != null)
            {
                if (_image != null)
                    _image.sprite = sprite;
                else if (_spriteRenderer != null)
                    _spriteRenderer.sprite = sprite;
            }
            else
            {
                // Restore original if no localized sprite found
                if (_image != null)
                    _image.sprite = _originalSprite;
                else if (_spriteRenderer != null)
                    _spriteRenderer.sprite = _originalSprite;
            }
        }

        private void CacheComponents()
        {
            if (_image == null && _spriteRenderer == null)
            {
                _image = GetComponent<Image>();

                if (_image == null)
                    _spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void CacheOriginal()
        {
            if (_originalCached)
                return;

            if (_image != null)
                _originalSprite = _image.sprite;
            else if (_spriteRenderer != null)
                _originalSprite = _spriteRenderer.sprite;

            _originalCached = true;
        }
    }
}
