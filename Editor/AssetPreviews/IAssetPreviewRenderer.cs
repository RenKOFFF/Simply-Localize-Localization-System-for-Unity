using UnityEngine;

namespace SimplyLocalize.Editor.AssetPreviews
{
    /// <summary>
    /// Renders a large preview of a localized asset in the Assets tab.
    /// Implement this interface to add preview support for custom asset types.
    ///
    /// Discovery is automatic: any non-abstract class implementing this interface
    /// is instantiated once by AssetPreviewRegistry and used by the AssetsTab.
    ///
    /// Built-in implementations live next to this file:
    ///   - SpritePreviewRenderer
    ///   - TexturePreviewRenderer
    ///   - AudioClipPreviewRenderer
    ///   - DefaultAssetPreviewRenderer (fallback)
    ///
    /// Example for Mesh previews:
    /// <code>
    /// public class MeshPreviewRenderer : IAssetPreviewRenderer
    /// {
    ///     public int Priority => 10;
    ///     public bool CanRender(Object asset) => asset is Mesh;
    ///     public void DrawPreview(Rect rect, Object asset)
    ///     {
    ///         var mesh = (Mesh)asset;
    ///         var tex = AssetPreview.GetAssetPreview(mesh);
    ///         if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IAssetPreviewRenderer
    {
        /// <summary>
        /// Returns true if this renderer can draw a preview for the given asset.
        /// Called once per cell — keep it cheap (prefer `is` checks over reflection).
        /// </summary>
        bool CanRender(Object asset);

        /// <summary>
        /// Draws the preview into the given rect. Called from IMGUI / OnGUI context
        /// inside an IMGUIContainer — use EditorGUI / GUI APIs freely.
        /// </summary>
        void DrawPreview(Rect rect, Object asset);

        /// <summary>
        /// Priority resolves conflicts when multiple renderers claim the same asset.
        /// Higher wins. Built-in renderers use 10. Use 100+ to override built-ins.
        /// The fallback DefaultAssetPreviewRenderer uses 0.
        /// </summary>
        int Priority { get; }
    }
}
