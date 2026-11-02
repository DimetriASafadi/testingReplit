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

Final asphalt updates require dirty-region invalidation AND stable world-aligned road pieces. Merely yielding between tiles does not make a whole-city repaint cheap.

**Why:** The user reported another hitch at the final paving layer. A junction change altered merged-run signatures, which unnecessarily invalidated whole crossing streets even when only their end caps changed.

**How to apply:** Compare short stable pieces and actual diagonal footprints, retain unaffected GPU textures, split planning from raster/upload work, preserve pending dirty tiles when plans change, and retain the visible fresh mat until its replacement is ready.

Concurrent paving handovers belong to individual jobs, not to a global “some coat is pending” flag. An older returning machine must not recreate its retired street when a different machine finishes.

**Why:** The user specifically reproduced renewed stutter with several pavers working concurrently, after the single-street dirty-tile fix.

**How to apply:** Track fresh-mat ownership per job (including machine reuse), retain only existing owned mat during handover, and share a fair bounded raster-update budget across preparation/asphalt jobs. Deferred work uses current progress; stale/offscreen requests cannot block other machines. Verify successive completions with a third job still active, not only a single-machine fixture.

Treat a late-game saved district—not a new empty district—as the startup/rendering acceptance case, especially the user's Android save at 93% progress.

**Why:** The user reported that after force-closing and reopening the Android game at 93%, it stayed on “Preparing district ground.” A new-game screenshot does not exercise dense-save restoration.

**How to apply:** Preserve the existing save and cold-open a populated Android district through ground, roads, and plot restoration; require visible progress to reach gameplay. Keep loading interruptible between parcels and do not recommend clearing app data as a test fix.

Use the user's reported Android handset as the low-end reference: 4 GB RAM, a Helio processor up to 2 GHz, and Android 11.

**Why:** This is the phone where dense-district loading trouble was observed; desktop or high-memory tests cannot establish its behavior.

**How to apply:** Verify the runtime-selected low-memory profile on that handset when possible. If WebView hides device-memory information, treat the profile result as unconfirmed rather than assuming the 4 GB tier was selected.

Do not assume a stalled late-game load is purely a memory problem: validate art coverage across the whole catalog and all saved orientations.

**Why:** A mixed full-city fixture exposed incomplete view families that stopped scene creation during shadow generation. Empty-city checks never visited those subjects/angles.

**How to apply:** Distinguish recovery with an existing view of the same subject from genuinely completed four-face art. Report recovery explicitly, preserve the saved orientation and fixed sunlight, and never publish previously rejected staging images merely to fill missing slots.
