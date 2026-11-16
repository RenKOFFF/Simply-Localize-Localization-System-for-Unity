using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.HorrorLab.Editor
{
    /// <summary>Internal fixture, never part of the distributed package. No menus or automatic generation.</summary>
    public static class ScopedResourcesFixture
    {
        private const string Root = "Assets/HorrorLab/Resources/ScopedResourcesProbe";
        public static void Create()
        {
            if (Directory.Exists(Root)) throw new InvalidOperationException("Fixture already exists; creation will not overwrite it.");
            Directory.CreateDirectory(Root + "/en"); Directory.CreateDirectory(Root + "/ru");
            File.WriteAllText(Root + "/en/selected.json", "{\"translations\":{\"probe\":\"Ready\"}}");
            File.WriteAllText(Root + "/ru/selected.json", "{\"translations\":{}}");
            WriteTexture("SLScoped_Selected", 2); WriteTexture("SLScoped_LargeNeighbor", 2048);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string name in new[] { "SLScoped_Selected", "SLScoped_LargeNeighbor" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/" + name + ".png");
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false; importer.isReadable = false; importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            foreach (string code in new[] { "en", "ru" })
            {
                var selected = ScriptableObject.CreateInstance<LocalizationAssetTable>();
                if (code == "en") selected.Set("picture", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/SLScoped_Selected.png"));
                AssetDatabase.CreateAsset(selected, Root + "/" + code + "/SLScoped_SelectedTable.asset");
                var neighbor = ScriptableObject.CreateInstance<LocalizationAssetTable>();
                neighbor.Set("neighbor", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/SLScoped_LargeNeighbor.png"));
                AssetDatabase.CreateAsset(neighbor, Root + "/" + code + "/SLScoped_NeighborTable.asset");
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
        private static void WriteTexture(string name, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var colors = new Color32[size * size];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(210, 55, 160, 255);
                texture.SetPixels32(colors); texture.Apply(); File.WriteAllBytes(Root + "/" + name + ".png", texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
