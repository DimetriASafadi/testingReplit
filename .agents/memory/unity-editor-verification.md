---
name: Unity editor verification
description: Why native source fixtures alone missed Unity editor compilation errors.
---

Do not treat a passing runtime fixture or syntax parser as evidence that editor smoke checks compile.

**Why:** The user's Unity console reported inaccessible internal runtime members, missing scalar overloads and an obsolete audio-importer API even though the source checks and equipment fixture passed. The fixture compiled runtime sources together, unlike Unity's separate runtime and editor assemblies.

**How to apply:** Preserve the runtime/editor assembly boundary in targeted compilation checks, including actual visibility and signatures. Model obsolete APIs as compiler errors, not warnings to suppress. Keep access limited to the intended editor assembly rather than making all runtime internals public. Minimal API contracts can catch known regressions but are not a substitute for a full compilation against the user's Unity version.

Keep source-only delivery possible when the workspace lacks a licensed Editor, but label it unverified; verified delivery must require a fresh real-Editor compilation.

**Why:** This project is authored in an environment without Unity. Requiring Editor access for all source exports would block delivery, while calling a source export verified would repeat the previous missed compiler regressions.

**How to apply:** Distinguish runner protocol tests, isolated real-Unity compiler probes, full-project compilation, and Play Mode/device checks in reports. Never describe one as evidence that another passed.