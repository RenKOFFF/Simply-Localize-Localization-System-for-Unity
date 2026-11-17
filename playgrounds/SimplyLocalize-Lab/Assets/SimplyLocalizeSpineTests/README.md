# Optional Spine Editor regressions

This development-only assembly is outside the distributed UPM package. It is enabled
only when the real `com.esotericsoftware.spine.spine-unity` package is installed.
It does not install or redistribute Spine and does not use runtime stubs.

Run `SpineEditorChannelTests` in EditMode with Unity Test Framework. The C5 run used
Unity 6000.0.62f1, Spine Unity 4.2.121 and spine-csharp 4.2.37. Alternatively, copy
this folder into a separate Unity test project referencing the local Simply Localize
package, those Spine packages and `com.unity.test-framework`. Keep the project path
short enough for Unity/.NET file operations and their temporary backup filenames.

The tests create and remove their own small PNG/config/table fixtures. They cover
actual extraction pixels and import settings, error handling, stable bindings and
channel fallback. They do not validate production skeleton rendering or runtime
skin/resource lifecycles; those require separate integration acceptance.
