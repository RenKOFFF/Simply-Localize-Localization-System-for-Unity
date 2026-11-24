using System;
using System.Collections.Generic;
using SimplyLocalize.Editor.Data;
using UnityEngine;

namespace SimplyLocalize.Editor.Windows
{
    [Serializable] internal sealed class SetupSelectionState
    {
        public bool Initialized;
        public string[] Codes, Names;
        public string Default, Fallback;
    }
    [Serializable] internal sealed class ProjectToolsState
    {
        public LocalizationProjectConfig Config;
        public string Channel, Set;
        public bool Initialized, Pending, ClearEmpty, IncludeSources;
        public string Csv;
    }
    [Serializable] internal sealed class ProjectAutoState
    {
        public LocalizationProjectConfig Config;
        public string Channel, Set;
        public bool Initialized, SelectedObjects, SkipNumeric, SkipLocalized;
        public int Minimum;
        public AutoLocalizePrefabMode Prefabs;
        public AutoLocalizeKeyPattern Pattern;
        public string Custom, Prefix, Destination;
        public List<AutoLocalizeCandidate.State> Candidates = new();
    }
}
