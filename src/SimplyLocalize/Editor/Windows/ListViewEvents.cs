using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    internal static class ListViewEvents
    {
        internal static void RegisterSelectionChanged(this ListView list, Action<IEnumerable<object>> callback)
        {
#if UNITY_2022_3_OR_NEWER
            list.selectionChanged += callback;
#else
            list.onSelectionChange += callback;
#endif
        }

        internal static void RegisterItemsChosen(this ListView list, Action<IEnumerable<object>> callback)
        {
#if UNITY_2022_3_OR_NEWER
            list.itemsChosen += callback;
#else
            list.onItemsChosen += callback;
#endif
        }
    }
}
