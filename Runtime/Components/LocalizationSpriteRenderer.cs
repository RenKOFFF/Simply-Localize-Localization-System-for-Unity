using UnityEngine;

namespace SimplyLocalize
{
    [AddComponentMenu("Simply Localize/Localization Sprite Renderer")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LocalizationSpriteRenderer : LocalizationObject<Sprite>, ICanOverrideLocalizationTarget<SpriteRenderer>
    {
        [SerializeField] private bool _overrideLocalizationTarget;
        [SerializeField] private SpriteRenderer _localizationTarget;

        public bool OverrideLocalizationTarget => _overrideLocalizationTarget;
        public SpriteRenderer LocalizationTarget => _localizationTarget;

        protected override void OnValidate()
        {
            base.OnValidate();
            
            if (Localization.CanTranslateInEditor() == false) return;
            
            _localizationTarget ??= GetComponent<SpriteRenderer>();

            if (_keyObject == null && LocalizationTarget != null)
            {
                _keyObject = LocalizationTarget.sprite;
            }
        }

        protected override void SetTranslate(Object translatedImage)
        {
            LocalizationTarget.sprite = translatedImage as Sprite;
        }

        protected override void OnHasNotTranslated(Object key)
        {
            LocalizationTarget.sprite = key as Sprite;
            
            Logging.Log($"Translated image not founded by key: {key} in object: {gameObject.name}. Assigned default image.", LogType.Warning, this);
        }
    }
}