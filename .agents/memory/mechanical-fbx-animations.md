---
name: Mechanical FBX animation stacks
description: Why articulated multi-object machines need synchronized animation exports rather than one take per moving part.
---

Do not assume equally named per-object NLA strips become one synchronized FBX machinery clip.

**Why:** Blender's FBX exporter creates a separate animation stack for each NLA strip. For an object-hierarchy machine, that separates the boom, turret, bucket, pistons and truck bed into independent takes instead of a complete working cycle.

**How to apply:** Export the whole hierarchy's active actions together in one scene take, with separate captured clip ranges staged consecutively and documented for splitting, or use a properly synchronized skeletal rig. Verify the imported take duration and articulated channels, not just the presence of animation keys.

Transform samples do not capture track shoes animated by in-place mesh-vertex updates.

**Why:** The native track rig deforms a reused mesh; a transform-only FBX snapshot retains that mesh's captured shape. The source game's procedural motion remains the complete implementation.

**How to apply:** Distinguish editable geometry/transform clips from full native runtime animation. If a future deliverable requires standalone continuous track animation, explicitly export deformation or authored track-pad transforms rather than claiming local TRS alone preserves it.