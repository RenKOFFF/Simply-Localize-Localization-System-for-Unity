using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public class StringKeysSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private List<string> _keys;
        private Action<string> _onSelectEntry;
        public void SetKeys(List<string> keys, Action<string> onSelectEntry)
        {
            _keys = keys;
            _onSelectEntry = onSelectEntry;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var sortedKeys = GetSortedKeys();
            
            var searchTree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Keys"))
            };
            
            var groups = new List<string>();
            foreach (var item in sortedKeys)
            {
                var entryTitle = item.Split('/');
                var groupName = string.Empty;

                for (var i = 0; i < entryTitle.Length - 1; i++)
                {
                    groupName += entryTitle[i];

                    if (!groups.Contains(groupName))
                    {
                        var group = new SearchTreeGroupEntry(new GUIContent(entryTitle[i]), i + 1);
                        searchTree.Add(group);
                        
                        groups.Add(groupName);    
                    }
                
                    groupName += "/";
                }
                
                var entry = new SearchTreeEntry(new GUIContent(entryTitle.Last()))
                {
                    level = entryTitle.Length,
                    userData = item
                };
                
                searchTree.Add(entry);
            }

            return searchTree;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            _onSelectEntry?.Invoke(searchTreeEntry.userData as string);
            return true;
        }

        private List<string> GetSortedKeys()
        {
            var groupedKeys = _keys
                .GroupBy(key => key.Split('/')[0])
                .OrderBy(group => group.Key)
                .SelectMany(group =>
                {
                    return group.OrderByDescending(key => key.Count(c => c == '/'))
                        .ThenBy(key => key);
                });

            return groupedKeys.ToList();
        }
    }
}