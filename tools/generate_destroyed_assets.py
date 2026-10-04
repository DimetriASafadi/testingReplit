#!/usr/bin/env python3
"""Create original, UV-mapped destroyed-only Gaza-inspired game assets.

Run with Blender 4.4:
  blender --background --factory-startup --python tools/generate_destroyed_assets.py

All geometry and the shared weathered albedo atlas are authored procedurally;
no intact-building model, external asset, network, or render engine is used.
"""

import json
import math
import random
import shutil
import sys
from array import array
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parent.parent
RESOURCE_DIR = ROOT / "testingReplic/Assets/NewGaza/Resources/Models"
FBX_DIR = ROOT / "testingReplic/Assets/NewGaza/Art/DestroyedFBX"
EXPORT_MANIFEST = ROOT / "exports/destroyed-assets/Manifest.json"
REFERENCE_ASSETS = [
    "IMG-20261002-WA0003_1790946116113.jpg",
    "IMG-20261002-WA0004_1790946116076.jpg",
]
ATLAS_SIZE = 512
ATLAS_GRID = 4
ATLAS_TILES = {
    "concrete": 0,
    "plaster": 1,
    "brick": 2,
    "rust": 3,
    "scorch": 4,
    "stone": 5,
    "soil": 6,
    "metal": 7,
}
TILE_COLORS = [
    (0.48, 0.46, 0.41),  # weathered reinforced concrete
    (0.66, 0.55, 0.40),  # dirty beige plaster
    (0.48, 0.25, 0.16),  # exposed red brick
    (0.30, 0.20, 0.14),  # corroded rebar
    (0.15, 0.13, 0.12),  # soot / blackened surfaces
    (0.57, 0.51, 0.40),  # cut masonry / limestone
    (0.39, 0.32, 0.24),  # dusty rubble
    (0.20, 0.21, 0.19),  # scorched vehicle metal
]

REGIONS = [
    ("ruin_shujaiya", "Shujaiya", "family_courtyard", 2, "partial_frame", 10.0, 8.0),
    ("ruin_tuffah", "Tuffah", "row_home", 3, "corner_collapse", 10.5, 8.5),
    ("ruin_sheikh_radwan", "Sheikh Radwan", "family_block", 3, "broken_front", 11.0, 9.0),
    ("ruin_daraj", "Daraj", "mixed_shopfront", 4, "shopfront_breach", 11.5, 9.3),
    ("ruin_karama", "Karama", "apartments", 4, "slab_collapse", 12.0, 9.7),
    ("ruin_old_city", "Old City", "heritage_stone_arches", 3, "stone_breach", 12.5, 10.0),
    ("ruin_nasr", "Nasr", "urban_frame", 6, "frame_shear", 13.0, 10.5),
    ("ruin_sabra", "Sabra", "attached_row", 4, "party_wall_failure", 13.5, 11.0),
    ("ruin_zeitoun", "Zeitoun", "family_block", 5, "roof_and_corner_loss", 14.0, 11.5),
    ("ruin_rimal", "Rimal", "balcony_tower", 7, "balcony_collapse", 15.0, 12.0),
    ("ruin_tel_al_hawa", "Tel al-Hawa", "modern_tower", 8, "curtainwall_failure", 16.0, 13.0),
    ("ruin_sheikh_ijlin", "Sheikh Ijlin", "coastal_terraces", 6, "terrace_shear", 17.0, 14.0),
    ("ruin_rashid", "Rashid", "coastal_hotel_wing", 9, "wing_collapse", 18.0, 15.0),
]
EXTRAS = [
    ("ruin_mosque", "mosque", "fractured_dome_minaret", 0, 13.0, 11.0),
    ("ruin_school", "school", "collapsed_classroom_wing", 0, 15.0, 11.0),
    ("ruin_clinic", "clinic", "destroyed_health_building", 0, 11.0, 9.0),
    ("ruin_civic", "civic", "collapsed_civic_frontage", 0, 12.0, 10.0),
    ("ruin_wall", "wall", "ragged_breached_masonry", 0, 8.0, 0.8),
    ("ruin_car", "car", "burnt_vehicle_wreck", 0, 4.2, 1.9),
    ("ruin_crater", "crater", "static_soil_rim_bowl", 0, 7.0, 6.5),
    ("ruin_debris", "debris", "mixed_concrete_brick_metal_pile", 0, 8.0, 7.0),
]


