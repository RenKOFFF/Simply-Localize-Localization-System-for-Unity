using SimplyLocalize.Components;
using UnityEngine;

namespace SimplyLocalize.Tests
{
    // Lives outside the package to verify project-side component extension and inherited Inspector support.
    [AddComponentMenu("")]
    public sealed class CustomFormattableText : FormattableLocalizedText
    {
        public int AdditionalSetting = 7;
    }
}
