"""Bake articulated, consistently lit 2D equipment sprites with Blender CPU.

blender -b --factory-startup --python tools/render_machinery.py -- --proof
Drop --proof for every heading, rolling tread, loading and tipping frame.
The Meshy excavator is an existing user-supplied, separated source (read-only).
No Unity files or metadata are written.
"""
import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "art-src/machinery-frames"
OUT.mkdir(parents=True, exist_ok=True)
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
p = argparse.ArgumentParser()
p.add_argument("--proof", action="store_true")
p.add_argument("--retip", action="store_true", help="Refresh just the truck unloading take")
options = p.parse_args(args)
PROOF, RETIP = options.proof, options.retip
SIZE, ORTHO = 224, 13.2
bpy.ops.wm.open_mainfile(filepath=str(ROOT.parent / "exports/meshy_excavator/Meshy_Excavator_MobileSplit.blend"))
for o in list(bpy.context.scene.objects):
    o.animation_data_clear()
    if o.type in ("CAMERA", "LIGHT"):
        bpy.data.objects.remove(o, do_unlink=True)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 12
scene.cycles.use_denoising = True
scene.render.threads_mode = "FIXED"
scene.render.threads = 6
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Medium Low Contrast"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.72, .76, .79, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = .7


def material(name, color, metal=0, rough=.65):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bs = m.node_tree.nodes.get("Principled BSDF")
    bs.inputs["Base Color"].default_value = (*color, 1)
    bs.inputs["Metallic"].default_value = metal
    bs.inputs["Roughness"].default_value = rough
    # Fine, restrained paint grain; no manufacturer markings or large random stains.
    noise = m.node_tree.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 46
    bump = m.node_tree.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .11
    bump.inputs["Distance"].default_value = .035
    m.node_tree.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    m.node_tree.links.new(bump.outputs["Normal"], bs.inputs["Normal"])
    return m


yellow = material("Dusty ochre enamel", (.72, .45, .065), .12)
steel = material("Warm dark steel", (.11, .13, .13), .5)
rubber = material("Dusty tread rubber", (.075, .082, .076), 0, .95)
chrome = material("Hydraulic polished steel", (.48, .52, .51), .8, .28)
glass = material("Opaque muted blue cab glass", (.095, .23, .27), .28, .22)
white = material("Warm ivory cabin", (.77, .76, .68), .08)
green = material("Muted green tipper enamel", (.21, .34, .115), .12)
red = material("Rear safety lamps", (.55, .055, .025), .1)
lamp = material("Warm headlamps", (.84, .83, .68), .15)
stone = [material("Concrete rubble " + str(i), c) for i, c in enumerate([
    (.43, .43, .39), (.55, .52, .46), (.37, .38, .36), (.62, .57, .49)])]
for m in list(bpy.data.materials):
    replacement = rubber if "Rubber" in m.name else steel if "Steel" in m.name else glass if "Tinted" in m.name else yellow
    if m.name in ("Construction_Yellow", "Dark_Steel", "Material", "Opaque_Tinted_Cab", "Track_Rubber"):
        for o in scene.objects:
            if o.type == "MESH":
                for slot in o.material_slots:
                    if slot.material == m:
                        slot.material = replacement


def empty(name, parent=None, loc=(0, 0, 0)):
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.parent = parent
    o.location = loc
    return o


def finish(o, name, mat, parent, loc):
    o.name = name
    o.parent = parent
    o.location = loc
    o.data.materials.clear()
    o.data.materials.append(mat)
    return o


def box(name, loc, size, mat, parent, bevel=.035):
    bpy.ops.mesh.primitive_cube_add()
    o = finish(bpy.context.object, name, mat, parent, loc)
    o.scale = Vector(size) / 2
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        b = o.modifiers.new("Soft manufactured edge", "BEVEL")
        b.width = bevel
        b.segments = 2
        o.modifiers.new("Weighted face normals", "WEIGHTED_NORMAL")
    return o


def cylinder(name, a, b, radius, mat, parent, vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=1)
    o = finish(bpy.context.object, name, mat, parent, (Vector(a) + Vector(b)) / 2)
    o.rotation_euler = (Vector(b) - Vector(a)).to_track_quat("Z", "Y").to_euler()
    o.scale.z = (Vector(b) - Vector(a)).length
    for face in o.data.polygons:
        face.use_smooth = True
    return o


def update_cylinder(o, a, b):
    o.location = (a + b) / 2
    o.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    o.scale.z = (b - a).length


