using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Fallback preview for asset types without a specialized renderer.
    /// Shows the asset's mini-thumbnail centered, plus the type name below.
    ///
    /// Users who want a nicer preview for their custom types should implement
    /// IAssetPreviewRenderer with a higher priority (10+).
    /// </summary>
    public class DefaultAssetPreviewRenderer : IAssetPreviewRenderer
    {
        public int Priority => 0;

        public bool CanRender(Object asset) => true;

        public void DrawPreview(Rect rect, Object asset)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.05f));

            if (asset == null)
            {
                EditorGUI.LabelField(rect, "(not assigned)",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter
                    });
                return;
            }

            // Try to get a rich asset preview first (for prefabs, materials, meshes)
            var rich = AssetPreview.GetAssetPreview(asset);

            if (rich != null)
            {
                var previewRect = new Rect(
                    rect.x + (rect.width - 64) / 2f,
                    rect.y + 6,
                    64, 64);
                GUI.DrawTexture(previewRect, rich, ScaleMode.ScaleToFit);
            }
            else
            {
                var thumb = AssetPreview.GetMiniThumbnail(asset);
                if (thumb != null)
                {
                    var iconRect = new Rect(
                        rect.x + rect.width / 2f - 24,
                        rect.y + 8,
                        48, 48);
                    GUI.DrawTexture(iconRect, thumb, ScaleMode.ScaleToFit);
                }
            }

            var labelRect = new Rect(rect.x, rect.y + rect.height - 18, rect.width, 14);
            EditorGUI.LabelField(labelRect, asset.GetType().Name,
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
