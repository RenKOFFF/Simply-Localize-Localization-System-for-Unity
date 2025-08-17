using UnityEditor;
using UnityEngine;

namespace SimplyLocalize.Editor
{
    [CustomEditor(typeof(LocalizationSpriteRenderer), true)]
    public class LocalizationSpriteRendererEditor : LocalizationObjectEditor<LocalizationSpriteRenderer, SpriteRenderer, Sprite> { }
}