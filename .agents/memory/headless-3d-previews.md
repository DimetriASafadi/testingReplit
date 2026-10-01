---
name: Headless 3D previews
description: Why Blender asset previews need a CPU renderer in this workspace.
---

Use Cycles CPU directly for Blender previews when the workspace lacks a working
GLX/EGL graphics context. Do not initialize Eevee first and rely on a Python
exception handler to fall back.

**Why:** Eevee initialization in this workspace aborted the native process inside
the graphics dispatcher. Python could not catch that failure. CPU Cycles rendered
the same optimized imported assets successfully.

**How to apply:** Check graphics-context availability before choosing a preview
renderer. In this headless environment, use bounded CPU samples and thread counts.
Validate mesh/texture exports separately from preview rendering, and identify
offline renders as such; they do not establish Unity rendering or device performance.