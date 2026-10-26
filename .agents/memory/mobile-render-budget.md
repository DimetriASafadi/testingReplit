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

Procedural ground performance must account for replayed draw commands, not only object counts or texture allocation counts.

**Why:** The paving machine was associated with severe phone slowdowns even with a bounded number of actors: retained Graphics can replay thousands of grain/ellipse commands each render. Static-looking terrain is not automatically cheap.

**How to apply:** Separate ground-detail refresh from smooth vehicle/effect animation. Prefer bounded, mobile-sized raster sections for slowly changing surfaces; keep effects local to active machinery and verify cleanup across repeated jobs and district changes.

Treat a late-game saved district, not a new empty district, as the startup/rendering acceptance case.

**Why:** The user reported progressive slowdown and loading hangs after extensive rebuilding. A quick new-game screenshot does not exercise accumulated courtyard variants, income indicators, agricultural animation or bulk reconstruction of a save.

**How to apply:** Exercise a filled, mixed-building district through rotation, reopening and scene rebuild. Keep loading work interruptible between parcels; preserve original logical anchors when trimming transparent texture space. Limit decorative animation work independently of how many finished buildings are owned, without changing gameplay deadlines or removing their base artwork.

Do not assume a stalled late-game load is purely a memory problem: validate art coverage across the whole catalog and all saved orientations.

**Why:** A mixed full-city fixture exposed incomplete view families that stopped scene creation during shadow generation. Empty-city checks never visited those subjects/angles.

**How to apply:** Distinguish recovery with an existing view of the same subject from genuinely completed four-face art. Report recovery explicitly, preserve the saved orientation and fixed sunlight, and never publish previously rejected staging images merely to fill missing slots.
