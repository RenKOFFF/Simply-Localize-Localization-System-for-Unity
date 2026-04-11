using System;
using System.Collections.Generic;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    /// <summary>
    /// Builds a hierarchical tree from slash-separated keys (e.g. "UI/Popup/Title").
    /// Used by tabs that show keys in a collapsible group view.
    ///
    /// Input keys SHOULD be pre-sorted alphabetically — the builder does not re-sort
    /// leaves at each level for performance. It does sort child groups when flattening
    /// because Dictionary iteration order is not strictly guaranteed.
    ///
    /// TODO: TranslationsTab still has its own private copy of TreeNode and flattening
    /// logic. Migrating it to use this builder is safe but non-trivial because of the
    /// tight coupling with ListView/RowItem bindings. Do it in a later refactor.
    /// </summary>
    public static class KeyTreeBuilder
    {
        public class TreeNode
        {
            public string Name;
            public string FullPath;
            public Dictionary<string, TreeNode> Children = new();
            public List<string> Keys = new();
            public int TotalKeyCount;

            /// <summary>Optional tag for two-level groupings (e.g. asset type name).</summary>
            public string Tag;

            public TreeNode(string name) { Name = name; FullPath = name; }
        }

        /// <summary>
        /// Builds a tree from a flat list of slash-separated keys.
        /// </summary>
        public static TreeNode Build(IEnumerable<string> keys)
        {
            var root = new TreeNode("(root)");
            if (keys == null) return root;

            foreach (var key in keys)
                InsertKey(root, key);

            return root;
        }

        /// <summary>
        /// Builds a two-level tree: first grouped by <paramref name="groupKeyOf"/>,
        /// then by slash-separated path within each group.
        /// Used by the Assets tab in "All" mode to group keys by asset type.
        /// </summary>
        public static TreeNode BuildGrouped(
            IEnumerable<string> keys,
            Func<string, string> groupKeyOf)
        {
            var root = new TreeNode("(root)");
            if (keys == null) return root;

            foreach (var key in keys)
            {
                string groupName = groupKeyOf(key) ?? "Other";

                if (!root.Children.TryGetValue(groupName, out var groupNode))
                {
                    groupNode = new TreeNode(groupName) { Tag = groupName };
                    root.Children[groupName] = groupNode;
                }

                groupNode.TotalKeyCount++;
                InsertKey(groupNode, key);
            }

            return root;
        }

        private static void InsertKey(TreeNode parent, string key)
        {
            int slashIdx = key.IndexOf('/');
            if (slashIdx < 0)
            {
                parent.Keys.Add(key);
                return;
            }

            var current = parent;
            int start = 0;

            while (slashIdx >= 0)
            {
                string segment = key.Substring(start, slashIdx - start);

                if (!current.Children.TryGetValue(segment, out var child))
                {
                    child = new TreeNode(segment);
                    current.Children[segment] = child;
                }

                current = child;
                current.TotalKeyCount++;
                start = slashIdx + 1;
                slashIdx = key.IndexOf('/', start);
            }

            current.Keys.Add(key);
        }

        // ──────────────────────────────────────────────
        //  Flattening for rendering
        // ──────────────────────────────────────────────

        public enum FlatRowType { GroupHeader, KeyRow }

        public class FlatRow
        {
            public FlatRowType Type;
            public int Depth;

            // GroupHeader
            public string GroupPath;
            public string GroupDisplayName;
            public int GroupKeyCount;
            public bool IsCollapsed;

            // KeyRow
            public string Key;
        }

        /// <summary>
        /// Flattens a tree into a list of rows suitable for direct rendering.
        /// Group paths that exist in <paramref name="collapsedGroups"/> are rendered
        /// as headers but their contents are skipped.
        /// </summary>
        public static List<FlatRow> Flatten(TreeNode root, HashSet<string> collapsedGroups)
        {
            var result = new List<FlatRow>();
            FlattenNode(root, 0, true, collapsedGroups, result);
            return result;
        }

        private static void FlattenNode(
            TreeNode node,
            int depth,
            bool isRoot,
            HashSet<string> collapsedGroups,
            List<FlatRow> output)
        {
            if (node.Children.Count > 0)
            {
                // Sort children by name (Dictionary iteration order is not guaranteed)
                var sortedChildren = new KeyValuePair<string, TreeNode>[node.Children.Count];
                int idx = 0;
                foreach (var kvp in node.Children) sortedChildren[idx++] = kvp;
                Array.Sort(sortedChildren, (a, b) => string.CompareOrdinal(a.Key, b.Key));

                foreach (var childKvp in sortedChildren)
                {
                    string childName = childKvp.Key;
                    var childNode = childKvp.Value;

                    string fullPath = isRoot ? childName : $"{node.FullPath}/{childName}";
                    childNode.FullPath = fullPath;

                    bool collapsed = collapsedGroups != null && collapsedGroups.Contains(fullPath);

                    output.Add(new FlatRow
                    {
                        Type = FlatRowType.GroupHeader,
                        Depth = depth,
                        GroupPath = fullPath,
                        GroupDisplayName = childName,
                        GroupKeyCount = childNode.TotalKeyCount,
                        IsCollapsed = collapsed
                    });

                    if (!collapsed)
                        FlattenNode(childNode, depth + 1, false, collapsedGroups, output);
                }
            }

            // Leaf keys (already sorted because input is sorted)
            foreach (var key in node.Keys)
            {
                output.Add(new FlatRow
                {
                    Type = FlatRowType.KeyRow,
                    Depth = depth,
                    Key = key
                });
            }
        }
    }
}
