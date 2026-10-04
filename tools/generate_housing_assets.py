#!/usr/bin/env python3
"""Author and export the ten intact New Gaza housing archetypes in four stages.

Run with Blender 4.4:
  blender --background --threads 2 --python-exit-code 1 --python tools/generate_housing_assets.py --

All meshes and their shared albedo atlas are generated locally and deterministically.
"""

import importlib.util
import json
import math
import shutil
import sys
from array import array
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
RESOURCE_DIR = ROOT / "testingReplic/Assets/NewGaza/Resources/Models"
FBX_DIR = ROOT / "testingReplic/Assets/NewGaza/Art/HousingFBX"
MANIFEST_PATH = ROOT / "exports/housing-assets/Manifest.json"
ATLAS_PATH = FBX_DIR / "HousingSharedAtlas.png"
ATLAS_SIZE = 512
GRID_X, GRID_Y = 4, 2
TILE_COLORS = [
    (0.76, 0.68, 0.54),  # warm ivory plaster
    (0.66, 0.53, 0.37),  # buff limestone
    (0.62, 0.20, 0.105),  # terracotta
    (0.105, 0.23, 0.31),  # blue-black glazing
    (0.57, 0.55, 0.48),  # reinforced concrete
    (0.53, 0.32, 0.21),  # warm brick
    (0.32, 0.35, 0.31),  # dark metal
    (0.32, 0.42, 0.26),  # planted green
]
TILES = {"plaster": 0, "stone": 1, "terracotta": 2, "glass": 3,
         "concrete": 4, "brick": 5, "metal": 6, "plant": 7}
ARCHETYPES = [
    ("house_small_redtile", "Small red-tile house", 1, 8.5, 7.0, "Compact one-storey home with a pitched terracotta roof."),
    ("house_cream_family", "Cream family house", 3, 10.5, 8.5, "Three-storey warm plaster family house with roof service box."),
    ("house_modern_villa", "Modern villa", 3, 12.0, 10.0, "Three-storey modern villa with offset volumes and recessed glazing."),
    ("apartment_4floor_balcony", "Four-floor balcony apartment", 4, 10.0, 8.5, "Four-storey apartment with repeating open balconies."),
    ("apartment_6floor_balcony", "Six-floor balcony apartment", 6, 11.0, 9.0, "Six-storey apartment with a strong balcony rhythm."),
    ("house_compound", "Courtyard compound", 3, 15.0, 13.0, "Two house wings linked around an enclosed planted courtyard."),
    ("apartment_blueglass_midrise", "Blue-glass midrise", 6, 12.0, 9.0, "Six-storey contemporary midrise with a vertical blue glazed core."),
    ("house_traditional_stonearches", "Traditional stone-arch house", 3, 11.5, 9.5, "Three-storey buff limestone house with deep arched openings."),
    ("apartment_12floor_tower", "Twelve-floor slim tower", 12, 10.0, 8.0, "Slim twelve-storey residential tower with continuous balconies."),
    ("house_coastal_white_pool", "Coastal white villa", 3, 13.0, 11.0, "Stepped white coastal villa with roof terraces and an inset pool."),
]
STAGES = (
    ("foundation", "Ground footings, connected grade beams, and low foundation walls."),
    ("frame", "Clean reinforced-concrete columns and floor slabs; no damaged or broken members."),
    ("finishing", "Partially completed masonry infill, selective plaster, scaffold, and incomplete roof/windows."),
    ("final", "Completed detailed architecture, installed openings, roof services, and compact landscaping."),
)


