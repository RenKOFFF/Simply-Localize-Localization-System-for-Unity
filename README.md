# Simply Localize

![Simply Localize Banner](docs/images/simply-localize-banner.png)

Simply Localize translates text and Unity assets. A project can use one language for its menus and subtitles and another for voice. Small games can keep everything in the default channel and content set.

[Русский](README_RU.md) · [Changelog](CHANGELOG.md)

[Installation](#installation) · [Quick start](#quick-start) · [Editor](#editor-window) · [Runtime API](#runtime-api) · [Formatting](#pluralization--parameters) · [Upgrading](#upgrading-from-202)

## Installation

This guide covers **2.1.0**. OpenUPM installs the published release. To install from a local checkout, select **Package Manager → + → Install package from disk** and open `src/SimplyLocalize/package.json`.

### Via OpenUPM

**Option A — OpenUPM CLI** (recommended):

```bash
npm install -g openupm-cli
openupm add com.renkoff.simply-localize
```

**Option B — manually via `Packages/manifest.json`**:

1. Add a scoped registry entry:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.renkoff"
      ]
    }
  ],
  "dependencies": {
    "com.renkoff.simply-localize": "2.1.0"
  }
}
```

### Via Package Manager (Git URL)

For a Git installation, use the package subdirectory and replace `<revision>` with a published tag or a commit available on your remote.

```text
https://github.com/RenKOFFF/Simply-Localize-Localization-System-for-Unity.git?path=/src/SimplyLocalize#<revision>
```

## Quick start

1. Open **Window → Simply Localize → Localization Editor**. In Setup, select the languages, choose default and fallback, then click **Create localization**. Setup creates `Assets/SimplyLocalize/Resources/SimplyLocalizeSettings.asset`.
2. Keep **Main / Common** selected. In **Translations**, add `Menu/Play`, enter a value for each language, and save.
3. Add `LocalizedText` to a TMP or legacy Text object and select the entry. Enter Play Mode and switch the Main language to check both values. Use **Assets** for sprites and audio clips.
4. For existing files, connect the JSON or asset table under **Settings → Content sets → Connect a source**, select the language and register the keys. JSON can also be edited outside Unity and reread with **Reload from disk**.

A **channel** chooses a language. A **content set** groups entries loaded together. If only English voice exists, create an independent Voice channel with `en` and connect the set containing the clips. Keep subtitles in Main with `en` and `ru`. For a chapter the game loads later, enable its set for that channel and acquire it when entering the chapter.

### JSON File Format

```json
{
  "translations": {
    "UI/MainMenu/Play": "Play",
    "UI/MainMenu/Quit": "Quit",
    "Game/Score": "Score: {0}",
    "Game/Coins": "You have {0} {0|coin|coins}",
    "Dialogue/Greeting": "Hello, {playerName}!"
  }
}
```

## Editor Window

The window edits one project configuration. Select a channel and content set for content
work; this Editor selection does not change the language of a running game.
Setup starts with Main/Common. Add independent or FollowMain channels only when needed.

### Translations Tab

Choose an entry in the searchable, virtualized list, then edit its language values and
description in the detail pane. Direct values and fallback results are shown separately.
Save commits the draft; Cancel discards it. Moving between contexts with pending edits
offers Save, Cancel or Discard. Reload from disk reads external JSON edits.

Renaming or moving an entry keeps its ID. Components bound by ID do not require scene
or prefab rewrites; string lookups still depend on their channel/set/key.
Notes are keyed by EntryId in `Assets/SimplyLocalize/Editor/Notes/<config GUID>.json`,
outside runtime Resources. Conflicts and external changes stop a stale save.

### Assets Tab

Choose a registered asset entry, inspect direct/fallback values and previews, and assign
assets per language. Create or connect exact source tables. When multiple tables are
connected, choose the destination explicitly. Native assets and configuration changes
commit together, with rollback and Unity Undo/Redo.

### Languages Tab

Search the language catalog by names or standard codes, or add a custom language.
Choose a channel's available languages, default and fallback from the project catalog.
The window shows built-in, custom or generic plural-rule status; a language name does not
guarantee dedicated plural rules or font coverage. Sources are registered explicitly;
removing a definition does not silently delete translation files.

### Appearance Tab

Language identity and presentation are separate. Create or connect a
`LocalizationAppearance` source and edit fonts, typography, spacing, direction and layout
in the window. The project config stores its address, not direct references to all fonts.

### Coverage Tab

Coverage reads the selected channel/set's registered sources. It distinguishes direct,
empty, whitespace-only and fallback values, and checks parameters and plural forms.
Unregistered keys are reported separately; failed or malformed sources make the check
incomplete. Asset presence alone does not prove compatibility with every component type.
The check does not change runtime state.

### Auto Localize Tab

Scan selected scene objects for TMP/Text, review generated keys and bindings, then apply.
Choose how prefab instances are handled: skip, instance overrides or prefab assets.
The operation records stable entry IDs and joins scene changes with translation-file Undo.
After an assembly reload, scan/CSV input is retained but a fresh preview is required.

### Tools Tab

- Export to CSV (for translation services)
- Import from CSV with validation and a preview before applying

CSV targets the selected channel and content set. Use `Key,en,ru,...`; optional `SetId`, `EntryId`
and `Description` columns identify the destination, retain identity across renames/moves, and carry notes.
Export includes identities. Optional `Source:en` columns select an already connected source address
when several text files exist. The old `File` column and `CsvExporter` API have been removed;
export a new CSV before editing. There is no automatic conversion of the old config/profile format.

Quoted commas, escaped quotes, multiline values, BOM and CR/LF/CRLF are supported. File preview
strictly decodes UTF-8, or UTF-16/UTF-32 with BOM. Translation/description whitespace is preserved;
keys cannot start/end with whitespace. Language codes are normalized (`EN` → `en`, `pt-br` → `pt-BR`).
Duplicate normalized columns, conflicting identities, malformed rows and unconnected destinations
reject the entire preview without writing data.

Empty cells **keep existing values by default**; select explicit clearing to erase them.
Omitted language/description columns remain unchanged. Review the preview and apply it, or cancel
without creating files or definitions. New languages are enabled only in the selected channel.
For large imports, filter the change list by key, destination file, language or change type.
Select a row to compare the full values. **Apply all changes** saves the entire preview, including rows hidden by filters.
You can cancel preparation from the progress bar. External changes invalidate the preview. After assembly reload, rebuild the preview from retained input.

Editor scripts use `ProjectCsv.Export(config)` and `ProjectCsv.Preview(config, csv)`.
Dispose the preview on cancellation; `plan.Apply()` saves one isolated transaction and returns a
`LocalizationProjectChange`. To register Unity Undo, use
`LocalizationProjectUndo.Record(plan.Apply(), "Import CSV")`. The Tools window does this automatically.
Use `emptyCells: CsvEmptyCellPolicy.Clear` for explicit clearing. A header without rows is a no-op;
empty input is invalid. Separate pending edits are not saved by importing CSV.

Saving uses temporary files and per-file replacement. A caught failure rolls back earlier writes,
including config changes and newly created sources. Additional JSON fields are retained. A failed
preview remains available for retry when the cause can be resolved without invalidating its baseline.
This is not a crash/power-loss journal; failed rollback reports retained backups. Keep version
control/backups for recovery.

### Settings Tab

- Automatic startup or explicit bootstrap; optional device-language detection.
- Channels, Independent/FollowMain mode, available content sets and startup sets.
- Exact source registrations and visible, read-only stable IDs.
- **Game View / Simulator language dropdown → Show in Play Mode** — displays a language selector during Play Mode. In Game View, the selector appears over the game preview; in Device Simulator, it is located under **Control Panel → Simply Localize**. Language selection is available after localization is initialized.
- **Game View position** — position of the Game View dropdown: left, center, or right.

### Diagnostics Tab

Inspect runtime channels, active owners, shared sources, loading and release state.
Releasing ownership is distinct from proven physical memory reclamation. A provider's
snapshot cannot account for every external reference or replace Unity's memory profiler.

## Runtime API

The static facade reads the Main channel. Await startup before a first read in game code. UI components refresh when localization becomes ready.

```csharp
using SimplyLocalize;
using System.Threading.Tasks;

