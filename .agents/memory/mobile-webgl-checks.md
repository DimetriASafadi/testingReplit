---
name: Mobile WebGL checks
description: Why Canvas-only city screenshots miss mobile GPU limits and how to avoid false reload readiness.
---

Do not treat a successful headless Canvas screenshot as proof that the same city will render on mobile WebGL.

**Why:** Expanded terrain can exceed a phone GPU's maximum texture size, while Canvas still renders it successfully. Earlier desktop checks therefore did not expose a credible cause of the user's blank district after reset.

**How to apply:** Verify the actual renderer, exercise a realistic GPU texture ceiling, and inspect the rendered city—not just object counts. Test fresh entry/reset as well as an already populated art fixture. Keep terrain artwork and its world coordinates intact when addressing device limits.

A reload's readiness must belong to the new document.

**Why:** The reload command can return while the old document still reports a ready scene; a readiness assertion can pass immediately and then lose that scene as navigation completes.

**How to apply:** Wait for a changed document/loader identity before checking scene readiness after reload.
