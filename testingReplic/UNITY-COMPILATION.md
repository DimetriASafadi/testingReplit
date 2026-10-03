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
```

Compilation is run afresh before touching any existing ZIP or delivery parts.
Failed/unavailable Unity refuses packaging and leaves existing exports unchanged;
do not mistake a previous ZIP for a newly verified one.
Without `--unity`, packaging remains available for source-only delivery but emits
an explicit **UNVERIFIED** warning. A source ZIP is not a compiled game.
Imported tracked `.meta` files are preserved; newly generated metadata and Unity
caches are excluded. No new `.meta` files should be committed.

## Verification boundaries

```sh
python3 -m unittest discover -s tools/tests -p 'test_unity_compile.py' -v
dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release
```

Python tests simulate the process protocol (paths with spaces, pass/failure,
nonzero exits, zero-exit compiler errors, stale/missing/invalid result, wrong
version, merged assemblies, timeout, unavailable Editor and packaging refusal).
**They are not a Unity compilation.**
`Tests/SourceChecks/EditorCompilationChecks.cs` compiles limited API contracts and
targeted files; it is not a full-project compile against Unity assemblies.
`Assets/NewGaza/Editor/NewGazaSmokeTest.cs` is a separate real-Unity Play Mode check:
run `New Gaza → Run play mode smoke test` after compilation, in a graphics-capable
Editor. Headless compilation does not prove gameplay or visual correctness.

في بيئة Replit الحالية لا يوجد Unity Editor: اختبارات بروتوكول المشغّل ممكنة،
لكن **تجميع المشروع وحالات النجاح/الفشل داخل Unity لم تُشغّل هنا**.
شغّل الأوامر السابقة على جهازك الذي يحتوي على الإصدار المطابق. لا تُعتبر
اختبارات المصدر أو بروتوكول Python بديلًا عن تجميع Unity الحقيقي.