using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only checks for the repository's local-package layout; no runtime workarounds.
public static class RepositoryMigrationChecks
{
    public static void Verify()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SimplyLocalize.LocalizationRuntime).Assembly);
        if (package == null || package.source != PackageSource.Local)
            throw new InvalidOperationException("Simply Localize is not a local package.");
        string expected = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../src/SimplyLocalize"));
        if (!string.Equals(Path.GetFullPath(package.resolvedPath), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected package path: " + package.resolvedPath);
        if (Directory.Exists("Assets/SimplyLocalize"))
            throw new InvalidOperationException("Duplicate plugin inside Assets.");

        var failures = new List<string>();
        var unresolvedReferences = new List<string>();
        var guids = new Dictionary<string, string>();
        foreach (string root in new[] { Application.dataPath, package.resolvedPath })
        foreach (string meta in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
        {
            var match = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-f0-9]{32})");
            if (!match.Success) { failures.Add("No GUID: " + meta); continue; }
            if (guids.TryGetValue(match.Groups[1].Value, out var previous))
                failures.Add("Duplicate GUID: " + meta + " / " + previous);
            else guids.Add(match.Groups[1].Value, meta);
        }

        int references = 0;
        foreach (string file in Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                     .Where(p => new[] { ".unity", ".prefab", ".asset", ".mat", ".controller" }.Contains(Path.GetExtension(p))))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"guid: ([a-f0-9]{32})"))
        {
            string guid = match.Groups[1].Value;
            if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
            references++;
            if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
            {
                string message = "Unresolved GUID " + guid + " in " + file;
                unresolvedReferences.Add(message);
                // Report unrelated template data too, without treating it as proof that
                // moving the localization package broke Night Shift.
                string relative = file.Substring(Application.dataPath.Length).Replace('\\', '/');
                if (relative.StartsWith("/HorrorLab/"))
                    failures.Add(message);
            }
        }

        string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        failures.Add("Missing script: " + path + " / " + transform.name);
            }
        }
        finally
        {
            if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // Construct the actual editor window and its UI without showing or focusing it.
        var window = ScriptableObject.CreateInstance<SimplyLocalize.Editor.Windows.LocalizationEditorWindow>();
        try
        {
            var createGui = window.GetType().GetMethod("CreateGUI", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            createGui?.Invoke(window, null);
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }

        const string probe = "Packages/com.renkoff.simply-localize/Editor/LocalPackageMetaProbe.cs";
        var result = new {
            verifiedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            package = package.name, package.version, source = package.source.ToString(), package.resolvedPath,
            metadataGuids = guids.Count, checkedReferences = references, scenes,
            editorWindowConstructed = true, probeGuid = AssetDatabase.AssetPathToGUID(probe),
            allProjectReferencesResolved = unresolvedReferences.Count == 0, unresolvedReferences,
            playgroundChecksPassed = failures.Count == 0, failures
        };
        Directory.CreateDirectory("TestResults/RepositoryMigration");
        File.WriteAllText("TestResults/RepositoryMigration/integration.json", JsonConvert.SerializeObject(result, Formatting.Indented));
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        Debug.Log("Repository migration checks passed.");
    }

    public static void VerifyAndBuild()
    {
        Verify();
        SimplyLocalize.HorrorLab.Editor.HorrorArchitectureSetup.BuildAndVerify();
    }
}
