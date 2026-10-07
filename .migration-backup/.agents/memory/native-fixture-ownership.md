---
name: Native fixture ownership
description: Avoid misleading native mesh tests when selection, material batches and hierarchy destruction change.
---

Shared primitive meshes must outlive individual factory/equipment rigs that borrow them.

**Why:** Instanced moving parts share primitive geometry across disposable visuals; removing one rig must not destroy other rigs or the rest of the city.

**How to apply:** Release unique generated batches per visual; release borrowed primitives and shared materials only when their geometry owner is disposed.

Native rendering fixtures must model destruction of hierarchy nodes and distinguish static surface meshes from independent selection geometry.

**Why:** No-op destruction retained old overlays in the fixture, while an aggregate mesh comparison incorrectly treated a new selection highlight as a static-road rebuild. More paving legitimately adds curb and marking batches, so equal counts across different material states do not prove ownership.

**How to apply:** Check old object identities disappear after invalidation, settled-state identities stay stable, destroyed nodes leave the hierarchy, and batch bounds follow the actual chunk/material combinations.

Evaluate tiny surface-triangle orientation using local, translated coordinates and sufficient numerical precision.

**Why:** At geographic city coordinates, global single-precision area products cancelled for thin crack markings, producing downward-facing triangles that a small origin-centered sample did not reveal.

**How to apply:** Include the full sourced map in winding checks, not just a synthetic road near the origin. Inspect emitted triangles rather than relying on a polygon-wide area sign.

Parked-track fixtures must wait for heading as well as position to settle.

**Why:** A crawler can have zero translation while its smooth heading is still turning; differential track-chain motion during that turn is correct, not idle creep.

**How to apply:** Check settled rotation before comparing stationary shoe vertices. Do not use a fixed short wait as proof that the complete chassis pose has stopped.