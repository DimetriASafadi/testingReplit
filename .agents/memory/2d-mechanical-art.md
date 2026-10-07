---
name: Mechanically consistent 2D artwork
description: Why vehicle animation uses offline articulated rendering and must be checked inside the city.
---

For multi-heading digging/loading animations, prefer rendering a single articulated source offline over generating each pose independently. Keep Phaser as the 2D runtime, not a second live 3D engine.

**Why:** Independent generated views can change vehicle identity and pivot geometry between frames. An articulated source keeps the chassis, bucket, payload and tipping hinge consistent, while packed sprites avoid a phone-side 3D model/rendering cost.

**How to apply:** Use a common orthographic view, light direction and grade; project actual tool endpoints and ground anchors into the sprite plane. Check bucket/bed contact in world coordinates and then inspect the playable city: foreground buildings can hide otherwise correct machinery. Preserve depth ordering and selectively reveal genuine occluders rather than drawing vehicles on top of roofs.
