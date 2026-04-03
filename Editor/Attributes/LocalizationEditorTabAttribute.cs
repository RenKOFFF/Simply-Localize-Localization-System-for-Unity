using System;

namespace SimplyLocalize.Editor
{
    /// <summary>
    /// Register a custom tab in the Localization Editor Window.
    /// The class must implement IEditorTab.
    ///
    /// Usage (in your Editor folder):
    /// <code>
    /// [LocalizationEditorTab("Materials", order: 50)]
    /// public class MaterialsTab : IEditorTab
    /// {
    ///     public void Build(VisualElement container) { ... }
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class LocalizationEditorTabAttribute : Attribute
    {
        public string TabName { get; }
        public int Order { get; }

        public LocalizationEditorTabAttribute(string tabName, int order = 100)
        {
            TabName = tabName;
            Order = order;
        }
    }
}