def rock(name, loc, scale, parent, seed):
    r = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1)
    o = finish(bpy.context.object, name, stone[seed % 4], parent, loc)
    o.scale = scale
    o.rotation_euler = [r.random() * 2 for _ in range(3)]
    return o


exc = bpy.data.objects.get("Excavator_Root")
boom, stick, bucket = [bpy.data.objects[n] for n in ("Boom", "Stick", "Bucket")]
upper = bpy.data.objects["UpperBody"]
bpy.context.view_layer.update()
base_upper_inverse = upper.matrix_world.inverted()
origins = [o.matrix_world.translation.copy() for o in (boom, stick, bucket)]
v1, v2 = origins[1] - origins[0], origins[2] - origins[1]
L1, L2 = v1.length, v2.length
A1, A2 = math.atan2(v1.z, v1.y), math.atan2(v2.z, v2.y)
bucket_points = [bucket.matrix_world @ Vector(v) for v in bucket.bound_box]
bucket_low = min(v.z for v in bucket_points)
lip_depth = origins[2].z - bucket_low
lip = bucket.matrix_world.inverted() @ Vector((origins[2].x, origins[2].y + .35, bucket_low))
load_center = bucket.matrix_world.inverted() @ Vector((origins[2].x, origins[2].y + .08, bucket_low + .28))
bucket_load = [rock("Held concrete", load_center + Vector(((i % 3 - 1) * .15, (i // 3) * .12, .05)),
                    (.14, .13, .11), bucket, i + 113) for i in range(6)]
# Glazing, framing and service details remain attached to the upper carriage.
# The source mesh's broad paint surfaces alone read as a toy at map scale.
def exc_detail(name, loc, size, mat, bevel=.025):
    return box(name, base_upper_inverse @ Vector(loc), size, mat, upper, bevel)

exc_detail("Excavator side glazing", (-1.05, .37, 2.02), (.035, .95, .85), glass)
exc_detail("Excavator front glazing", (-.63, .87, 2.02), (.78, .035, .85), glass)
exc_detail("Excavator window pillar", (-1.07, .40, 2.03), (.045, .055, .9), yellow)
exc_detail("Excavator roof rim", (-.63, .37, 2.49), (.90, 1.02, .10), yellow)
for n in range(8):
    exc_detail("Service cooling vent", (.61, -.84 + n * .095, 1.6), (.033, .045, .33), steel)
for y in (-.65, .03):
    exc_detail("Access step", (-1.04, y, 1.17), (.30, .28, .075), steel)
# Explicit anchored hydraulic endpoints extend with the real joint, not with the whole image.
tube = cylinder("Boom ram barrel", (0, 0, 0), (0, 0, 1), .075, yellow, exc)
rod = cylinder("Boom ram piston", (0, 0, 0), (0, 0, 1), .044, chrome, exc)
for side in (-1, 1):
    for y in (-.95, -.35, .35, .95):
        cylinder("Visible track roller", (side * 1.12, y, .46), (side * 1.20, y, .46), .22, steel, exc)
    for n in range(18):
        box("Excavator track shoe", (side * 1.02, -1.5 + n * .17, .90),
            (.54, .13, .065), steel, exc, .01)


def exc_pose(phase, direction=12, work=True, roll=0):
    exc.rotation_euler.z = -math.pi / 2 - direction * math.pi / 8
    upper.rotation_euler.z = 0
    if not work:
        target_y, target_z, curl = 3.5, 2.6, .25
    else:
        # Dig at the ruin edge, curl retaining material, lift clear, swing to the bed,
        # release over it, return empty. The undercarriage never flips headings.
        if phase < .16:
            q = phase / .16
            target_y, target_z, curl = 4.6, lip_depth + .07 + .3 * (1 - q), -.10 * q
        elif phase < .35:
            q = (phase - .16) / .19
            target_y, target_z, curl = 4.6 - .35 * q, lip_depth + .07 + 2.6 * q, .35 * q
        elif phase < .59:
            q = (phase - .35) / .24
            target_y, target_z, curl = 4.25 + .60 * q, lip_depth + 2.67, .35
            upper.rotation_euler.z = -math.pi / 2 * (q * q * (3 - 2 * q))
        elif phase < .72:
            q = (phase - .59) / .13
            target_y, target_z, curl = 4.85, lip_depth + 2.67 - .45 * q, .35 - .85 * q
            upper.rotation_euler.z = -math.pi / 2
        elif phase < .90:
            q = (phase - .72) / .18
            target_y, target_z, curl = 4.85 - .25 * q, lip_depth + 2.22, -.5 + .5 * q
            upper.rotation_euler.z = -math.pi / 2 * (1 - q * q * (3 - 2 * q))
        else:
            q = (phase - .90) / .10
            target_y, target_z, curl = 4.6, lip_depth + .07 + 2.15 * (1 - q), 0
    dy, dz = target_y - origins[0].y, target_z - origins[0].z
    d = math.hypot(dy, dz)
    theta = math.atan2(dz, dy) + math.acos(max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d))))
    phi = math.atan2(dz - L1 * math.sin(theta), dy - L1 * math.cos(theta))
    boom.rotation_euler.x = theta - A1
    stick.rotation_euler.x = (phi - theta) - (A2 - A1)
    bucket.rotation_euler.x = -boom.rotation_euler.x - stick.rotation_euler.x + curl
    for o in bucket_load:
        o.hide_render = not (work and .13 <= phase < .65)
    bpy.context.view_layer.update()
    a = Vector((-.42, .45, 1.25))
    b = exc.matrix_world.inverted() @ (boom.matrix_world @ Vector((-.38, 2.0, .3)))
    update_cylinder(tube, a, a + (b - a) * .57)
    update_cylinder(rod, a + (b - a) * .45, b)
    return bucket.matrix_world @ lip


