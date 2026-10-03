# Real Unity basemap loading gate / فحص تحميل الخريطة داخل Unity

**Workspace engine result: NOT RUN.** No licensed matching Unity Editor is
available here. The .NET basemap fixture uses `System.Text.Json` and a simulated
missing-origin object; neither it nor Python protocol tests proves Unity
deserialization or Resources import.

## Run on your machine before starting the game

Requires Python 3.10+ and an already licensed **Unity 6000.3.18f1** Editor
(version read from `ProjectSettings/ProjectVersion.txt`). No installation or
license credentials are requested or automated.

Close this project in Unity first. From the repository root:

```powershell
# Windows PowerShell
python tools/unity_compile.py --basemap --unity "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe"
```

```sh
# macOS
python3 tools/unity_compile.py --basemap --unity "/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity"
# Linux: substitute your installed Editor executable, not Unity Hub
python3 tools/unity_compile.py --basemap --unity "/opt/Unity/Editor/Unity"
```

Use `--project "/path/to/testingReplic-copy"` to check a project copy. Unity
generates its usual local import caches and `.meta` files on that machine;
these must not be added to source delivery. The gate itself creates no `.meta`
files. First import needs access to the project's existing package registry or
cache. Use `--timeout 3600` if needed.

This invokes `NewGaza.Editor.NewGazaBasemapGate.Run` only after Unity compiles
the real project. It stays in **Edit Mode**, does not open or generate scenes,
invoke setup or Play Mode smoke checks, create GameSession/CityWorld, reset
progress, access game saves/PlayerPrefs, or regenerate source/surveyed geometry.
Only small in-memory JSON fixtures are created for compatibility checks.
`--basemap` cannot be combined with `--target`.

## Required checks

- Load the actual `Resources.Load<TextAsset>("GazaBasemap")`, verify its imported
  asset path, and inspect real `JsonUtility.FromJson<CityBasemap>` metadata.
- Load through `CityBasemap.LoadFromResources`; assert latitude **31.515**,
  longitude **34.45**, scale **50**, schema **1**, and **4032 roads / 12000
  buildings / 528 areas**, attribution and extent from `Tests/Basemap/Program.cs`.
- Fully validate metadata-only, explicit legacy origin, and flat headers,
  including both flat scale names. These cases must succeed through feature
  validation, not merely fail later with a missing-road error.
- Ignore origin/metadata names nested in objects or escaped inside string
  values when genuine headers are present. Reject those decoys when no genuine
  projection header exists, including array-nested and Unicode-escaped header
  names. Reject wrong latitude, longitude, explicit legacy
  precedence over otherwise valid metadata, scale and schema.
- Reject missing Resources assets, missing required roads, empty and malformed
  JSON. Retain a named PASS/FAIL and diagnostic for every case.

## Evidence and failures

The command exits **0 only on complete success**, otherwise **1**. Each fresh
run retains `Editor.log`, `launcher.log`, `unity-result.json` (if Unity reached
the gate), and `report.json` under `exports/unity-basemap/<unique-run>/`.
The result contains actual `Application.unityVersion`, separate runtime/editor
assembly identities, a fresh nonce, resource path, Edit Mode confirmation and
all 26 named checks. The launcher requires all checks plus the PASS log marker;
zero exit code, a compilation-only proof, wrong versions, stale proofs or
partial check lists cannot pass.

Use `--log-dir "/absolute/path/outside-the-project"` for a chosen evidence
directory. Reusing a directory removes old proof and logs; archive runs before
reuse. Do not place output inside Assets/Resources. Editor failures and timeouts
retain their logs; missing Unity reports **NOT RUN** and never substitutes the
.NET simulation. Keep the full evidence directory when sharing a failed run.

The engine run is still required on the user's licensed machine. This is not a
Play Mode, visual/device, audio-balance or performance check.

## Local runner/source checks (not engine evidence)

```sh
python3 -m unittest discover -s tools/tests -p 'test_unity_basemap.py' -v
dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release
dotnet run --project testingReplic/Tests/Basemap/BasemapTests.csproj --configuration Release
```

Python uses a simulated process to test evidence validation, failures, timeouts
and stale-result rejection. The separate-assembly C# check uses minimal API
contracts to catch access/syntax regressions only. Neither executes Unity.