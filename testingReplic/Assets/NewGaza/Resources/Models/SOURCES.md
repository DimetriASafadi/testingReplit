# Imported city models

These are actual artist-authored meshes, not runtime-generated building primitives.
They are generic urban assets adapted for New Gaza; they are **not scans of Gaza,
surveyed buildings, or a reconstruction of a specific address**.

## Geometry sources — CC0 1.0

- **Apartment:** Quaternius, *Ultimate Textured Building Pack*, selected flat-roof
  four-storey building. Original meshes were textured with a muted concrete
  surface and reduced for mobile use.
  https://opengameart.org/content/lowpoly-buildings-pack
  https://quaternius.com/packs/ultimatetexturedbuildings.html
- **Ruined building and rubble:** Fleurman, *Destroyed City Assets*. The ruined
  building is extracted from an existing assembled building in the original
  FBX. The rubble uses the artist's broken slabs and concrete fragments,
  arranged into a separate static mesh asset.
  https://opengameart.org/content/destroyed-city-assets

Both source pages explicitly publish their downloadable model packs under CC0.
CC0 permits modification, commercial use and redistribution:
https://creativecommons.org/publicdomain/zero/1.0/

## Surface source — CC0 1.0

The concrete albedo uses Poly Haven's **concrete_wall_006** texture, baked into
the model UV atlases. The building meshes themselves are not photogrammetry.
https://polyhaven.com/a/concrete_wall_006
https://polyhaven.com/license

## Formats and preparation

- Native Unity resources: OBJ meshes and matching `*_albedo.png` textures.
- Prepared source meshes: `attached_assets/licensed_models` at the repository
  root; GLB files include their texture images. The source ZIP also includes
  these GLBs under `testingReplic/Design/ModelSources`.
- Reproducible preparation scripts: `tools/assemble_licensed_city_sources.py`
  and `tools/prepare_city_models.py` at the repository root.
- Actual optimized triangle counts, texture sizes and vertex attributes are
  recorded in the adjacent per-model `*_manifest.json` files.
- Preview PNGs are renders of the optimized meshes in Blender, **not Unity
  screenshots and not proof of device performance**.

Unity loads resources by name, applies the retained URP Lit material and batches
the meshes. The folder intentionally ships without `.meta` files; Unity creates
them on the user's machine. The scoped model postprocessor enables readable
meshes for runtime batching; do not disable Read/Write on these three models.