truck = empty("Ivory green truck")
box("Chassis rails", (-.25, 0, .78), (6.0, 1.50, .25), steel, truck)
box("Ivory cabin", (2.02, 0, 1.85), (1.95, 2.0, 1.92), white, truck, .13)
box("Cab roof", (2.0, 0, 2.86), (2.0, 2.07, .12), white, truck)
box("Front windscreen", (3.01, 0, 2.31), (.025, 1.73, .80), glass, truck, .04)
for s in (-1, 1):
    box("Cab side window", (2.05, s * 1.025, 2.34), (1.50, .025, .74), glass, truck)
    box("Cab lower green door", (2.08, s * 1.025, 1.47), (1.6, .04, .7), green, truck)
    box("Door handle", (1.65, s * 1.05, 1.99), (.25, .04, .045), steel, truck)
    box("Step", (1.35, s * 1.15, .89), (.55, .28, .12), chrome, truck)
    cylinder("Mirror arm", (2.85, s * 1.04, 2.6), (2.86, s * 1.30, 2.65), .035, steel, truck)
    box("Mirror", (2.82, s * 1.32, 2.50), (.2, .09, .35), steel, truck)
box("Grille", (3.02, 0, 1.54), (.035, 1.10, .48), steel, truck)
for z in (1.4, 1.53, 1.66):
    box("Grille slat", (3.05, 0, z), (.03, 1.1, .035), chrome, truck)
box("Bumper", (3.12, 0, .99), (.18, 2.15, .25), white, truck)
for s in (-1, 1):
    box("Front lamp", (3.12, s * .75, 1.40), (.045, .36, .2), lamp, truck)
    box("Rear lamp", (-3.13, s * .76, .98), (.06, .23, .13), red, truck)
    box("Mud flap", (-2.66, s * 1.1, .47), (.1, .35, .5), rubber, truck)
truck_wheels = []
for x in (2.0, -.90, -2.12):
    for s in (-1, 1):
        w = empty("Wheel axle", truck, (x, s * 1.0, .56))
        truck_wheels.append(w)
        cylinder("Deep rubber tire", (0, -.19, 0), (0, .19, 0), .54, rubber, w, 24)
        cylinder("Rim", (0, s * .19, 0), (0, s * .23, 0), .31, white, w)
        cylinder("Wheel hub", (0, s * .22, 0), (0, s * .28, 0), .10, steel, w)
        for n in range(8):
            angle = n * math.tau / 8
            cylinder("Rim bolt", (.20 * math.cos(angle), s * .231, .20 * math.sin(angle)),
                     (.20 * math.cos(angle), s * .25, .20 * math.sin(angle)), .027, chrome, w, 8)
        for n in range(24):
            angle = n * math.tau / 24
            tread = box("Tire lug", (.535 * math.cos(angle), 0, .535 * math.sin(angle)),
                        (.09, .41, .06), steel, w, .009)
            tread.rotation_euler.y = -angle
bed = empty("Rear hinge dump bed", truck, (-2.98, 0, 1.15))
box("Bed floor", (2.00, 0, .05), (4.12, 1.97, .15), green, bed)
for s in (-1, 1):
    box("Ribbed dump side", (2.00, s * 1.03, .68), (4.16, .10, 1.17), green, bed)
    for n in range(9):
        box("Dump box reinforcing rib", (.18 + n * .46, s * 1.11, .68), (.07, .065, 1.18), green, bed)
    box("Bed top rim", (2.0, s * 1.07, 1.30), (4.25, .16, .12), green, bed)
