using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes a Sprite on Image (UI) or SpriteRenderer (world-space).
    ///
    /// Resolves a ready asset from the bound channel and retains its source until detached.
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