public static class LanguageMenu
{
    public static async Task<string> SelectAsync(string language)
    {
        await Localization.InitializeAsync();
        await Localization.SetLanguageAsync(language);
        return Localization.Get("Menu/Play");
    }
}
```

Use `Localization.GetAsset<AudioClip>(key)` for audio and `Localization.ReloadAsync()` to reread sources. `AvailableLanguages` returns codes. An explicit bootstrap accepts a `LocalizationProjectConfig`. A game using dependency injection can own a `LocalizationRuntime`, await `InitializeAsync()` and call `ReleaseAsync()` at shutdown.

For an independent channel, pass its ID to `runtime.SwitchLanguageAsync(channelId, "en")`. To load a chapter, keep the handle returned by `await runtime.AcquireSetAsync(channelId, setId)` and await `handle.ReleaseAsync()` when the chapter closes. Other owners can keep the same data loaded.

## Components

### LocalizedText

Localizes a TextMeshPro or legacy `Text` component.

Select an entry on the component to bind the text to its channel and content set.

### FormattableLocalizedText

Same as `LocalizedText`, but supports runtime parameters (indexed and/or named).

```csharp
var text = GetComponent<FormattableLocalizedText>();

// Indexed: "Score: {0}" in JSON
text.SetArgs(100);
// or set just one index
text.SetArg(0, 100);

