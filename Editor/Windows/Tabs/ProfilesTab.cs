using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows.Tabs
{
    public class ProfilesTab : IEditorTab
    {
        private readonly LocalizationConfig _config;

        public ProfilesTab(LocalizationConfig config)
        {
            _config = config;
        }

        public void Build(VisualElement container)
        {
            var root = new VisualElement();
            root.style.paddingTop = 12;
            root.style.paddingLeft = 16;
            root.style.paddingRight = 16;

            if (_config == null || _config.languages.Count == 0)
            {
                root.Add(new Label("No languages configured."));
                container.Add(root);
                return;
            }

            // Profile selector
            var selectorRow = new VisualElement();
            selectorRow.style.flexDirection = FlexDirection.Row;
            selectorRow.style.marginBottom = 12;
            selectorRow.style.alignItems = Align.Center;

            var selectLabel = new Label("Profile:");
            selectLabel.style.fontSize = 12;
            selectLabel.style.marginRight = 8;
            selectorRow.Add(selectLabel);

            var names = new string[_config.languages.Count];
            for (int i = 0; i < _config.languages.Count; i++)
            {
                var p = _config.languages[i];
                names[i] = p != null ? $"{p.displayName} ({p.Code})" : "(null)";
            }

            var dropdown = new PopupField<string>(names.Length > 0
                ? new System.Collections.Generic.List<string>(names) : new() { "(none)" }, 0);
            dropdown.style.flexGrow = 1;
            selectorRow.Add(dropdown);

            root.Add(selectorRow);

            // Embedded inspector
            var inspectorContainer = new IMGUIContainer();
            int selectedIndex = dropdown.index;

            dropdown.RegisterValueChangedCallback(evt =>
            {
                selectedIndex = dropdown.index;
                inspectorContainer.MarkDirtyRepaint();
            });

            inspectorContainer.onGUIHandler = () =>
            {
                if (selectedIndex < 0 || selectedIndex >= _config.languages.Count) return;

                var profile = _config.languages[selectedIndex];
                if (profile == null) return;

                var editor = UnityEditor.Editor.CreateEditor(profile);

                if (editor != null)
                {
                    EditorGUI.BeginChangeCheck();
                    editor.OnInspectorGUI();

                    if (EditorGUI.EndChangeCheck())
                        EditorUtility.SetDirty(profile);

                    Object.DestroyImmediate(editor);
                }
            };

            root.Add(inspectorContainer);
            container.Add(root);
        }
    }
}
