# Real Unity compilation gate / فحص التجميع الفعلي

Requires Python 3.10+ (standard library only) and **Unity 6000.3.18f1** installed
and licensed on the machine running the command. The authoritative version is
`ProjectSettings/ProjectVersion.txt`; no Unity installation, credentials or
licensed CI configuration are automated here.

Close this project in Unity before running. First import can take several minutes
and requires access to the Unity package registry/cache for the retained URP,
Input System and other package dependencies. Existing activation must work in
batch mode. License, package, project-lock, timeout and compilation failures are
not converted into a pass.

Run from the repository root, using an explicit Editor executable (not Unity Hub):

```powershell
# Windows PowerShell
python tools/unity_compile.py --unity "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe"
```

```sh
# macOS
python3 tools/unity_compile.py --unity "/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity"
# Linux: replace with your actual installation path
python3 tools/unity_compile.py --unity "/opt/Unity/Editor/Unity"
```

## What it proves

The runner invokes the **actual testingReplic project**, its assets and pinned
packages with `-batchmode -nographics -quit -executeMethod`.
Unity imports and compiles before invoking `NewGazaCompilationGate.Run`.
That entry point checks the running Unity version and compilation status, then
checks that `GameSession` and `NewGazaSmokeTest` were loaded in separate
`Assembly-CSharp` / `Assembly-CSharp-Editor` assemblies with compiled outputs.
It writes a per-run nonce-bearing result only after those checks pass.

Success requires all of: zero Editor exit code, a nonempty retained compiler log,
no compiler-error diagnostics, and a fresh matching result with the exact Unity
version and assembly identities. A zero exit code alone cannot pass. An older
Unity version cannot pass. Missing Unity explicitly fails without running fixture
checks. Normal Unity incremental compilation is retained; this is not a forced
clean rebuild.

The default timeout is 1800 seconds; use `--timeout 3600` for a slow first import.
Each run keeps `Editor.log`, `launcher.log`, `unity-result.json` when produced,
and `report.json` under a unique `exports/unity-compile/` directory.
Use `--log-dir "/absolute/path/logs"` to choose a directory; reusing a directory
replaces its previous four files. Logs are ignored by Git and not packaged.

This does **not** enter Play Mode, open a scene, alter game saves, invoke project
setup, validate shader rendering or produce a player/store build. Unity itself
creates Library/import caches and local `.meta` files as usual. The gate does not
create or ship `.meta` files. Original imported metadata is retained.

## Opt-in Android / iOS player build gate

The default command still checks **Editor only**. No mobile target runs unless
you explicitly select it; only `Android` and `iOS` are approved options:

```sh
# Actual Android APK or AAB, using existing project settings
python3 tools/unity_compile.py --unity "/path/to/Unity" --target Android --timeout 7200
# macOS only: Unity iOS/IL2CPP export to Xcode, NOT an Xcode build or signed IPA
python3 tools/unity_compile.py --unity "/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity" --target iOS --timeout 7200
```

Windows supports the Android command with the explicit `Unity.exe` path shown
above. Use a **copy of the project** if you do not want Unity's local import,
target-selection and build caches changed (`--project "/path/to/project-copy"`).
Close it in Unity first. The gate does not call setup menus, change player
settings, generate a game scene, modify package dependencies, or add metadata
to source delivery. It builds only `Assets/NewGaza/Scenes/NewGaza.unity`, not the
disabled imported racing scene. If the entry scene is missing, it fails and asks
you to run approved setup manually.

Prerequisites, supplied/approved on your machine, not installed by these tools:

- Exact Editor version from `ProjectSettings/ProjectVersion.txt`, activated for
  batch mode, and the target's Unity Build Support module.
- Android: configured Android SDK, NDK and OpenJDK compatible with that Editor,
  plus the configured scripting backend's toolchain (IL2CPP for this game).
  Android **Export Project** must be disabled: exporting Gradle alone would not
  prove a native APK/AAB build. The existing **Build App Bundle** setting chooses
  `.aab` versus `.apk`. Existing custom keystore/signing settings must work; the
  gate neither requests passwords nor replaces them with another configuration.
