using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes an AudioClip on an AudioSource component.
    ///
    /// Audio clips are loaded from the data provider by key and language.
    /// Falls back to the fallback language if a clip is missing for the current language.
    /// If no clip is found at all, the original clip is preserved.
    ///
    /// Optionally auto-plays the clip after loading.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Audio")]
    public class LocalizedAudio : LocalizedComponentBase
    {
        [Header("Audio Settings")]
        [Tooltip("Automatically play the clip when it's loaded or the language changes")]
        [SerializeField] private bool _autoPlay;

        private AudioSource _audioSource;
        private AudioClip _originalClip;
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

            if (_audioSource == null)
                return;

            var clip = Localization.GetAudio(_key);

            if (clip != null)
            {
                _audioSource.clip = clip;
            }
            else
            {
                _audioSource.clip = _originalClip;
            }

            if (_autoPlay && _audioSource.clip != null)
            {
                _audioSource.Stop();
                _audioSource.Play();
            }
        }

        /// <summary>
        /// Plays the currently loaded localized clip.
        /// </summary>
        public void Play()
        {
            if (_audioSource != null && _audioSource.clip != null)
                _audioSource.Play();
        }

        private void CacheComponents()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        private void CacheOriginal()
        {
            if (_originalCached)
                return;

            if (_audioSource != null)
                _originalClip = _audioSource.clip;

            _originalCached = true;
        }
    }
}
