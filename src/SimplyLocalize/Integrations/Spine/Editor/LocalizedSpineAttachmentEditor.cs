using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Spine;
using global::Spine.Unity;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.Editor.Inspectors;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.SpineIntegration.Editor
{
    [CustomEditor(typeof(LocalizedSpineAttachment))]
    public sealed class LocalizedSpineAttachmentEditor : LocalizedAssetEditor
    {
        private SerializedProperty _targetProperty;
        private SerializedProperty _regionPathProperty;
        private SerializedProperty _atlasPageProperty;
        private SerializedProperty _monitorSkinChangesProperty;

        private readonly List<RegionDescriptor> _regions = new();
        private string[] _regionLabels = Array.Empty<string>();
        private Object _cachedSkeletonDataAsset;
        private string _regionLoadError;

        private int _languageIndex;
        private bool _assignExtractedTexture = true;
        private bool _showAdvanced;
        private string _destinationAddress;
        protected override void DrawComponentProperties() { }

        private void OnEnable()
        {
            BindProperties();
            RebuildRegionCache(force: true);
        }

        private void BindProperties()
        {
            _targetProperty = serializedObject.FindProperty("_target");
            _regionPathProperty = serializedObject.FindProperty("_regionPath");
            _atlasPageProperty = serializedObject.FindProperty("_atlasPage");
            _monitorSkinChangesProperty = serializedObject.FindProperty("_monitorSkinChanges");
        }

        public override void OnInspectorGUI()
        {
            if (_targetProperty == null ||
                _regionPathProperty == null || _atlasPageProperty == null ||
                _monitorSkinChangesProperty == null)
            {
                BindProperties();
                RebuildRegionCache(force: true);
            }

            base.OnInspectorGUI();
            serializedObject.Update();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Spine Attachment", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawRegionSelector();

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetProperty, new GUIContent("Spine Target"));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    RebuildRegionCache(force: true);
                    serializedObject.Update();
                }
                EditorGUILayout.PropertyField(_monitorSkinChangesProperty);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
            RebuildRegionCache(force: false);

            EditorGUILayout.Space(8);
            try
            {
                DrawValidationAndPreview();
                EditorGUILayout.Space(8);
                DrawExtractionTool();
            }
            catch (Exception failure) when (!(failure is ExitGUIException))
            { EditorGUILayout.HelpBox(failure.Message, MessageType.Error); }
        }

        private void DrawRegionSelector()
        {
            if (!string.IsNullOrEmpty(_regionLoadError))
            {
                EditorGUILayout.PropertyField(_regionPathProperty, new GUIContent("Region Path"));
                EditorGUILayout.HelpBox(_regionLoadError, MessageType.Error);
                return;
            }

            if (_regions.Count == 0)
            {
                EditorGUILayout.PropertyField(_regionPathProperty, new GUIContent("Region Path"));
                EditorGUILayout.HelpBox(
                    "No textured RegionAttachment or MeshAttachment was found in the selected SkeletonDataAsset.",
                    MessageType.Error);
                return;
            }

            var currentIndex = _regions.FindIndex(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal) &&
                ReferenceEquals(region.AtlasPage, _atlasPageProperty.objectReferenceValue));

            // Existing components created before Atlas Page was serialized only have a path.
            // Preserve that path and bind it to its first matching page instead of resetting
            // the selection to the first region in the skeleton.
            var pathOnlyIndex = _regions.FindIndex(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal));
            var displayedIndex = currentIndex >= 0
                ? currentIndex
                : Mathf.Max(0, pathOnlyIndex);
            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup(
                new GUIContent("Atlas Region"),
                displayedIndex,
                _regionLabels);

            if (EditorGUI.EndChangeCheck() || currentIndex < 0)
            {
                var selectedRegion = _regions[selectedIndex];
                _regionPathProperty.stringValue = selectedRegion.Path;
                _atlasPageProperty.objectReferenceValue = selectedRegion.AtlasPage;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawValidationAndPreview()
        {
            var descriptor = GetSelectedRegion();
            if (descriptor == null)
            {
                DrawStatus("Select a valid Spine atlas region.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Attachment Validation", EditorStyles.boldLabel);
            DrawStatus(
                $"{descriptor.Path} [{descriptor.PageTextureName}]: " +
                $"{descriptor.OriginalWidth}×{descriptor.OriginalHeight}, " +
                $"{descriptor.AttachmentNames.Count} attachment(s), {descriptor.SlotNames.Count} slot(s).",
                MessageType.Info);

            if (descriptor.HasUnsupportedAttachment)
            {
                DrawStatus(
                    "The region is also used by an unsupported attachment type. " +
                    "Only RegionAttachment and MeshAttachment are replaced.",
                    MessageType.Error);
            }

            if (descriptor.HasSequence)
            {
                DrawStatus(
                    "At least one attachment uses a Spine sequence. Only its base region is localized.",
                    MessageType.Warning);
            }

            if (descriptor.HasDifferentLogicalSizes)
            {
                DrawStatus(
                    "Attachments with this path report different logical region sizes.",
                    MessageType.Error);
            }

            if (descriptor.IsRotated || descriptor.IsTrimmed)
            {
                var details = string.Join(", ", new[]
                {
                    descriptor.IsRotated ? "rotated" : null,
                    descriptor.IsTrimmed ? "trimmed" : null
                }.Where(value => value != null));

                DrawStatus(
                    $"The atlas region is {details}. Extraction restores its original orientation and canvas.",
                    MessageType.Warning);
            }

            DrawLocalizedTextureValidation(descriptor);
            DrawSourcePreview(descriptor);
        }

        private void DrawLocalizedTextureValidation(RegionDescriptor descriptor)
        {
            var languageAssets = GetDirectLanguageAssets();
            if (languageAssets.Count == 0)
            {
                DrawStatus(
                    "No direct texture is assigned yet. Missing translations use channel fallback, then the original Spine attachment.",
                    MessageType.Warning);
                return;
            }

            foreach (var languageAsset in languageAssets)
            {
                if (languageAsset.Asset == null)
                    continue;

                if (languageAsset.Asset is not Texture2D texture)
                {
                    DrawStatus(
                        $"{languageAsset.LanguageCode}: asset '{languageAsset.Asset.name}' is not a Texture2D.",
                        MessageType.Error);
                    continue;
                }

                var width = texture.width;
                var height = texture.height;
                var exactSize = width == descriptor.OriginalWidth && height == descriptor.OriginalHeight;

                DrawStatus(
                    exactSize
                        ? $"{languageAsset.LanguageCode}: {texture.name}, {width}×{height}."
                        : $"{languageAsset.LanguageCode}: {texture.name} is {width}×{height}; " +
                          $"required size is {descriptor.OriginalWidth}×{descriptor.OriginalHeight}.",
                    exactSize ? MessageType.Info : MessageType.Error);

                DrawImporterWarnings(languageAsset.LanguageCode, texture);
            }
        }

        private static void DrawImporterWarnings(string languageCode, Texture2D texture)
        {
            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                return;

            var warnings = new List<string>();
            if (importer.wrapMode != TextureWrapMode.Clamp)
                warnings.Add("Wrap Mode is not Clamp");
            if (importer.mipmapEnabled)
                warnings.Add("Mip Maps are enabled");

            if (warnings.Count > 0)
            {
                DrawStatus(
                    $"{languageCode}: {string.Join("; ", warnings)}.",
                    MessageType.Warning);
            }
        }

        private static void DrawSourcePreview(RegionDescriptor descriptor)
        {
            if (descriptor.SourceTexture == null || descriptor.Region == null)
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Source region preview", EditorStyles.boldLabel);

            const float maxWidth = 320f;
            const float maxHeight = 220f;
            var aspect = descriptor.OriginalHeight > 0
                ? (float)descriptor.OriginalWidth / descriptor.OriginalHeight
                : 1f;
            var width = Mathf.Min(maxWidth, maxHeight * aspect);
            var height = width / Mathf.Max(0.001f, aspect);
            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));

            var sourceRect = SpineRegionExtraction.GetUnityAtlasRect(
                descriptor.Region,
                descriptor.SourceTexture.height);
            var uv = new Rect(
                sourceRect.x / descriptor.SourceTexture.width,
                sourceRect.y / descriptor.SourceTexture.height,
                sourceRect.width / descriptor.SourceTexture.width,
                sourceRect.height / descriptor.SourceTexture.height);

            EditorGUI.DrawPreviewTexture(rect, Texture2D.grayTexture);
            GUI.DrawTextureWithTexCoords(rect, descriptor.SourceTexture, uv, true);
        }

        private void DrawExtractionTool()
        {
            EditorGUILayout.LabelField("Localization Template", EditorStyles.boldLabel);

            var context = LocalizationEditorBindings.ContextFor((LocalizedSpineAttachment)target, out var entry);
            if (context == null || entry == null)
            {
                DrawStatus("Choose an asset entry in the selected channel and content set.", MessageType.Info);
                return;
            }
            var languages = context.Channel.Languages.ToArray();
            _languageIndex = Mathf.Clamp(_languageIndex, 0, languages.Length - 1);
            _languageIndex = EditorGUILayout.Popup("Target Language", _languageIndex, languages);
            string language = languages[_languageIndex];
            _assignExtractedTexture = EditorGUILayout.ToggleLeft("Assign extracted texture to this entry", _assignExtractedTexture);
            var sources = context.Set.Sources.FirstOrDefault(s => s.Language == language)?.Sources
                .Where(s => s.Kind == LocalizationSourceKind.AssetTable && s.ProviderId == "resources").ToArray()
                ?? Array.Empty<LocalizationSourceReference>();
            if (!sources.Any(s => s.Address == _destinationAddress)) _destinationAddress = null;
            if (sources.Length == 1) _destinationAddress = sources[0].Address;
            if (_assignExtractedTexture && sources.Length > 1)
            {
                int selected = Array.FindIndex(sources, s => s.Address == _destinationAddress) + 1;
                int next = EditorGUILayout.Popup("Destination table", selected,
                    new[] { "Choose a table" }.Concat(sources.Select(s => s.Address)).ToArray());
                _destinationAddress = next == 0 ? null : sources[next - 1].Address;
            }
            if (_assignExtractedTexture && _destinationAddress == null)
                EditorGUILayout.HelpBox("Connect a Resources asset table in Simply Localize > Assets and choose the destination. External providers use their own authoring tools.", MessageType.Info);
            var descriptor = GetSelectedRegion();
            bool canExtract = descriptor?.SourceTexture != null && (!_assignExtractedTexture || _destinationAddress != null);
            using (new EditorGUI.DisabledScope(!canExtract))
                if (GUILayout.Button("Extract and Import Localization Template", GUILayout.Height(28)))
                    ExtractTemplate(descriptor, context, entry.Id, language, _destinationAddress);
            if (descriptor?.SourceTexture == null)
                EditorGUILayout.HelpBox("Select a valid atlas region to enable extraction.", MessageType.Info);
        }

        private void ExtractTemplate(RegionDescriptor descriptor, EditorProjectContext context, string entryId, string language, string address)
        {
            string outputPath = SpineRegionExtraction.BuildOutputPath(descriptor.SourceTexture, descriptor.Path, language);
            if (File.Exists(ToAbsolutePath(outputPath)) && !EditorUtility.DisplayDialog("Overwrite localization template?",
                    "The file already exists:\n" + outputPath, "Overwrite", "Cancel")) return;
            Texture2D texture = null;
            try
            {
                // Resolve source ownership before writing a PNG; never pick an unrelated first table.
                using var edit = _assignExtractedTexture ? new LocalizationProjectAssetEdit(context.Config) : null;
                if (edit != null) edit.SetAsset(entryId, language, null, address);
                texture = SpineRegionExtraction.ExtractAndImport(descriptor.Region, outputPath);
                if (texture == null) throw new InvalidOperationException("Unity did not import the extracted PNG as a Texture2D.");
                if (edit != null)
                {
                    edit.SetAsset(entryId, language, texture, address);
                    LocalizationProjectUndo.Record(edit.Commit(), "Assign localized Spine attachment");
                }
                Selection.activeObject = texture; EditorGUIUtility.PingObject(texture); Repaint();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Spine localization extraction failed",
                    exception.Message + (texture != null ? "\nThe extracted PNG remains at " + outputPath + "; localization assignment was not saved." : ""), "OK");
            }
        }

        private void RebuildRegionCache(bool force)
        {
            var skeletonDataAsset = GetSkeletonDataAsset(_targetProperty?.objectReferenceValue as Component);
            if (!force && ReferenceEquals(_cachedSkeletonDataAsset, skeletonDataAsset))
                return;

            _cachedSkeletonDataAsset = skeletonDataAsset;
            _regions.Clear();
            _regionLabels = Array.Empty<string>();
            _regionLoadError = null;

            if (skeletonDataAsset == null)
            {
                _regionLoadError = "Select a SkeletonRenderer/SkeletonAnimation or SkeletonGraphic with a SkeletonDataAsset.";
                return;
            }

            SkeletonData skeletonData;
            try
            {
                skeletonData = skeletonDataAsset.GetSkeletonData(true);
            }
            catch (Exception exception)
            {
                _regionLoadError = $"Could not load SkeletonData: {exception.Message}";
                return;
            }

            if (skeletonData == null)
            {
                _regionLoadError = "SkeletonDataAsset returned no SkeletonData.";
                return;
            }

            var descriptors = new Dictionary<RegionKey, RegionDescriptor>();
            var visitedSkins = new HashSet<Skin>();

            AddSkinRegions(skeletonData.DefaultSkin, skeletonData, descriptors, visitedSkins);
            foreach (var skin in skeletonData.Skins)
                AddSkinRegions(skin, skeletonData, descriptors, visitedSkins);

            _regions.AddRange(descriptors.Values
                .OrderBy(region => region.Path, StringComparer.Ordinal)
                .ThenBy(region => region.PageTextureName, StringComparer.Ordinal));
            _regionLabels = _regions.Select(region =>
                $"{region.Path}    [{region.PageTextureName}]    " +
                $"{region.OriginalWidth}×{region.OriginalHeight}    " +
                $"({region.AttachmentNames.Count})").ToArray();
        }

        private static void AddSkinRegions(
            Skin skin,
            SkeletonData skeletonData,
            IDictionary<RegionKey, RegionDescriptor> descriptors,
            ISet<Skin> visitedSkins)
        {
            if (skin == null || !visitedSkins.Add(skin))
                return;

            foreach (var entry in skin.Attachments)
            {
                if (entry.Attachment is not IHasTextureRegion textured ||
                    string.IsNullOrWhiteSpace(textured.Path))
                {
                    continue;
                }

                var atlasPage = (textured.Region as AtlasRegion)?.page?.rendererObject as Material;
                var key = new RegionKey(textured.Path, atlasPage);
                if (!descriptors.TryGetValue(key, out var descriptor))
                {
                    descriptor = new RegionDescriptor(textured.Path, atlasPage);
                    descriptors.Add(key, descriptor);
                }

                descriptor.Add(entry, textured, skeletonData);
            }
        }

        private RegionDescriptor GetSelectedRegion()
        {
            return _regions.FirstOrDefault(region =>
                string.Equals(region.Path, _regionPathProperty.stringValue, StringComparison.Ordinal) &&
                ReferenceEquals(region.AtlasPage, _atlasPageProperty.objectReferenceValue));
        }

        private static SkeletonDataAsset GetSkeletonDataAsset(Component component)
        {
            return component switch
            {
                SkeletonRenderer renderer => renderer.SkeletonDataAsset,
                SkeletonGraphic graphic => graphic.SkeletonDataAsset,
                _ => null
            };
        }

        private List<LanguageAsset> GetDirectLanguageAssets()
        {
            var result = new List<LanguageAsset>();
            var context = LocalizationEditorBindings.ContextFor((LocalizedSpineAttachment)target, out var entry);
            if (context == null || entry == null) return result;
            foreach (string language in context.Channel.Languages)
                if (context.Assets.TryGetDirect(entry.Id, language, out var cell))
                    result.Add(new LanguageAsset(language, cell.Asset));
            return result;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static void DrawStatus(string message, MessageType type)
        {
            var previousColor = GUI.color;
            GUI.color = type switch
            {
                MessageType.Error => new Color(1f, 0.72f, 0.72f),
                MessageType.Warning => new Color(1f, 0.9f, 0.55f),
                _ => new Color(0.78f, 1f, 0.78f)
            };
            EditorGUILayout.HelpBox(message, type);
            GUI.color = previousColor;
        }

        private sealed class RegionDescriptor
        {
            public readonly string Path;
            public readonly Material AtlasPage;
            public readonly HashSet<string> AttachmentNames = new(StringComparer.Ordinal);
            public readonly HashSet<string> SlotNames = new(StringComparer.Ordinal);

            public AtlasRegion Region;
            public Texture2D SourceTexture;
            public int OriginalWidth;
            public int OriginalHeight;
            public bool HasUnsupportedAttachment;
            public bool HasSequence;
            public bool HasDifferentLogicalSizes;

            public string PageTextureName => AtlasPage?.mainTexture != null
                ? AtlasPage.mainTexture.name
                : "Unknown page";

            public bool IsRotated => Region != null && Region.degrees != 0;
            public bool IsTrimmed => Region != null &&
                                     (Region.originalWidth != Region.packedWidth ||
                                      Region.originalHeight != Region.packedHeight ||
                                      !Mathf.Approximately(Region.offsetX, 0f) ||
                                      !Mathf.Approximately(Region.offsetY, 0f));

            public RegionDescriptor(string path, Material atlasPage)
            {
                Path = path;
                AtlasPage = atlasPage;
            }

            public void Add(Skin.SkinEntry entry, IHasTextureRegion textured, SkeletonData skeletonData)
            {
                AttachmentNames.Add(entry.Name);
                var slots = skeletonData.Slots;
                if (entry.SlotIndex >= 0 && entry.SlotIndex < slots.Count)
                    SlotNames.Add(slots.Items[entry.SlotIndex].Name);

                if (entry.Attachment is not RegionAttachment && entry.Attachment is not MeshAttachment)
                    HasUnsupportedAttachment = true;
                if (textured.Sequence != null)
                    HasSequence = true;

                if (textured.Region is not AtlasRegion atlasRegion)
                    return;

                var width = atlasRegion.originalWidth > 0
                    ? atlasRegion.originalWidth
                    : atlasRegion.packedWidth;
                var height = atlasRegion.originalHeight > 0
                    ? atlasRegion.originalHeight
                    : atlasRegion.packedHeight;

                if (Region != null && (OriginalWidth != width || OriginalHeight != height))
                    HasDifferentLogicalSizes = true;

                Region ??= atlasRegion;
                OriginalWidth = OriginalWidth == 0 ? width : OriginalWidth;
                OriginalHeight = OriginalHeight == 0 ? height : OriginalHeight;

                if (SourceTexture == null && atlasRegion.page?.rendererObject is Material material)
                    SourceTexture = material.mainTexture as Texture2D;
            }
        }

        private readonly struct RegionKey : IEquatable<RegionKey>
        {
            private readonly string _path;
            private readonly Material _atlasPage;

            public RegionKey(string path, Material atlasPage)
            {
                _path = path;
                _atlasPage = atlasPage;
            }

            public bool Equals(RegionKey other)
            {
                return string.Equals(_path, other._path, StringComparison.Ordinal) &&
                       ReferenceEquals(_atlasPage, other._atlasPage);
            }

            public override bool Equals(object obj)
            {
                return obj is RegionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_path != null ? StringComparer.Ordinal.GetHashCode(_path) : 0) * 397) ^
                           (_atlasPage != null ? _atlasPage.GetInstanceID() : 0);
                }
            }
        }

        private readonly struct LanguageAsset
        {
            public readonly string LanguageCode;
            public readonly Object Asset;

            public LanguageAsset(string languageCode, Object asset)
            {
                LanguageCode = languageCode;
                Asset = asset;
            }
        }
    }

    internal static class SpineRegionExtraction
    {
        public static string BuildOutputPath(Texture2D sourceTexture, string regionPath, string languageCode)
        {
            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture).Replace('\\', '/');
            var sourceDirectory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            if (!sourceDirectory.StartsWith("Assets/", StringComparison.Ordinal) && sourceDirectory != "Assets")
                sourceDirectory = "Assets/SimplyLocalize/Extracted/Spine";
            var safeRegionPath = Sanitize(regionPath.Replace('/', '_'));
            var leafName = Sanitize(regionPath.Split('/').Last());
            var fileName = $"{leafName}_{Sanitize(languageCode.ToLowerInvariant())}.png";
            return $"{sourceDirectory}/Localized/{safeRegionPath}/{fileName}";
        }

        public static Texture2D ExtractAndImport(AtlasRegion region, string outputAssetPath)
        {
            EditorDataPath.ValidateNewAssetPath(outputAssetPath);
            LocalizationProjectChange.ValidateWritable(outputAssetPath);
            if (!string.Equals(Path.GetExtension(outputAssetPath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a PNG output path under Assets.", nameof(outputAssetPath));
            if (File.Exists(outputAssetPath) && (File.GetAttributes(outputAssetPath) & FileAttributes.ReadOnly) != 0)
                throw new IOException("The output PNG is read-only: " + outputAssetPath);
            if (region?.page?.rendererObject is not Material material ||
                material.mainTexture is not Texture2D sourceTexture)
            {
                throw new InvalidOperationException("The Spine atlas region has no readable Texture2D page.");
            }

            var sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
            if (AssetImporter.GetAtPath(sourcePath) is not TextureImporter sourceImporter)
                throw new InvalidOperationException("The source Spine atlas has no TextureImporter.");

            var wasReadable = sourceImporter.isReadable;
            // A package atlas can be read when already readable, but its importer is never modified.
            if (!wasReadable)
            {
                LocalizationProjectChange.ValidateWritable(sourcePath);
                if (File.Exists(sourcePath + ".meta") && (File.GetAttributes(sourcePath + ".meta") & FileAttributes.ReadOnly) != 0)
                    throw new IOException("The source atlas importer is read-only. Copy the atlas into Assets before extracting.");
            }
            Color32[] sourcePixels;
            int sourceWidth;
            int sourceHeight;

            try
            {
                if (!wasReadable)
                {
                    sourceImporter.isReadable = true;
                    sourceImporter.SaveAndReimport();
                }

                var readableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                sourcePixels = readableTexture.GetPixels32();
                sourceWidth = readableTexture.width;
                sourceHeight = readableTexture.height;
            }
            finally
            {
                if (!wasReadable)
                {
                    sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                    if (sourceImporter != null)
                    {
                        sourceImporter.isReadable = false;
                        sourceImporter.SaveAndReimport();
                    }
                }
            }

            var packedPixels = ExtractPackedPixels(region, sourcePixels, sourceWidth, sourceHeight);
            var logicalPixels = RestoreOriginalCanvas(region, packedPixels);

            if (region.page.pma)
                UnpremultiplyAlpha(logicalPixels);

            var outputWidth = GetOriginalWidth(region);
            var outputHeight = GetOriginalHeight(region);
            var outputTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false, false);
            try
            {
                outputTexture.SetPixels32(logicalPixels);
                outputTexture.Apply(false, false);
                var absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputAssetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
                File.WriteAllBytes(absolutePath, outputTexture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(outputTexture); }

            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureLocalizedTextureImporter(outputAssetPath, sourceImporter);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
        }

        public static Rect GetUnityAtlasRect(AtlasRegion region, int textureHeight)
        {
            var width = region.degrees == 270 ? region.packedHeight : region.packedWidth;
            var height = region.degrees == 270 ? region.packedWidth : region.packedHeight;
            return new Rect(region.x, textureHeight - region.y - height, width, height);
        }

        private static PackedPixels ExtractPackedPixels(
            AtlasRegion region,
            IReadOnlyList<Color32> sourcePixels,
            int sourceWidth,
            int sourceHeight)
        {
            var rect = GetUnityAtlasRect(region, sourceHeight);
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            var startX = Mathf.RoundToInt(rect.x);
            var startY = Mathf.RoundToInt(rect.y);

            if (startX < 0 || startY < 0 || startX + width > sourceWidth || startY + height > sourceHeight)
                throw new InvalidOperationException("The Spine atlas region lies outside its source texture.");

            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = sourcePixels[(startY + y) * sourceWidth + startX + x];
            }

            if (region.degrees == 270)
                return RotateCounterClockwise(pixels, width, height);

            if (region.degrees != 0)
            {
                throw new NotSupportedException(
                    $"Atlas rotation of {region.degrees} degrees is not supported by the extractor.");
            }

            return new PackedPixels(pixels, width, height);
        }

        private static PackedPixels RotateCounterClockwise(Color32[] source, int width, int height)
        {
            var outputWidth = height;
            var outputHeight = width;
            var output = new Color32[outputWidth * outputHeight];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var destinationX = y;
                    var destinationY = width - 1 - x;
                    output[destinationY * outputWidth + destinationX] = source[y * width + x];
                }
            }

            return new PackedPixels(output, outputWidth, outputHeight);
        }

        private static Color32[] RestoreOriginalCanvas(AtlasRegion region, PackedPixels packed)
        {
            var outputWidth = GetOriginalWidth(region);
            var outputHeight = GetOriginalHeight(region);
            var output = new Color32[outputWidth * outputHeight];
            var offsetX = Mathf.RoundToInt(region.offsetX);
            var offsetY = Mathf.RoundToInt(region.offsetY);

            if (offsetX < 0 || offsetY < 0 ||
                offsetX + packed.Width > outputWidth ||
                offsetY + packed.Height > outputHeight)
            {
                throw new InvalidOperationException(
                    "The packed region and its trim offsets do not fit the original canvas.");
            }

            for (var y = 0; y < packed.Height; y++)
            {
                Array.Copy(
                    packed.Pixels,
                    y * packed.Width,
                    output,
                    (offsetY + y) * outputWidth + offsetX,
                    packed.Width);
            }

            return output;
        }

        private static void UnpremultiplyAlpha(Color32[] pixels)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                if (color.a == 0 || color.a == 255)
                    continue;

                color.r = (byte)Mathf.Min(255, Mathf.RoundToInt(color.r * 255f / color.a));
                color.g = (byte)Mathf.Min(255, Mathf.RoundToInt(color.g * 255f / color.a));
                color.b = (byte)Mathf.Min(255, Mathf.RoundToInt(color.b * 255f / color.a));
                pixels[i] = color;
            }
        }

        private static void ConfigureLocalizedTextureImporter(string assetPath, TextureImporter sourceImporter)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                throw new InvalidOperationException("Unity did not create a TextureImporter for the extracted PNG.");

            importer.textureType = TextureImporterType.Default;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = sourceImporter != null ? sourceImporter.filterMode : FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static int GetOriginalWidth(AtlasRegion region)
        {
            return region.originalWidth > 0 ? region.originalWidth : region.packedWidth;
        }

        private static int GetOriginalHeight(AtlasRegion region)
        {
            return region.originalHeight > 0 ? region.originalHeight : region.packedHeight;
        }

        private static string Sanitize(string value)
        {
            return EditorDataPath.StorageName(value.Replace(' ', '_'), "region");
        }

        private readonly struct PackedPixels
        {
            public readonly Color32[] Pixels;
            public readonly int Width;
            public readonly int Height;

            public PackedPixels(Color32[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }
    }
}
