using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public interface IEditorTab
    {
        void Build(VisualElement container);
    }
    /// <summary>Optional context injection for project-owned tabs. Does not initialize the game runtime.</summary>
    public interface IContextualLocalizationTab : IEditorTab
    {
        void SetContext(SimplyLocalize.Editor.Data.EditorProjectContext context);
    }
}