def load_builder():
    path = ROOT / "tools/generate_destroyed_assets.py"
    spec = importlib.util.spec_from_file_location("destroyed_asset_helpers", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.ATLAS_TILES = dict(TILES)
    return module


HELPERS = load_builder()
MeshBuilder = HELPERS.MeshBuilder


def create_atlas():
    image = bpy.data.images.new("HousingSharedAtlas", width=ATLAS_SIZE, height=ATLAS_SIZE, alpha=False)
    pixels = array("f")
    tw, th = ATLAS_SIZE // GRID_X, ATLAS_SIZE // GRID_Y
    for y in range(ATLAS_SIZE):
        tile_y, ly = divmod(y, th)
        for x in range(ATLAS_SIZE):
            tile_x, lx = divmod(x, tw)
            tile = tile_y * GRID_X + tile_x
            color = TILE_COLORS[tile]
            grain = (math.sin(lx * .11 + tile * 1.7) * math.sin(ly * .08 + tile) * .020
                     + math.sin(lx * .033 - ly * .027 + tile) * .013)
            noise = (((lx * 73856093 ^ ly * 19349663 ^ tile * 83492791) & 255) / 255 - .5) * .022
            mortar = 0.0
            if tile == TILES["stone"] and (ly % 39 < 2 or (lx + (25 if (ly // 39) % 2 else 0)) % 64 < 2):
                mortar = -.10
            elif tile == TILES["brick"] and (ly % 25 < 2 or (lx + (14 if (ly // 25) % 2 else 0)) % 40 < 2):
                mortar = -.10
            for c in range(3):
                pixels.append(max(0.0, min(1.0, color[c] + grain + noise + mortar)))
            pixels.append(1.0)
    image.pixels.foreach_set(pixels)
    image.file_format = "PNG"
    image.filepath_raw = str(ATLAS_PATH)
    image.save()
    return image


def uv_tile(tile, corner, count):
    col, row = tile % GRID_X, tile // GRID_X
    x0, y0 = col / GRID_X, row / GRID_Y
    width, height = 1 / GRID_X, 1 / GRID_Y
    inset = .012
    coords = ((.08, .08), (.92, .08), (.92, .92), (.08, .92))
    if count == 3:
        coords = ((.08, .08), (.92, .08), (.50, .92))
    elif count != 4:
        coords = tuple((.5 + .42 * math.cos(math.tau * i / count),
                        .5 + .42 * math.sin(math.tau * i / count)) for i in range(count))
    u, v = coords[corner]
    return (x0 + inset + u * (width - 2 * inset),
            y0 + inset + v * (height - 2 * inset))


def make_object(builder, key, atlas):
    mesh = bpy.data.meshes.new(f"{key}_Geometry")
    mesh.from_pydata(builder.vertices, [], builder.faces)
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for poly, tile in zip(mesh.polygons, builder.face_tiles):
        for ci, li in enumerate(poly.loop_indices):
            uv.data[li].uv = uv_tile(tile, ci, len(poly.loop_indices))
    mat = bpy.data.materials.new(f"{key}_HousingAtlasMaterial")
    mat.diffuse_color = (.73, .65, .52, 1)
    mat.use_nodes = True
    mat.node_tree.nodes.clear()
    output = mat.node_tree.nodes.new("ShaderNodeOutputMaterial")
    shader = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Roughness"].default_value = .8
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.name = "HousingAlbedoAtlas"
    tex.image = atlas
    uvnode = mat.node_tree.nodes.new("ShaderNodeUVMap")
    uvnode.uv_map = "UVMap"
    mat.node_tree.links.new(uvnode.outputs["UV"], tex.inputs["Vector"])
    mat.node_tree.links.new(tex.outputs["Color"], shader.inputs["Base Color"])
    mat.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(key, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)
    low, high = HELPERS.mesh_bounds(mesh)
    cx, cy = (low.x + high.x) * .5, (low.y + high.y) * .5
    for v in mesh.vertices:
        v.co.x -= cx
        v.co.y -= cy
        v.co.z -= low.z
    mesh.update()
    return obj


def box(b, center, size, material, rotation=(0.0, 0.0, 0.0)):
    b.box(center, size, material, rotation=rotation)


def wall_panel(b, x0, x1, z0, z1, y, depth, material="plaster", opening=None):
    """Solid wall bands surrounding a real window aperture, with glazing behind it."""
    if opening is None:
        box(b, ((x0+x1)/2, y, (z0+z1)/2), (x1-x0, depth, z1-z0), material)
        return
    ox0, ox1, oz0, oz1 = opening
    for xa, xb in ((x0, ox0), (ox1, x1)):
        if xb - xa > .06:
            box(b, ((xa+xb)/2, y, (z0+z1)/2), (xb-xa, depth, z1-z0), material)
    for za, zb in ((z0, oz0), (oz1, z1)):
        if zb - za > .06:
            box(b, ((ox0+ox1)/2, y, (za+zb)/2), (ox1-ox0, depth, zb-za), material)
    box(b, ((ox0+ox1)/2, y+depth*.15, (oz0+oz1)/2),
        (ox1-ox0-.06, depth*.38, oz1-oz0-.06), "glass")


def side_panel(b, x, y0, y1, z0, z1, depth=.25):
    oy0, oy1, oz0, oz1 = (y0+y1)*.5-.28, (y0+y1)*.5+.28, z0+.36, z1-.38
    for ya, yb in ((y0, oy0), (oy1, y1)):
        if yb-ya > .06:
            box(b, (x, (ya+yb)/2, (z0+z1)/2), (depth, yb-ya, z1-z0), "plaster")
    for za, zb in ((z0, oz0), (oz1, z1)):
        if zb-za > .06:
            box(b, (x, (oy0+oy1)/2, (za+zb)/2), (depth, oy1-oy0, zb-za), "plaster")
    box(b, (x-depth*.15, (oy0+oy1)/2, (oz0+oz1)/2),
        (depth*.38, oy1-oy0-.06, oz1-oz0-.06), "glass")


def arched_opening(b, x0, x1, z0, z1, y, depth, doorway=False):
    """Build a real curved-headed aperture with individual buff-stone voussoirs."""
    opening_w = min((1.48 if doorway else 1.18), (x1-x0)*.70)
    ox0, ox1 = (x0+x1-opening_w)/2, (x0+x1+opening_w)/2
    radius = opening_w*.5
    sill = z0 + (.12 if doorway else .34)
    spring = z0 + (1.63 if doorway else .98)
    if spring+radius > z1-.12:
        spring = z1-radius-.18
    ydir = 1 if y < 0 else -1
    stone_depth = depth*.84
    for xa, xb in ((x0, ox0), (ox1, x1)):
        if xb-xa > .04:
            box(b, ((xa+xb)/2, y, (z0+z1)/2), (xb-xa, depth, z1-z0), "stone")
    if sill-z0 > .04:
        box(b, ((ox0+ox1)/2, y, (z0+sill)/2), (opening_w, depth, sill-z0), "stone")
    arch_top = spring+radius+.12
    if z1-arch_top > .04:
        box(b, ((ox0+ox1)/2, y, (arch_top+z1)/2),
            (opening_w, depth, z1-arch_top), "stone")
    # Individual wedge voussoirs form an unmistakably curved stone arch.
    segments = 9
    for i in range(segments):
        a0 = math.pi*i/segments
        a1 = math.pi*(i+1)/segments
        inner, outer = radius*.88, radius+ .13
        outline = [
            (ox0+radius+inner*math.cos(a0), spring+inner*math.sin(a0)),
            (ox0+radius+outer*math.cos(a0), spring+outer*math.sin(a0)),
            (ox0+radius+outer*math.cos(a1), spring+outer*math.sin(a1)),
            (ox0+radius+inner*math.cos(a1), spring+inner*math.sin(a1)),
        ]
        b.prism(outline, stone_depth, y, "stone")
    outline = [(ox0, y, sill), (ox1, y, sill), (ox1, y, spring)]
    for i in range(1, 13):
        angle = math.pi*i/12
        outline.append((ox0+radius+radius*math.cos(angle),
                        y, spring+radius*math.sin(angle)))
    if doorway:
        # Recessed dark timber/metal door follows the stone arch profile.
        door_outline = [(ox0+.08, sill+.04), (ox1-.08, sill+.04),
                        (ox1-.08, spring-.04)]
        for i in range(1, 13):
            angle = math.pi*i/12
            door_outline.append((ox0+radius+max(.08, radius-.08)*math.cos(angle),
                                 spring-.04+max(.08, radius-.08)*math.sin(angle)))
        b.prism(door_outline, .065, y+ydir*depth*.46, "metal")
        box(b, (ox1-.23, y+ydir*depth*.42, (sill+spring)*.52),
            (.045, .045, .10), "stone")
        box(b, ((ox0+ox1)/2, y, sill-.05), (opening_w+.16, depth+.08, .10), "stone")
    else:
        b.prism([(p[0], p[2]) for p in outline], .07,
                y+ydir*depth*.46, "glass")
        # Slender inset mullion stops below the arch and keeps the opening legible.
        box(b, ((ox0+ox1)/2, y+ydir*depth*.40, (sill+spring)*.5),
            (.045, .06, spring-sill), "metal")


def rail(b, x0, x1, y, z, depth=.08, height=.88):
    box(b, ((x0+x1)/2, y, z+height*.92), (x1-x0, depth, .10), "metal")
    posts = max(2, int((x1-x0)/.45)+1)
    for i in range(posts):
        x = x0 + (x1-x0)*i/(posts-1)
        box(b, (x, y, z+height*.48), (.065, depth, height*.90), "metal")


def add_window_frame(b, x0, x1, z0, z1, y, depth=.12, stone=False):
    mat = "stone" if stone else "plaster"
    for x in (x0, x1):
        box(b, (x, y, (z0+z1)/2), (.09, depth, z1-z0+.06), mat)
    for z in (z0, z1):
        box(b, ((x0+x1)/2, y, z), (x1-x0+.10, depth, .09), mat)


def add_roof_services(b, cx, cy, roof_z, width, depth):
    box(b, (cx-width*.23, cy+depth*.12, roof_z+.48), (width*.22, depth*.25, .96), "plaster")
    # water tank elevated on a short plinth, plus solar/service detail
    box(b, (cx+width*.23, cy+depth*.15, roof_z+.55), (width*.20, depth*.20, 1.10), "metal")
    box(b, (cx, cy-depth*.25, roof_z+.14), (width*.30, depth*.24, .08), "glass")


def building_shell(b, cx, cy, width, depth, floors, floor_h, style, stage,
                   balconies=False, glazing_core=False, arch=False, partial=False):
    base_z = .22
    top = base_z + floors*floor_h
    wall_depth = .25
    # Vertical structure: all-frame stage is orderly and complete.
    if stage == "frame":
        for floor in range(floors+1):
            z = base_z + floor*floor_h
            box(b, (cx, cy, z), (width, depth, .22), "concrete")
        nx = 3 if width > 8 else 2
        ny = 3 if depth > 7 else 2
        for i in range(nx):
            x = cx-width*.43 + width*.86*i/(nx-1)
            for j in range(ny):
                y = cy-depth*.42 + depth*.84*j/(ny-1)
                box(b, (x, y, (base_z+top)/2), (.24, .24, top-base_z), "concrete")
        return top

    # Completed floor slabs and facades. In finishing, deliberately omit alternating bays.
    for floor in range(floors):
        z0 = base_z + floor*floor_h
        z1 = z0 + floor_h
        box(b, (cx, cy, z0+.12), (width, depth, .24), "concrete")
        front_y = cy-depth/2
        back_y = cy+depth/2
        bays = 1 if floors >= 8 and width <= 7 else max(2, int(width/2.7))
        bay_w = width/bays
        for side, y in (("front", front_y), ("back", back_y)):
            for bay in range(bays):
                x0 = cx-width/2 + bay*bay_w
                x1 = x0+bay_w
                omit = stage == "finishing" and ((floor*3+bay) % 5 == 1)
                if omit:
                    # Unfinished open bay with a visible small brick infill panel.
                    box(b, ((x0+x1)/2, y, z0+.32), (bay_w-.08, .18, .65), "brick")
                    continue
                if arch:
                    doorway = side == "front" and floor == 0 and bay == bays//2
                    arched_opening(b, x0, x1, z0, z1, y, wall_depth, doorway=doorway)
                else:
                    glazing = (x0+bay_w*.23, x1-bay_w*.23, z0+.34, z1-.34)
                    if glazing_core and bay == bays//2:
                        glazing = (x0+bay_w*.08, x1-bay_w*.08, z0+.20, z1-.20)
                    wall_panel(b, x0, x1, z0, z1, y, wall_depth,
                               "stone" if style == "limestone" else "plaster", glazing)
                    add_window_frame(b, glazing[0], glazing[1], glazing[2], glazing[3],
                                     y-depth*.10, stone=style == "limestone")
                    if glazing_core and bay == bays//2:
                        box(b, ((glazing[0]+glazing[1])/2, y-.17,
                                (glazing[2]+glazing[3])/2), (.07, .10, glazing[3]-glazing[2]), "metal")
                if not arch:
                    # visible openings on regular façades; arch openings already have curved heads
                    gx0, gx1 = (x0+bay_w*.23, x1-bay_w*.23)
                    box(b, ((gx0+gx1)/2, y-.14, (z0+z1)/2),
                        (.055, .07, max(.15, floor_h*.45)), "metal")
        # Narrow solid side elevations with paired punched windows.
        side_windows = (cy,) if floors >= 8 else (cy-depth*.27, cy+depth*.27)
        for x in (cx-width/2, cx+width/2):
            for sy in side_windows:
                side_panel(b, x, sy-.72, sy+.72, z0, z1, wall_depth)
        if balconies and floor < floors-1 and stage == "final":
            for bay in range(max(2, int(width/3))):
                bx0 = cx-width*.38 + bay*(width*.76/max(1, int(width/3)-1))
                bw = min(2.1, width*.30)
                box(b, (bx0, front_y-.48, z0+.28), (bw, .95, .12), "concrete")
                rail(b, bx0-bw/2, bx0+bw/2, front_y-.93, z0+.34)
    if stage in ("final", "finishing") and style != "pitched":
        # Parapet edges frame a true flat usable rooftop, never conceal it with a box.
        parapet_h = .78
        for y in (cy-depth/2, cy+depth/2):
            box(b, (cx, y, top+parapet_h/2), (width, .18, parapet_h), "plaster")
        for x in (cx-width/2, cx+width/2):
            box(b, (x, cy, top+parapet_h/2), (.18, depth, parapet_h), "plaster")
        if stage == "final":
            add_roof_services(b, cx, cy, top+parapet_h, width, depth)
        else:
            box(b, (cx-width*.2, cy, top+.45), (width*.30, .20, .90), "brick")
    return top+(.78 if stage in ("final", "finishing") else 0)


def add_pitch_roof(b, cx, cy, width, depth, z):
    # Four separately sloped hip planes converge on a raised ridge, never a flat pad.
    rise = min(1.18, depth*.30)
    half_w, half_d = width*.5, depth*.5
    ridge_half = max(.30, (width-depth)*.5)

    def facet(points):
        vectors = [Vector(p) for p in points]
        normal = (vectors[1]-vectors[0]).cross(vectors[2]-vectors[0]).normalized()
        lower = [tuple(p-normal*.055) for p in vectors]
        upper = [tuple(p) for p in vectors]
        count = len(points)
        faces = [tuple(range(count)), tuple(reversed(range(count, count*2)))]
        faces.extend((i, (i+1) % count, (i+1) % count+count, i+count)
                     for i in range(count))
        b.add_solid(upper+lower, faces, TILES["terracotta"])

    front_left = (cx-half_w, cy-half_d, z)
    front_right = (cx+half_w, cy-half_d, z)
    back_right = (cx+half_w, cy+half_d, z)
    back_left = (cx-half_w, cy+half_d, z)
    ridge_left = (cx-ridge_half, cy, z+rise)
    ridge_right = (cx+ridge_half, cy, z+rise)
    facet((front_left, front_right, ridge_right, ridge_left))
    facet((back_right, back_left, ridge_left, ridge_right))
    facet((front_left, back_left, ridge_left))
    facet((front_right, ridge_right, back_right))
    # Dense eave edging and staggered horizontal terracotta courses make the pitch read.
    box(b, (cx, cy-half_d-.06, z-.03), (width+.12, .13, .16), "terracotta")
    box(b, (cx, cy+half_d+.06, z-.03), (width+.12, .13, .16), "terracotta")
    for row in range(1, 6):
        t = row/6
        course_y = half_d*(1-t)
        course_z = z+rise*t-.018
        course_w = width-2*(half_d-course_y)*.60
        for sign in (-1, 1):
            box(b, (cx, cy+sign*course_y, course_z),
                (course_w, .045, .055), "terracotta")
    box(b, (cx, cy, z+rise+.055), (ridge_half*2+.32, .20, .16), "terracotta")


def add_blueglass_curtain_wall(b, cx, cy, width, depth, floors, floor_h):
    """A single tall, multi-storey central curtain-wall bay, not a tiled window grid."""
    front_y = cy-depth*.5-.12
    base_z = .34
    wall_h = floors*floor_h-.24
    glass_w = min(width*.32, 2.85)
    box(b, (cx, front_y, base_z+wall_h*.5), (glass_w, .12, wall_h), "glass")
    # Continuous vertical divisions and restrained occasional transoms.
    for offset in (-glass_w*.5, -glass_w/6, glass_w/6, glass_w*.5):
        box(b, (cx+offset, front_y-.075, base_z+wall_h*.5),
            (.065, .07, wall_h+.06), "metal")
    for floor in (2, 4):
        z = base_z+floor*floor_h
        box(b, (cx, front_y-.08, z), (glass_w, .08, .055), "metal")
    for side in (-1, 1):
        box(b, (cx+side*(glass_w*.5+.13), front_y-.04,
                base_z+wall_h*.5), (.20, .18, wall_h+.12), "stone")
    # Broad stone brow and entry canopy root the central glazed spine.
    box(b, (cx, front_y-.05, base_z+wall_h+.10), (glass_w+.42, .22, .20), "stone")
    box(b, (cx, front_y-.28, .58), (glass_w+.56, .62, .18), "concrete")


def parcel_and_stage(b, pw, pd, stage):
    # This exact common parcel perimeter is present in all four meshes.
    border = .18
    box(b, (0, 0, .10), (pw, pd, .20), "concrete")
    for x in (-pw/2+border/2, pw/2-border/2):
        box(b, (x, 0, .28), (border, pd, .36), "stone")
    for y in (-pd/2+border/2, pd/2-border/2):
        box(b, (0, y, .28), (pw, border, .36), "stone")
    if stage == "foundation":
        # Footings and grade beams are low but trace full logical house positions.
        for x in (-pw*.28, pw*.28):
            for y in (-pd*.28, pd*.28):
                box(b, (x, y, .58), (1.25, 1.15, .80), "concrete")
        for y in (-pd*.28, pd*.28):
            box(b, (0, y, .62), (pw*.61, .34, .65), "concrete")
        for x in (-pw*.28, pw*.28):
            box(b, (x, 0, .62), (.34, pd*.61, .65), "concrete")
        # low perimeter starter walls, clearly an excavation/foundation phase
        box(b, (0, -pd*.29, .62), (pw*.62, .25, .68), "brick")
        # tidy starter bars rise from the footings above the low grade beams
        for x in (-pw*.28, pw*.28):
            for y in (-pd*.28, pd*.28):
                for dx, dy in ((-.42,-.38),(.42,-.38),(-.42,.38),(.42,.38)):
                    box(b, (x+dx, y+dy, 1.25), (.045, .045, 1.25), "metal")
        return
    if stage == "final":
        # Low planted beds, paths and any water feature all remain inside parcel.
        for x in (-pw*.39, pw*.39):
            for y in (-pd*.38, pd*.38):
                box(b, (x, y, .55), (1.05, .82, .20), "plant")


def build_architecture(record, stage):
    key, label, floors, pw, pd, description = record
    b = MeshBuilder(seed=sum(ord(c) for c in key), fracture_boxes=False)
    parcel_and_stage(b, pw, pd, stage)
    if stage == "foundation":
        return b
    # Individual footprint, massing, and roof language per authored type.
    if key == "house_compound":
        wing_w = pw*.38
        wing_d = pd*.47
        building_shell(b, -pw*.27, 0, wing_w, wing_d, min(floors, 3), 2.65,
                       "plaster", stage, balconies=stage == "final")
        building_shell(b, pw*.27, 0, wing_w, wing_d, min(floors, 2), 2.65,
                       "limestone", stage, arch=True)
        # Connected rear gallery / gatehouse closes a recognizable courtyard.
        box(b, (0, pd*.20, 1.10), (pw*.28, .35, 1.75), "stone")
        if stage in ("frame",):
            for x in (-pw*.14, pw*.14):
                box(b, (x, pd*.20, 2.0), (.22, .22, 3.4), "concrete")
            box(b, (0, pd*.20, 3.65), (pw*.30, .35, .22), "concrete")
        if stage == "final":
            box(b, (0, 0, .30), (pw*.28, pd*.34, .12), "plant")
            for x in (-pw*.12, pw*.12):
                box(b, (x, .15, 1.15), (.20, .20, 1.70), "plaster")
    else:
        style = ("pitched" if key == "house_small_redtile" else
                 "limestone" if key == "house_traditional_stonearches" else "plaster")
        if key == "house_small_redtile":
            w, d, n, fh = pw*.70, pd*.67, 1, 2.85
        elif key == "house_cream_family":
            w, d, n, fh = pw*.72, pd*.70, 3, 2.75
        elif key == "house_modern_villa":
            w, d, n, fh = pw*.73, pd*.68, 3, 2.85
        elif key == "apartment_4floor_balcony":
            w, d, n, fh = pw*.77, pd*.72, 4, 2.72
        elif key == "apartment_6floor_balcony":
            w, d, n, fh = pw*.77, pd*.72, 6, 2.68
        elif key == "apartment_blueglass_midrise":
            w, d, n, fh = pw*.76, pd*.72, 6, 2.75
        elif key == "apartment_12floor_tower":
            w, d, n, fh = pw*.62, pd*.60, 12, 2.62
        elif key == "house_coastal_white_pool":
            w, d, n, fh = pw*.76, pd*.70, 3, 2.78
        else:
            w, d, n, fh = pw*.7, pd*.7, floors, 2.75
        if key == "house_modern_villa":
            # offset upper floor creates modern cantilevered silhouette
            if stage == "final":
                building_shell(b, -.18, 0, w, d, 2, fh, style, stage, glazing_core=True)
                building_shell(b, .72, 0, w*.72, d*.68, 1, fh, style, stage, glazing_core=True)
            else:
                building_shell(b, 0, 0, w, d, n, fh, style, stage, glazing_core=True)
        elif key == "house_coastal_white_pool":
            if stage == "final":
                building_shell(b, -.28, 0, w, d, 2, fh, style, stage, glazing_core=True)
                building_shell(b, .70, .45, w*.70, d*.60, 1, fh, style, stage, glazing_core=True)
                # inset blue pool set into roof terrace, not beyond parcel bounds
                box(b, (pw*.23, -pd*.23, 2*fh+.52), (pw*.25, pd*.22, .16), "glass")
                for x in (pw*.23-pw*.125, pw*.23+pw*.125):
                    box(b, (x, -pd*.23, 2*fh+.64), (.10, pd*.22, .12), "stone")
            else:
                building_shell(b, 0, 0, w, d, n, fh, style, stage, glazing_core=True)
        else:
            core = key == "apartment_blueglass_midrise"
            balcony = key in ("apartment_4floor_balcony", "apartment_6floor_balcony",
                              "apartment_12floor_tower", "house_cream_family")
            arch = key == "house_traditional_stonearches"
            building_shell(b, 0, 0, w, d, n, fh, style, stage,
                           balconies=balcony, glazing_core=core, arch=arch)
            if key == "apartment_blueglass_midrise" and stage == "final":
                add_blueglass_curtain_wall(b, 0, 0, w, d, n, fh)
        if key == "house_small_redtile" and stage in ("final", "finishing"):
            add_pitch_roof(b, 0, 0, w, d, .22+fh+.1)
        elif stage == "final" and key not in ("house_coastal_white_pool", "house_modern_villa"):
            # clean rooftop box and service equipment; central tower's blue core rises visibly
            if key == "apartment_blueglass_midrise":
                box(b, (0, -d*.1, .22+n*fh+1.2), (w*.17, .20, 2.2), "glass")
                box(b, (0, -d*.1, .22+n*fh+.80), (w*.21, .24, .15), "metal")
    if stage == "finishing":
        # A few safe modular scaffold runs and selected plaster patches (not wreckage).
        main_w = pw*.70
        for z in (.22+2.7, .22+5.4):
            if z < .22+floors*2.75:
                for x in (-main_w*.48, main_w*.48):
                    box(b, (x, -pd*.41, z), (.075, .08, 2.4), "metal")
                box(b, (0, -pd*.41, z+.92), (main_w, .08, .075), "metal")
    if stage == "final":
        # A discreet entry stair and planted frontage, contained by the perimeter.
        box(b, (0, -pd*.43, .32), (2.4, .50, .22), "stone")
        for x in (-pw*.39, pw*.39):
            box(b, (x, -pd*.40, .52), (.55, .45, .48), "plant")
    return b


def triangulate(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.update()


def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_NONE",
                             global_scale=1.0, apply_unit_scale=True, axis_forward="-Z",
                             axis_up="Y", use_mesh_modifiers=True, use_tspace=True,
                             path_mode="AUTO", embed_textures=False, bake_anim=False,
                             add_leaf_bones=False)


def export_obj(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.wm.obj_export(filepath=str(path), export_selected_objects=True,
                          export_uv=True, export_normals=True, export_materials=False,
                          export_triangulated_mesh=True, forward_axis="NEGATIVE_Z",
                          up_axis="Y", global_scale=1.0, path_mode="AUTO")


def bounds(mesh):
    low, high = HELPERS.mesh_bounds(mesh)
    return {"min": [float(x) for x in low], "max": [float(x) for x in high],
            "width": float(high.x-low.x), "height": float(high.z-low.z),
            "depth": float(high.y-low.y)}


def uv_bounds(mesh):
    values = [tuple(float(x) for x in loop.uv) for loop in mesh.uv_layers[0].data]
    if not values or any(not math.isfinite(x) for uv in values for x in uv):
        raise RuntimeError("Missing or non-finite UVs")
    if any(x < -1e-5 or x > 1.00001 for uv in values for x in uv):
        raise RuntimeError("Atlas UV outside 0-1")
    return {"min": [min(x[0] for x in values), min(x[1] for x in values)],
            "max": [max(x[0] for x in values), max(x[1] for x in values)]}


def obj_triangles(path):
    vertices = uvs = normals = tris = 0
    positions = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("v "):
            vals = [float(x) for x in line.split()[1:4]]
            if any(not math.isfinite(x) for x in vals):
                raise RuntimeError(f"Non-finite OBJ vertex: {path}")
            positions.append(Vector(vals))
            vertices += 1
        elif line.startswith("vt "):
            vals = [float(x) for x in line.split()[1:3]]
            if any(not math.isfinite(x) or x < -1e-5 or x > 1.00001 for x in vals):
                raise RuntimeError(f"Invalid OBJ UV: {path}")
            uvs += 1
        elif line.startswith("vn "):
            vals = [float(x) for x in line.split()[1:4]]
            if any(not math.isfinite(x) for x in vals):
                raise RuntimeError(f"Invalid OBJ normal: {path}")
            normals += 1
        elif line.startswith("f "):
            parts = line.split()[1:]
            if len(parts) < 3:
                raise RuntimeError(f"Degenerate OBJ face: {path}")
            indices = [int(part.split("/")[0]) for part in parts]
            if any(index == 0 or abs(index) > len(positions) for index in indices):
                raise RuntimeError(f"OBJ face has an invalid vertex reference: {path}")
            points = [positions[index-1] if index > 0 else positions[len(positions)+index]
                      for index in indices]
            for i in range(1, len(points)-1):
                if (points[i]-points[0]).cross(points[i+1]-points[0]).length < 1e-8:
                    raise RuntimeError(f"Degenerate OBJ triangle: {path}")
            tris += len(parts)-2
    if not vertices or not uvs or not normals or not tris:
        raise RuntimeError(f"Incomplete OBJ export: {path}")
    lows = [min(v[axis] for v in positions) for axis in range(3)]
    highs = [max(v[axis] for v in positions) for axis in range(3)]
    return {"vertices": vertices, "uvs": uvs, "normals": normals, "triangles": tris,
            "boundsYUpMeters": {"min": lows, "max": highs,
                                "width": highs[0]-lows[0], "height": highs[1]-lows[1],
                                "depth": highs[2]-lows[2]}}


def main():
    script_args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    resources_only = "--resources-only" in script_args
    selected_keys = None
    if "--keys" in script_args:
        index = script_args.index("--keys")+1
        if index >= len(script_args):
            raise ValueError("--keys requires a comma-separated list of archetype keys")
        selected_keys = set(script_args[index].split(","))
        unknown = selected_keys-{item[0] for item in ARCHETYPES}
        if unknown:
            raise ValueError(f"Unknown housing archetype keys: {sorted(unknown)}")
    selected_archetypes = [item for item in ARCHETYPES
                           if selected_keys is None or item[0] in selected_keys]
    RESOURCE_DIR.mkdir(parents=True, exist_ok=True)
    FBX_DIR.mkdir(parents=True, exist_ok=True)
    MANIFEST_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if resources_only:
        if not ATLAS_PATH.exists():
            raise FileNotFoundError(f"Resource-only export requires existing shared atlas: {ATLAS_PATH}")
        atlas = bpy.data.images.load(str(ATLAS_PATH), check_existing=True)
    else:
        atlas = create_atlas()
    records = []
    for key, label, floors, pw, pd, description in selected_archetypes:
        stages = {}
        stage_audits = {}
        shared_xy = None
        for suffix, stage_description in STAGES:
            builder = build_architecture((key, label, floors, pw, pd, description), suffix)
            obj = make_object(builder, f"{key}_{suffix}", atlas)
            triangulate(obj)
            tri_count = sum(len(poly.vertices)-2 for poly in obj.data.polygons)
            if tri_count <= 0 or tri_count > 12000:
                raise RuntimeError(f"{obj.name} triangle count out of range: {tri_count}")
            if any(not math.isfinite(float(c)) for v in obj.data.vertices for c in v.co):
                raise RuntimeError(f"Non-finite geometry in {obj.name}")
            if any(not math.isfinite(float(c)) for poly in obj.data.polygons for c in poly.normal):
                raise RuntimeError(f"Non-finite face normal in {obj.name}")
            for poly in obj.data.polygons:
                pts = [obj.data.vertices[i].co for i in poly.vertices]
                if len(pts) < 3 or (pts[1]-pts[0]).cross(pts[2]-pts[0]).length < 1e-8:
                    raise RuntimeError(f"Degenerate face in {obj.name}")
            bb = bounds(obj.data)
            if shared_xy is None:
                shared_xy = (bb["width"], bb["depth"])
            elif abs(bb["width"]-shared_xy[0]) > 1e-5 or abs(bb["depth"]-shared_xy[1]) > 1e-5:
                raise RuntimeError(f"Stage footprint changed for {key}_{suffix}: {bb}")
            uvs = uv_bounds(obj.data)
            fbx_path = FBX_DIR / f"{key}_{suffix}.fbx"
            stages[suffix] = {
                "description": stage_description,
                "triangles": tri_count, "vertices": len(obj.data.vertices),
                "boundsMeters": bb, "uvBounds": uvs,
            }
            if not resources_only:
                export_fbx(obj, fbx_path)
            stages[suffix]["fbx"] = f"Art/HousingFBX/{fbx_path.name}"
            obj_path = RESOURCE_DIR / f"{key}_{suffix}.obj"
            export_obj(obj, obj_path)
            audit = obj_triangles(obj_path)
            if audit["triangles"] != tri_count:
                raise RuntimeError(
                    f"OBJ triangle mismatch {key}_{suffix}: {audit['triangles']} != {tri_count}")
            for dimension in ("width", "height", "depth"):
                if abs(audit["boundsYUpMeters"][dimension]-bb[dimension]) > .02:
                    raise RuntimeError(f"OBJ Y-up metre bounds mismatch {key}_{suffix} {dimension}")
            albedo_path = RESOURCE_DIR / f"{key}_{suffix}_albedo.png"
            shutil.copyfile(ATLAS_PATH, albedo_path)
            manifest_path = RESOURCE_DIR / f"{key}_{suffix}_manifest.json"
            stage_audits[suffix] = {"audit": audit, "manifestPath": manifest_path}
            stages[suffix]["obj"] = f"Resources/Models/{obj_path.name}"
            stages[suffix]["albedo"] = f"Resources/Models/{albedo_path.name}"
            stages[suffix]["manifest"] = f"Resources/Models/{manifest_path.name}"
            stages[suffix]["objAudit"] = audit
            bpy.data.objects.remove(obj, do_unlink=True)
        logical_final_height = stages["final"]["boundsMeters"]["height"]
        for suffix, stage_description in STAGES:
            stage_bounds = stages[suffix]["boundsMeters"]
            asset = stages[suffix]
            stage_manifest = {
                "key": f"{key}_{suffix}", "archetypeKey": key,
                "archetype": label, "stage": suffix,
                "stageDescription": stage_description,
                "modelPath": asset["obj"], "albedoPath": asset["albedo"],
                "scaleConvention": "1 Blender unit = 1 metre; native OBJ is Y-up",
                "floorCount": floors, "dimensionsMeters": {
                    "width": stage_bounds["width"], "depth": stage_bounds["depth"],
                    "actualStageHeight": stage_bounds["height"],
                    "logicalFinalHeight": logical_final_height,
                    "logicalHeightCap": logical_final_height,
                },
                "metersPerCityUnit": 20.0,
                "nativeScaleConvention": "uniform widthFit; keep completed logical height cap in every stage",
                "triangleCount": asset["triangles"], "vertexCount": asset["vertices"],
                "boundsMeters": stage_bounds, "uvBounds": asset["uvBounds"],
                "atlas": "HousingSharedAtlas.png",
                "objAudit": stage_audits[suffix]["audit"],
            }
            stage_audits[suffix]["manifestPath"].write_text(
                json.dumps(stage_manifest, indent=2)+"\n", encoding="utf-8")
        # Remove only this generator's earlier unsuffixed final aliases. The native
        # housing library resolves the explicit four-suffix key set exclusively.
        for old_alias in (RESOURCE_DIR / f"{key}.obj",
                          RESOURCE_DIR / f"{key}_albedo.png",
                          RESOURCE_DIR / f"{key}_manifest.json"):
            if old_alias.exists():
                old_alias.unlink()
        records.append((key, stages))
        print(f"GENERATED {key}: {floors} floors; stages=" +
              ", ".join(f"{name}:{data['triangles']}tri" for name, data in stages.items()))
    manifest = {
        "schemaVersion": 1, "units": "metres",
        "coordinateSystems": {"fbx": "static mesh, -Z forward / Y up",
                              "runtimeOBJ": "Y up; 1 Blender unit = 1 metre"},
        "sharedAtlas": "Art/HousingFBX/HousingSharedAtlas.png",
        "runtimeModels": "Assets/NewGaza/Resources/Models",
        "nativeScaleConvention": "uniform widthFit; logical height cap equals completed archetype height",
        "footprintInvariant": "All four stage meshes share the exact parcel width/depth bounds per archetype.",
        "archetypes": [
            {"key": key, "name": next(x[1] for x in ARCHETYPES if x[0] == key),
             "floorCount": next(x[2] for x in ARCHETYPES if x[0] == key),
             "dimensionsMeters": {"width": stages["final"]["boundsMeters"]["width"],
                                  "depth": stages["final"]["boundsMeters"]["depth"],
                                  "finalHeight": stages["final"]["boundsMeters"]["height"],
                                  "logicalHeightCap": stages["final"]["boundsMeters"]["height"]},
             "metersPerCityUnit": 20.0,
             "description": next(x[5] for x in ARCHETYPES if x[0] == key),
             "stages": stages}
            for key, stages in records
        ],
    }
    if selected_keys and MANIFEST_PATH.exists():
        previous = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        replacements = {item["key"]: item for item in manifest["archetypes"]}
        merged = []
        for item in previous["archetypes"]:
            merged.append(replacements.pop(item["key"], item))
        merged.extend(replacements.values())
        manifest["archetypes"] = merged
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    if not resources_only:
        # Round-trip validate every static FBX, including topology, UVs, and metre bounds.
        for archetype in manifest["archetypes"]:
            for stage, asset in archetype["stages"].items():
                path = FBX_DIR / Path(asset["fbx"]).name
                bpy.ops.wm.read_factory_settings(use_empty=True)
                bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False,
                                         ignore_leaf_bones=True, automatic_bone_orientation=False)
                meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
                if len(meshes) != 1:
                    raise RuntimeError(f"FBX round-trip mesh count mismatch: {path}")
                mesh = meshes[0].data
                if sum(len(p.vertices)-2 for p in mesh.polygons) != asset["triangles"]:
                    raise RuntimeError(f"FBX triangle round-trip mismatch: {path}")
                if not mesh.uv_layers or not uv_bounds(mesh):
                    raise RuntimeError(f"FBX UV round-trip missing: {path}")
                if not mesh.materials or not any(material for material in mesh.materials):
                    raise RuntimeError(f"FBX material round-trip missing: {path}")
                if any(not math.isfinite(float(c)) for v in mesh.vertices for c in v.co):
                    raise RuntimeError(f"FBX non-finite position: {path}")
                if any(not math.isfinite(float(c)) for p in mesh.polygons for c in p.normal):
                    raise RuntimeError(f"FBX non-finite normal: {path}")
                mesh.calc_loop_triangles()
                for tri in mesh.loop_triangles:
                    points = [mesh.vertices[i].co for i in tri.vertices]
                    if (points[1]-points[0]).cross(points[2]-points[0]).length < 1e-8:
                        raise RuntimeError(f"FBX degenerate triangle: {path}")
                imported = bounds(mesh)
                for axis in ("width", "depth", "height"):
                    if abs(imported[axis]-asset["boundsMeters"][axis]) > .03:
                        raise RuntimeError(f"FBX metre bounds mismatch {path} {axis}: {imported[axis]}")
                bpy.ops.wm.read_factory_settings(use_empty=True)
    product = "40 stage runtime OBJs; FBX bytes preserved" if resources_only else "40 FBX + 40 stage runtime OBJs"
    print(f"COMPLETE: {len(records)} archetypes, {product}; manifest={MANIFEST_PATH}")


if __name__ == "__main__":
    main()