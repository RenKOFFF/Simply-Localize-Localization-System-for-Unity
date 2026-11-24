using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>
    /// Localizes an AudioClip on an AudioSource component.
    ///
    /// Resolves a ready asset from the bound channel and retains its source until detached.
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

            _source.Stop();
            _source.clip = asset;

            if (_autoPlay && asset != null)
                _source.Play();
        }

        protected override AudioClip ReadCurrentAsset()
        {
            CacheTarget();
            return _source != null ? _source.clip : null;
        }

        protected override void RestoreAsset(AudioClip asset)
        {
            CacheTarget();
            if (_source == null) return;
            _source.Stop();
            _source.clip = asset;
        }

private void CacheTarget()
        {
            if (_source == null) _source = GetComponent<AudioSource>();
        }
    }
}
