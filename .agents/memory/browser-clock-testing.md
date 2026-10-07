---
name: Browser clock testing
description: Why wall-clock browser tests must run after frontend edits have settled.
---

Run browser tests that override the browser clock only after frontend edits have settled.

**Why:** Vite hot reload can reset an injected clock while persisted job deadlines remain advanced, making construction appear stalled and invalidating an otherwise correct timer test.

**How to apply:** Keep fake clocks inside disposable test browser contexts, never in shipped game code; do not edit frontend files concurrently with time-advance browser checks.
