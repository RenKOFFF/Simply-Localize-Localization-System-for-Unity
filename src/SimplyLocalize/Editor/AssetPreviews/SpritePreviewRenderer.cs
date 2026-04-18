using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Draws a Sprite preview respecting its rect within the texture atlas.
    /// Supports sliced/packed sprites — not just standalone textures.
    /// </summary>
    public class SpritePreviewRenderer : IAssetPreviewRenderer
    {
        public int Priority => 10;

        public bool CanRender(Object asset) => asset is Sprite;

        public void DrawPreview(Rect rect, Object asset)
        {
            var sprite = (Sprite)asset;
            if (sprite == null) return;

            // Dark background to show transparency
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f));

            var tex = sprite.texture;
            if (tex == null) return;

            var spriteRect = sprite.rect;
            var uv = new Rect(
                spriteRect.x / tex.width,
                spriteRect.y / tex.height,
                spriteRect.width / tex.width,
                spriteRect.height / tex.height);

            // Preserve aspect ratio inside the target rect
            float aspect = spriteRect.width / spriteRect.height;
            float drawW = rect.width;
            float drawH = rect.width / aspect;

            if (drawH > rect.height)
            {
                drawH = rect.height;
                drawW = rect.height * aspect;
            }

            var drawRect = new Rect(
                rect.x + (rect.width - drawW) / 2f,
                rect.y + (rect.height - drawH) / 2f,
                drawW, drawH);

            GUI.DrawTextureWithTexCoords(drawRect, tex, uv);
        }
    }
}
