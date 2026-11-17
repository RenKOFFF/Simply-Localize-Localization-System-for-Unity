using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SimplyLocalize.Components;
using SimplyLocalize.Editor.Data;
using SimplyLocalize.SpineIntegration;
using SimplyLocalize.SpineIntegration.Editor;
using Spine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Tests
{
    // Optional tests: use the real Spine package, never a substitute API or runtime stub.
    public class SpineEditorChannelTests
    {
        private string root, prefix, sourcePath;
        private Texture2D source;
        private Material material;
        private string[] selection;
        private static readonly Type Extraction = typeof(LocalizedSpineAttachmentEditor).Assembly.GetType("SimplyLocalize.SpineIntegration.Editor.SpineRegionExtraction", true);
        [SetUp] public void Setup()
        {
            prefix = "SLSpine_" + Guid.NewGuid().ToString("N").Substring(0, 8); root = "Assets/" + prefix;
            selection = new[] { "Config", "Channel", "Set" }.Select(k => SessionState.GetString("SimplyLocalize.ProjectEditor." + k, "")).ToArray();
            Directory.CreateDirectory(root); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            sourcePath = root + "/Atlas.png";
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            image.SetPixels32(new[] { new Color32(128, 0, 0, 128), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255) });
            image.Apply(); File.WriteAllBytes(sourcePath, image.EncodeToPNG()); Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            importer.isReadable = false; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.sRGBTexture = false; importer.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = source };
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(material);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            AssetDatabase.DeleteAsset(root);
            for (int i = 0; i < 3; i++) SessionState.SetString("SimplyLocalize.ProjectEditor." + new[] { "Config", "Channel", "Set" }[i], selection[i]);
            EditorProjectSelection.Invalidate();
        }
        private static T Call<T>(string name, params object[] args)
        {
            try { return (T)Extraction.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException failure) { throw failure.InnerException ?? failure; }
        }
        private AtlasRegion Region() => new()
        {
            page = new AtlasPage { rendererObject = material, width = 2, height = 2, pma = true },
            x = 0, y = 1, packedWidth = 1, packedHeight = 1, originalWidth = 3, originalHeight = 3, offsetX = 1, offsetY = 1
        };
        [Test] public void ExtractRestoresTrimAndStraightAlphaWithoutChangingSourceImporter()
        {
            byte[] before = File.ReadAllBytes(sourcePath + ".meta");
            string output = root + "/Output/region.png"; var texture = Call<Texture2D>("ExtractAndImport", Region(), output);
            Assert.That(texture.width, Is.EqualTo(3)); Assert.That(texture.height, Is.EqualTo(3));
            var decoded = new Texture2D(2, 2);
            try
            {
                Assert.That(decoded.LoadImage(File.ReadAllBytes(output)), Is.True);
                var colors = decoded.GetPixels32(); Assert.That(colors[4], Is.EqualTo(new Color32(255, 0, 0, 128)));
                Assert.That(colors.Where((_, i) => i != 4).All(c => c.a == 0), Is.True);
            }
            finally { Object.DestroyImmediate(decoded); }
            Assert.That(File.ReadAllBytes(sourcePath + ".meta"), Is.EqualTo(before));
            var importer = (TextureImporter)AssetImporter.GetAtPath(output);
            Assert.That(importer.isReadable || importer.mipmapEnabled, Is.False);
        }
        [TestCase(90)][TestCase(180)][TestCase(-1)]
        public void UnsupportedRotationFailsAndRestoresSource(int rotation)
        {
            var region = Region(); region.degrees = rotation; byte[] before = File.ReadAllBytes(sourcePath + ".meta");
            Assert.Throws<NotSupportedException>(() => Call<Texture2D>("ExtractAndImport", region, root + "/bad.png"));
            Assert.That(File.Exists(root + "/bad.png"), Is.False); Assert.That(File.ReadAllBytes(sourcePath + ".meta"), Is.EqualTo(before));
        }
        [Test] public void RotatedRegionHasExpectedOrientation()
        {
            var region = Region(); region.y = 0; region.degrees = 270; region.packedWidth = 2; region.packedHeight = 1;
            region.originalWidth = 2; region.originalHeight = 1; region.offsetX = region.offsetY = 0;
            string output = root + "/rotated.png"; Call<Texture2D>("ExtractAndImport", region, output);
            var decoded = new Texture2D(2, 2);
            try { decoded.LoadImage(File.ReadAllBytes(output)); Assert.That(decoded.GetPixels32(), Is.EqualTo(new[] { new Color32(255, 0, 0, 128), new Color32(0, 0, 255, 255) })); }
            finally { Object.DestroyImmediate(decoded); }
        }
        [TestCase("../outside.png")][TestCase("Packages/Spine/out.png")][TestCase("Assets/../out.png")]
        [TestCase("Assets/CON/out.png")][TestCase("Assets/.hidden/out.png")][TestCase("Assets/out.tmp/file.png")]
        [TestCase("Assets/out.asset")]
        public void InvalidDestinationFailsBeforeReadingOrChangingImporter(string path)
        {
            byte[] before = File.ReadAllBytes(sourcePath + ".meta");
            Assert.Catch<Exception>(() => Call<Texture2D>("ExtractAndImport", Region(), path));
            Assert.That(File.ReadAllBytes(sourcePath + ".meta"), Is.EqualTo(before));
        }
        [Test] public void ReadOnlyOutputIsNotOverwritten()
        {
            string path = root + "/readonly.png"; File.WriteAllText(path, "Keep");
            File.SetAttributes(path, FileAttributes.ReadOnly); byte[] before = File.ReadAllBytes(sourcePath + ".meta");
            Assert.Throws<IOException>(() => Call<Texture2D>("ExtractAndImport", Region(), path));
            Assert.That(File.ReadAllText(path), Is.EqualTo("Keep")); Assert.That(File.ReadAllBytes(sourcePath + ".meta"), Is.EqualTo(before));
        }
        [Test] public void OutOfBoundsFailsWithoutOutputAndRestoresImporter()
        {
            var region = Region(); region.x = 20; byte[] before = File.ReadAllBytes(sourcePath + ".meta");
            Assert.Throws<InvalidOperationException>(() => Call<Texture2D>("ExtractAndImport", region, root + "/bad.png"));
            Assert.That(File.Exists(root + "/bad.png"), Is.False); Assert.That(File.ReadAllBytes(sourcePath + ".meta"), Is.EqualTo(before));
        }
        [TestCase("CON")][TestCase("icons/..")][TestCase("icons/A:B?")][TestCase("icons/日本語")]
        public void SuggestedNamesAreSafeAndExtractable(string region)
        {
            string output = Call<string>("BuildOutputPath", source, region, "ru");
            Assert.That(output, Does.StartWith(root + "/Localized/"));
            Assert.That(Call<Texture2D>("ExtractAndImport", Region(), output), Is.Not.Null);
        }
        [TestCase(typeof(LocalizedSpineAttachment))][TestCase(typeof(LocalizedSpineTexture))]
        public void InspectorBindingUsesItsChannelAndStableIdentityAfterRename(Type componentType)
        {
            var config = LocalizationProjectConfig.Create(new[] { "en", "ru" });
            var mainSet = config.Sets[0]; mainSet.AddEntry("poster", LocalizationEntryKind.Asset);
            var art = config.AddSet("Art"); var entry = art.AddEntry("poster", LocalizationEntryKind.Asset);
            var channel = config.AddChannel("Visuals", new[] { "ru", "en" }); channel.IncludeSet(art.Id); channel.FallbackLanguage = "en";
            Directory.CreateDirectory(root + "/Resources/" + prefix); AssetDatabase.Refresh();
            var table = ScriptableObject.CreateInstance<LocalizationAssetTable>(); table.Set("poster", source);
            AssetDatabase.CreateAsset(table, root + "/Resources/" + prefix + "/Table.asset");
            art.SetSources("en", new LocalizationSourceReference("resources", prefix + "/Table", LocalizationSourceKind.AssetTable));
            AssetDatabase.CreateAsset(config, root + "/Project.asset"); AssetDatabase.SaveAssetIfDirty(config); AssetDatabase.SaveAssetIfDirty(table);
            EditorProjectSelection.Select(config); // Window remains Main/Common, component uses Visuals/Art.
            var go = new GameObject("Spine consumer"); go.SetActive(false);
            UnityEditor.Editor inspector = null;
            try
            {
                var component = (LocalizedComponentBase)go.AddComponent(componentType);
                LocalizationEditorBindings.SetEntry(new SerializedObject(component), config, channel.Id, entry.Id);
                using (var edit = new LocalizationProjectAssetEdit(config)) { edit.RenameAssetEntry(entry.Id, "poster.new"); edit.Commit(); }
                EditorProjectSelection.Invalidate();
                var context = LocalizationEditorBindings.ContextFor(component, out var resolved);
                Assert.That(context.ChannelId, Is.EqualTo(channel.Id)); Assert.That(resolved.Key, Is.EqualTo("poster.new"));
                Assert.That(context.Assets.TryResolve(entry.Id, "ru", out var cell, typeof(Texture2D)), Is.True);
                Assert.That(cell.Asset, Is.SameAs(source));
                inspector = UnityEditor.Editor.CreateEditor(component);
                Assert.That(inspector, Is.InstanceOf(componentType == typeof(LocalizedSpineAttachment) ? typeof(LocalizedSpineAttachmentEditor) : typeof(LocalizedSpineTextureEditor)));
                if (componentType == typeof(LocalizedSpineAttachment))
                {
                    var direct = (IList)inspector.GetType().GetMethod("GetDirectLanguageAssets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inspector, null);
                    Assert.That(direct.Count, Is.EqualTo(1)); Assert.That(direct[0].GetType().GetField("LanguageCode").GetValue(direct[0]), Is.EqualTo("en"));
                    Assert.That(direct[0].GetType().GetField("Asset").GetValue(direct[0]), Is.SameAs(source));
                }
            }
            finally { if (inspector != null) Object.DestroyImmediate(inspector); Object.DestroyImmediate(go); }
        }
    }
}