// Named: "Hello, {playerName}!" in JSON
text.SetParam("playerName", "Alex");

// Multiple named at once
text.SetParams(new Dictionary<string, object>
{
    { "playerName", "Alex" },
    { "count", 5 }
});

// Clear all parameters
text.ClearParams();
```

Default parameter values can also be set in the Inspector via the **Parameters** list — handy for static text with placeholders that don't need code at all. Use a numeric name (`0`, `1`, ...) for indexed parameters, or any string for named ones.

### LocalizedSprite

Localizes a Sprite on `Image` (UI) or `SpriteRenderer` (world-space). Auto-detects which target is on the same GameObject.

### LocalizedAudioClip

Localizes an AudioClip on an `AudioSource`. Optionally auto-plays on language change (inspector toggle).

### LocalizedEvent

Invokes a different `UnityEvent` per language. Useful for analytics, localized cutscenes, or any language-specific side-effects.

### LocalizedProfileOverride

Select a language code for each override on the component. Font, typography, spacing and layout can be customized for that object. Use **Appearance** in the window for language-wide settings.

### Spine Integration

When `com.esotericsoftware.spine.spine-unity` is installed, Simply Localize automatically
enables its separate Spine runtime and editor assemblies. Spine is optional and is not added as
a package dependency.

- `Localized Spine Attachment` replaces one atlas region using a localized `Texture2D`. The
  inspector identifies the target by `Atlas Page + Region Path`, previews every language,
  validates dimensions and import settings, and can extract the source region into a
  `Localized/` folder as a translation template.
- `Localized Spine Texture` replaces a complete atlas page. Select the original page explicitly
  in multi-page skeletons and add one component/key for every page that contains localized art.
  The inspector validates page ownership, dimensions, sampler settings, mipmaps, and PMA mode.

Both components use runtime skins or Spine material overrides. They do not modify the shared
`SkeletonDataAsset`, atlas materials, or source textures. Missing localized assets leave the
original Spine content unchanged. See
[`Integrations/Spine/README.md`](src/SimplyLocalize/Integrations/Spine/README.md) for the full
workflow and multi-page rules.

## Pluralization & Parameters

### Indexed Parameters

```json
"Score": "Your score is {0}"
```

```csharp
Localization.Get("Score", 100);  // "Your score is 100"
```

### Named Parameters

```json
"Greeting": "Hello, {playerName}!"
```

```csharp
Localization.Get("Greeting",
    new Dictionary<string, object> { { "playerName", "Alex" } });
// "Hello, Alex!"
```

### Pluralization

The syntax `{N|singular|plural|...}` selects a form based on the value of parameter `N` using the language of the resolved translation, including fallback translations.

```json
"Items": "{0} {0|item|items}",
"Minutes": "{0} {0|minute|minutes}",
"ItemsRu": "{0} {0|предмет|предмета|предметов}"
```

```csharp
Localization.Get("Items", 1);   // "1 item"
Localization.Get("Items", 5);   // "5 items"

