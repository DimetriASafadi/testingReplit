---
name: Browser clock testing
description: Why wall-clock browser tests must run after frontend edits have settled.
---

Run browser tests that override the browser clock only after frontend edits have settled.

**Why:** Vite hot reload can reset an injected clock while persisted job deadlines remain advanced, making construction appear stalled and invalidating an otherwise correct timer test.

**How to apply:** Keep fake clocks inside disposable test browser contexts, never in shipped game code; do not edit frontend files concurrently with time-advance browser checks.

CDP reload acknowledgement does not guarantee replacement of the old window. A readiness check can briefly observe the previous initialized scene.

**Why:** A factory-motion check observed the old scene, then attempted to inspect the replacement while its artwork was still loading.

**How to apply:** Wait for the document generation to change, then artwork loading and the new scene's readiness, before inspecting motion or capturing a screenshot.
