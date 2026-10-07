---
name: Mechanical contact checks
description: Validating articulated equipment motion against real mesh contact points and the city's map scale.
---

Author equipment poses from transformed contact targets, then validate the entire cycle against the actual hierarchy and production geometry.

**Why:** A source fixture can pass extensive finite-geometry, envelope and joint-angle checks while a bucket digs above the ground, penetrates it when lifting, or releases below and outside the truck bed. Likewise, distance-derived wheel rotation does not prove realistic movement when map-unit travel speed is interpreted as metres per second.

**How to apply:** Test cutting teeth against the ground, release points against the bed interior and wall height, and cargo hand-off against the receiving truck's current position. Sample between keyframes, not only at keys. Track surfaces must visibly move during travel and stay still when parked. Convert geographic speeds using the 20-metres-per-unit map scale; presentation motion must not accelerate to match economy timers.

Measure machine support and receiving surfaces from actual generated vertices and authored surface transforms, not legacy root-height constants or nominal tyre radii. Require nondegenerate triangles and solid shoe volume as well as valid bounds.

**Why:** Old placement offsets can float wheels above a work surface, while an unrelated raised depot apron can bury tracks. A mesh can also have correct counts and finite coordinates while its thickness axis is parallel to its travel tangent, collapsing it to a sheet.

**How to apply:** Check parked, local-work and imported-work feet against their actual support surfaces. After geometry changes, derive root clearances and bucket targets together rather than raising the truck or rig independently.

Advance contact fixtures through the production clock only after establishing the actual loading dock state.

**Why:** Dock-gated machinery deliberately holds its dig clock when the truck is absent; assigning a nominal phase or stepping a held clock tests the waiting pose instead of the intended contact pose.

**How to apply:** Assert the receiver is docked, step real update delta time, and assert clock advancement before measuring transformed tooth vertices.

Exercise the configured production transport path as well as any compatibility-only fixture.

**Why:** Introducing street routing initially moved the configured receiver to the excavator's center while the older unconfigured contact tests still passed. A correct path alone does not preserve a calibrated loading dock.

**How to apply:** Measure actual receiving-bed contact after the configured fleet arrives through the sourced network. Keep access routing separate from the authored mechanical dock arrangement.

Check geometric role ownership as well as valid meshes and joint motion when separating welded machinery.

**Why:** An arm/body half-space cut on an image-reconstructed excavator assigned front track fragments to the arm assembly. Finite geometry, closed cut surfaces and a moving hierarchy all passed; the posed render revealed the misplaced undercarriage.

**How to apply:** Protect the stationary undercarriage before arm segmentation, assert that its source vertices never enter arm/dipper/bucket parts, inspect posed renders, and verify measured movement after FBX re-import rather than merely counting animation curves.