// Russian plural rules (one / few / many):
Localization.Get("ItemsRu", 1); // "1 предмет"
Localization.Get("ItemsRu", 3); // "3 предмета"
Localization.Get("ItemsRu", 5); // "5 предметов"
```

### Plural rules and compatibility

Rules use [Unicode CLDR 48 cardinals](https://www.unicode.org/cldr/charts/48/supplemental/language_plural_rules.html) for ordinary numbers. The table lists **positional order in this package**, not CLDR category order. Existing slots stay in place; newly supported categories are appended.

| Language codes | Forms in template order |
|---|---|
| `en de nl sv fy fi et gl` | one, other |
| `af fo no nb nn ka tr hu` | one, other (including decimal 1.0) |
| `da is` | one, other, using their respective fractional rules |
| `fr pt pt-BR` | one, other, **many** |
| `es it ca pt-PT` | one, other, **many** (language-specific one rules) |
| `ru uk be pl` | one, few, many, **other** |
| `hr sr bs` | one, few, other |
| `cs sk` | one, few, other, **many** |
| `sl` | one, few, other, **two** |
| `ro` | one, other, **few** |
| `ja zh ko vi th id ms my lo km` | other |
| `ar` | zero, one, two, few, many, other |

Short templates still clamp to their last available form. For example, old French `{0|pièce|pièces}` remains valid; a third form can be appended for `many`. Corrected language rules intentionally change previously incorrect choices, such as Polish 21 or Spanish 0. Occitan (`oc`) retains its legacy two-form rule; unknown codes use the English-style rule.

Plural rules use the language code of the resolved translation, including regional variants such as `pt-PT`. For game-specific rules, register `IPluralRule` with `LocalizationPluralRules` on the formatter used by the runtime.

Built-in rules accept all standard integer types and fractions without Int32 wraparound. Use `decimal` or an invariant numeric string to preserve visible fractional digits (`1m` and `1.0m` can have different rules). Float/double use their round-trip decimal representation. Missing, non-numeric, non-finite, and out-of-Decimal-range values retain the zero default. Existing custom `IPluralRule.Evaluate(int)` implementations remain valid: fractions truncate and out-of-range counts saturate to Int32 bounds. Implement the optional `IDecimalPluralRule` to handle the full supported range. Compact-number exponents and ordinal/range plural rules are not supported.

### Explicit selectors and template handling

Ordinary numeric parameters always use plural rules, preserving existing templates. For a choice by index, pass `FormSelector`; the template itself stays unchanged:

```csharp
using SimplyLocalize.TextProcessing;
// Translation: "{gender|He|She} plays as {0|Warrior|Mage|Rogue}"
Localization.Get("Character", new object[] { new FormSelector(2) },
    new Dictionary<string, object> { ["gender"] = new FormSelector(1) });
// "She plays as Rogue", in every language
```

Selectors are zero-based and clamp to the first/last available form. Escapes `{{` and `}}` produce literal braces in every `Get` overload and inside form text. For compatibility, three consecutive closing braces end the token first and then append a literal `}`: `{0|one|other}}}` with 1 produces `one}`. Missing simple parameters keep their original spelling. Malformed or nested token expressions remain literal text; recursive expressions, `string.Format` alignment and format specifiers are not part of this syntax. Literal parameter values are not parsed again as templates.

## Extensibility

### Custom Asset Preview Renderers

Want a 3D preview for localized meshes? A waveform for audio clips? Implement `IAssetPreviewRenderer` anywhere in your Editor code — it's auto-discovered via `TypeCache`.

```csharp
using SimplyLocalize.Editor.AssetPreviews;
using UnityEditor;
using UnityEngine;

public class MeshPreviewRenderer : IAssetPreviewRenderer
{
    public int Priority => 10;

    public bool CanRender(Object asset) => asset is Mesh;

    public void DrawPreview(Rect rect, Object asset)
    {
        var mesh = (Mesh)asset;
        var preview = AssetPreview.GetAssetPreview(mesh);
        if (preview != null)
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
    }
}
```

Higher `Priority` wins if multiple renderers claim the same asset. The built-in sprite/texture/audio renderers use priority `10`, so any user implementation with priority `11+` will override them.

### Custom Asset Type Filters

The Assets tab dropdown is built from discovered `IAssetTypeFilter` implementations plus auto-generated filters for any type found in your tables. You usually don't need to write one — the auto-generator uses `ObjectNames.NicifyVariableName` for display names. But if you want a nicer name than the default (e.g. "UI Icons" instead of "Sprite"), implement the interface:

```csharp
using System;
using System.Collections.Generic;
using SimplyLocalize;
using SimplyLocalize.Editor.AssetFilters;
using UnityEngine;

public class UIIconFilter : IAssetTypeFilter
{
    public string DisplayName => "UI Icons";
    public int Order => 5;
    public Type AcceptedFieldType => typeof(Sprite);

    public bool MatchesKey(string key, IReadOnlyDictionary<string, LocalizationAssetTable> tables)
    {
        if (!key.StartsWith("ui/icons/")) return false;

        foreach (var t in tables.Values)
        {
            if (t == null) continue;
            var a = t.Get(key);
            if (a == null) continue;
            if (a is Sprite) return true;
        }
        return true; // include unassigned keys so they stay visible
    }
}
```

### Custom Editor Tabs

Add your own tab to the localization editor window:

```csharp
using SimplyLocalize.Editor;
using SimplyLocalize.Editor.Windows.Tabs;
using UnityEngine.UIElements;

