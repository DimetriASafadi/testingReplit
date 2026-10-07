# Realistic art pipeline
- Raw generated sprites: `art-src/raw/*.png` (git-ignored masters). Run `python3 tools/pack_art.py` to trim/scale to WebP in `public/art/realistic/s` and write manifest (`ay` = ground-footprint anchor).
- Ruins come from parent `ruins-sheet.png` (12 variants); ground from `ground-{earth,sand,asphalt,gravel,water}.jpg`.
- `src/realart.ts` maps every catalog id to its own front/back sprite (`<key>_front|_back`); farms have true entrance/rear variants.
- Foundation render sets: house_c, apt_c, ind_c, tower_c, shop_c.
- `src/construction-art.ts` composes stages lazily from the chosen building's actual front/back render: foundation, growing unpainted shell, then facade finishing with scaffolding. Rear construction uses the real rear render, never a mirrored front. Farms progressively plant strips of their own field/orchard instead of fading in a finished icon. These are composited construction visuals, not separate hand-authored animation sheets for every building.
- Workers: `src/workers.ts` rig at scale .34 (about 20px). Vehicles remain procedural.
- QA harness (in-memory, no saves): `/render-check.html?mode=ruins|built|back|build&zoom=&x=&y=`.
## Limitations
- Foundation imagery is shared within structural classes; shell/finishing geometry and agricultural planting use each individual building's render.
- Vehicles still procedural; art.ts building draw code unused.
- Generated front/rear variants are distinct renders rather than rotations of a single 3D model; very fine architectural details can differ between views.
- This is original game art inspired by local buildings, not surveyed documentation or a pixel-perfect reproduction of the reference.

## Environment cohesion
- The rendering target is comfortable, semi-realistic **game illustration**, not a photograph or isolated cutout collage.
- `tools/harmonize_art.py` matches black/highlight ranges, saturation and fine texture across terrain and all building renders without changing alpha footprints. Local baselines are ignored. Run normally for deterministic reprocessing; after replacing/packing ungraded source sprites, use `--refresh-baseline` to update the authoring baseline.
- `tools/pack_environment.py` crops twelve original AI-generated, transparent ground/curb/rubble decals. `src/environment-art.ts` keeps their ground anchors consistent.
- Lot skirts combine fine earth, rubble and ground decals with a feathered alpha mask. Ruined skirts disappear upon clearing; clean/construction/completed sites use their own lighter surroundings. Road centres remain open rather than being covered by opaque rubble patches.
- Rounded block corners, broken curbs, quiet cleared-land markings, smaller contact shadows and a continuous earth/coast surface replace hard square pads and the bounded land diamond.
- Smaller visual footprints and capped tall-remnant scale preserve street space. Gameplay coordinates, plot selection, timers and saves are unchanged.
- Browser art fixtures reset their own wall-clock timestamp: a previous accelerated regression run must not silently complete the visual construction fixtures.
