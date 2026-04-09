using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Draws a Texture2D as a scaled-to-fit image.
    /// </summary>
    public class TexturePreviewRenderer : IAssetPreviewRenderer
    {
        public int Priority => 10;

        public bool CanRender(Object asset) => asset is Texture2D;

        public void DrawPreview(Rect rect, Object asset)
        {
            var tex = (Texture2D)asset;
            if (tex == null) return;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }
    }
}
