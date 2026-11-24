using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SimplyLocalize.Components;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimplyLocalize.Editor.Data
{
    public enum AutoLocalizePrefabMode { Skip, InstanceOverrides, PrefabAssets }
    public enum AutoLocalizeKeyPattern { SceneParentName, ParentName, ObjectName, Custom }
    public sealed class AutoLocalizeOptions
    {
        public AutoLocalizePrefabMode Prefabs { get; set; } = AutoLocalizePrefabMode.Skip;
        public AutoLocalizeKeyPattern KeyPattern { get; set; } = AutoLocalizeKeyPattern.ParentName;
        public string CustomPattern { get; set; } = "{parent}/{name}";
        public string KeyPrefix { get; set; } = "";
        public bool SkipNumericOnly { get; set; } = true;
        public bool SkipAlreadyLocalized { get; set; } = true;
        public int MinimumLength { get; set; } = 2;
    }
    public sealed class AutoLocalizeCandidate
    {
        [Serializable] internal sealed class State
        {
            public GameObject Target;
            public string Text, Path, PrefabPath, Key, Binding, Hierarchy;
            public bool Selected, IsPrefab;
        }
        public GameObject Target { get; }
        public string Text { get; }
        public string Path { get; }
        public string PrefabPath { get; }
        public string Key { get; set; }
        public bool Selected { get; set; } = true;
        internal readonly string Binding, Hierarchy;
        internal State Capture() => new() { Target = Target, Text = Text, Path = Path, PrefabPath = PrefabPath, Key = Key, Selected = Selected, Binding = Binding, Hierarchy = Hierarchy, IsPrefab = PrefabPath != null };
        internal AutoLocalizeCandidate(State state)
        { Target = state.Target; Text = state.Text; Path = state.Path; PrefabPath = state.IsPrefab ? state.PrefabPath : null; Key = state.Key; Selected = state.Selected; Binding = state.Binding; Hierarchy = state.Hierarchy; }
        internal AutoLocalizeCandidate(GameObject target, string key)
        {
            Target = target; Text = ProjectAutoLocalize.Text(target); Key = key;
            Path = ProjectAutoLocalize.Path(target); PrefabPath = EditorUtility.IsPersistent(target) ? AssetDatabase.GetAssetPath(target) : null;
            Binding = ProjectAutoLocalize.Binding(target); Hierarchy = ProjectAutoLocalize.Hierarchy(target);
        }
        internal void Validate()
        {
            if (Target == null || ProjectAutoLocalize.Text(Target) != Text || ProjectAutoLocalize.Binding(Target) != Binding
                || ProjectAutoLocalize.Hierarchy(Target) != Hierarchy || (PrefabPath != null && AssetDatabase.GetAssetPath(Target) != PrefabPath))
                throw new InvalidOperationException("Text, binding or hierarchy changed after scanning: " + Path + ". Scan again.");
        }
    }
    public static class ProjectAutoLocalize
    {
        public static IReadOnlyList<AutoLocalizeCandidate> Scan(LocalizationProjectConfig config, IEnumerable<GameObject> roots,
            AutoLocalizeOptions options = null, string channelId = null, string setId = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow(); EnsureEditMode();
            options ??= new AutoLocalizeOptions();
            if (!Enum.IsDefined(typeof(AutoLocalizePrefabMode), options.Prefabs) || !Enum.IsDefined(typeof(AutoLocalizeKeyPattern), options.KeyPattern)) throw new ArgumentException("Unknown scan option.");
            var channel = config.Channels.Single(c => c.Id == (channelId ?? config.MainChannelId)); setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available in this channel.");
            var keys = new HashSet<string>(config.Sets.Single(s => s.Id == setId).Entries.Select(e => e.Key), StringComparer.Ordinal);
            var scanned = new HashSet<int>(); var targets = new HashSet<int>(); var result = new List<AutoLocalizeCandidate>();
            void Visit(GameObject go)
            {
                if (go == null || !scanned.Add(go.GetInstanceID())) return;
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && go.scene == stage.scene)
                    throw new InvalidOperationException("Scan a prefab asset from the Project window, or a scene instance outside Prefab Mode.");
                bool persistent = EditorUtility.IsPersistent(go);
                // Added instance children are not themselves prefab objects, but still belong to
                // that instance. They must follow the selected prefab policy as well.
                GameObject instanceObject = null;
                for (var t = go.transform; t != null; t = t.parent)
                    if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) { instanceObject = t.gameObject; break; }
                bool instance = instanceObject != null;
                if (options.Prefabs == AutoLocalizePrefabMode.Skip && (persistent || instance)) return;
                if (persistent && options.Prefabs != AutoLocalizePrefabMode.PrefabAssets) return;
                var target = go;
                if (instance && !persistent && options.Prefabs == AutoLocalizePrefabMode.PrefabAssets)
                {
                    string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceObject);
                    target = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(go, prefab);
                    if (target == null) throw new InvalidOperationException("Added instance objects have no prefab source: " + Path(go) + ". Use instance overrides.");
                }
                if (targets.Add(target.GetInstanceID()))
                {
                    string text = Text(target);
                    if (text != null && text.Length >= Math.Max(1, options.MinimumLength)
                        && (!options.SkipAlreadyLocalized || target.GetComponent<LocalizedText>() == null)
                        && (!options.SkipNumericOnly || text.Any(c => !char.IsDigit(c) && !char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))))
                    {
                        string scene = target.scene.IsValid() ? target.scene.name : "Prefab";
                        string parent = target.transform.parent != null ? target.transform.parent.name : "Root";
                        string raw = options.KeyPattern switch
                        {
                            AutoLocalizeKeyPattern.SceneParentName => scene + "/" + parent + "/" + target.name,
                            AutoLocalizeKeyPattern.ObjectName => target.name,
                            AutoLocalizeKeyPattern.Custom => (options.CustomPattern ?? "").Replace("{scene}", scene).Replace("{parent}", parent).Replace("{name}", target.name)
                                .Replace("{text}", Regex.Replace(text.Substring(0, Math.Min(20, text.Length)), @"\s+", "_")),
                            _ => parent + "/" + target.name
                        };
                        raw = Regex.Replace(Regex.Replace((options.KeyPrefix ?? "").TrimEnd('/') + "/" + raw, @"[^\w/\-]", "_"), @"/+", "/").Trim('/');
                        if (raw.Length == 0) raw = "Text";
                        string key = raw; int suffix = 1; while (!keys.Add(key)) key = raw + "_" + suffix++;
                        result.Add(new AutoLocalizeCandidate(target, key));
                    }
                }
                for (int i = 0; i < go.transform.childCount; i++) Visit(go.transform.GetChild(i).gameObject);
            }
            foreach (var root in roots ?? throw new ArgumentNullException(nameof(roots))) Visit(root);
            return result.AsReadOnly();
        }
        public static AutoLocalizePlan Preview(LocalizationProjectConfig config, IEnumerable<AutoLocalizeCandidate> candidates,
            string channelId = null, string setId = null, string destinationAddress = null, string newSourcePath = null)
            => AutoLocalizePlan.Create(config, candidates, channelId, setId, destinationAddress, newSourcePath);
        internal static void EnsureEditMode()
        { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Auto-Localize edits scenes and assets outside Play mode."); }
        internal static string Text(GameObject go) => go.GetComponent<TMP_Text>() is TMP_Text tmp ? tmp.text : go.GetComponent<Text>() is Text text ? text.text : null;
        internal static string Binding(GameObject go)
        {
            var component = go.GetComponent<LocalizedText>(); if (component == null) return "(none)";
            return string.Join("\n", component.GetInstanceID(), component.Key, component.ChannelId, component.SetId, component.Entry.ChannelId, component.Entry.EntryId);
        }
        internal static string Path(GameObject go)
        { var parts = new List<string>(); for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }
        internal static string Hierarchy(GameObject go)
        { var parts = new List<string>(); for (var t = go.transform; t != null; t = t.parent) parts.Add(t.GetInstanceID() + ":" + t.GetSiblingIndex() + ":" + t.name); return go.scene.handle + ":" + string.Join("/", parts); }
        internal static void Bind(LocalizedText component, string channel, string set, string entry, string key)
        {
            using var serialized = new SerializedObject(component);
            serialized.FindProperty("_key").stringValue = key; serialized.FindProperty("_channelId").stringValue = channel; serialized.FindProperty("_setId").stringValue = set;
            var reference = serialized.FindProperty("_entry"); reference.FindPropertyRelative("_channelId").stringValue = channel; reference.FindPropertyRelative("_entryId").stringValue = entry;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    public sealed class AutoLocalizeBinding
    {
        public GameObject Target { get; }
        public string Path { get; }
        public string PrefabPath { get; }
        public string Key { get; }
        public string EntryId { get; }
        public bool CreatesEntry { get; }
        internal AutoLocalizeBinding(AutoLocalizeCandidate candidate, string id, bool creates)
        { Target = candidate.Target; Path = candidate.Path; PrefabPath = candidate.PrefabPath; Key = candidate.Key; EntryId = id; CreatesEntry = creates; }
    }
    public sealed class AutoLocalizePlan : IDisposable
    {
        private LocalizationProjectEdit edit;
        private AutoLocalizeCandidate[] candidates;
        private string channel, set;
        private readonly Dictionary<string, byte[]> prefabs = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<AutoLocalizeBinding> Bindings { get; private set; }
        public IReadOnlyList<string> Warnings { get; private set; }
        public string CreatedSourcePath { get; private set; }
        public bool IsApplied { get; private set; }
        internal static AutoLocalizePlan Create(LocalizationProjectConfig config, IEnumerable<AutoLocalizeCandidate> source, string channelId, string setId, string address, string newPath)
        {
            ProjectAutoLocalize.EnsureEditMode(); if (config == null) throw new ArgumentNullException(nameof(config)); config.ValidateOrThrow();
            var selected = (source ?? throw new ArgumentNullException(nameof(source))).Where(c => c.Selected).ToArray();
            if (selected.Length == 0) throw new ArgumentException("Select at least one text component.");
            if (selected.Select(c => c.Target).Distinct().Count() != selected.Length) throw new ArgumentException("A target appears more than once.");
            var channel = config.Channels.Single(c => c.Id == (channelId ?? config.MainChannelId)); setId ??= channel.DefaultSetId;
            if (!channel.SetIds.Contains(setId)) throw new ArgumentException("Set is not available in this channel.");
            var set = config.Sets.Single(s => s.Id == setId);
            foreach (var candidate in selected) candidate.Validate();
            foreach (var group in selected.GroupBy(c => c.Key))
                if (!set.Entries.Any(e => e.Key == group.Key) && group.Select(c => c.Text).Distinct().Count() > 1)
                    throw new ArgumentException("Different texts target the same new key: " + group.Key);
            var plan = new AutoLocalizePlan { candidates = selected, channel = channel.Id, set = setId };
            try
            {
                bool writesText = selected.Any(c => !set.Entries.Any(e => e.Key == c.Key));
                plan.edit = new LocalizationProjectEdit(config, writesText ? new[] { setId } : Array.Empty<string>());
                var draftSet = plan.edit.Draft.Sets.Single(s => s.Id == setId);
                if (writesText && !draftSet.Sources.Any(s => s.Language == channel.DefaultLanguage && s.Sources.Any(r => r.Kind == LocalizationSourceKind.TextTable)))
                {
                    if (address != null) throw new ArgumentException("Connect the chosen destination before preparing Auto-Localize.");
                    plan.CreatedSourcePath = newPath ?? LocalizationProjectAuthoring.SuggestTextSourcePath(config, set, channel.DefaultLanguage);
                    plan.edit.CreateTextSource(setId, channel.DefaultLanguage, plan.CreatedSourcePath);
                }
                var bindings = new List<AutoLocalizeBinding>(); var warnings = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in selected)
                {
                    var entry = draftSet.Entries.SingleOrDefault(e => e.Key == candidate.Key); bool creates = entry == null;
                    if (creates)
                    {
                        entry = draftSet.AddEntry(candidate.Key); plan.edit.ValidateTextEntrySources(entry.Id);
                        if (plan.edit.HasStoredTextEntry(entry.Id)) throw new InvalidOperationException("Key already exists in a source: " + candidate.Key + ". Register it first or choose another key.");
                        string resource = ResourcesEditorTextReader.ResourceAddress(plan.edit.ResolveTextWritePath(entry.Id, channel.DefaultLanguage, address));
                        var connections = plan.edit.Draft.Sets.SelectMany(s => s.Sources.Where(l => l.Sources.Any(r => r.ProviderId == "resources" && r.Kind == LocalizationSourceKind.TextTable && string.Equals(r.Address, resource, StringComparison.OrdinalIgnoreCase)))
                            .Select(l => s.Name + " / " + l.Language)).ToArray();
                        if (connections.Length > 1) warnings.Add("Shared source " + resource + ": changes also affect " + string.Join(", ", connections) + ".");
                        plan.edit.SetTranslation(entry.Id, channel.DefaultLanguage, candidate.Text, address);
                    }
                    else if (entry.Kind != LocalizationEntryKind.Text) throw new ArgumentException("Text key conflicts with an asset entry: " + candidate.Key);
                    bindings.Add(new AutoLocalizeBinding(candidate, entry.Id, creates));
                    if (candidate.PrefabPath != null && !plan.prefabs.ContainsKey(candidate.PrefabPath))
                    {
                        ValidatePrefab(candidate.PrefabPath); plan.prefabs.Add(candidate.PrefabPath, File.ReadAllBytes(candidate.PrefabPath));
                    }
                }
                plan.Bindings = bindings.AsReadOnly(); plan.Warnings = warnings.OrderBy(w => w, StringComparer.Ordinal).ToList().AsReadOnly(); return plan;
            }
            catch { plan.Dispose(); throw; }
        }
        public LocalizationProjectUndo Apply()
        {
            ProjectAutoLocalize.EnsureEditMode(); if (edit == null || IsApplied) throw new InvalidOperationException("This preview is closed. Scan and preview again.");
            foreach (var candidate in candidates) candidate.Validate();
            foreach (var prefab in prefabs) { ValidatePrefab(prefab.Key); if (!EditorFileTransaction.Equal(File.ReadAllBytes(prefab.Key), prefab.Value)) throw new InvalidOperationException("Prefab changed after preview: " + prefab.Key); }
            var contents = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            LocalizationProjectChange change = null; LocalizationProjectUndo record = null;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Auto-Localize");
            try
            {
                foreach (string path in prefabs.Keys) contents.Add(path, PrefabUtility.LoadPrefabContents(path));
                change = edit.CommitWithNativeFiles(prefabs, transaction =>
                {
                    foreach (var binding in Bindings)
                    {
                        var target = binding.Target;
                        if (binding.PrefabPath != null)
                        {
                            var indices = new List<int>(); for (var t = target.transform; t.parent != null; t = t.parent) indices.Add(t.GetSiblingIndex());
                            var transformed = contents[binding.PrefabPath].transform;
                            foreach (int index in indices.AsEnumerable().Reverse()) transformed = transformed.GetChild(index);
                            target = transformed.gameObject;
                            if (ProjectAutoLocalize.Text(target) != candidates.Single(c => c.Target == binding.Target).Text) throw new InvalidOperationException("Prefab text differs from the preview: " + binding.Path);
                        }
                        var component = target.GetComponent<LocalizedText>();
                        if (binding.PrefabPath == null)
                        {
                            if (component == null) component = Undo.AddComponent<LocalizedText>(target); else Undo.RegisterCompleteObjectUndo(component, "Auto-Localize");
                            ProjectAutoLocalize.Bind(component, channel, set, binding.EntryId, binding.Key);
                            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                            EditorSceneManager.MarkSceneDirty(target.scene);
                        }
                        else { if (component == null) component = target.AddComponent<LocalizedText>(); ProjectAutoLocalize.Bind(component, channel, set, binding.EntryId, binding.Key); }
                    }
                    foreach (var prefab in contents)
                    {
                        transaction.TouchExternal(prefab.Key); PrefabUtility.SaveAsPrefabAsset(prefab.Value, prefab.Key, out bool success);
                        if (!success) throw new IOException("Could not save prefab: " + prefab.Key);
                    }
                });
                record = LocalizationProjectUndo.Record(change, "Auto-Localize", Bindings.Any(b => b.PrefabPath == null)); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                IsApplied = true; Dispose(); return record;
            }
            catch (Exception failure)
            {
                record?.Dispose();
                try { if (change?.IsApplied == true) change.Undo(); }
                catch (Exception rollback) { throw new AggregateException(failure, rollback); }
                finally { Undo.RevertAllDownToGroup(group); }
                throw;
            }
            finally { foreach (var root in contents.Values) if (root != null) PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void ValidatePrefab(string path)
        {
            LocalizationProjectChange.ValidateWritable(path);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Model assets cannot be edited as prefabs. Use instance overrides: " + path);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path && stage.scene.isDirty) throw new InvalidOperationException("Save or discard changes in Prefab Mode first: " + path);
        }
        public void Dispose() { edit?.Dispose(); edit = null; }
    }
}
