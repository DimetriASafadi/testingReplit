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

Treat a late-game saved district—not a new empty district—as the startup/rendering acceptance case, especially the user's Android save at 93% progress.

**Why:** The user reported that after force-closing and reopening the Android game at 93%, it stayed on “Preparing district ground.” A new-game screenshot does not exercise dense-save restoration.

**How to apply:** Preserve the existing save and cold-open a populated Android district through ground, roads, and plot restoration; require visible progress to reach gameplay. Keep loading interruptible between parcels and do not recommend clearing app data as a test fix.

Do not assume a stalled late-game load is purely a memory problem: validate art coverage across the whole catalog and all saved orientations.

**Why:** A mixed full-city fixture exposed incomplete view families that stopped scene creation during shadow generation. Empty-city checks never visited those subjects/angles.

**How to apply:** Distinguish recovery with an existing view of the same subject from genuinely completed four-face art. Report recovery explicitly, preserve the saved orientation and fixed sunlight, and never publish previously rejected staging images merely to fill missing slots.
