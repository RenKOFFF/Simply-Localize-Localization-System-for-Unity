using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    /// <summary>
    /// Native Unity search window for localization keys.
    /// Groups keys hierarchically by '/' separator.
    /// Sorting: group entries always above leaf entries at each level, alphabetical within.
    /// </summary>
    public class KeySearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private const string NoneKey = "<None>";
        private const string AddNewPrefix = "+ Add: ";

        private List<string> _keys;
        private Action<string> _onSelectEntry;
        private string _pendingNewKey;

        public void Init(List<string> keys, Action<string> onSelectEntry, string pendingNewKey = null)
        {
            _keys = keys;
            _onSelectEntry = onSelectEntry;
            _pendingNewKey = pendingNewKey;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Localization keys"), 0)
            };

            // None option
            tree.Add(new SearchTreeEntry(new GUIContent(NoneKey))
            {
                level = 1,
                userData = ""
            });

            // "Add new key" option
            if (!string.IsNullOrEmpty(_pendingNewKey) && !_keys.Contains(_pendingNewKey))
            {
                tree.Add(new SearchTreeEntry(new GUIContent(AddNewPrefix + _pendingNewKey))
                {
                    level = 1,
                    userData = AddNewPrefix + _pendingNewKey
                });
            }

            if (_keys == null || _keys.Count == 0)
                return tree;

            // Build a tree structure, then flatten with correct ordering
            var root = new TreeNode("", false);

            foreach (var key in _keys)
            {
                var parts = key.Split('/');
                var current = root;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!current.Children.TryGetValue(parts[i], out var child))
                    {
                        child = new TreeNode(parts[i], false);
                        current.Children[parts[i]] = child;
                    }

                    current = child;
                }

                // Leaf node — the actual key
                string leafName = parts[^1];
                var leaf = new TreeNode(leafName, true) { FullKey = key };
                current.Children[key] = leaf; // use full key to avoid name collisions
            }

            // Flatten tree into search entries
            FlattenNode(root, 1, tree);

            return tree;
        }

        private void FlattenNode(TreeNode node, int level, List<SearchTreeEntry> tree)
        {
            // Sort: groups first (alphabetically), then leaves (alphabetically)
            var groups = node.Children.Values
                .Where(c => !c.IsLeaf)
                .OrderBy(c => c.Name)
                .ToList();

            var leaves = node.Children.Values
                .Where(c => c.IsLeaf)
                .OrderBy(c => c.Name)
                .ToList();

            // Groups first
            foreach (var group in groups)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent(group.Name), level));
                FlattenNode(group, level + 1, tree);
            }

            // Then leaves
            foreach (var leaf in leaves)
            {
                string display = leaf.FullKey.Contains('/')
                    ? $"{leaf.Name}    ({leaf.FullKey})"
                    : leaf.Name;

                tree.Add(new SearchTreeEntry(new GUIContent(display))
                {
                    level = level,
                    userData = leaf.FullKey
                });
            }
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            string selected = entry.userData as string;

            if (selected != null && selected.StartsWith(AddNewPrefix))
            {
                string newKey = selected.Substring(AddNewPrefix.Length);
                _onSelectEntry?.Invoke(newKey);
                return true;
            }

            _onSelectEntry?.Invoke(selected);
            return true;
        }

        private class TreeNode
        {
            public string Name;
            public bool IsLeaf;
            public string FullKey;
            public Dictionary<string, TreeNode> Children = new();

            public TreeNode(string name, bool isLeaf)
            {
                Name = name;
                IsLeaf = isLeaf;
            }
        }
    }
}
