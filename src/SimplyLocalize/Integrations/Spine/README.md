# Simply Localize — Spine integration

Optional Spine support for localizing individual attachments and complete atlas pages.

Runtime and editor code live in separate assemblies. Both assemblies are enabled through a
Unity version define only when `com.esotericsoftware.spine.spine-unity` is installed, so the
Spine remains an optional dependency of the game project.

## Usage

1. Add `Localized Spine Attachment` next to a `SkeletonAnimation`, `SkeletonRenderer`,
   or `SkeletonGraphic` component.
2. Create an asset entry in Simply Localize > Assets and assign its channel, set and entry
   in the component Inspector. The binding stores the entry ID and survives key renames.
3. Select the Spine atlas region path to replace, for example `props/poster_scotch`.
4. Open Saved translations preview to compare the selected channel's fallback textures.
   Edit direct assignments in Simply Localize > Assets.
5. Under `Localization Template`, select a language and extract the source region.

The generated PNG is imported as a standalone `Texture2D`. Its width and height must
exactly match the original Spine region; a mismatch is shown as an error. Import-setting
issues are shown as warnings. The generated texture can optionally be assigned to the
selected language's registered Resources table immediately. Choose the destination when
the set has several tables; extraction does not search language folders or pick an arbitrary
table. Native assignment uses the project edit transaction and Unity Undo. If assignment
fails after a successful extraction, the dialog reports the PNG retained for manual use.
External providers supply their own authoring workflow.

PMA conversion is selected automatically from the original Spine atlas page. If no
localized texture is available for the active language, the original attachment remains.

An attachment target is stored as `Atlas Page + Region Path`. The page is assigned
automatically by the region picker and is shown in square brackets in the popup. Therefore a
skeleton may use any number of atlas pages while this component replaces only the selected
region on one selected page. Attachments in different skins that use the same region on that
page are localized together; an identically named region on another page is left untouched.

`Advanced/Spine Target` is normally resolved automatically. `Monitor Skin Changes`
reapplies the localized overlay when another runtime system replaces the active Spine skin.

Use one `Localized Spine Attachment` owner per target skeleton. Independent attachment
components targeting the same skeleton each maintain their own combined skin; their skin
monitors can interpret each other's overlays as external changes. That combination is not
currently supported. This restriction does not prevent separate atlas-page material overrides.

The stage 4 lifecycle regression run was made in a Unity project without Spine Runtime.
The C5 Inspector port has passed 20 Editor tests with Spine 4.2.121 on Unity 6000.0.62f1
in an isolated local UPM project. These cover channel/entry identity after rename,
fallback, actual PNG pixels (trim, rotation, PMA), importer restoration and rejected
output paths. Automatic power-of-two rescaling is disabled for extracted PNGs;
platform texture size limits still apply. Output must
be under Assets; extraction never edits a package atlas importer. Copy a non-readable
package atlas into Assets first. These checks do not prove skeleton rendering or resource cleanup.
The extractor supports unrotated and 270-degree packed regions; other rotations are rejected.
Skeleton rendering, skin changes, multi-page atlases and Spine resource cleanup therefore
remain unverified in that run. Before shipping this optional integration, test these cases
with the installed Spine version and production skeletons, including missing translations,
component disable/enable, and destruction. No Spine dependency is installed automatically.

## Full atlas page replacement

Use `Localized Spine Texture` when most or all of an atlas page is localized. The component
uses a Simply Localize `Texture2D` key and finds a `SkeletonRenderer` or `SkeletonGraphic` on
the same GameObject automatically. Select the original material under `Atlas Page`; the
inspector presents it as the page texture name, dimensions, and material name. This explicit
reference is the reliable page identity even when several pages have identical dimensions.

`<Auto: size and filename>` is available as a fallback for simple or previously configured
objects. It first filters pages by exact dimensions and then chooses the longest source page
name contained in the localized filename. For example, `MagazineCovers_2_ru` resolves to
`MagazineCovers_2`, not `MagazineCovers`. If automatic matching is ambiguous, the component
logs an error and leaves the original materials untouched. Explicit `Atlas Page` selection is
recommended for every multi-page skeleton.

One component replaces one atlas page. If a skeleton uses several pages/materials, add one
`Localized Spine Texture` component and one Simply Localize key for every page that contains
localized graphics. Spine may switch atlas pages while an animation changes attachments, so
leaving one of those pages untranslated will make the animation switch back to its original
material. For `MagazineCovers_SkeletonData`, `MagazineCovers_2.png` contains the `stage0`
covers, while `MagazineCovers.png` contains the `stage1` and `stage2` covers.

The localized texture must preserve the source atlas page dimensions, layout, alpha mode
(the MagazineCovers atlas uses PMA), and relevant sampler/import settings. The inspector reports
dimension mismatches as errors, warns when Wrap Mode, Filter Mode, or mip-map settings differ
from the source page, shows its PMA mode, and rejects two components targeting the same page.
PMA pixel data cannot be proven from Unity's import settings, so the source atlas alpha mode is
shown explicitly. The component
clones the source material per skeleton instance and uses Spine's material override dictionaries,
so the shared material asset and other skeleton instances are not modified.
