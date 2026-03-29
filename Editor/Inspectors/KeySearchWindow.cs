using System;
using System.Collections.Generic;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor.Inspectors
{
    /// <summary>
    /// Native Unity search window for localization keys.
    /// Groups keys hierarchically by '/' separator.
    /// Supports file filtering and "Add new key" option.
    /// </summary>
    public class KeySearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private const string NoneKey = "<None>";
        private const string AddNewPrefix = "+ Add: ";

        private List<string> _keys;
        private Action<string> _onSelectEntry;
        private string _pendingNewKey;

        /// <summary>
        /// Initializes the search window with available keys.
        /// </summary>
        /// <param name="keys">All available keys.</param>
        /// <param name="onSelectEntry">Callback when a key is selected. 
        /// Returns empty string for None, or the selected/new key.</param>
        /// <param name="pendingNewKey">If non-empty, shows an "Add: {key}" option at the top.</param>
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

            // "Add new key" option if there's a pending key
            if (!string.IsNullOrEmpty(_pendingNewKey) && !_keys.Contains(_pendingNewKey))
            {
                tree.Add(new SearchTreeEntry(new GUIContent(AddNewPrefix + _pendingNewKey))
                {
                    level = 1,
                    userData = AddNewPrefix + _pendingNewKey
                });
            }

            // Separate flat keys and categorized keys
            var flatKeys = _keys.Where(k => !k.Contains('/')).OrderBy(k => k).ToList();
            var categorizedKeys = _keys.Where(k => k.Contains('/')).OrderBy(k => k).ToList();

            // Flat keys
            if (flatKeys.Count > 0 && categorizedKeys.Count > 0)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("Flat keys"), 1));

                foreach (var key in flatKeys)
                {
                    tree.Add(new SearchTreeEntry(new GUIContent(key))
                    {
                        level = 2,
                        userData = key
                    });
                }
            }
            else if (flatKeys.Count > 0)
            {
                foreach (var key in flatKeys)
                {
                    tree.Add(new SearchTreeEntry(new GUIContent(key))
                    {
                        level = 1,
                        userData = key
                    });
                }
            }

            // Categorized keys — build hierarchy from '/'
            if (categorizedKeys.Count > 0)
            {
                var addedGroups = new HashSet<string>();

                foreach (var key in categorizedKeys)
                {
                    var parts = key.Split('/');
                    string currentPath = "";

                    // Add group entries for each path segment
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        currentPath += (i > 0 ? "/" : "") + parts[i];

                        if (!addedGroups.Contains(currentPath))
                        {
                            int level = i + 1;
                            if (flatKeys.Count > 0) level++;

                            tree.Add(new SearchTreeGroupEntry(new GUIContent(parts[i]), level));
                            addedGroups.Add(currentPath);
                        }
                    }

                    // Add the key entry
                    int keyLevel = parts.Length;
                    if (flatKeys.Count > 0) keyLevel++;

                    string shortName = parts[^1];
                    tree.Add(new SearchTreeEntry(new GUIContent($"{shortName}    ({key})"))
                    {
                        level = keyLevel,
                        userData = key
                    });
                }
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            string selected = entry.userData as string;

            if (selected != null && selected.StartsWith(AddNewPrefix))
            {
                // This is an "Add new" request
                string newKey = selected.Substring(AddNewPrefix.Length);
                _onSelectEntry?.Invoke(newKey);
                return true;
            }

            _onSelectEntry?.Invoke(selected);
            return true;
        }
    }
}
