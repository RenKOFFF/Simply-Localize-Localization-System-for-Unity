using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public abstract class LocalizationObject<TLocalizationTarget> : MonoBehaviour where TLocalizationTarget : Object
    {
        public event Action LanguageChanged;

        [SerializeField] protected TLocalizationTarget _keyObject;
        
        public bool IsInitialized { get; protected set; }
        public bool Translated { get; protected set; }
        
        public TLocalizationTarget KeyObject
        {
            get => _keyObject;
            set => TranslateByKey(value);
        }

        public static Dictionary<Object, Object> CurrentLocalization => Localization.CurrentLocalizationObjects;

        protected virtual void OnValidate()
        {
            if (Localization.CanTranslateInEditor() == false) return;
            
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

        protected virtual void OnDisable()
        {
            Localization.LanguageChanged -= SetLanguage;
        }

        public virtual void TranslateByKey(Object key)
        {
            if (key == null) 
            {
                Logging.Log($"{GetType().Name}: key is null in object: {gameObject.name}", LogType.Error, this);
                return;
            }
            
            _keyObject = key as TLocalizationTarget;
            
            if (Localization.CanTranslateInEditor() == false)
                return;
            
            if (!IsInitialized)
            {
                Initialize();
            }
            else
            {
                Translate();
            }
        }

        protected abstract void SetTranslate(Object translatedObject);
        protected abstract void OnHasNotTranslated(Object key);

        private void Translate()
        {
            if (CurrentLocalization.TryGetValue(_keyObject, out var localizedObject) && localizedObject != null)
            {
                SetTranslate(localizedObject);
                
                Translated = true;
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