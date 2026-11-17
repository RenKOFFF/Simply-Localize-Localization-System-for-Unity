using SimplyLocalize.Components;
using UnityEditor;

namespace SimplyLocalize.Editor.Inspectors
{
    [CustomEditor(typeof(LocalizedAsset<>), true), CanEditMultipleObjects]
    public class LocalizedAssetEditor : LocalizedComponentEditor { }
}
