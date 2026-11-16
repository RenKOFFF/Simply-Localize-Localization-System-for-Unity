using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace SimplyLocalize.HorrorLab.Editor
{
    public static class HorrorBuildIdentity
    {
        private static string Git(string directory, string arguments)
        {
            try
            {
                var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NIGHTSHIFT_GIT") ?? "git", arguments) {
                    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    process.StandardError.ReadToEnd(); process.WaitForExit();
                    return process.ExitCode == 0 ? result : "unavailable";
                }
            }
            catch { return "unavailable"; }
        }
        private static string HashFile(string path)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static string TreeHash(string root, Func<string, bool> include)
        {
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(include)
                .Select(p => new { path = p, relative = p.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/') })
                .OrderBy(p => p.relative, StringComparer.Ordinal);
            string manifest = string.Join("\n", files.Select(p => p.relative + " " + HashFile(p.path)));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(manifest))).Replace("-", "").ToLowerInvariant();
        }
        public static void Write()
        {
            string sample = Path.GetFullPath(HorrorLabSetup.Root);
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationManager).Assembly);
            if (packageInfo == null || packageInfo.name != "com.renkoff.simply-localize")
                throw new InvalidOperationException("Simply Localize must be installed as a UPM package.");
            string plugin = packageInfo.resolvedPath;
            var package = JObject.Parse(File.ReadAllText(Path.Combine(plugin, "package.json")));
            var version = JObject.Parse(File.ReadAllText(Path.Combine(sample, "VERSION.json")));
            string commit = Git(plugin, "rev-parse HEAD");
            var result = new JObject {
                ["playgroundVersion"] = version["version"],
                ["measurementProtocol"] = version["measurementProtocol"],
                ["localizerVersion"] = package["version"],
                ["localizerPackageSha256"] = HashFile(Path.Combine(plugin, "package.json")),
                ["localizerCommit"] = commit,
                ["localizerDescribe"] = Git(plugin, "describe --tags --always --dirty"),
                ["localizerWorkingTreeStatus"] = Git(plugin, "status --porcelain -- ."),
                ["localizerRuntimeSha256"] = TreeHash(Path.Combine(plugin, "Runtime"), p => p.EndsWith(".cs") || p.EndsWith(".asmdef")),
                ["playgroundSourceSha256"] = TreeHash(sample, p => p.EndsWith(".cs") || p.EndsWith(".wav") || p.EndsWith(".png") || p.EndsWith("VERSION.json") || p.Contains(Path.DirectorySeparatorChar + "Content" + Path.DirectorySeparatorChar)),
                ["worldSceneSha256"] = HashFile(HorrorLabSetup.ScenePath),
                ["menuSceneSha256"] = HashFile(HorrorArchitectureSetup.MenuPath),
                ["fixtureDataSha256"] = TreeHash(Path.Combine(sample, "Data"), p => p.EndsWith(".asset")),
                ["builtUtc"] = DateTime.UtcNow.ToString("O"),
                ["runId"] = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "_nightshift-" + version["version"] + "_" + commit.Substring(0, Math.Min(7, commit.Length)),
                ["unityVersion"] = UnityEngine.Application.unityVersion,
                ["buildKind"] = "Windows x64 Release",
                ["audioFixture"] = "DecompressOnLoad; PreloadAudioData=false; LoadInBackground=true; no sample LoadAudioData",
                ["cleanupPolicy"] = "Prepared data handles disposed after apply; no ClearCaches, UnloadAsset(s), UnloadAudioData or forced GC in playground"
            };
            string json = result.ToString(Formatting.Indented);
            File.WriteAllText(HorrorLabSetup.Root + "/Resources/NightShift/BuildIdentity.json", json);
            Directory.CreateDirectory("TestResults/NightShiftArchitecture");
            File.WriteAllText("TestResults/NightShiftArchitecture/build-identity.json", json);
            AssetDatabase.Refresh();
        }
    }
}

