using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes an AudioClip on an AudioSource component.
    ///
    /// Supports two modes:
    ///   1. Direct entries: drag clips per language in the Inspector
    ///   2. Path-based: set a key, clips loaded from Resources/{lang}/audio/
    ///
    /// Optional: autoPlay plays the clip immediately after language switch.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimplyLocalize/Localized Audio Clip")]
    public class LocalizedAudioClip : LocalizedAsset<AudioClip>
    {
        [SerializeField]
        [Tooltip("Automatically play the clip when the language changes")]
        private bool _autoPlay;

        private AudioSource _source;

        protected override void ApplyAsset(AudioClip asset)
        {
            CacheTarget();

            if (_source == null) return;

            _source.clip = asset;

            if (_autoPlay && asset != null)
                _source.Play();
        }

        protected override AudioClip ReadCurrentAsset()
        {
            CacheTarget();
            return _source != null ? _source.clip : null;
        }

        protected override Object LoadFromProvider(string languageCode)
        {
            return Localization.GetAsset<AudioClip>(_key);
        }

        private void CacheTarget()
        {
            if (_source == null) _source = GetComponent<AudioSource>();
        }
    }
}