using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Abstract base class for all localized components.
    ///
    /// Handles subscription to language change events and provides a common
    /// key field with automatic refresh. Subclasses implement Refresh() to
    /// apply the localized value to their specific target (text, image, audio).
    /// </summary>
    public abstract class LocalizedComponentBase : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Localization key used to look up the translated value")]
        protected string _key;

        /// <summary>
        /// The localization key. Changing this at runtime triggers a Refresh.
        /// </summary>
        public string Key
        {
            get => _key;
            set
            {
                if (_key == value)
                    return;

                _key = value;
                if (isActiveAndEnabled)
                    Refresh();
            }
        }

        protected virtual void OnEnable()
        {
            Localization.OnLanguageChanged += HandleLanguageChanged;

            if (Localization.IsInitialized)
                Refresh();
        }

        protected virtual void OnDisable()
        {
            Localization.OnLanguageChanged -= HandleLanguageChanged;
        }

        private void HandleLanguageChanged(string newLanguage)
        {
            Refresh();
        }

        /// <summary>
        /// Forces the component to re-read its localized value and update the target.
        /// Called automatically on enable and on language change.
        /// </summary>
        public abstract void Refresh();

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            // Refresh in editor when key is changed via Inspector
            if (Application.isPlaying && isActiveAndEnabled)
                Refresh();
        }
#endif
    }
}
