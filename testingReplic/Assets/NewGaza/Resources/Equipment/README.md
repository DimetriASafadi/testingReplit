# Meshy excavator trial

The original excavator has NOT been removed or overwritten. Its full procedural
construction and joint rig are retained by `CityFleet`. Existing equipment FBX
files in `Art/EquipmentFBX` are also untouched.

## Install on your Unity PC

1. Update the project's source from GitHub.
2. Extract `Meshy-Excavator-Game-Art.zip` into the Unity project directory
   (the directory containing `Assets`, `Packages` and `ProjectSettings`).
3. Confirm this file exists:
   `Assets/NewGaza/Resources/Equipment/MeshyExcavator.bytes`.
4. Open Unity, allow it to import the files and generate its own `.meta` files.
5. Select **New Gaza / Equipment / Use Meshy excavator**, then restart Play mode.

The source setting initially selects `meshy`. If the art file is missing, the
game reports a clear installation error rather than silently showing the old
model. The `.bytes` file contains the actual prepared Meshy vertices, normals
and UVs, split into seven physical parts / ten material meshes. It is not an
image, placeholder, or replacement for gameplay logic.

## Return to the original

Select **New Gaza / Equipment / Use original excavator**, then restart Play mode.
Alternatively change `ExcavatorAppearance.json` to:

```json
{ "model": "original" }
```

No save reset or file deletion is necessary. Both choices use the same saved
vehicle poses, work timing, routes, joint chain and carried-rubble behavior.
Future source updates can update the setting file; re-select your desired model
if that happens.

## Current limitations

- 19,500 visible triangles, ten material meshes, shared between fleet teams.
  This is a mobile-oriented art budget, **not** a measured phone FPS result.
- Arm and bucket surfaces are retargeted to the existing game's joint lengths
  and contact tip. Proportions / neutral orientations differ from the standalone
  FBX review version.
- Track assemblies travel with the vehicle but do not yet circulate shoes.
- Original logical track and hydraulic rigs remain underneath with their
  surfaces hidden. Hydraulic details fused into the Meshy surfaces are not a
  complete telescoping mechanism.
- The uploaded FBX had no materials, texture images or UVs. The prepared version
  has generated UVs and four plain URP materials, not original Meshy textures.
- Native fixture checks and CPU proof renders do not verify Unity import,
  on-device rendering, performance, or full mechanical articulation.

Heavy art stays out of Git. Rebuild the derived asset with:
`blender -b --python tools/export_meshy_game_asset.py`
after preparing the standalone model with `tools/prepare_meshy_excavator.py`.