class MeshBuilder:
    """Append separate authored solid components into a single UV-ready mesh."""

    def __init__(self, seed, fracture_boxes=True):
        self.rng = random.Random(seed)
        self.seed = seed
        self.fracture_boxes = fracture_boxes
        self.vertices = []
        self.faces = []
        self.face_tiles = []

    def add_solid(self, vertices, faces, tile):
        base = len(self.vertices)
        self.vertices.extend(tuple(float(v) for v in p) for p in vertices)
        self.faces.extend(tuple(base + index for index in face) for face in faces)
        self.face_tiles.extend([tile] * len(faces))

    def box(self, center, size, tile="concrete", rotation=(0.0, 0.0, 0.0), cut_top=False):
        cx, cy, cz = center
        sx, sy, sz = size
        # Small asymmetry on damaged concrete gives broken edges a non-machined look.
        ztop = sz * (0.47 if cut_top else 0.5)
        zbot = -sz * 0.5
        corners = [
            (-sx / 2, -sy / 2, zbot), (sx / 2, -sy / 2, zbot),
            (sx / 2, sy / 2, zbot), (-sx / 2, sy / 2, zbot),
            (-sx / 2, -sy / 2, ztop), (sx / 2, -sy / 2, ztop),
            (sx / 2, sy / 2, ztop), (-sx / 2, sy / 2, ztop),
        ]
        if self.fracture_boxes and tile in ("concrete", "plaster", "brick", "stone"):
            # Broken masonry edges rather than mathematically perfect cuboids.
            # Keep thin-axis displacement bounded to avoid inverted slab faces.
            edge_rng = random.Random(self.seed * 131 + len(self.vertices))
            corners = [(x + edge_rng.uniform(-.055, .055) * sx,
                        y + edge_rng.uniform(-.055, .055) * sy,
                        z + edge_rng.uniform(-.055, .055) * sz) for x, y, z in corners]
        rx, ry, rz = rotation
        crx, srx = math.cos(rx), math.sin(rx)
        cry, sry = math.cos(ry), math.sin(ry)
        crz, srz = math.cos(rz), math.sin(rz)
        vertices = []
        for x, y, z in corners:
            y, z = y * crx - z * srx, y * srx + z * crx
            x, z = x * cry + z * sry, -x * sry + z * cry
            x, y = x * crz - y * srz, x * srz + y * crz
            vertices.append((x + cx, y + cy, z + cz))
        self.add_solid(
            vertices,
            [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)],
            ATLAS_TILES[tile],
        )

    def prism(self, outline, depth, center_y, tile="concrete", rotation=(0.0, 0.0, 0.0)):
        """Extrude an irregular X/Z outline along Y; outline points are (x,z)."""
        count = len(outline)
        points = []
        for y in (center_y - depth / 2, center_y + depth / 2):
            points.extend((x, y, z) for x, z in outline)
        faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
        faces.extend((i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count))
        rx, ry, rz = rotation
        transformed = []
        for x, y, z in points:
            y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
            x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
            x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
            transformed.append((x, y, z))
        self.add_solid(transformed, faces, ATLAS_TILES[tile])

    def rock(self, center, size, tile=None):
        """Low-poly irregular faceted boulder, never a repeated cube."""
        tile = tile or self.rng.choice(("concrete", "brick", "stone", "soil"))
        x, y, z = center
        sx, sy, sz = size
        sides = self.rng.randint(5, 7)
        rings = []
        for ring_i, height_factor in enumerate((-0.5, 0.50)):
            ring = []
            phase = self.rng.uniform(0, math.tau)
            for i in range(sides):
                angle = math.tau * i / sides + phase
                radius = self.rng.uniform(0.72, 1.16)
                h = height_factor + self.rng.uniform(-0.11, 0.11)
                ring.append((x + math.cos(angle) * sx * radius / 2,
                             y + math.sin(angle) * sy * radius / 2,
                             z + h * sz))
            rings.append(ring)
        verts = rings[0] + rings[1]
        # The underside sits embedded in the rubble/ground and is intentionally
        # open to save useful face budget for the visible, faceted top surfaces.
        faces = [tuple(sides + i for i in range(sides))]
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((i, j, sides + j, sides + i))
        self.add_solid(verts, faces, ATLAS_TILES[tile])

    def broken_plate(self, center, size, rotation=(0.0, 0.0, 0.0), tile="concrete"):
        """Irregular, jagged six-sided floor plate chunk, tilted as it fell."""
        cx, cy, cz = center
        sx, sy, thickness = size
        points = [(-.50, -.36), (-.31, -.52), (.33, -.49),
                  (.52, -.14), (.38, .47), (-.31, .51)]
        points = [(x * sx * self.rng.uniform(.91, 1.09),
                   y * sy * self.rng.uniform(.90, 1.10)) for x, y in points]
        verts = [(x, y, z) for z in (-thickness / 2, thickness / 2) for x, y in points]
        faces = [tuple(reversed(range(6))), tuple(range(6, 12))]
        faces.extend((i, (i + 1) % 6, (i + 1) % 6 + 6, i + 6) for i in range(6))
        rx, ry, rz = rotation
        transformed = []
        for x, y, z in verts:
            y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
            x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
            x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
            transformed.append((x + cx, y + cy, z + cz))
        self.add_solid(transformed, faces, ATLAS_TILES[tile])

    def rod_between(self, start, end, thickness, tile="rust"):
        a, b = Vector(start), Vector(end)
        delta = b - a
        center = (a + b) * 0.5
        # Make a beam along local Z; its box is solid exposed reinforcement.
        rotation = delta.to_track_quat("Z", "Y").to_euler()
        self.box(center, (thickness, thickness, delta.length), tile, rotation)

    def cylinder(self, center, radius, height, sides=10, tile="concrete", top_radius=None):
        top_radius = radius if top_radius is None else top_radius
        cx, cy, cz = center
        phase = self.rng.uniform(0, math.tau)
        verts = []
        for z, rad in ((cz - height / 2, radius), (cz + height / 2, top_radius)):
            for i in range(sides):
                angle = phase + math.tau * i / sides
                verts.append((cx + math.cos(angle) * rad, cy + math.sin(angle) * rad, z))
        faces = [tuple(reversed(range(sides))), tuple(range(sides, sides * 2))]
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((i, j, sides + j, sides + i))
        self.add_solid(verts, faces, ATLAS_TILES[tile])

    def damaged_wall(self, x0, x1, z0, z1, y, depth, hole=None, tile="plaster"):
        """Build split wall panels with a visibly ragged aperture, not a box slab."""
        if hole is None:
            # Bent top contour plus a chunked missing upper corner.
            width = x1 - x0
            self.prism([(x0, z0), (x0 + width * .46, z0), (x0 + width * .44, z1 * .83),
                        (x0 + width * .34, z1 * .93), (x0 + width * .22, z1 * .75),
                        (x0 + width * .12, z1 * .88), (x0, z1 * .68)], depth, y, tile)
            self.prism([(x0 + width * .75, z0), (x1, z0), (x1, z1 * .70),
                        (x1 - width * .12, z1 * .82), (x1 - width * .20, z1 * .56)], depth, y, tile)
            return
        hx0, hx1, hz0, hz1 = hole
        # Four independently fractured strips enclose an irregular room/window breach.
        self.prism([(x0, z0), (hx0, z0), (hx0 - .05, hz1 * .54), (hx0 + .09, hz1),
                    (hx0 - .03, hz1 + .12), (x0, z1 * .85), (x0 - .04, z1 * .68)],
                   depth, y, tile)
        self.prism([(hx1, z0), (x1, z0), (x1 + .04, z1 * .69), (x1 - .08, z1 * .84),
                    (hx1 + .03, hz1 + .08), (hx1 - .10, hz1 * .50)], depth, y, tile)
        self.prism([(hx0, hz1), (hx0 + .12, hz1 + .09), (hx1 - .12, hz1 - .04),
                    (hx1, hz1), (x1 * .98, z1), (x0 * .98, z1 * .91)],
                   depth, y, tile)
        self.prism([(hx0, z0), (hx1, z0), (hx1 - .08, hz0 + .08),
                    (hx0 + .10, hz0 - .04)], depth, y, "brick")


