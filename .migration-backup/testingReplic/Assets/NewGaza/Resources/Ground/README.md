# Semi-realistic ground art

Three generated, processed seamless albedo images:

- `UrbanGround_Albedo.png`: dusty neutral urban soil / crushed limestone.
- `CoastalSand_Albedo.png`: pale Mediterranean coastal sand.
- `DryGround_Albedo.png`: dry soil with sparse sage/olive ground cover.

These are illustrative game materials, not satellite imagery or a ground survey.
No map geometry, roads, neighborhood locations, saves or access rules change.

## Install on your Unity PC

1. Pull the source update from GitHub.
2. Extract `NewGaza-Ground-Art.zip` into the Unity project ROOT (beside `Assets`,
   `Packages` and `ProjectSettings`, not inside `Assets`).
3. Confirm the three PNGs are at `Assets/NewGaza/Resources/Ground/`.
4. Allow Unity to import them and create its own `.meta` files.
5. Restart Play mode.

The shader is source-controlled at `Resources/NewGazaGround.shader`.
The texture importer sets repeating wrap, mipmaps, trilinear filtering, maximum
1024 resolution and compressed Android/iPhone formats. Source images are not
CPU-readable in player builds. No normal maps, giant map-sized image, new ground
meshes or per-building renderers are required.

City floor, beach and mapped open land use their own materials. Each tile covers
eight real metres at the basemap projection scale; this does not depend on the
size or local UVs of a parcel. A second, broader texture sample and gentle
world-space variation reduce obvious repetition. Ground receives the main
directional light and its realtime shadows when enabled in the project's URP.

Missing art or shader produces a Console warning and keeps the previous ground
material instead of stopping city initialization.

The PNGs stay out of Git. Regenerate the processed files / review sheet using
`tools/prepare_ground_textures.py` after obtaining the generated source images.

Native fixture checks and offline previews do not prove Unity shader compilation
or phone performance. Inspect both near and far zooms in Unity on your PC.
