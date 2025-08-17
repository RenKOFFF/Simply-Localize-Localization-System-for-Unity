using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    [AddComponentMenu("Simply Localize/Localization Image")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LocalizationImage : LocalizationObject<Sprite>, ICanOverrideLocalizationTarget<Image>
    {
        [SerializeField] private bool _overrideLocalizationTarget;
        [SerializeField] private Image _localizationTarget;

        public bool OverrideLocalizationTarget => _overrideLocalizationTarget;
        public Image LocalizationTarget => _localizationTarget;

        protected override void OnValidate()
        {
            base.OnValidate();
            
            if (Localization.CanTranslateInEditor() == false) return;
            
            _localizationTarget ??= GetComponent<Image>();

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