box("Bed front bulkhead", (4.05, 0, .70), (.12, 2.03, 1.23), green, bed)
tail = empty("Opening tailgate", bed, (0, 0, 1.28))
box("Tailgate", (0, 0, -.59), (.12, 2.03, 1.22), green, tail)
tip_tube = cylinder("Tip ram barrel", (0, 0, 1), (0, 0, 2), .11, steel, truck)
tip_rod = cylinder("Tip ram piston", (0, 0, 1), (0, 0, 2), .07, chrome, truck)
truck_load = []
for n in range(45):
    r = random.Random(n + 50)
    truck_load.append(rock("Transported rubble", (.22 + r.random() * 3.6, (r.random() - .5) * 1.68,
                         .38 + r.random() * .68), (.18 + r.random() * .13, .14 + r.random() * .13, .16),
                         bed, n + 29))


def truck_pose(direction, roll=0, loaded=0, tipping=0):
    truck.rotation_euler.z = -direction * math.pi / 8
    for w in truck_wheels:
        w.rotation_euler.y = roll * .13
    angle = math.sin(math.pi * tipping) ** .7 * math.radians(48) if tipping else 0
    bed.rotation_euler.y = -angle
    tail.rotation_euler.y = angle * .85
    for n, o in enumerate(truck_load):
        o.hide_render = bool(n >= int(45 * loaded) or (tipping and .40 < tipping < 1 and n / 45 < min(1, (tipping - .4) / .30)))
    bpy.context.view_layer.update()
    a = Vector((-.1, 0, 1.0))
    b = truck.matrix_world.inverted() @ (bed.matrix_world @ Vector((2.65, 0, .04)))
    update_cylinder(tip_tube, a, a + (b - a) * .5)
    update_cylinder(tip_rod, a + (b - a) * .4, b)
    return bed.matrix_world @ Vector((0, 0, .10))


dozer = empty("Tracked yellow bulldozer")
box("Dozer chassis", (-.10, 0, .9), (3.45, 1.8, .48), yellow, dozer)
box("Engine hood", (.80, 0, 1.47), (1.8, 1.55, .85), yellow, dozer)
for n in range(9):
    box("Engine grille vent", (1.73, -.63 + n * .16, 1.53), (.035, .045, .53), steel, dozer)
box("Dozer operator cab", (-.8, 0, 2.1), (1.35, 1.55, 1.95), yellow, dozer, .075)
for s in (-1, 1):
    box("Dozer cab sideglass", (-.80, s * .79, 2.26), (1.09, .025, 1.08), glass, dozer)
box("Dozer windshield", (-.10, 0, 2.25), (.035, 1.30, 1.02), glass, dozer)
box("Cab roof overhang", (-.80, 0, 3.10), (1.5, 1.75, .13), yellow, dozer)
cylinder("Exhaust", (.48, -.35, 1.92), (.48, -.35, 2.88), .09, steel, dozer)
dozer_shoes = []
for s in (-1, 1):
    box("Track frame", (-.25, s * 1.03, .52), (3.55, .58, .75), steel, dozer, .2)
    for x in (-1.54, -.95, -.35, .3, 1.0):
        cylinder("Dozer track roller", (x, s * 1.20, .49), (x, s * 1.34, .49), .31, steel, dozer)
    for n in range(19):
        for z in (.10, .91):
            o = box("Dozer track shoe", (-1.78 + n * .17, s * 1.03, z), (.135, .64, .085), rubber, dozer, .012)
            dozer_shoes.append((o, o.location.x))
blade = empty("Dozer blade lift", dozer, (1.83, 0, .38))
box("Wide curved steel blade", (.28, 0, .38), (.24, 2.78, 1.0), yellow, blade, .10)
box("Blade cutting edge", (.44, 0, -.10), (.14, 2.84, .12), steel, blade)
for s in (-1, 1):
    box("Blade side cheek", (.21, s * 1.35, .36), (.51, .12, .96), yellow, blade)
blade_rams = []
for s in (-1, 1):
    box("Blade push arm", (.55, s * .98, .57), (2.55, .13, .18), steel, dozer)
    blade_rams.append((cylinder("Blade ram", (0, 0, 0), (0, 0, 1), .075, yellow, dozer), s))


