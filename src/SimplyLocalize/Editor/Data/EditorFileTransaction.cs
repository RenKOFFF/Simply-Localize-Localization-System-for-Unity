using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimplyLocalize.Editor.Data
{
    // Atomic per-file replacement with rollback of an interrupted multi-file operation.
    // Backups survive if rollback itself fails. This is not a power-loss recovery journal.
    internal sealed class EditorFileTransaction : IDisposable
    {
        private sealed class Entry
        {
            internal string Path, Temporary, Backup;
            internal byte[] Before, After;
            internal bool Applied, External;
        }

        private readonly List<Entry> _entries = new();
        private readonly List<string> _createdDirectories = new();
        private bool _complete;

        internal void Add(string path, byte[] before, byte[] after)
        {
            path = System.IO.Path.GetFullPath(path);
            if (_entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Duplicate write destination: " + path);
            _entries.Add(new Entry { Path = path, Before = before, After = after });
        }

        internal static byte[] Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
        internal static bool Equal(byte[] a, byte[] b) => ReferenceEquals(a, b) || a != null && b != null && a.SequenceEqual(b);

        internal void Execute(Action afterWrite = null)
        {
            try
            {
                Commit();
                try { afterWrite?.Invoke(); }
                finally { foreach (var entry in _entries.Where(e => e.External && e.Applied)) entry.After = Read(entry.Path); }
                Complete();
            }
            catch (Exception failure)
            {
                try { Rollback(); }
                catch (Exception rollback) { throw new AggregateException("Save failed and rollback could not finish.", failure, rollback); }
                throw;
            }
        }

        // Unity serializes profiles/config itself; protect those files alongside JSON writes.
        internal void TrackExternal(string path)
        {
            var before = Read(path);
            Add(path, before, before);
            _entries[_entries.Count - 1].External = true;
        }

        // Claim the file immediately before Unity writes it, not when staging backups.
        internal void TouchExternal(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            var entry = _entries.Single(e => e.External && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            CheckUnchanged(entry);
            entry.Applied = true;
        }

        internal void Commit()
        {
            // Validate every destination before creating directories or staging files.
            foreach (var entry in _entries)
            {
                CheckUnchanged(entry);
                if (Directory.Exists(entry.Path)) throw new IOException("A directory occupies the file path: " + entry.Path);
                if ((!Equal(entry.Before, entry.After) || entry.External) && File.Exists(entry.Path)
                    && (File.GetAttributes(entry.Path) & FileAttributes.ReadOnly) != 0)
                    throw new UnauthorizedAccessException("File is read-only: " + entry.Path);
                // Unity native asset saving can wait indefinitely for a sharing lock. Fail in managed code first.
                if (entry.External && File.Exists(entry.Path))
                {
                    using var probe = new FileStream(entry.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                }
            }
            foreach (var entry in _entries.Where(e => !Equal(e.Before, e.After) || e.External))
            {
                string directory = System.IO.Path.GetDirectoryName(entry.Path);
                CreateDirectory(directory);
                string prefix = System.IO.Path.Combine(directory, ".sl-" + Guid.NewGuid().ToString("N"));
                entry.Backup = prefix + ".bak";
                if (entry.External)
                {
                    if (entry.Before != null) File.WriteAllBytes(entry.Backup, entry.Before);
                    continue;
                }
                if (entry.After == null) continue;
                entry.Temporary = prefix + ".tmp";
                using var stream = new FileStream(entry.Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(entry.After, 0, entry.After.Length);
                stream.Flush(true);
            }
            foreach (var entry in _entries.Where(e => !Equal(e.Before, e.After)))
            {
                CheckUnchanged(entry);
                if (entry.After == null) File.Move(entry.Path, entry.Backup);
                else if (entry.Before == null) File.Move(entry.Temporary, entry.Path);
                else File.Replace(entry.Temporary, entry.Path, entry.Backup);
                entry.Applied = true;
            }
        }

        internal void Complete()
        {
            _complete = true;
            Cleanup(removeBackups: true);
        }

        internal void Rollback()
        {
            var failures = new List<Exception>();
            foreach (var entry in _entries.AsEnumerable().Reverse().Where(e => e.Applied))
            {
                try
                {
                    var current = Read(entry.Path);
                    if (Equal(current, entry.Before)) { entry.Applied = false; continue; }
                    if (!Equal(current, entry.After))
                        throw new IOException("File changed during rollback: " + entry.Path);
                    if (entry.Before == null) File.Delete(entry.Path);
                    else if (File.Exists(entry.Path)) File.Replace(entry.Backup, entry.Path, null);
                    else File.Move(entry.Backup, entry.Path);
                    entry.Applied = false;
                }
                catch (Exception error)
                {
                    failures.Add(new IOException($"Could not restore '{entry.Path}'. Recovery backup: '{entry.Backup}'.", error));
                }
            }
            _complete = true;
            Cleanup(removeBackups: failures.Count == 0);
            foreach (string directory in _createdDirectories.AsEnumerable().Reverse())
            {
                try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch (Exception error) { Debug.LogWarning("[SimplyLocalize Editor] Could not remove empty staging directory: " + error.Message); }
            }
            if (failures.Count > 0) throw new AggregateException("File rollback failed; retain the listed recovery backups.", failures);
        }

        public void Dispose()
        {
            if (!_complete) Rollback();
        }

        private static void CheckUnchanged(Entry entry)
        {
            if (!Equal(Read(entry.Path), entry.Before))
                throw new InvalidOperationException("File changed or was not loaded: " + entry.Path + ". Reload before saving.");
        }

        private void CreateDirectory(string directory)
        {
            if (Directory.Exists(directory)) return;
            CreateDirectory(System.IO.Path.GetDirectoryName(directory));
            Directory.CreateDirectory(directory);
            _createdDirectories.Add(directory);
        }

        private void Cleanup(bool removeBackups)
        {
            foreach (var entry in _entries)
            {
                DeleteStagingFile(entry.Temporary);
                if (removeBackups) DeleteStagingFile(entry.Backup);
            }
        }

        private static void DeleteStagingFile(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception error) { Debug.LogWarning("[SimplyLocalize Editor] Could not remove staging file '" + path + "': " + error.Message); }
        }
    }
}
