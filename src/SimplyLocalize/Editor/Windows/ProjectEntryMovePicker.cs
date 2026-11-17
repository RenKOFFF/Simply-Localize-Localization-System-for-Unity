using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace SimplyLocalize.Editor.Windows
{
    /// <summary>Shared destination selection for text and asset moves. Does not change source routing.</summary>
    internal sealed class ProjectEntryMovePicker : VisualElement
    {
        internal ProjectEntryMovePicker(LocalizationProjectConfig config, string sourceSetId, LocalizationSourceKind kind,
            string prefix, Action<string, IReadOnlyDictionary<string, string>> move)
        {
            var targets = config.Sets.Where(s => s.Id != sourceSetId).ToArray();
            if (targets.Length == 0) { Add(new Label("Create another content set in Settings to move this entry.")); return; }
            var target = new DropdownField("Target set", targets.Select(s => s.Id).ToList(), targets[0].Id) { name = "move" + prefix + "Target" };
            target.formatListItemCallback = target.formatSelectedValueCallback = id => targets.Single(s => s.Id == id).Name; Add(target);
            Add(new Label("Moving does not connect the target set to any channel.") { style = { whiteSpace = WhiteSpace.Normal } });
            var choices = new VisualElement(); Add(choices); var addresses = new Dictionary<string, string>(StringComparer.Ordinal);
            var languages = config.Sets.Single(s => s.Id == sourceSetId).Sources.Where(s => s.Sources.Any(r => r.Kind == kind)).Select(s => s.Language).ToArray();
            void Refresh()
            {
                choices.Clear(); addresses.Clear(); var set = targets.Single(s => s.Id == target.value);
                foreach (string code in languages)
                {
                    var sources = set.Sources.FirstOrDefault(s => s.Language == code)?.Sources.Where(r => r.Kind == kind).Select(r => r.Address).ToList() ?? new List<string>();
                    if (sources.Count < 2) continue;
                    var field = new DropdownField(code + " destination", new[] { "Choose a source" }.Concat(sources).ToList(), "Choose a source") { name = "move" + prefix + "Destination_" + code };
                    field.RegisterValueChangedCallback(e => { if (sources.Contains(e.newValue)) addresses[code] = e.newValue; else addresses.Remove(code); }); choices.Add(field);
                }
            }
            target.RegisterValueChangedCallback(_ => Refresh()); Refresh();
            Add(new Button(() => move(target.value, new Dictionary<string, string>(addresses))) { text = "Move entry", name = "move" + prefix });
        }
    }
}
