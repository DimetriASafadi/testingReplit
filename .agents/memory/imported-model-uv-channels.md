---
name: Imported model UV channels
description: Avoiding texture corruption when converting multi-UV source meshes into native Unity OBJ assets.
---

When converting an imported model to an OBJ with one albedo texture, export the
UV set actually referenced by that texture. Do not assume the first UV layer is
the correct one.

**Why:** Baked source GLBs retained original UVs in channel zero and referenced
the new albedo atlas in channel one. Exporting channel zero still produced valid
vertices, triangle indices and UV counts, but the texture mapping was wrong.
Geometry checks alone did not reveal this.

**How to apply:** Inspect the source texture's UV reference, select that map for
OBJ export, and verify the optimized mesh with its exported albedo. For a
single-material atlas, removing unused source UV layers and exporting the atlas
as channel zero simplifies later imports. Keep material-preview acceptance
separate from triangle-budget and source-code checks.