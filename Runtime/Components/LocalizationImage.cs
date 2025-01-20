using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    [AddComponentMenu("Simply Localize/Localization Image")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LocalizationImage : MonoBehaviour
    {
        public event Action LanguageChanged;

        [SerializeField] private bool _overrideImage;
        [SerializeField] private Sprite _keyObject;
        [SerializeField] private Image _image;
        
        public bool IsInitialized { get; protected set; }
        public bool Translated { get; protected set; }
        public Sprite DefaultSprite { get; protected set; }

        public static Dictionary<Object, Object> CurrentLocalization => Localization.CurrentLocalizationObjects;

        private void OnValidate()
        {
            if (Localization.CanTranslateInEditor() == false) return;
            
            _image ??= GetComponent<Image>();

            if (_keyObject == null && _image != null)
            {
                _keyObject = _image.sprite;
            }
            
            if (_keyObject != null)
            {
                TranslateByKey(_keyObject);
            }
        }

        protected virtual void OnEnable()
        {
            if (Localization.CanTranslateInEditor() == false) return;

            if (_keyObject == null)
            {
                Logging.Log($"{nameof(LocalizationImage)}: _keyObject is null in object: {gameObject.name}", LogType.Error, this);
                return;
            }
            
            if (!IsInitialized)
            {
                Initialize();
            }
            else
            {
                SetLanguage();
            }

            Localization.LanguageChanged += SetLanguage;
        }

        private void OnDisable()
        {
            Localization.LanguageChanged -= SetLanguage;
        }

        public void TranslateByKey(Object key)
        {
            _keyObject = key as Sprite;
            
            if (!IsInitialized)
            {
                Initialize();
            }
            else
            {
                Translate();
            }
        }

        protected void SetTranslate(Object translatedImage)
        {
            _image.sprite = translatedImage as Sprite;
        }

        protected void OnHasNotTranslated(Object key)
        {
            _image.sprite = key as Sprite;
            
            Logging.Log($"Translated image not founded by key: {key} in object: {gameObject.name}. Assigned default image.", LogType.Warning, this);
        }


        protected virtual void ApplyTranslate(Object translatedObject)
        {
            DefaultSprite = translatedObject as Sprite;
            Translated = true;
        }

        private void Translate()
        {
            if (CurrentLocalization.TryGetValue(_keyObject, out var localizedObject) && localizedObject != null)
            {
                ApplyTranslate(_keyObject);
                SetTranslate(localizedObject);
            }
            else OnHasNotTranslated(_keyObject);
        }

        private void Initialize()
        {
            SetLanguage();
            IsInitialized = true;
        }
        
        private void SetLanguage()
        {
            Translate();
            
            LanguageChanged?.Invoke();
        }
    }
}