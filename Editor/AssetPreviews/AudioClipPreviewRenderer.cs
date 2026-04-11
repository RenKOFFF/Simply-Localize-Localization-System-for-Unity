using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Draws an AudioClip preview with metadata (duration / frequency / channels)
    /// and a Play button that uses Unity's internal AudioUtil via reflection.
    ///
    /// Play always stops any previously-playing preview clip first, so rapid clicks
    /// don't produce overlapping audio.
    /// </summary>
    public class AudioClipPreviewRenderer : IAssetPreviewRenderer
    {
        public int Priority => 10;

        public bool CanRender(Object asset) => asset is AudioClip;

        public void DrawPreview(Rect rect, Object asset)
        {
            var clip = (AudioClip)asset;
            if (clip == null) return;

            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.08f));

            // Icon
            var iconRect = new Rect(rect.x + 8, rect.y + 8, 32, 32);
            var thumb = AssetPreview.GetMiniThumbnail(clip);
            if (thumb != null)
                GUI.DrawTexture(iconRect, thumb, ScaleMode.ScaleToFit);

            // Name
            var infoRect = new Rect(rect.x + 48, rect.y + 8, rect.width - 56, 16);
            EditorGUI.LabelField(infoRect, clip.name, EditorStyles.miniLabel);

            // Metadata
            var metaRect = new Rect(rect.x + 48, rect.y + 26, rect.width - 56, 14);
            string meta = $"{clip.length:F2}s | {clip.frequency / 1000}kHz | {clip.channels}ch";
            EditorGUI.LabelField(metaRect, meta, EditorStyles.miniLabel);

            // Play button
            var btnRect = new Rect(rect.x + 8, rect.y + rect.height - 24, rect.width - 16, 18);

            if (GUI.Button(btnRect, "▶ Play", EditorStyles.miniButton))
            {
                StopAllPreviewClips();
                PlayPreviewClip(clip);
            }
        }

        // ──────────────────────────────────────────────
        //  Internal: reflection into UnityEditor.AudioUtil
        // ──────────────────────────────────────────────

        private static System.Type _audioUtilType;
        private static MethodInfo _playMethod;
        private static MethodInfo _stopAllMethod;
        private static bool _reflectionInitialized;

        private static void EnsureReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            _audioUtilType = System.Type.GetType("UnityEditor.AudioUtil, UnityEditor");
            if (_audioUtilType == null) return;

            // Unity 2020+: PlayPreviewClip(AudioClip, int startSample, bool loop)
            _playMethod = _audioUtilType.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public);

            // Older fallback
            if (_playMethod == null)
            {
                _playMethod = _audioUtilType.GetMethod(
                    "PlayClip",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(AudioClip) }, null);
            }

            // StopAllPreviewClips was renamed in 2020+
            _stopAllMethod = _audioUtilType.GetMethod(
                "StopAllPreviewClips",
                BindingFlags.Static | BindingFlags.Public);

            if (_stopAllMethod == null)
            {
                _stopAllMethod = _audioUtilType.GetMethod(
                    "StopAllClips",
                    BindingFlags.Static | BindingFlags.Public);
            }
        }

        private static void PlayPreviewClip(AudioClip clip)
        {
            EnsureReflection();
            if (_playMethod == null) return;

            var parameters = _playMethod.GetParameters();

            if (parameters.Length == 3)
                _playMethod.Invoke(null, new object[] { clip, 0, false });
            else if (parameters.Length == 1)
                _playMethod.Invoke(null, new object[] { clip });
        }

        private static void StopAllPreviewClips()
        {
            EnsureReflection();
            _stopAllMethod?.Invoke(null, null);
        }
    }
}
