---
name: 2D artwork pipeline ordering
description: Keep source baselines, grading and ground projection consistent during sprite regeneration.
---

## Extracting icons from composite UI references

Do not identify the foreground solely by its color when separating an icon from its badge and counter panel. Tinted metal reflections can share the badge's hue.

**Why:** Automatic background removal can preserve pieces of the adjacent numeric panel, while color-only cleanup can erase legitimate highlights and parts of the object.

**How to apply:** Inspect the complete object silhouette on a contrasting background before integrating it. Separate the object from the panel artwork without transferring reference numbers, and preserve its highlights and shaded edges.

## City artwork

Pack newly authored sprites first, grade their colors second, align planted scenery's ground third, and feather ground seams last. Refresh a stage's source baseline only from the preceding stage's fresh output, never from its own processed result. Grading/projection baselines start with newly packed, unaligned artwork; the final seam baseline starts with already graded/aligned geometry.

**Why:** Color grading restores a separate source baseline. Running it after ground alignment can restore the old projection while leaving the newer contact metadata in place. Reusing stale geometry baselines also discards newly authored art.

**How to apply:** Treat artwork and its ground-contact metadata as one export. Correct planted bases offline while retaining upper canopy proportions; do not add per-frame image warping or pixel readback to fix asset perspective. After regeneration, verify both orientations in the actual city, not only the isolated image.

Ground overlays must be below the shared cast-shadow layer, not sorted individually with buildings.

**Why:** Per-parcel ground depth can cut a neighboring tall building's projected shadow at the parcel boundary, making disconnected shadow fragments.

**How to apply:** Keep architectural/foreground occlusion separate from ground compositing; inspect shadows across empty adjacent land as well as under their own building.

Use bounded background-cleanup batches, collect individual results, and retry only failed images.

**Why:** A wide simultaneous cleanup batch returned rate-limit errors while most images succeeded; failing the aggregate hid those successful results.

**How to apply:** Retain returned file paths and handle errors per image rather than regenerating the entire batch.

Background cleanup returns the actual output path; a repeated requested filename may produce a suffixed new file rather than overwrite the old one.

**Why:** Reviewing the requested name after a successful retry can show stale artwork, making a corrected output appear unchanged.

**How to apply:** Follow the returned path, then replace the canonical staging file explicitly before review or packing.

Generative multi-view sheets are not reliably divided into equal quadrants, and successful packing does not prove a real quarter-turn or the correct camera.

**Why:** Real subjects crossed nominal gutters, while other outputs fit their boxes but repeated the front or switched farms to overhead/pentagonal ground. Forcing dimensions cannot correct those projections.

**How to apply:** Separate subjects by actual components/gutters, reject real source-edge clipping, and inspect camera and physical identity independently of numeric checks. Never publish a rejected view just to fill the requested count.

Use one texture-limit normalization and one world render scale for a building's entire view family.

**Why:** Independent height caps can change the building's apparent footprint while rotating, even when source ground dimensions were equal.

**How to apply:** Bound the largest member without stretching axes, retain equal ground metrics across the quartet, and verify physical size in the live city.

For uploaded UI icon sheets, preserve the supplied illustration but keep small navigation captions as live Arabic text rather than shrinking the baked-in caption with the artwork.

**Why:** Six toolbar actions share a narrow phone screen; raster captions shrink below readable size, while native captions retain the game's chosen font and accessible button names.

**How to apply:** Extract transparent, consistently padded artwork; retain the existing action identifiers and captions. Verify portrait and short landscape layouts, including nearby floating controls, not just whether the images loaded.

Match the visible size of toolbar illustrations, not just their CSS image dimensions, and keep disabled-action artwork recognizable.

**Why:** The income collection icon looked larger than adjacent artwork despite equal image dimensions because its circular background filled its entire box. Fading the whole disabled button also made the illustration unclear.

**How to apply:** Account for background and transparent padding when comparing icon families. Preserve actual disabled behavior, distinguish inactivity with the caption, and let long Arabic captions wrap on narrow phones rather than shrinking them below a readable size.