def create_atlas():
    image = bpy.data.images.new("DestroyedSharedAtlas", width=ATLAS_SIZE, height=ATLAS_SIZE, alpha=False)
    pixels = array("f")
    tile_size = ATLAS_SIZE // ATLAS_GRID
    for y in range(ATLAS_SIZE):
        ty, local_y = divmod(y, tile_size)
        for x in range(ATLAS_SIZE):
            tx, local_x = divmod(x, tile_size)
            tile = ty * ATLAS_GRID + tx
            base = TILE_COLORS[tile % len(TILE_COLORS)]
            # Deterministic layered mineral stains, aggregate grain and fine pits.
            n = math.sin(local_x * .095 + tile * 2.1) * math.sin(local_y * .077 + tile * .7)
            n += math.sin(local_x * .031 - local_y * .043 + tile) * .65
            n += math.sin(local_x * .43 + math.cos(local_y * .21) * 2.0) * .17
            h = ((local_x * 73856093 ^ local_y * 19349663 ^ tile * 83492791) & 255) / 255.0 - .5
            variation = n * .065 + h * .11
            # Hairline cracks and eroded scratch marks.
            scratch = (local_x * 17 + local_y * 31 + tile * 11) % 197
            if scratch < 2:
                variation -= .105
            if abs(math.sin(local_x * .026 + math.sin(local_y * .043) * 1.7 + tile)) < .013:
                variation -= .19
            # Brick has visible mortar courses and offset vertical joints.
            if tile == ATLAS_TILES["brick"]:
                if local_y % 26 < 3 or (local_x + (13 if (local_y // 26) % 2 else 0)) % 39 < 3:
                    variation -= .17
            # Concrete aggregate flecks and plaster mottling.
            if tile in (0, 1, 5, 6) and (local_x * 19 + local_y * 43) % 127 < 4:
                variation += .11
            for channel, color in enumerate(base):
                pixels.append(min(1.0, max(0.0, color + variation * (1.0 if channel != 2 else .88))))
            pixels.append(1.0)
    image.pixels.foreach_set(pixels)
    image.file_format = "PNG"
    image.filepath_raw = str(FBX_DIR / "sharedatlastex.png")
    image.save()
    return image


def uv_tile(tile_index, corner_index, count):
    tile_size = 1.0 / ATLAS_GRID
    col, row = tile_index % ATLAS_GRID, tile_index // ATLAS_GRID
    inset = tile_size * 0.025
    coords = ((0.08, 0.08), (0.92, 0.08), (0.92, 0.92), (0.08, 0.92))
    if count == 3:
        coords = ((0.08, 0.08), (0.92, 0.08), (0.50, 0.92))
    elif count != 4:
        angle = math.tau * corner_index / count
        coords = tuple((0.5 + 0.42 * math.cos(angle), 0.5 + 0.42 * math.sin(angle))
                       for angle in (math.tau * i / count for i in range(count)))
    u, v = coords[corner_index]
    return (col * tile_size + inset + u * (tile_size - 2 * inset),
            row * tile_size + inset + v * (tile_size - 2 * inset))


def make_object(builder, key, atlas):
    mesh = bpy.data.meshes.new(f"{key}_DestroyedGeometry")
    mesh.from_pydata(builder.vertices, [], builder.faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, tile in zip(mesh.polygons, builder.face_tiles):
        for corner_index, loop_index in enumerate(polygon.loop_indices):
            uv_layer.data[loop_index].uv = uv_tile(tile, corner_index, len(polygon.loop_indices))
        polygon.material_index = 0
    mesh.materials.clear()
    material = bpy.data.materials.new(f"{key}_weathered_atlas")
    material.diffuse_color = (0.49, 0.44, 0.36, 1.0)
    material.use_nodes = True
    material.node_tree.nodes.clear()
    output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
    shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Roughness"].default_value = 0.92
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.name = "RuntimeAlbedoTexture"
    texture.image = atlas
    uv = material.node_tree.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    material.node_tree.links.new(uv.outputs["UV"], texture.inputs["Vector"])
    material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    material.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    mesh.materials.append(material)
    obj = bpy.data.objects.new(key, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    # Bake a true bottom-center origin into mesh vertices; no object-level
    # correction transform is left for Unity/FBX consumers.
    bounds = mesh_bounds(mesh)
    low, high = bounds
    center_x, center_y = (low.x + high.x) / 2, (low.y + high.y) / 2
    if abs(center_x) > 1e-7 or abs(center_y) > 1e-7 or abs(low.z) > 1e-7:
        for vertex in mesh.vertices:
            vertex.co.x -= center_x
            vertex.co.y -= center_y
            vertex.co.z -= low.z
        mesh.update()
    return obj


def mesh_bounds(mesh):
    xs = [vertex.co.x for vertex in mesh.vertices]
    ys = [vertex.co.y for vertex in mesh.vertices]
    zs = [vertex.co.z for vertex in mesh.vertices]
    return (Vector((min(xs), min(ys), min(zs))),
            Vector((max(xs), max(ys), max(zs))))


def add_ground_rubble(builder, width, depth, count, severe, seed_offset=0):
    rng = builder.rng
    amount = count + (3 if severe else 0)
    for i in range(amount):
        x = rng.uniform(-width * .40, width * .40)
        y = rng.uniform(-depth * .40, depth * .40)
        scale = rng.choices((.48, .78, 1.15, 1.65), (14, 32, 36, 18))[0] * rng.uniform(.78, 1.20)
        z = scale * .24 + rng.uniform(0.0, .10)
        builder.rock((x, y, z), (scale * 1.55, scale * rng.uniform(.65, 1.2), scale * .78))
    # A few unmistakable snapped floor plates mixed through rubble.
    for i in range(2 if severe else 1):
        x = rng.uniform(-width * .36, width * .36)
        y = rng.uniform(-depth * .35, depth * .35)
        builder.broken_plate((x, y, .38 + i * .2), (rng.uniform(1.1, 2.0), rng.uniform(.5, 1.1), .24),
                    (rng.uniform(-.45, .45), rng.uniform(-.4, .4), rng.uniform(-.7, .7)), "concrete")


def build_region(key, district, archetype, floors, damage, width, depth, seed):
    b = MeshBuilder(seed)
    rng = b.rng
    height = floors * 3.0
    halfx, halfy = width / 2, depth / 2
    x_bays = max(2, min(2 if floors >= 6 else 4, round(width / 4.2)))
    grid_x = [-halfx + i * width / x_bays for i in range(x_bays + 1)]
    grid_y = [-halfy, halfy]
    # Wrecked reinforced-concrete skeleton: selective columns and severed beams,
    # intentionally missing bays and upper-floor supports.
    missing_column = {
        "partial_frame": {(1, 2, floors - 1)},
        "corner_collapse": {(0, 0, floors - 1), (0, 0, floors - 2)},
        "broken_front": {(len(grid_x) - 1, 0, floors - 1)},
        "shopfront_breach": {(0, 0, 0), (1, 0, 0)},
        "slab_collapse": {(len(grid_x) - 1, 2, floors - 1), (len(grid_x) - 1, 2, floors - 2)},
        "stone_breach": {(0, 0, floors - 1)},
        "frame_shear": {(len(grid_x) - 1, 0, floors - 1), (0, 2, floors - 2)},
        "party_wall_failure": {(0, 2, floors - 1)},
        "roof_and_corner_loss": {(0, 0, floors - 1), (0, 0, floors - 2)},
        "balcony_collapse": {(0, 0, floors - 1), (len(grid_x) - 1, 2, floors - 1)},
        "curtainwall_failure": {(0, 0, floors - 2), (len(grid_x) - 1, 2, floors - 1)},
        "terrace_shear": {(len(grid_x) - 1, 2, floors - 1), (0, 0, floors - 2)},
        "wing_collapse": {(len(grid_x) - 1, 2, floors - 1), (len(grid_x) - 1, 2, floors - 2)},
    }[damage]
    for floor in range(floors):
        base_z = floor * 3.0
        surviving_fraction = 0.34 if floors >= 6 else (
            0.40 if damage in ("slab_collapse", "wing_collapse", "frame_shear", "terrace_shear") else .52
        )
        for ix, x in enumerate(grid_x):
            for iy, y in enumerate(grid_y):
                if floors >= 6 and 0 < ix < len(grid_x) - 1 and (ix + floor) % 2 == 0:
                    continue
                if (ix, iy, floor) in missing_column:
                    continue
                if floor >= floors - 2 and rng.random() < .38:
                    continue
                col_w = rng.uniform(.32, .48)
                # Columns have chipped/scorched bases and exposed bars on broken ends.
                column_h = rng.uniform(1.85, 2.92) if floor >= floors - 2 else rng.uniform(2.55, 2.98)
                b.box((x, y, base_z + column_h / 2), (col_w, col_w, column_h), "concrete",
                      (rng.uniform(-.025, .025), rng.uniform(-.03, .03), 0))
                if floor == floors - 1 or (ix + iy + floor) % 4 == 0:
                    for bar in range(2):
                        bx = x + (bar - .5) * col_w * .44
                        by = y + rng.uniform(-.06, .06)
                        endpoint = (bx + rng.uniform(-.22, .23), by + rng.uniform(-.18, .16),
                                    base_z + 3.2 + rng.uniform(.10, .60))
                        b.rod_between((bx, by, base_z + 2.62), endpoint, .035, "rust")
        # Broken girders, interrupted over visibly failed bays.
        for y_index, y in enumerate(grid_y):
            if floors >= 9 and (floor + y_index) % 5 != 0:
                continue
            if 8 <= floors < 9 and (floor + y_index) % 3 != 0:
                continue
            if 6 <= floors < 8 and (floor + y_index) % 2 == 1:
                continue
            for ix in range(len(grid_x) - 1):
                if (ix + floor + (0 if y < 0 else 1)) % 3 == 1 and floor >= floors - 2:
                    continue
                if floor < floors - 1 and rng.random() < .12:
                    continue
                xmid = (grid_x[ix] + grid_x[ix + 1]) / 2
                span = grid_x[ix + 1] - grid_x[ix]
                b.box((xmid, y, base_z + 2.87), (span + .35, .30, .34), "concrete",
                      (rng.uniform(-.04, .04), rng.uniform(-.03, .03), 0), cut_top=True)
        # Leaning and fractured floor plates leave the room cross-section exposed.
        if floor == floors - 1 or rng.random() < surviving_fraction:
            slab_w = width * (rng.uniform(.58, .84) if floor >= floors - 2 else .76)
            slab_d = depth * (rng.uniform(.55, .82) if floor >= floors - 2 else .74)
            center_x = rng.uniform(-width * .08, width * .08)
            center_y = rng.uniform(-depth * .07, depth * .07)
            rotation = (rng.uniform(-.18, .18), rng.uniform(-.13, .20), rng.uniform(-.08, .08))
            b.broken_plate((center_x, center_y, base_z + 2.99), (slab_w, slab_d, .27),
                           rotation, "concrete")
            # A jagged, sloped broken plate edge beneath the silhouette.
            if floor >= floors - 2 and damage not in ("partial_frame",):
                span = slab_w * rng.uniform(.23, .38)
                cx = center_x + rng.uniform(-slab_w * .2, slab_w * .2)
                b.prism([(cx - span / 2, base_z + 2.72), (cx + span / 2, base_z + 2.75),
                         (cx + span * .35, base_z + 2.94), (cx - span * .30, base_z + 3.05)],
                        slab_d * .4, center_y, "brick",
                        (rng.uniform(-.17, .17), rng.uniform(-.12, .15), 0))
        # Exposed facade remnants: intentionally interrupted wall panels with holes.
        wall_y = -halfy + .08
        skip_facade_floor = floors >= 6 and floor > 0 and floor < floors - 1 and floor % 4 != 0
        for segment in range(len(grid_x) - 1):
            if skip_facade_floor:
                continue
            if floor == floors - 1 and rng.random() < .62:
                continue
            if floor > 0 and floor % 2 == 1 and rng.random() < .48:
                continue
            x0, x1 = grid_x[segment] + .10, grid_x[segment + 1] - .10
            z0, z1 = base_z + .15, base_z + 2.78
            if archetype == "heritage_stone_arches" and floor == 0:
                # Deep stone arch remains framing a fractured lower entrance.
                mid = (x0 + x1) / 2
                radius = (x1 - x0) * .29
                b.damaged_wall(x0, x1, z0, z1, wall_y, .42,
                               hole=(mid - radius, mid + radius, z0 + .18, z0 + 1.85), tile="stone")
                for step in range(7):
                    angle = math.pi * step / 6
                    bx = mid + radius * math.cos(angle)
                    bz = z0 + 1.80 + radius * .58 * math.sin(angle)
                    b.box((bx, wall_y - .24, bz), (.29, .38, .27), "stone",
                          (0, 0, angle - math.pi / 2))
            elif archetype in ("modern_tower", "hotel_wing", "coastal_hotel_wing"):
                # A torn window mullion / curtainwall remnant, without intact glazing.
                if segment % 2 == 0:
                    b.damaged_wall(x0, x1, z0, z1, wall_y, .20,
                                   hole=(x0 + .24, x1 - .18, z0 + .50, z0 + 2.05), tile="plaster")
                for mullion in (x0 + .08, x1 - .08):
                    b.box((mullion, wall_y - .13, base_z + 1.35), (.10, .12, 2.5), "rust")
            elif segment == (floor + (1 if damage == "shopfront_breach" else 0)) % (len(grid_x) - 1):
                b.damaged_wall(x0, x1, z0, z1, wall_y, .25,
                               hole=(x0 + (x1 - x0) * .34, x0 + (x1 - x0) * .68,
                                     z0 + .62, z0 + 1.95), tile="brick" if floor == 0 else "plaster")
            elif (segment + floor) % 3 != 0:
                b.damaged_wall(x0, x1, z0, z1, wall_y, .22, tile="plaster")
        # Side wall patches create actual broken/exposed rooms, not a closed shell.
        sidewall_due = floor % (4 if floors >= 6 else 2) == 0
        if sidewall_due or archetype in ("family_courtyard", "coastal_terraces"):
            for sign in (-1, 1):
                x = sign * (halfx - .08)
                x0, x1 = x - .12, x + .12
                b.box(((x0 + x1) / 2, halfy * .43, base_z + 1.25),
                      (.22, depth * rng.uniform(.34, .58), 2.35), "plaster",
                      (rng.uniform(-.08, .08), 0, rng.uniform(-.05, .05)), cut_top=True)

        # Balcony bands make the tower and terrace types visually unmistakable.
        balcony_period = 3 if floors >= 6 else 2
        if archetype in ("balcony_tower", "coastal_terraces", "hotel_wing", "coastal_hotel_wing") and floor % balcony_period == 0:
            projection = 1.0 if archetype == "balcony_tower" else 1.35
            b.box((0, -halfy - projection / 2, base_z + 2.75),
                  (width * .72, projection, .20), "concrete",
                  (rng.uniform(-.08, .10), rng.uniform(-.12, .08), rng.uniform(-.025, .025)))
            # broken parapet fragments instead of tidy complete railings
            for dx in (-width * .25, width * .18):
                b.box((dx, -halfy - projection * .82, base_z + 3.06),
                      (width * .22, .12, .48), "plaster", (0, 0, rng.uniform(-.08, .08)))
        if archetype == "mixed_shopfront" and floor == 0:
            b.box((0, -halfy - .17, .82), (width * .78, .24, .23), "stone")
        # Burn scars on exposed room interiors and impact-struck beams.
        if floor == floors - 1 or floor == max(0, floors // 2):
            b.box((rng.uniform(-width * .22, width * .22), -halfy + .24, base_z + 1.65),
                  (rng.uniform(1.2, 2.8), .028, rng.uniform(.8, 1.3)), "scorch")

    # Bent rebar extensions and dangling reinforcement concentrated at jagged roofline.
    rebar_count = (1 + floors // 3) if floors >= 6 else (3 + floors)
    for i in range(rebar_count):
        x = rng.uniform(-halfx * .82, halfx * .82)
        y = rng.choice((-halfy * .88, 0, halfy * .88))
        z = height + rng.uniform(-.12, .22)
        end = (x + rng.uniform(-.45, .45), y + rng.uniform(-.25, .25), z + rng.uniform(.25, .95))
        b.rod_between((x, y, z - .35), end, rng.uniform(.025, .045), "rust")

    # Two large, irregular, visibly leaning plates break the stacked-floor
    # silhouette on every regional asset; broken edges remain readable at phone scale.
    b.broken_plate((-width * .22, -depth * .16, height * .40),
                   (width * .66, depth * .34, .42),
                   (rng.uniform(.43, .72), rng.uniform(-.44, .24), rng.uniform(-.12, .12)))
    b.broken_plate((width * .27, depth * .10, height * .68),
                   (width * .48, depth * .30, .36),
                   (rng.uniform(-.58, -.32), rng.uniform(.35, .66), rng.uniform(-.14, .14)),
                   "stone" if archetype == "heritage_stone_arches" else "concrete")
    for j in range(4):
        start = (halfx * .1 + j * .24, -halfy * .15, height * .54)
        b.rod_between(start, (start[0] + .55, start[1] - .35, start[2] + .6), .035)

    severe = damage in ("slab_collapse", "frame_shear", "wing_collapse", "terrace_shear",
                        "corner_collapse", "roof_and_corner_loss", "balcony_collapse")
    add_ground_rubble(b, width, depth, 29 if severe else 25, severe)
    return b, {"districtId": key.removeprefix("ruin_"), "category": archetype,
               "floors": floors, "damageType": damage, "width": width, "depth": depth}


def build_mosque(seed):
    b = MeshBuilder(seed)
    # Damaged prayer hall with openings, fractured shallow dome and snapped minaret.
    for x in (-5.4, 5.4):
        for y in (-4.1, 4.1):
            b.box((x, y, 3.0), (.55, .55, 5.9), "stone", cut_top=True)
    b.box((0, 0, 2.86), (9.8, 7.7, .30), "concrete", (.02, -.07, 0))
    b.damaged_wall(-5.1, 5.1, .16, 5.6, -4.03, .40, hole=(-1.7, 1.5, .2, 2.9), tile="stone")
    b.damaged_wall(-5.2, 5.0, .15, 4.8, 4.0, .38,
                   hole=(-1.5, 1.8, 1.0, 3.2), tile="plaster")
    b.damaged_wall(-5.1, 5.0, .15, 4.7, 0, 8.1, tile="stone")
    # Broken dome shell: several polygon rings, deliberately missing one arc.
    cx, cy = 0.0, 0.0
    rings = []
    for ring_index, (radius, z) in enumerate(((2.7, 5.5), (2.45, 6.35), (1.85, 7.1), (1.0, 7.68))):
        points = []
        for i in range(12):
            angle = math.tau * i / 12
            points.append((cx + math.cos(angle) * radius, cy + math.sin(angle) * radius,
                           z + (0.13 * math.sin(angle * 3) if ring_index == 1 else 0)))
        rings.append(points)
    for row in range(3):
        for i in range(12):
            if (row == 1 and 8 <= i <= 10) or (row == 2 and i in (8, 9)):
                continue
            j = (i + 1) % 12
            # slanted ring sections add broken dome panel silhouette
            b.add_solid([rings[row][i], rings[row][j], rings[row + 1][j], rings[row + 1][i]],
                        [(0, 1, 2, 3)], ATLAS_TILES["stone"] if row == 0 else ATLAS_TILES["plaster"])
    b.cylinder((0, 0, 7.74), 1.05, .26, 12, "stone", .65)
    # Tapered minaret with fractured crown and missing vertical sector.
    b.cylinder((4.4, 2.2, 5.7), .68, 10.8, 10, "stone", .42)
    b.box((4.4, 2.2, 9.9), (1.45, 1.35, .24), "stone")
    for i in range(7):
        a = i * math.tau / 7
        b.box((4.4 + math.cos(a) * .5, 2.2 + math.sin(a) * .5, 10.25),
              (.16, .16, .8), "plaster", (0, 0, a))
    b.cylinder((4.4, 2.2, 11.0), .40, 1.5, 8, "stone", .08)
    b.box((3.9, 2.15, 11.8), (.36, .25, 1.5), "rust", (0, .28, -.2))
    add_ground_rubble(b, 13, 11, 14, True)
    return b, {"districtId": None, "category": "mosque", "floors": None,
               "damageType": "fractured_dome_and_minaret"}


def build_school(seed):
    b = MeshBuilder(seed)
    for floor in range(2):
        z = floor * 3.0
        for x in (-6.5, -3.2, 0, 3.2, 6.5):
            b.box((x, 0, z + 1.45), (.38, .4, 2.9), "concrete", cut_top=(floor == 1 and x > 3))
        for x0, x1 in ((-6.3, -3.3), (-3.0, 0), (3.4, 6.3)):
            b.damaged_wall(x0, x1, z + .12, z + 2.75, -3.1, .24,
                           hole=(x0 + .9, x1 - .7, z + .7, z + 2.0), tile="plaster")
        b.box((-1.8, .2, z + 2.88), (6.8, 5.8, .27), "concrete",
              (.10 if floor else 0, -.20 if floor else .04, .03))
    # Exposed classroom partition edges, broken stair landing and beam fragments.
    for i in range(5):
        b.box((-5.2 + i * 2.3, 1.2, 1.25), (1.8, .16, 2.3), "brick",
              (0, rng_uniform(seed, i, -.12, .16), rng_uniform(seed, i + 1, -.08, .08)), cut_top=True)
    b.box((2.0, -1.0, 2.0), (5.6, 1.0, .28), "concrete", (.38, -.34, .1))
    add_ground_rubble(b, 15, 11, 14, True)
    return b, {"districtId": None, "category": "school", "floors": 2,
               "damageType": "collapsed_classroom_wing"}


def rng_uniform(seed, index, low, high):
    return random.Random(seed * 17 + index * 101).uniform(low, high)


def build_clinic(seed):
    b = MeshBuilder(seed)
    b.box((0, 0, .12), (10.4, 8.4, .24), "concrete")
    # Generic unbranded damaged clinic: no legible marks, logos, or official identity.
    for floor in range(3):
        z = floor * 3
        for x in (-4.7, -1.6, 1.6, 4.7):
            if not (floor == 2 and x > 1):
                b.box((x, 0, z + 1.45), (.40, .42, 2.9), "concrete", cut_top=(floor == 2))
        b.box((0, 0, z + 2.9), (9.8 if floor != 2 else 6.2, 7.7 if floor != 2 else 6.0, .3),
              "concrete", (0, .05 if floor == 2 else 0, 0))
        b.damaged_wall(-4.5, 4.4, z + .1, z + 2.6, -3.85, .22,
                       hole=(-.9, 1.3, z + .5, z + 2.2), tile="plaster")
    b.box((2.2, 0, 5.6), (5.0, 2.2, .36), "concrete", (.34, -.30, .04))
    add_ground_rubble(b, 11, 9, 12, True)
    return b, {"districtId": None, "category": "clinic", "floors": 3,
               "damageType": "destroyed_generic_health_building"}


def build_civic(seed):
    b = MeshBuilder(seed)
    # Collapsed civic frontage: wide public colonnade and fallen entablature.
    for x in (-5.2, -2.6, 0, 2.6, 5.2):
        b.box((x, -3.1, 2.7), (.45, .45, 5.3), "stone", (0, 0, .02))
    b.damaged_wall(-5.4, 5.4, .18, 5.2, 3.0, .34,
                   hole=(-2.9, 2.0, 1.0, 3.6), tile="plaster")
    b.box((-1.7, -3.1, 5.1), (5.5, .75, .50), "stone", (.08, -.30, -.04))
    b.box((2.0, -2.8, 1.3), (7.2, 1.0, .4), "concrete", (.15, -.42, .12))
    b.box((0, .3, 5.8), (8.2, 5.2, .3), "concrete", (.12, .08, .04))
    add_ground_rubble(b, 12, 10, 14, True)
    return b, {"districtId": None, "category": "civic", "floors": 2,
               "damageType": "collapsed_civic_frontage"}


def build_wall(seed):
    b = MeshBuilder(seed)
    b.damaged_wall(-4.3, 4.3, .12, 3.8, 0, .75,
                   hole=(-1.4, 1.25, .2, 2.9), tile="brick")
    # Alternating broken coping blocks and exposed rebar at the breached edges.
    for x in (-4.25, -3.1, 3.2, 4.15):
        b.box((x, 0, 3.7 + rng_uniform(seed, int(x * 10), -.2, .2)),
              (1.1, .86, .35), "stone", (0, rng_uniform(seed, int(x * 11), -.15, .15), 0))
    for x in (-1.4, 1.3):
        for y in (-.25, .25):
            b.rod_between((x, y, 2.8), (x + rng_uniform(seed, int(x * 8), -.28, .3),
                                         y + .14, 4.35), .04, "rust")
    for i in range(24):
        b.rock((rng_uniform(seed, i, -4.4, 4.4), rng_uniform(seed, i + 30, -1.4, 1.4),
                rng_uniform(seed, i + 50, .1, .4)),
               (rng_uniform(seed, i + 60, .35, 1.0), rng_uniform(seed, i + 70, .25, .7), .45))
    return b, {"districtId": None, "category": "wall", "floors": None,
               "damageType": "ragged_breached_masonry"}


def build_car(seed):
    b = MeshBuilder(seed)
    # Distinct compact scorched car silhouette; no smooth showroom shell.
    b.box((0, 0, .68), (4.2, 1.72, .78), "metal", (.02, -.03, .01), cut_top=True)
    b.box((-.10, .02, 1.28), (2.40, 1.48, .82), "scorch", (0, -.09, -.03), cut_top=True)
    # Roof remnant and collapsed hood panels.
    b.box((-.12, .05, 1.73), (1.65, 1.35, .16), "metal", (.12, .08, -.04))
    b.box((-1.45, -.05, 1.02), (1.1, 1.72, .16), "rust", (.08, -.10, .02))
    for x in (-1.35, 1.35):
        for y in (-.92, .92):
            b.cylinder((x, y, .47), .43, .24, 10, "scorch", .40)
            b.cylinder((x, y, .47), .19, .27, 8, "rust", .19)
    # Broken door / twisted frame / exposed chassis.
    b.box((.35, -.92, .85), (1.25, .12, .58), "metal", (0, -.28, -.18))
    b.rod_between((-1.2, -.6, 1.55), (.5, -.6, 1.94), .055, "rust")
    b.rod_between((.65, .58, 1.45), (1.30, .85, 1.95), .05, "rust")
    for i in range(16):
        b.rock((rng_uniform(seed, i, -2.5, 2.5), rng_uniform(seed, i + 16, -1.5, 1.5),
                rng_uniform(seed, i + 32, .08, .22)),
               (rng_uniform(seed, i + 48, .2, .55), rng_uniform(seed, i + 64, .15, .45),
                rng_uniform(seed, i + 80, .12, .28)), "soil")
    return b, {"districtId": None, "category": "car", "floors": None,
               "damageType": "burnt_scorched_vehicle_wreck"}


def build_crater(seed):
    b = MeshBuilder(seed)
    rng = b.rng
    rings = []
    # Static raised rim and bowl surface. This is a standalone ground mesh, not
    # destructive terrain editing or a heightmap cutout.
    for ring_id, (radius, z) in enumerate(((3.55, .15), (3.15, .78), (2.3, .92),
                                           (1.55, .28), (.65, .10))):
        ring = []
        for i in range(20):
            angle = math.tau * i / 20
            wobble = rng.uniform(.86, 1.14)
            ring.append((math.cos(angle) * radius * wobble, math.sin(angle) * radius * wobble,
                         z + (rng.uniform(-.10, .14) if ring_id in (1, 2) else rng.uniform(-.04, .04))))
        rings.append(ring)
    for row in range(len(rings) - 1):
        for i in range(20):
            j = (i + 1) % 20
            b.add_solid([rings[row][i], rings[row][j], rings[row + 1][j], rings[row + 1][i]],
                        [(0, 1, 2, 3)], ATLAS_TILES["soil"] if row < 2 else ATLAS_TILES["concrete"])
    center = (0, 0, .08)
    center_index = len(b.vertices)
    b.vertices.append(center)
    for i in range(20):
        b.faces.append((center_index, 4 * 20 + i, 4 * 20 + (i + 1) % 20))
        b.face_tiles.append(ATLAS_TILES["soil"])
    for i in range(18):
        angle = rng.uniform(0, math.tau)
        radius = rng.uniform(3.0, 4.1)
        size = rng.uniform(.24, .70)
        b.rock((math.cos(angle) * radius, math.sin(angle) * radius, .18),
               (size * 1.5, size, size * .65), rng.choice(("soil", "stone", "concrete")))
    return b, {"districtId": None, "category": "crater", "floors": None,
               "damageType": "static_soil_rim_bowl"}


def build_debris(seed):
    b = MeshBuilder(seed)
    rng = b.rng
    # Layered mound of differently scaled fractured solids plus twisted metal.
    for i in range(34):
        x = rng.uniform(-3.8, 3.8)
        y = rng.uniform(-3.2, 3.2)
        base = max(.18, 2.15 * (1.0 - (x / 4.4) ** 2) * (1.0 - (y / 3.9) ** 2))
        height = rng.uniform(.25, .95)
        scale = rng.choices((.24, .43, .70, 1.0, 1.45), (15, 28, 29, 20, 8))[0]
        b.rock((x, y, base + height * .28),
               (scale * rng.uniform(.65, 1.45), scale * rng.uniform(.55, 1.2), height),
               rng.choice(("concrete", "brick", "stone", "soil")))
    for i in range(8):
        x = rng.uniform(-2.8, 2.8)
        y = rng.uniform(-2.3, 2.3)
        z = rng.uniform(.6, 1.8)
        end = (x + rng.uniform(-1.1, 1.1), y + rng.uniform(-.9, .9), z + rng.uniform(-.35, .65))
        b.rod_between((x, y, z), end, rng.uniform(.035, .075), "rust" if i % 3 else "metal")
    for i in range(6):
        b.broken_plate((rng.uniform(-3, 3), rng.uniform(-2, 2), rng.uniform(.55, 1.45)),
              (rng.uniform(.8, 1.7), rng.uniform(.28, .7), .18),
              (rng.uniform(-.40, .40), rng.uniform(-.48, .48), rng.uniform(-.8, .8)), "concrete")
    return b, {"districtId": None, "category": "debris", "floors": None,
               "damageType": "mixed_fractured_concrete_brick_metal_pile"}


CONTEXT_KEYS = ["ruin_context_collapse", "ruin_context_shell",
                "ruin_context_pancake", "ruin_context_masonry"]


def build_reference_region(row, seed):
    """Grounded partial shell plus a dense collapsed foreground, not clean frames."""
    key, district, archetype, floors, damage, width, depth = row
    b = MeshBuilder(seed)
    rng = b.rng
    # One grounded breached rear fragment identifies the former building.
    # Tall districts retain a damaged corner, not floating disconnected storeys.
    remaining = min(floors * 2.2, 17.0)
    wall_y = depth * .28
    wall_material = "stone" if archetype in ("historic", "stone") else "plaster"
    b.damaged_wall(-width * .36, width * .34, 0, remaining, wall_y, .38,
                   hole=(-width * .14, width * .17, .7, min(3.4, remaining * .6)),
                   tile=wall_material)
    b.box((-width * .35, depth * .09, remaining * .23),
          (.42, depth * .43, remaining * .46), "brick", cut_top=True)
    # Slabs lie on one another with varied tilt and lateral offsets. Grounded
    # piles are visibly compact, rather than an orderly unfinished floor frame.
    for i in range(min(floors + 2, 7)):
        z = .5 + i * .43
        b.broken_plate((rng.uniform(-width * .14, width * .14),
                        rng.uniform(-depth * .19, depth * .02), z),
                       (width * rng.uniform(.38, .62), depth * rng.uniform(.35, .54), .32),
                       (rng.uniform(-.16, .2), rng.uniform(-.18, .22), rng.uniform(-.55, .55)))
    if floors >= 4:
        # An attached torn remnant bears on the standing rear wall.
        b.broken_plate((width * .08, wall_y - .45, remaining * .57),
                       (width * .47, depth * .22, .3), (.10, -.08, .13))
    for i in range(42):
        x, y = rng.uniform(-width * .46, width * .46), rng.uniform(-depth * .44, depth * .4)
        s = rng.choices((.32, .65, 1.0, 1.5, 2.0), (10, 27, 33, 20, 10))[0]
        b.rock((x, y, s * .32 + rng.uniform(0, .22)),
               (s * 1.3, s * .85, s * .64), rng.choice(("concrete", "concrete", "brick", "stone")))
    # Tilted fractured floor plates with projecting bent reinforcing bars.
    for i in range(3):
        x = -width * .22 + i * width * .2
        y = -depth * .3
        b.broken_plate((x, y, .75), (2.2, 1.8, .24), (.2, .35, i * .4))
        start = (x + .2, y + .3, 1.2)
        joint = (x + .15, y + .45, 1.7)
        b.rod_between(start, joint, .045)
        b.rod_between(joint, (x + .5, y + .6, 1.9), .045)
    return b, {"districtId": district, "category": archetype, "floors": floors,
               "damageType": "grounded_breached_shell_compact_collapsed_slabs_dense_mixed_rubble",
               "visualReference": "User supplied collapse piles, exposed rebar and destroyed masonry"}


def build_context(index):
    """Reference-led original background LODs, not intact imported buildings."""
    b = MeshBuilder(92017 + index * 431)
    rng = b.rng
    for i in range(18 if index != 1 else 10):
        x, y = rng.uniform(-3.1, 3.1), rng.uniform(-2.5, 2.5)
        mound = max(.12, (1 - (x / 3.8)**2) * (1 - (y / 3.2)**2))
        s = rng.uniform(.45, 1.25)
        b.rock((x, y, mound * (1.1 if index == 0 else .45)),
               (s, s * .75, s * .6), "brick" if index == 3 and i % 3 else "concrete")
    if index == 0:
        for i in range(3):
            b.broken_plate((i * 1.15 - 1.2, .2, 1.05 + i * .15), (2.8, 1.8, .25),
                           (.1 + i * .12, -.12, i * .5))
        for i in range(4):
            b.rod_between((-.8 + i * .3, .8, 1.5), (-.6 + i * .3, 1.1, 2.2), .045)
    elif index == 1:
        b.damaged_wall(-3, 3, 0, 5.6, 1.7, .28, hole=(-1.5, 1.1, .8, 3.8))
        b.damaged_wall(-3, .7, 0, 3.6, -1.7, .25, tile="brick")
        for i in range(3):
            b.broken_plate((-.7, .1, .7 + i * .55), (3.8, 2.5, .25), (.23, -.12 * i, .12))
        for i in range(3):
            b.rod_between((2.3 + i * .12, 1.7, 4.7), (2.25 + i * .12, 1.6, 5.8), .035)
    elif index == 2:
        for i in range(5):
            b.broken_plate((.22 * i, -.12 * i, .55 + i * .65), (5.8 - i * .45, 4.1, .28),
                           (.08 * i, -.10 + i * .08, .05 * i))
        b.box((-2.3, .9, 1.35), (.32, .35, 2.5), cut_top=True)
        b.rod_between((1.3, -.8, 3.5), (1.7, -.6, 4.1), .05)
    else:
        b.damaged_wall(-2.9, 2.7, 0, 2.2, 1.8, .3, tile="brick")
        b.broken_plate((-.4, .2, 1.1), (4.5, 1.7, .28), (.3, -.2, -.35))
        # Two visibly hollow broken masonry units, not solid miniature cubes.
        for x in (-1.8, 1.3):
            for side in (-1, 1):
                b.box((x, -.7 + side * .32, .6), (.95, .12, .6), "concrete")
                b.box((x + side * .42, -.7, .6), (.12, .65, .6), "concrete")
    return b, {"districtId": None, "category": "destroyed_context_lod", "contextLOD": True,
               "floors": None, "damageType": CONTEXT_KEYS[index],
               "visualReference": "User rubble, pancake-collapse and breached-wall photographs"}


def triangulate(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.update()


def triangle_count(mesh):
    return sum(max(0, len(poly.vertices) - 2) for poly in mesh.polygons)


def uv_report(mesh):
    if not mesh.uv_layers:
        raise RuntimeError("Generated mesh has no UV channel.")
    layer = mesh.uv_layers.active or mesh.uv_layers[0]
    values = [tuple(float(v) for v in item.uv) for item in layer.data]
    if not values or any(not math.isfinite(v) for uv in values for v in uv):
        raise RuntimeError("Generated UVs are empty or non-finite.")
    if any(u < -1e-5 or u > 1.00001 or v < -1e-5 or v > 1.00001 for u, v in values):
        raise RuntimeError("Generated atlas UV escaped the 0-1 range.")
    return {"layer": layer.name, "loopCount": len(values),
            "finiteLoopCount": len(values),
            "bounds": {"min": [min(v[0] for v in values), min(v[1] for v in values)],
                       "max": [max(v[0] for v in values), max(v[1] for v in values)]}}


def export_obj(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.wm.obj_export(filepath=str(path), export_selected_objects=True,
                          export_uv=True, export_normals=True, export_materials=False,
                          export_triangulated_mesh=True, forward_axis="NEGATIVE_Z",
                          up_axis="Y", global_scale=1.0, path_mode="AUTO")


def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_NONE",
                             global_scale=1.0, apply_unit_scale=True,
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True,
                             mesh_smooth_type="FACE", use_tspace=True,
                             path_mode="AUTO", embed_textures=False,
                             bake_anim=False, add_leaf_bones=False)


def bounds_dict(mesh):
    low, high = mesh_bounds(mesh)
    return {"min": [float(v) for v in low], "max": [float(v) for v in high],
            "width": float(high.x - low.x), "height": float(high.z - low.z),
            "depth": float(high.y - low.y)}


def audit_fbx(path, expected_triangles, expected_vertices, expected_bounds):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False,
                             ignore_leaf_bones=True, automatic_bone_orientation=False)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"FBX audit expected one mesh object, found {len(meshes)}: {path.name}")
    obj = meshes[0]
    triangles = triangle_count(obj.data)
    if triangles != expected_triangles or len(obj.data.vertices) != expected_vertices:
        raise RuntimeError(
            f"FBX round-trip topology mismatch for {path.name}: "
            f"{triangles}/{len(obj.data.vertices)} vs {expected_triangles}/{expected_vertices}")
    if not obj.data.uv_layers or not obj.data.uv_layers[0].data:
        raise RuntimeError(f"FBX round-trip has no UV channel: {path.name}")
    for uv_data in obj.data.uv_layers[0].data:
        if not all(math.isfinite(float(value)) for value in uv_data.uv):
            raise RuntimeError(f"FBX round-trip has non-finite UV: {path.name}")
    imported_bounds = bounds_dict(obj.data)
    for field in ("width", "height", "depth"):
        if abs(imported_bounds[field] - expected_bounds[field]) > 0.02:
            raise RuntimeError(
                f"FBX round-trip bounds mismatch for {path.name} {field}: "
                f"{imported_bounds[field]} vs {expected_bounds[field]}")
    texture_names = set()
    for material in obj.data.materials:
        if material and material.use_nodes and material.node_tree:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    texture_names.add(Path(bpy.path.abspath(node.image.filepath)).name)
    if "sharedatlastex.png" not in texture_names:
        # FBX readers may omit external texture image links. The file contract
        # still includes its authored shared atlas alongside the FBX.
        raise RuntimeError(f"FBX texture reference did not round-trip sharedatlastex.png: {path.name}")
    return {"triangles": triangles, "vertices": len(obj.data.vertices),
            "bounds": imported_bounds, "uvChannels": len(obj.data.uv_layers),
            "textures": sorted(texture_names)}


def parse_obj_audit(path):
    vertex_count = uv_count = normal_count = face_triangles = 0
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("v "):
            values = [float(value) for value in line.split()[1:4]]
            if len(values) != 3 or not all(math.isfinite(v) for v in values):
                raise RuntimeError(f"OBJ contains non-finite vertex: {path.name}")
            vertex_count += 1
        elif line.startswith("vt "):
            values = [float(value) for value in line.split()[1:3]]
            if len(values) != 2 or not all(math.isfinite(v) for v in values):
                raise RuntimeError(f"OBJ contains non-finite UV: {path.name}")
            uv_count += 1
        elif line.startswith("vn "):
            values = [float(value) for value in line.split()[1:4]]
            if len(values) != 3 or not all(math.isfinite(v) for v in values):
                raise RuntimeError(f"OBJ contains non-finite normal: {path.name}")
            normal_count += 1
        elif line.startswith("f "):
            face_triangles += len(line.split()) - 3
    if not all((vertex_count, uv_count, normal_count)) or face_triangles < 1:
        raise RuntimeError(f"OBJ audit failed for {path.name}: missing vertices, UVs, normals, or faces")
    return {"vertices": vertex_count, "uvCoordinates": uv_count,
            "normals": normal_count, "triangles": face_triangles}


def write_asset(builder, key, metadata, atlas):
    # FBX round-trip auditing resets Blender between assets; reacquire the
    # authored atlas image datablock each time rather than retaining a stale RNA ref.
    atlas = bpy.data.images.load(str(FBX_DIR / "sharedatlastex.png"), check_existing=True)
    obj = make_object(builder, key, atlas)
    triangulate(obj)
    triangles = triangle_count(obj.data)
    limit = 450 if metadata.get("contextLOD") else 2200 if key.startswith("ruin_") and metadata.get("districtId") else (
        1600 if key in ("ruin_mosque", "ruin_school", "ruin_clinic", "ruin_civic") else 900)
    if triangles > limit:
        # Retain the mobile budget when fractured plate faces add a small excess.
        modifier = obj.modifiers.new("Bounded destruction LOD", "DECIMATE")
        modifier.ratio = (limit - 12) / triangles
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        triangles = triangle_count(obj.data)
        if triangles > limit:
            raise RuntimeError(f"{key} exceeds triangle budget: {triangles}>{limit}")
    if not obj.data.vertices or any(not math.isfinite(float(v)) for vertex in obj.data.vertices for v in vertex.co):
        raise RuntimeError(f"{key} has empty or non-finite geometry.")
    bounds = bounds_dict(obj.data)
    vertex_count = len(obj.data.vertices)
    if min(bounds["width"], bounds["height"], bounds["depth"]) <= 0:
        raise RuntimeError(f"{key} has degenerate bounds: {bounds}")
    if abs(bounds["min"][2]) > 1e-5:
        raise RuntimeError(f"{key} bottom-center pivot is not at ground Z=0: {bounds['min']}")
    uv = uv_report(obj.data)
    obj_path = RESOURCE_DIR / f"{key}.obj"
    texture_path = RESOURCE_DIR / f"{key}_albedo.png"
    manifest_path = RESOURCE_DIR / f"{key}_manifest.json"
    fbx_path = FBX_DIR / f"{key}.fbx"
    export_obj(obj, obj_path)
    shutil.copy2(FBX_DIR / "sharedatlastex.png", texture_path)
    export_fbx(obj, fbx_path)
    obj_audit = parse_obj_audit(obj_path)
    if obj_audit["triangles"] != triangles:
        raise RuntimeError(f"OBJ export triangle count mismatch for {key}: {obj_audit['triangles']} != {triangles}")
    fbx_audit = audit_fbx(fbx_path, triangles, vertex_count, bounds)
    metadata = dict(metadata)
    metadata.update({
        "key": key,
        "meshPath": f"Resources/Models/{key}.obj",
        "texturePath": f"Resources/Models/{key}_albedo.png",
        "fbxPath": f"Art/DestroyedFBX/{key}.fbx",
        "triangleCount": triangles,
        "vertexCount": vertex_count,
        "bounds": bounds,
        "units": "meters",
        "pivot": "bottom-center",
        "damageOnly": True,
        "originalAuthored": True,
        "sourceType": "procedurally_authored_original_geometry",
        "referenceAssets": REFERENCE_ASSETS,
        "uvCoverage": uv,
        "objAudit": obj_audit,
        "fbxAudit": fbx_audit,
        "albedo": {"file": f"{key}_albedo.png", "sourceMaterialCount": 1,
                   "sharedAtlas": "sharedatlastex.png", "uvLayer": uv["layer"]},
    })
    # Preserve field names consumed by prepare_city_models.py manifests.
    individual = {
        "formatVersion": 1, "key": key,
        "sourceFile": "original procedural geometry (no imported source model)",
        "targetTriangles": limit, "actualTriangles": triangles,
        "triangleTolerance": 0.0, "verifiedWithinBudget": triangles <= limit,
        "vertexCount": vertex_count,
        "dimensionsNormalizedBlenderXYZ": [bounds["width"], bounds["depth"], bounds["height"]],
        "origin": {"normalizedOriginBlender": [0.0, 0.0, 0.0], "pivot": "bottom-center"},
        **metadata,
    }
    manifest_path.write_text(json.dumps(individual, indent=2) + "\n", encoding="utf-8")
    return metadata


def main():
    RESOURCE_DIR.mkdir(parents=True, exist_ok=True)
    FBX_DIR.mkdir(parents=True, exist_ok=True)
    EXPORT_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    # Clean only this task's generated exact-key deliverables; never touch
    # existing shared Resources models or any Unity .meta files.
    all_keys = [row[0] for row in REGIONS] + [row[0] for row in EXTRAS] + CONTEXT_KEYS
    for key in all_keys:
        for path in (RESOURCE_DIR / f"{key}.obj", RESOURCE_DIR / f"{key}_albedo.png",
                     RESOURCE_DIR / f"{key}_manifest.json", FBX_DIR / f"{key}.fbx"):
            if path.exists():
                path.unlink()
    atlas_path = FBX_DIR / "sharedatlastex.png"
    if atlas_path.exists():
        atlas_path.unlink()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    atlas = create_atlas()
    records = []
    for index, row in enumerate(REGIONS):
        key, district, archetype, floors, damage, width, depth = row
        builder, metadata = build_reference_region(row, 73001 + index * 811)
        record = write_asset(builder, key, metadata, atlas)
        records.append(record)
        print(f"GENERATED {key}: {record['triangleCount']} triangles, {record['vertexCount']} vertices")
    extra_builders = [build_mosque, build_school, build_clinic, build_civic,
                      build_wall, build_car, build_crater, build_debris]
    for (key, category, damage_type, _floors, _width, _depth), builder_fn in zip(EXTRAS, extra_builders):
        builder, metadata = builder_fn(81107 + len(records) * 613)
        metadata["damageType"] = damage_type
        record = write_asset(builder, key, metadata, atlas)
        records.append(record)
        print(f"GENERATED {key}: {record['triangleCount']} triangles, {record['vertexCount']} vertices")
    manifest = {
        "formatVersion": 1,
        "assetSet": "original_destroyed_only_newgaza",
        "assetCount": len(records),
        "units": "meters",
        "pivot": "bottom-center",
        "upAxis": {"FBX": "Unity Y-up", "OBJ": "Unity Y-up"},
        "destructionOnly": True,
        "originalAuthored": True,
        "referenceAssets": REFERENCE_ASSETS,
        "atlas": "testingReplic/Assets/NewGaza/Art/DestroyedFBX/sharedatlastex.png",
        "generationCommand": "blender --background --factory-startup --python tools/generate_destroyed_assets.py",
        "assets": records,
    }
    for index, key in enumerate(CONTEXT_KEYS):
        builder, metadata = build_context(index)
        record = write_asset(builder, key, metadata, atlas)
        records.append(record)
        print(f"GENERATED {key}: {record['triangleCount']} triangles")
    manifest["assetCount"] = len(records)
    EXPORT_MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"COMPLETE: {len(records)} original destroyed assets; manifest={EXPORT_MANIFEST}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"generate_destroyed_assets.py: ERROR: {type(error).__name__}: {error}", file=sys.stderr)
        raise