- iOS: macOS and the matching iOS Build Support module. Unity compiles player C#
  and generates IL2CPP/Xcode output. **It does not compile generated C++ with
  Xcode, validate provisioning, sign, archive, or upload an IPA.** Those require
  a separately approved macOS/Xcode check and, for signed device builds, your
  existing Apple provisioning/signing setup.
- Registry/cache access for the retained URP/Input System packages, disk space,
  and sufficient build time. A missing license/module/SDK, native toolchain
  failure, signing failure, project lock or timeout is never success.

Unity starts with `-buildTarget` before executing the Editor-only
`NewGazaPlayerBuildGate`. It validates the exact version, requested active target,
installed module, separate runtime/Editor assemblies, and scene, then calls
`BuildPipeline.BuildPlayer` with `BuildOptions.None`. This compiles **player**
scripts with target defines and without `UNITY_EDITOR`, catching platform-only
code and runtime references to Editor APIs that Editor compilation cannot catch.
It retains the BuildReport result/error messages in `unity-result.json`.
Success additionally requires a fresh matching nonce/version/target/scene,
zero build errors, and a nonempty APK/AAB or generated
`Unity-iPhone.xcodeproj/project.pbxproj`. Nonzero exits or compiler errors in the
retained log fail even if a result claims success.

Logs remain under `exports/unity-compile/` by default. `report.json` identifies
the scope (`player-build`), target and unique `player-<nonce>/` output directory.
`Editor.log` contains player compiler, IL2CPP and native build diagnostics;
`launcher.log` retains startup output. A structured gate prerequisite failure
is labelled `prerequisite-missing`; startup failures before the gate may instead
be `error` with log diagnostics and a prerequisite checklist. All are nonzero
runner outcomes. Partial build outputs and logs are retained for diagnosis;
do not distribute failed outputs. Reusing `--log-dir` replaces the four log/proof
files, but never overwrites an earlier player output. Player log/output directories
must be **outside** the Unity project to avoid importing generated builds.

### Controlled real player pass/fail probes

```sh
python3 tools/verify_unity_player_cases.py --unity "/path/to/Unity" --target Android --timeout 7200
# On an approved macOS machine with iOS Build Support:
python3 tools/verify_unity_player_cases.py --unity "/path/to/Unity" --target iOS --timeout 7200
```

These use temporary isolated projects and separate Editor/runtime assemblies,
not modifications to the game. Each case must first pass the real Editor gate.
The clean case must build/export for the requested target. Negative cases guard
code with `UNITY_ANDROID` or `UNITY_IOS` **and** `!UNITY_EDITOR`:

- An explicit player-only `#error` must yield `CS1029` with the named failure marker.
- A player-only reference to `UnityEditor.EditorWindow` must yield the missing
  namespace/type diagnostic `CS0246` or `CS0234` naming `UnityEditor`.

A missing module/license/SDK, failed Editor baseline, unrelated diagnostic,
missing log or timeout never counts as a successfully detected injected error.
Logs, player outputs and `cases.json` survive fixture deletion in
`exports/unity-player-cases/`. Unavailable Unity produces failed/NOT RUN case
results and exits 1, not a pass. These minimal projects use no URP/art assets:
**they do not replace a full game player build**. Neither mobile gate proves
gameplay, rendering, performance, installability on a device, or store readiness.

## Controlled real-Unity pass/fail cases

```sh
python3 tools/verify_unity_compile_cases.py --unity "/path/to/Unity"
```