def dozer_pose(direction, roll=0, phase=None):
    dozer.rotation_euler.z = -direction * math.pi / 8
    blade.location.z = .48 if phase is None else .195 + .28 * max(0, math.sin((phase - .5) * math.tau))
    for shoe, x in dozer_shoes:
        shoe.location.x = x + (roll % 2) * .07
    bpy.context.view_layer.update()
    for ram, s in blade_rams:
        a = Vector((.05, s * .74, 1.18))
        b = dozer.matrix_world.inverted() @ (blade.matrix_world @ Vector((.0, s * .74, .42)))
        update_cylinder(ram, a, b)
    return blade.matrix_world @ Vector((.44, 0, -.10))


def members(root):
    return [root, *root.children_recursive]


rigs = {"excavator": exc, "truck": truck, "bulldozer": dozer}
for r in rigs.values():
    for o in members(r):
        o.hide_render = True
bpy.ops.mesh.primitive_plane_add(size=50, location=(0, 0, -.025))
pad = bpy.context.object
pad.name = "Transparent contact shadow catcher"
pad.is_shadow_catcher = True
pad.data.materials.append(material("Neutral shadow receiver", (.43, .42, .38)))
bpy.ops.object.camera_add(location=(10, -10, 8.0710678119))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 0, 1)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = ORTHO
scene.camera = camera
for name, location, power, size in [
    ("Shared upper left daylight", (-7, -9, 12), 1900, 7),
    ("Soft sky fill", (7, 5, 8), 800, 9),
]:
    bpy.ops.object.light_add(type="AREA", location=location)
    light = bpy.context.object
    light.name = name
    light.data.energy = power
    light.data.shape = "DISK"
    light.data.size = size
    light.rotation_euler = (Vector((0, 0, 1)) - light.location).to_track_quat("-Z", "Y").to_euler()


def projected(point):
    a = world_to_camera_view(scene, camera, point)
    return [round(a.x * SIZE, 3), round((1 - a.y) * SIZE, 3)]


foot = projected(Vector((0, 0, 0)))
manifest = {"size": SIZE, "ortho": ORTHO, "foot": foot, "clips": {}, "frames": {}}
if RETIP:
    manifest = json.loads((OUT / "manifest.json").read_text())


def render_frame(kind, clip, index, pose):
    for k, root in rigs.items():
        for o in members(root):
            o.hide_render = k != kind
    # Reapply pose after visibility: bucket cargo and transported load have their own state.
    point = pose()
    bpy.context.view_layer.update()
    key = f"{kind}-{clip}-{index}"
    scene.render.filepath = str(OUT / f"{key}.png")
    bpy.ops.render.render(write_still=True)
    manifest["frames"][key] = {"tip": projected(point), "worldTip": list(point)}
    print("MACHINERY_FRAME", key, flush=True)
    return key


for kind in ([] if RETIP else rigs):
    keys = []
    for direction in range(16 if not PROOF else 1):
        for roll in range(2 if not PROOF else 1):
            pose = (lambda d=direction, r=roll: exc_pose(0, d, False, r)) if kind == "excavator" else (
                (lambda d=direction, r=roll: truck_pose(d, r)) if kind == "truck" else
                (lambda d=direction, r=roll: dozer_pose(d, r)))
            keys.append(render_frame(kind, "drive", direction * 2 + roll, pose))
    manifest["clips"][kind + ":drive"] = keys
    if kind == "truck":
        keys = []
        for direction in range(16 if not PROOF else 1):
            for roll in range(2 if not PROOF else 1):
                keys.append(render_frame(kind, "loaded", direction * 2 + roll,
                                         lambda d=direction, r=roll: truck_pose(d, r, 1)))
        manifest["clips"]["truck:loaded"] = keys
        keys = []
        for n in range(4):
            keys.append(render_frame(kind, "fill", n, lambda level=n / 3: truck_pose(0, 0, level)))
        manifest["clips"]["truck:fill"] = keys
    count = 72 if kind == "excavator" else 36
    keys = []
    indices = [0, int(count * .47), int(count * .65)] if PROOF else range(count)
    for index in indices:
        phase = index / count
        pose = (lambda f=phase: exc_pose(f)) if kind == "excavator" else (
            (lambda f=phase: truck_pose(8, 0, 1, f)) if kind == "truck" else
            (lambda f=phase: dozer_pose(12, int(f * 16), f)))
        keys.append(render_frame(kind, "work", index, pose))
    manifest["clips"][kind + ":work"] = keys
if RETIP:
    manifest["clips"]["truck:work"] = [
        render_frame("truck", "work", n, lambda f=n / 36: truck_pose(8, 0, 1, f))
        for n in range(36)]
(OUT / ("proof-manifest.json" if PROOF else "manifest.json")).write_text(json.dumps(manifest))
print("MACHINERY_RENDER_COMPLETE", len(manifest["frames"]), flush=True)
