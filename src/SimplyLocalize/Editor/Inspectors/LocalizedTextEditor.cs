using SimplyLocalize.Components;
using UnityEditor;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedText), true), CanEditMultipleObjects]
    public class LocalizedTextEditor : LocalizedComponentEditor { }

    [CustomEditor(typeof(FormattableLocalizedText), true), CanEditMultipleObjects]
    public class FormattableLocalizedTextEditor : LocalizedComponentEditor { }
}
