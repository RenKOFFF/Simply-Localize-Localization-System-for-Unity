# Independent lifecycle fixtures

These small imported assets exercise actual Resources loading, audio readiness,
fallback, overlapping owners, cancellation and native unloading. They belong to
the development project only, outside `src/SimplyLocalize`. Keep their `.meta`
files and GUIDs. They are reusable test inputs, not disposable run output.

- `Voice/en/Clips`: `audio/primary`, `audio/fallback`, `audio/extra` (three clips).
- `Voice/ru/Clips`: only `audio/primary` (a different clip).
- `Images/en/Image`: one `picture` entry; used as the state retained after an
  over-budget audio candidate fails.

Resources addresses start with `SimplyLocalizeTests/Lifecycle/`. Audio and image
files themselves are outside Resources and referenced by the tables. None of the
assets depend on either demo. `FixtureIsolationTests` enforces that boundary.
The helper in PlayMode stores addresses only, so it cannot retain native objects.

The four WAV files are original synthetic tones, mono PCM16, 22050 Hz, 1102 samples
each. In filename order English/Russian/Fallback/Extra, frequencies are
440/550/660/770 Hz and amplitude 1000 (signed PCM16). Samples use
`(short)(1000 * sin(2 * PI * frequency * i / 22050))`. Imported as PCM,
DecompressOnLoad, PreserveSampleRate, LoadInBackground; PreloadAudioData is false.
The 2x2 RGBA image contains white/red/green/blue pixels. These values make the
fixtures reproducible without a voice service or copying the game's recordings.

Three English clips deliberately produce enough ownership events to exercise
bounded diagnostic history, and exceed the one-entry budget used in failure
tests. The missing Russian fallback key exercises actual fallback resolution.
The fixture does not benchmark production audio sizes, streaming or GPU memory.
