---
name: Startup first paint
description: Keep the boot screen styled before Vite loads the game module.
---

Keep essential boot-screen layout and background styling in the initial HTML, not exclusively in module-imported CSS.

**Why:** On a slow mobile development connection, the logo loaded before the game module's stylesheet and appeared oversized on a white page. The user wants the logo only inside the opening loading screen.

**How to apply:** When changing boot layout, keep the initial HTML styles aligned with the full stylesheet; preserve the boot-complete hide behavior and do not add this logo to neighborhood loading overlays.

Keep pre-entry choices separate from expensive synchronous city rendering.

**Why:** On phones, terrain and plot preparation behind the difficulty dialog blocked the browser's main thread, making visible difficulty buttons seem unresponsive.

**How to apply:** Let the player confirm difficulty before starting city preparation. Recheck the current district's eligibility when deferred render callbacks execute, not just when they are scheduled.

For the shared Replit preview, prefer a watched bundled build over the unbundled development module graph.

**Why:** Users repeatedly remained on «تحضير المدينة» with Vite connection messages but no Phaser startup, while a fresh internal screenshot could succeed. An HTTP 200 or a single successful internal capture did not establish that the user's startup path worked. The bundled preview removes the large unminified dependency graph and the HMR connection from that path.

**How to apply:** Keep source changes rebuilding on the same preview URL, preserve local saves, and expose module startup errors/retry rather than an indefinite initial loading screen. Investigate the phase visible in the user's screenshot before attributing a preview problem to Android or restarting alone.
