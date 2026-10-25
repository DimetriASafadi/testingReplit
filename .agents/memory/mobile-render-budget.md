---
name: Mobile render budget
description: Preserve world-space saves while improving high-density phone rendering and controlling procedural memory.
---

Keep saved camera zoom in logical world units, independent of framebuffer pixel density.

**Why:** Phone sharpness requires a larger backing canvas, but storing the physical zoom would change the same saved view when moving between different display densities or future quality settings.

**How to apply:** Convert at camera restore/save and screen-space HUD boundaries. Test touch selection, rotation and reload together whenever changing pixel density; increasing framebuffer resolution alone is not sufficient.

Treat completed procedural construction/clearing stages as disposable, but never evict a texture referenced by a live scene image.

**Why:** Long sessions can retain obsolete stage canvases and GPU copies. Making all artwork lower-resolution hides the memory problem by degrading visual quality.

**How to apply:** Prefer bounded working sets over globally blurred sprites. Verify actual Android/WebGL behavior separately from a desktop Canvas test.
