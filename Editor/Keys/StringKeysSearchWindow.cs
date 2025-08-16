using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    public class StringKeysSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private string _noneKey;
        private List<string> _keys;
        private Action<string> _onSelectEntry;

        public void SetKeys(string noneKey, List<string> keys, Action<string> onSelectEntry)
        {
            _noneKey = noneKey;
            _keys = keys;
            _onSelectEntry = onSelectEntry;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var searchTree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Localization Keys"), 0)
            };

            var noneKey = _keys.FirstOrDefault(k => k == _noneKey);
            var otherKeys = _keys.Where(k => k != _noneKey).ToList();
            
            var flatKeys = otherKeys.Where(k => !k.Contains('/')).OrderBy(k => k).ToList();
            var categories = otherKeys.Where(k => k.Contains('/')).OrderBy(k => k).ToList();

            if (noneKey != null)
            {
                searchTree.Add(new SearchTreeEntry(new GUIContent(noneKey)) 
                { 
                    level = 1,
                    userData = noneKey 
                });
                
                if (otherKeys.Count > 0)
                {
                    searchTree.Add(new SearchTreeGroupEntry(new GUIContent("Keys"), 1));
                }
            }

            if (flatKeys.Count > 0)
            {
                if (categories.Count == 0)
                {
                    foreach (var key in flatKeys)
                    {
                        searchTree.Add(new SearchTreeEntry(new GUIContent(key)) 
                        { 
                            level = noneKey != null ? 2 : 1,
                            userData = key 
                        });
                    }
                }
                else
                {
                    searchTree.Add(new SearchTreeGroupEntry(new GUIContent("Flat Keys"), noneKey != null ? 2 : 1));
                    foreach (var key in flatKeys)
                    {
                        searchTree.Add(new SearchTreeEntry(new GUIContent(key)) 
                        { 
                            level = noneKey != null ? 3 : 2,
                            userData = key 
                        });
                    }
                }
            }

            if (categories.Count > 0)
            {
                if (flatKeys.Count > 0)
                {
                    searchTree.Add(new SearchTreeGroupEntry(new GUIContent("Categories"), noneKey != null ? 2 : 1));
                }

                var pathEntries = new Dictionary<string, SearchTreeGroupEntry>();
                
                foreach (var key in categories)
                {
                    var parts = key.Split('/');
                    var currentPath = "";
                    
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        currentPath += (i > 0 ? "/" : "") + parts[i];
                        
                        if (!pathEntries.ContainsKey(currentPath))
                        {
                            var level = currentPath.Count(c => c == '/') + 1;
                            
                            if (noneKey != null) 
                                level++;
                            
                            if (flatKeys.Count > 0) 
                                level++;
                            
                            var group = new SearchTreeGroupEntry(
                                new GUIContent(parts[i]), 
                                level);
                            
                            pathEntries.Add(currentPath, group);
                            searchTree.Add(group);
                        }
                    }
                    
                    var keyLevel = key.Count(c => c == '/') + 1;
                    
                    if (noneKey != null) 
                        keyLevel++;
                    
                    if (flatKeys.Count > 0) 
                        keyLevel++;
                    
                    searchTree.Add(new SearchTreeEntry(
                        new GUIContent($"{parts.Last()} ({key})"))
                    {
                        level = keyLevel,
                        userData = key
                    });
                }
            }

            return searchTree;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            _onSelectEntry?.Invoke(searchTreeEntry.userData as string);
            return true;
        }
    }
}