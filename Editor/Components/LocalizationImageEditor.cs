using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationImage), true)]
    public class LocalizationImageEditor : LocalizationObjectEditor<LocalizationImage, Image, Sprite> { }
}