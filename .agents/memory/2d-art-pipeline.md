---
name: 2D artwork pipeline ordering
description: Keep source baselines, grading and ground projection consistent during sprite regeneration.
---

Pack newly authored sprites first, grade their colors second, align planted scenery's ground third, and feather ground seams last. Refresh a stage's source baseline only from the preceding stage's fresh output, never from its own processed result. Grading/projection baselines start with newly packed, unaligned artwork; the final seam baseline starts with already graded/aligned geometry.

**Why:** Color grading restores a separate source baseline. Running it after ground alignment can restore the old projection while leaving the newer contact metadata in place. Reusing stale geometry baselines also discards newly authored art.

**How to apply:** Treat artwork and its ground-contact metadata as one export. Correct planted bases offline while retaining upper canopy proportions; do not add per-frame image warping or pixel readback to fix asset perspective. After regeneration, verify both orientations in the actual city, not only the isolated image.

Ground overlays must be below the shared cast-shadow layer, not sorted individually with buildings.

**Why:** Per-parcel ground depth can cut a neighboring tall building's projected shadow at the parcel boundary, making disconnected shadow fragments.

**How to apply:** Keep architectural/foreground occlusion separate from ground compositing; inspect shadows across empty adjacent land as well as under their own building.

Use bounded background-cleanup batches, collect individual results, and retry only failed images.

**Why:** A wide simultaneous cleanup batch returned rate-limit errors while most images succeeded; failing the aggregate hid those successful results.

**How to apply:** Retain returned file paths and handle errors per image rather than regenerating the entire batch.
