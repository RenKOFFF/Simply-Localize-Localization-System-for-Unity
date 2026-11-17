using System.Collections.Generic;
using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.SpineIntegration.Editor
{
    [CustomEditor(typeof(LocalizedSpineTexture))]
    public sealed class LocalizedSpineTextureEditor : LocalizedAssetEditor
    {
        protected override void DrawComponentProperties() { }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            var atlasPageProperty = serializedObject.FindProperty("_atlasPage");
            var component = (LocalizedSpineTexture)target;
            var materials = CollectAtlasPageMaterials(component);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Spine Atlas Page", EditorStyles.boldLabel);
            DrawAtlasPageSelector(atlasPageProperty, materials);
            serializedObject.ApplyModifiedProperties();

            try { DrawValidation(component, materials); }
            catch (Exception failure) when (!(failure is ExitGUIException))
            { EditorGUILayout.HelpBox(failure.Message, MessageType.Error); }
        }

        private static List<Material> CollectAtlasPageMaterials(LocalizedSpineTexture component)
        {
            var result = new List<Material>();
            var skeletonDataAsset = component.SkeletonDataAsset;
            if (skeletonDataAsset?.atlasAssets == null)
                return result;

            foreach (var atlasAsset in skeletonDataAsset.atlasAssets)
            {
                if (atlasAsset == null)
                    continue;

                foreach (var material in atlasAsset.Materials)
                {
                    if (material != null && !result.Contains(material))
                        result.Add(material);
                }
            }

            return result;
        }

        private static void DrawAtlasPageSelector(
            SerializedProperty atlasPageProperty,
            IReadOnlyList<Material> materials)
        {
            var labels = new string[materials.Count + 1];
            labels[0] = "<Auto: size and filename>";

            var selectedIndex = 0;
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                var texture = material.mainTexture;
                labels[i + 1] = texture != null
                    ? $"{texture.name}  ({texture.width}x{texture.height}) — {material.name}"
                    : $"<No texture> — {material.name}";

                if (ReferenceEquals(atlasPageProperty.objectReferenceValue, material))
                    selectedIndex = i + 1;
            }

            EditorGUI.BeginChangeCheck();
            selectedIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Atlas Page",
                    "The original Spine atlas page material that will receive the localized texture."),
                selectedIndex,
                labels);
            if (EditorGUI.EndChangeCheck())
                atlasPageProperty.objectReferenceValue = selectedIndex == 0
                    ? null
                    : materials[selectedIndex - 1];
        }

        private static void DrawValidation(
            LocalizedSpineTexture component,
            IReadOnlyList<Material> materials)
        {
            if (component.SkeletonDataAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "Add this component to the same GameObject as SkeletonRenderer, " +
                    "SkeletonAnimation, or SkeletonGraphic.",
                    MessageType.Error);
                return;
            }

            var atlasPage = component.AtlasPage;
            if (atlasPage == null)
            {
                var messageType = materials.Count > 1 ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(
                    materials.Count > 1
                        ? "This skeleton has multiple atlas pages. Select Atlas Page explicitly; " +
                          "automatic matching is intended only as a safe fallback."
                        : "Atlas Page will be resolved automatically from localized texture size and filename.",
                    messageType);
                return;
            }

            if (!materials.Contains(atlasPage))
            {
                EditorGUILayout.HelpBox(
                    "The selected material is not an atlas page of this SkeletonDataAsset.",
                    MessageType.Error);
                return;
            }

            var sourceTexture = atlasPage.mainTexture;
            if (sourceTexture == null)
            {
                EditorGUILayout.HelpBox("The selected atlas page has no texture.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Source Texture", sourceTexture.name);
            EditorGUILayout.LabelField("Required Size", $"{sourceTexture.width} x {sourceTexture.height}");

            if (TryGetPremultiplyAlpha(component, atlasPage, out var premultiplyAlpha))
            {
                EditorGUILayout.LabelField(
                    "Atlas Alpha",
                    premultiplyAlpha ? "Premultiplied (PMA)" : "Straight");

                if (premultiplyAlpha)
                {
                    EditorGUILayout.HelpBox(
                        "This atlas page uses PMA. Localized pixels must also be exported with " +
                        "premultiplied alpha; Unity import settings alone cannot verify pixel data.",
                        MessageType.Info);
                }
            }

            var duplicates = component.GetComponents<LocalizedSpineTexture>()
                .Count(other => other != component && ReferenceEquals(other.AtlasPage, atlasPage));
            if (duplicates > 0)
            {
                EditorGUILayout.HelpBox(
                    "Another Localized Spine Texture component targets the same atlas page. " +
                    "Only one component should own each page.",
                    MessageType.Error);
            }

            ValidateLocalizedTextures(component, sourceTexture);
        }

        private static void ValidateLocalizedTextures(
            LocalizedSpineTexture component,
            Texture sourceTexture)
        {
            var context = LocalizationEditorBindings.ContextFor(component, out var entry);
            if (context == null || entry == null) return;
            var reportedTextures = new HashSet<Texture2D>();
            foreach (string language in context.Channel.Languages)
            {
                if (!context.Assets.TryResolve(entry.Id, language, out var result, typeof(Texture2D))) continue;
                var localizedTexture = result.Asset as Texture2D;
                if (localizedTexture == null || !reportedTextures.Add(localizedTexture)) continue;

                if (localizedTexture.width != sourceTexture.width ||
                    localizedTexture.height != sourceTexture.height)
                {
                    EditorGUILayout.HelpBox(
                        $"{localizedTexture.name}: {localizedTexture.width}x{localizedTexture.height}; " +
                        $"atlas page requires {sourceTexture.width}x{sourceTexture.height}.",
                        MessageType.Error);
                }

                if (localizedTexture.wrapMode != sourceTexture.wrapMode)
                {
                    EditorGUILayout.HelpBox(
                        $"{localizedTexture.name}: Wrap Mode differs from source " +
                        $"({localizedTexture.wrapMode} instead of {sourceTexture.wrapMode}).",
                        MessageType.Warning);
                }

                if (localizedTexture.filterMode != sourceTexture.filterMode)
                {
                    EditorGUILayout.HelpBox(
                        $"{localizedTexture.name}: Filter Mode differs from source " +
                        $"({localizedTexture.filterMode} instead of {sourceTexture.filterMode}).",
                        MessageType.Warning);
                }

                var localizedHasMipMaps = localizedTexture.mipmapCount > 1;
                var sourceHasMipMaps = sourceTexture.mipmapCount > 1;
                if (localizedHasMipMaps != sourceHasMipMaps)
                {
                    EditorGUILayout.HelpBox(
                        $"{localizedTexture.name}: mip-map setting differs from the source atlas page.",
                        MessageType.Warning);
                }
            }
        }

        private static bool TryGetPremultiplyAlpha(
            LocalizedSpineTexture component,
            Material atlasPageMaterial,
            out bool premultiplyAlpha)
        {
            var skeletonDataAsset = component.SkeletonDataAsset;
            if (skeletonDataAsset?.atlasAssets != null)
            {
                foreach (var atlasAsset in skeletonDataAsset.atlasAssets)
                {
                    var atlas = atlasAsset?.GetAtlas();
                    if (atlas == null)
                        continue;

                    foreach (var page in atlas.Pages)
                    {
                        if (!ReferenceEquals(page.rendererObject, atlasPageMaterial))
                            continue;

                        premultiplyAlpha = page.pma;
                        return true;
                    }
                }
            }

            premultiplyAlpha = false;
            return false;
        }

    }
}