Uses temporary **isolated Unity projects**, never error injection into the game.
The success case compiles public runtime access from a separate Editor assembly.
Negative cases require a real compiler diagnostic, not just a nonzero process:
runtime syntax error, Editor access to an internal runtime member (`CS0122`),
and Unity's removed/obsolete `AudioImporter.threeD` API (`CS0619`).
The version is read from this project's `ProjectVersion.txt`.
These minimal real-Unity probes do not use the game's `InternalsVisibleTo` grant,
and do not include its URP/art assets: run the full project gate separately.
Case logs survive removal of the temporary projects under
`exports/unity-compile-cases/`. License/startup failures do not count as expected
compiler failures. If Unity is missing the suite reports **NOT RUN** and exits 1.

## Packaging

```sh
python3 tools/package_unity_source.py --unity "/path/to/Unity"
# Opt in to fresh Android player verification as well:
python3 tools/package_unity_source.py --unity "/path/to/Unity" --player-target Android --compile-timeout 7200
# On macOS, require both (only if both Build Support modules are installed):
python3 tools/package_unity_source.py --unity "/path/to/Unity" --player-target Android --player-target iOS --compile-timeout 7200
```

Compilation is run afresh before touching any existing ZIP or delivery parts.
Failed/unavailable Unity refuses packaging and leaves existing exports unchanged;
do not mistake a previous ZIP for a newly verified one.
With `--player-target`, the Editor gate and each requested platform gate must
pass before any ZIP/delivery parts are touched. The flag requires `--unity`.
Without it, `--unity` still verifies **Editor only**, not Android or iOS.
Without `--unity`, packaging remains available for source-only delivery but emits
an explicit **UNVERIFIED** warning. A source ZIP is not a compiled game.
Imported tracked `.meta` files are preserved; newly generated metadata and Unity
caches are excluded. No new `.meta` files should be committed.

## Verification boundaries

```sh
python3 -m unittest discover -s tools/tests -p 'test_unity*.py' -v
dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release
```

Python tests simulate the process protocol (paths with spaces, pass/failure,
nonzero exits, zero-exit compiler errors, stale/missing/invalid result, wrong
version, merged assemblies, timeout, unavailable Editor and packaging refusal).
**They are not a Unity compilation.**
Player protocol tests additionally cover opt-in target/host/scene preflight,
missing modules/SDKs, native build failure, wrong target/scope/scene, missing
artifacts, zero-exit compiler errors, preservation of prior outputs, packaging
refusal, and the isolated probe acceptance rules. Simulated APK/Xcode files are
test fixtures, not playable builds.
`Tests/SourceChecks/EditorCompilationChecks.cs` compiles limited API contracts and
targeted files; it is not a full-project compile against Unity assemblies.
`Assets/NewGaza/Editor/NewGazaSmokeTest.cs` is a separate real-Unity Play Mode check:
use the explicit [graphics-capable smoke runner](UNITY-SMOKE.md) for retained
results and timeout handling (including approval of local game-save effects), or
run `New Gaza → Run play mode smoke test` after compilation, in a graphics-capable
Editor. Headless compilation does not prove gameplay or visual correctness.

في بيئة Replit الحالية لا يوجد Unity Editor: اختبارات بروتوكول المشغّل ممكنة،
لكن **تجميع المشروع وحالات النجاح/الفشل داخل Unity لم تُشغّل هنا**.
شغّل الأوامر السابقة على جهازك الذي يحتوي على الإصدار المطابق. لا تُعتبر
اختبارات المصدر أو بروتوكول Python بديلًا عن تجميع Unity الحقيقي.

فحص الهاتف اختياري: حدّد `--target Android` لبناء APK/AAB فعلي،
أو `--target iOS` على macOS لتصدير مشروع Xcode. يلزم إصدار Unity المطابق
ووحدات البناء وأدوات المنصة. **لم يُشغّل بناء Android/iOS أو الحالات السلبية
الفعلية في هذه البيئة**. تصدير iOS لا يثبت تجميع Xcode أو توقيع IPA.
لن تثبّت الأدوات Unity أو تطلب بيانات التوقيع، ولن تُسلّم ملفات `.meta` جديدة.
