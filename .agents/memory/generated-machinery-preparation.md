---
name: Generated machinery preparation
description: Limits of fused AI machinery exports and the checks needed before promising articulation or restored textures.
---

Treat semantic separation of a fused generated machine as an articulation draft,
not a complete mechanical rig. Track assemblies do not become cycling tracks,
and visible cylinders do not become telescoping mechanisms just because the
main arm meshes have been split.

**Why:** A Meshy parts-oriented prompt still produced one connected excavator;
moving the main meshes cannot make geometry welded across hydraulic joints
behave like independent mechanisms.

**How to apply:** Inspect the real mesh topology and joint ranges, distinguish
major-part pivots from functioning hydraulics/track motion, and do not replace
working gameplay equipment until the prepared appearance and mechanics are approved.

Closed cut surfaces alone are not a sufficient mesh-quality check.

**Why:** Intersecting part cuts can produce coincident caps/non-manifold edges
even when no open boundary is visible; polygon reduction can preserve them.

**How to apply:** Verify manifold topology and degeneracy after preparation and
again after export. Repair intersecting junctions before accepting the asset.

If the source export has no UVs, request the textured model, not only loose
texture images, when preserving its original appearance matters.

**Why:** Replacement UVs cannot recover the original image-to-surface mapping.

**How to apply:** Ask for a textured FBX with its images or a textured GLB.
Clearly label new solid-color materials as replacements.
