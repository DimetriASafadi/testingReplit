---
name: Unity editor verification
description: Why native source fixtures alone missed Unity editor compilation errors.
---

Native workspace migrations must distinguish incomplete local export fixtures from regressions.

**Why:** The source-only backup lacked authored housing and production-placement exports; the unchanged native checks failed identically before and after restoration.

**How to apply:** Compare failures against the original backup before editing tests or native source. Restore genuine local inputs separately; do not invent fixture data or report a full native pass from narrower checks.

Do not treat a passing runtime fixture or syntax parser as evidence that editor smoke checks compile.

Do not infer that an optional JSON header was supplied merely because its deserialized reference is non-null.

**Why:** Startup rejected the correct exported basemap origin, while the loader preferred a legacy origin object over supplied metadata. Materializing an absent legacy object with zero-valued fields reproduces this failure in the fixture; the previous .NET serializer left it null and missed it.

**How to apply:** Select compatibility layouts from properties actually present in the input. Exercise default-valued absent objects in JSON fixtures, preserve explicitly invalid-value rejection, and verify real Unity serialization separately before claiming an engine-tested fix.

**Why:** The user's Unity console reported inaccessible internal runtime members, missing scalar overloads and an obsolete audio-importer API even though the source checks and equipment fixture passed. The fixture compiled runtime sources together, unlike Unity's separate runtime and editor assemblies.

**How to apply:** Preserve the runtime/editor assembly boundary in targeted compilation checks, including actual visibility and signatures. Model obsolete APIs as compiler errors, not warnings to suppress. Keep access limited to the intended editor assembly rather than making all runtime internals public. Minimal API contracts can catch known regressions but are not a substitute for a full compilation against the user's Unity version.

UI refactors need semantic checks, not syntax checks alone; callback compatibility and identifier scope are both compiler concerns.

**Why:** Syntax-only checks missed both a callback conversion error and a stale local-variable reference after activity counting moved into a partial-class helper. The user's Unity compiler rejected them.

**How to apply:** Mirror native event delegate types and actual partial-class field declarations in focused regression contracts. Compile the real changed expressions and include a negative control for the reported error; do not infer compatibility or scope correctness from a passing parser.

Passing economy tests does not establish that a Unity action button responds to input.

**Why:** The user reported that «إزالة الدمار» did nothing despite passing salvage-domain tests; those tests did not exercise the click-to-controller path or overlapping UI layers.

**How to apply:** Exercise original click and dispatch methods rather than copying their logic into tests. Check silent disabled states, visible rejection feedback, and canvas/raycast ordering. Still distinguish this bounded coverage from real Unity pointer-input verification.

Keep source-only delivery possible when the workspace lacks a licensed Editor, but label it unverified; verified delivery must require a fresh real-Editor compilation.

**Why:** This project is authored in an environment without Unity. Requiring Editor access for all source exports would block delivery, while calling a source export verified would repeat the previous missed compiler regressions.

**How to apply:** Distinguish runner protocol tests, isolated real-Unity compiler probes, full-project compilation, and Play Mode/device checks in reports. Never describe one as evidence that another passed.

Keep asynchronous Play Mode smoke runs separate from the headless compilation gate,
and obtain explicit approval of local game-save effects before batch smoke runs.

**Why:** Unity's executeMethod returns before the Play Mode checks finish, so
compilation's quit/no-graphics flags invalidate smoke evidence. The checks avoid
deliberate purchases/rewards, but ordinary game startup ticks elapsed economy and
writes persistent saves, including on failing runs.

**How to apply:** Require matching real-Editor/graphics evidence and retain failure
and timeout logs. Back up saves on the approved machine; a project copy alone does
not isolate Unity persistentDataPath. Report unavailable Editor runs as NOT RUN.

Ensure custom .NET fixture project definitions are tracked despite Unity's generated-project ignore rules.

**Why:** A configured regression workflow lost its development-test project definition while its source files remained; that project had existed only as an ignored local file.

**How to apply:** Check tracking when creating fixture projects. Preserve Unity's normal generated-file exclusions; explicitly add the custom test project instead of relying on an untracked local build definition.

A non-null Input System mouse is not evidence that its current position is usable.

**Why:** The user's Unity Game view repeatedly supplied `inf, -inf` to screen ray projection while the mouse device existed. A .NET source fixture does not reproduce real device/window samples.

**How to apply:** Validate finite positions and the active camera's actual pixel viewport before projection and UI raycasts. Cancel an interrupted gesture rather than treating invalid input as a release/tap at the last valid position.