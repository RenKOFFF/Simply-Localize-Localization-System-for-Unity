using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;

namespace SimplyLocalize.Editor.Utilities
{
    /// <summary>Editor key renaming with preflight checks and rollback across data and serialized references.</summary>
    public static class LocalizationKeyRenamer
    {
        /// <summary>Saves the affected translations, metadata and optional text references as one operation.</summary>
        public static int RenameText(EditorLocalizationData data, string oldKey, string newKey,
            bool updateReferences = true, string[] searchRoots = null) =>
            RenameTextCore(data, oldKey, newKey, updateReferences, searchRoots, null);

        internal static int RenameTextCore(EditorLocalizationData data, string oldKey, string newKey,
            bool updateReferences, string[] searchRoots, Action afterReferences)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var mappings = ReferenceRenamePlan.Validate(new Dictionary<string, string> { [oldKey] = newKey });
            if (mappings.Count == 0) return 0;
            string file = data.GetFileForKey(oldKey);
            if (file == null) throw new InvalidOperationException("Key no longer exists: " + oldKey);
            if (data.GetFileForKey(newKey) != null) throw new InvalidOperationException("Key already exists: " + newKey);
            if (data.GetMeta(newKey) != null) throw new InvalidOperationException("Metadata already exists for key: " + newKey + ". Resolve it before renaming.");
            var plan = ReferenceRenamePlan.Prepare(updateReferences ? mappings : new(), ReferenceKind.Text, searchRoots);
            var copy = data.Clone();
            copy.RenameKey(oldKey, newKey);
            EditorFileTransaction transaction = null;
            try
            {
                copy.SaveRenamedFile(file, () => { plan.Apply(transaction); afterReferences?.Invoke(); }, t =>
                {
                    transaction = t;
                    plan.Enlist(t);
                });
            }
            catch { plan.Restore(); throw; }
            data.CopyFrom(copy);
            plan.Refresh();
            return plan.Count;
        }

        /// <summary>Renames asset-table entries and asset-component references; text keys remain independent.</summary>
        public static int RenameAssets(IEnumerable<LocalizationAssetTable> tables, string oldKey, string newKey,
            bool updateReferences = true, string[] searchRoots = null) =>
            RenameAssetsCore(tables, oldKey, newKey, updateReferences, searchRoots, null);

        internal static int RenameAssetsCore(IEnumerable<LocalizationAssetTable> tables, string oldKey, string newKey,
            bool updateReferences, string[] searchRoots, Action afterReferences)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var mappings = ReferenceRenamePlan.Validate(new Dictionary<string, string> { [oldKey] = newKey });
            if (mappings.Count == 0) return 0;
            var all = tables.Where(t => t != null).Distinct().ToArray();
            if (all.Any(t => t.HasKey(newKey))) throw new InvalidOperationException("Asset key already exists: " + newKey);
            var affected = all.Where(t => t.HasKey(oldKey)).ToArray();
            if (affected.Length == 0) throw new InvalidOperationException("Asset key no longer exists: " + oldKey);
            var snapshots = affected.ToDictionary(t => t, EditorJsonUtility.ToJson);
            var files = affected.Select(AssetDatabase.GetAssetPath).Distinct().ToDictionary(p => p, ReferenceRenamePlan.AssetDiskPath);
            var beforeFiles = files.Values.ToDictionary(p => p, EditorFileTransaction.Read);
            foreach (var table in affected)
            {
                if (table.Entries.Count(e => e.key == oldKey) != 1)
                    throw new InvalidOperationException("Duplicate key in asset table: " + table.name);
            }
            foreach (string path in files.Keys)
            {
                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(EditorUtility.IsDirty))
                    throw new InvalidOperationException("Save pending table changes before renaming: " + path);
                if (!AssetDatabase.IsOpenForEdit(path)) throw new IOException("Asset is not open for editing: " + path);
            }
            var plan = ReferenceRenamePlan.Prepare(updateReferences ? mappings : new(), ReferenceKind.Asset, searchRoots);
            using var transaction = new EditorFileTransaction();
            bool applied = false;
            try
            {
                foreach (var pair in beforeFiles)
                {
                    if (pair.Value == null || !EditorFileTransaction.Equal(pair.Value, EditorFileTransaction.Read(pair.Key)))
                        throw new InvalidOperationException("Table changed during rename. Try again: " + pair.Key);
                    transaction.TrackExternal(pair.Key);
                }
                foreach (var pair in snapshots)
                    if (!pair.Key || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                        throw new InvalidOperationException("Table changed during rename. Try again.");
                plan.Enlist(transaction);
                transaction.Execute(() =>
                {
                    applied = true;
                    foreach (string disk in files.Values) transaction.TouchExternal(disk);
                    foreach (var table in affected)
                    {
                        table.RenameKey(oldKey, newKey);
                        EditorUtility.SetDirty(table);
                    }
                    foreach (var table in affected)
                    {
                        AssetDatabase.SaveAssetIfDirty(table);
                        if (EditorUtility.IsDirty(table)) throw new IOException("Could not save table: " + AssetDatabase.GetAssetPath(table));
                    }
                    plan.Apply(transaction);
                    afterReferences?.Invoke();
                });
            }
            catch
            {
                if (applied)
                {
                    foreach (string path in files.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    foreach (var pair in snapshots)
                        if (pair.Key) { EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key); EditorUtility.ClearDirty(pair.Key); }
                }
                plan.Restore();
                throw;
            }
            plan.Refresh();
            return plan.Count;
        }
    }
}