[LocalizationEditorTab("Glossary", order: 50)]
public class GlossaryTab : IEditorTab
{
    public void Build(VisualElement container)
    {
        container.Add(new Label("My custom tab!"));
        // ... your UI here
    }
}
```

The tab appears automatically in the window's tab bar, sorted by `order`. No registration needed.

### Custom source providers

Implement `ILocalizationSourceProvider` in the game, register it by ID in `LocalizationSourceRegistry` and pass the registry to `Localization.InitializeAsync(config, providers: registry)`. Each successful load supplies a `LocalizationSourceLease` with a release action. The built-in Resources provider works out of the box. The package has no required Addressables or AssetBundle dependency.

### Chapter voice with Addressables (development branch)

Install **Addressables** through Unity Package Manager, then open **Simply Localize**.

1. In **Settings**, create an independent Voice channel with `en` and a Chapter 1 set. Include the set in Voice and turn off **Load at startup**. Keep subtitles in Main.
2. In **Content sets → Chapter 1 → Connect a source**, select `en` and **Create Addressables table**. Save outside Resources. To reuse a table, select **Existing table → Connect with Addressables**.
3. Select Voice and Chapter 1 at the top of the window. In **Assets**, add the voice key, assign the AudioClip and save. Select this entry on the **Localized Audio Clip** component next to the AudioSource.
4. Under **Settings → Channels → Voice**, expand **Load while a scene object is active** for this set. Select the chapter root and click **Bind chapter object**. Enabling it loads the table and its clips. Disabling it releases that object's ownership.

**Build Addressables content** builds the project's catalog and bundles for the active platform. For independent unloading, keep clips outside Resources and avoid direct scene references to them. New tables use separate bundles by default. Existing Addressables entries keep their original group.

## Attribute Reference

### `[LocalizationKey]`

Turns a `string` field into a searchable key picker in the Inspector.

```csharp
using SimplyLocalize;

public class DialogueLine : MonoBehaviour
{
    [LocalizationKey] public string key;
}
```

### `[LocalizationPreview]`

Shows a live preview of the resolved translation next to a `[LocalizationKey]` field.

```csharp
[LocalizationKey]
[LocalizationPreview]
public string key;
```

### `[LocalizationEditorTab(name, order)]`

Registers a class as a custom tab in the localization editor window. The class must implement `IEditorTab`.

---

## Upgrading from 2.0.2

**2.1.0 includes breaking changes.** Before updating, back up the game and record the old profiles, languages, fallback and source paths. If scripts reference removed types, update them first so Unity can compile the editor window.

- Create a project config in Setup and reconnect the original JSON and asset tables. Reuse unique string keys in Main, or bind entries explicitly when moving them into a different channel.
- Assign the new config and language in each explicit `LocalizationBootstrap`. Recreate language-wide settings under Appearance. On `LocalizedProfileOverride` and `LocalizedEvent`, select a language code for each existing row.
- Replace `Localization.SetLanguage("ru")` with `await Localization.SetLanguageAsync("ru")`. Port custom providers to the source registry. Export a fresh CSV if the old file uses the `File` column.

[Editor window in 2.0.2 (GIF)](docs/images/editor-overview.gif)

## Editor scripting and automated setup

```csharp
var result = SimplyLocalize.Editor.LocalizationProjectAuthoring.Configure(
    new[] { "en", "ru" }, "en", "en");
// result.Config; result.Change for an update to an existing startup config
```

Editor-only API without dialogs. Configure explicitly targets the fixed startup path. Repeated calls preserve identities, existing names, startup flags and translation files; supplied default/fallback choices are applied to Main. To reuse a config elsewhere, call `LocalizationProjectAuthoring.UseForAutomaticStartup(config)` instead. Use `LocalizationProjectAuthoring.AddLanguage(config, "pl")` without changing defaults; independent channels remain explicit. JSON files can be edited externally; refresh the editor snapshot afterwards. Unsaved config edits must be saved first. Existing conflicts or invalid input cause an exception rather than partial setup. The build check validates the reserved startup address/type/config; it does not initialize providers or require a startup config for an explicit bootstrap.

```text
Unity -batchmode -quit -projectPath <project>
  -executeMethod SimplyLocalize.Editor.LocalizationProjectAuthoring.ConfigureFromCommandLine
  -slLanguages en,ru -slDefault en -slFallback en
```

## License

MIT — see [LICENSE.txt](LICENSE.txt).

## Contributing

Issues and pull requests welcome at [github.com/RenKOFFF/Simply-Localize-Localization-System-for-Unity](https://github.com/RenKOFFF/Simply-Localize-Localization-System-for-Unity).
