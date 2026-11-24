using System;
using System.Linq;
using SimplyLocalize.Editor.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Project-model setup view shared by the main window and Editor integrations.</summary>
    public sealed class ProjectSetupView : ScrollView
    {
        public ProjectSetupView(EditorWindow owner, Action<LocalizationProjectConfig> configured)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (configured == null) throw new ArgumentNullException(nameof(configured));
            name = "projectSetup";
            style.flexGrow = 1; style.paddingTop = 24; style.paddingLeft = style.paddingRight = 24;
            contentContainer.style.maxWidth = 720; contentContainer.style.width = Length.Percent(100); contentContainer.style.alignSelf = Align.Center;
            var heading = new Label("Set up localization"); heading.style.fontSize = 20;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold; heading.style.marginBottom = 12; Add(heading);
            Add(new Label("Choose the languages for your project. Main and Common are ready to use; independent channels can be added when you need them.") { style = { whiteSpace = WhiteSpace.Normal } });
            var languages = new SetupLanguagePicker(owner); Add(languages);
            var path = new Label(LocalizationProjectAuthoring.ConfigAssetPath); path.style.fontSize = 10; path.style.whiteSpace = WhiteSpace.Normal; path.style.marginTop = 16; Add(path);
            var message = new HelpBox("", HelpBoxMessageType.Error) { name = "setupMessage" }; message.style.display = DisplayStyle.None;
            var create = new Button(() =>
            {
                try
                {
                    var result = LocalizationProjectAuthoring.Configure(languages.Selected.Select(l => l.Code), languages.DefaultCode, languages.FallbackCode,
                        languages.Selected.ToDictionary(l => l.Code, l => l.NativeName));
                    if (result.Change != null) LocalizationProjectUndo.Record(result.Change, "Set up localization");
                    configured(result.Config);
                }
                catch (Exception error) { message.text = error.Message; message.style.display = DisplayStyle.Flex; }
            }) { name = "createConfig", text = "Create localization" };
            languages.Changed += () => create.SetEnabled(languages.Selected.Count > 0);
            create.SetEnabled(languages.Selected.Count > 0); LanguagePickerStyle.Primary(create); create.style.marginTop = 16; create.style.height = 30;
            Add(create); Add(message);
            var existing = new Label("Already configured? Select the project config in the field above."); existing.style.whiteSpace = WhiteSpace.Normal; existing.style.marginTop = 20; Add(existing);
        }
    }
}
