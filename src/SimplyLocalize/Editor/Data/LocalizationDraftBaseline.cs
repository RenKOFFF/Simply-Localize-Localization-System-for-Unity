using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Original read baselines retained with an Editor draft across assembly reload.
    /// Restoring a view may read newer data; it must not silently advance permission to overwrite it.</summary>
    [Serializable]
    public sealed class LocalizationDraftBaseline
    {
        [Serializable] private struct FileState
        {
            [SerializeField] internal string Path, Hash;
            [SerializeField] internal bool Exists;
        }
        [Serializable] private struct NativeState
        {
            [SerializeField] internal ScriptableObject Asset;
            [SerializeField] internal string Path, State;
        }
        [SerializeField] private LocalizationProjectConfig config;
        [SerializeField] private string configPath, configJson;
        [SerializeField] private List<FileState> files = new();
        [SerializeField] private List<NativeState> native = new();
        internal LocalizationDraftBaseline(LocalizationProjectConfig config, string path, string json, byte[] bytes)
        { this.config = config; configPath = path; configJson = json; AddFile(path, bytes); }
        internal void AddFile(string path, byte[] bytes) => files.Add(new FileState { Path = path, Exists = bytes != null, Hash = Hash(bytes) });
        internal void AddNative(ScriptableObject asset, string path, string state) => native.Add(new NativeState { Asset = asset, Path = path, State = state });
        public void Validate()
        {
            if (config == null || AssetDatabase.GetAssetPath(config) != configPath || LocalizationProjectEdit.ConfigJson(config) != configJson)
                throw new InvalidOperationException("The project config changed since this draft was opened. Keep the draft for reference, or discard it and reload.");
            foreach (var file in files)
            {
                bool exists = File.Exists(file.Path);
                if (exists != file.Exists || exists && Hash(File.ReadAllBytes(file.Path)) != file.Hash)
                    throw new InvalidOperationException("Source changed since this draft was opened: " + file.Path + ". Keep the draft for reference, or discard it and reload.");
            }
            foreach (var item in native)
                if (item.Asset == null || AssetDatabase.GetAssetPath(item.Asset) != item.Path || LocalizationProjectAssetEdit.NativeState(item.Asset) != item.State)
                    throw new InvalidOperationException("Asset changed since this draft was opened: " + item.Path + ". Keep the draft for reference, or discard it and reload.");
        }
        private static string Hash(byte[] bytes)
        { if (bytes == null) return ""; using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(bytes)); }
    }
}
