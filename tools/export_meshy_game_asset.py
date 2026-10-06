"""Retarget the prepared Meshy surfaces to the existing game's joint frames.

Run with Blender. Original files and original runtime equipment stay unchanged.
The derived .bytes file is a local art asset, not a source-control deliverable.
"""
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "exports/meshy_excavator/Meshy_Excavator_MobileSplit.blend"
DEST = ROOT / "testingReplic/Assets/NewGaza/Resources/Equipment/MeshyExcavator.bytes"
PARTS = ["Undercarriage", "Track_Left", "Track_Right", "UpperBody", "Boom", "Stick", "Bucket"]
MATERIALS = ["Construction_Yellow", "Track_Rubber", "Dark_Steel", "Opaque_Tinted_Cab"]
TO_UNITY = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))


def align_yz(source, target):
    angle = math.atan2(target.z, target.y) - math.atan2(source.z, source.y)
    ratio = Vector((target.y, target.z)).length / Vector((source.y, source.z)).length
    return Matrix.Rotation(angle, 3, "X") @ Matrix.Diagonal(Vector((0.5, ratio, ratio)))


def main():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    objects = {n: bpy.data.objects[n] for n in PARTS}
    pivots = {n: TO_UNITY @ o.matrix_world.translation for n, o in objects.items()}
    matrices = {
        "UpperBody": Matrix.Diagonal(Vector((0.5, 0.5, 0.5))),
        "Boom": align_yz(pivots["Stick"] - pivots["Boom"], Vector((0, 1.28, 0.78))),
        "Stick": align_yz(pivots["Bucket"] - pivots["Stick"], Vector((0, -1.15, 0.35))),
    }
    bucket = objects["Bucket"]
    bucket_points = [TO_UNITY @ (bucket.matrix_world @ v.co) - pivots["Bucket"]
                     for v in bucket.data.vertices]
    # The scanned teeth point inward in the neutral pose. Align the central
    # lip tip to the exact point used by the existing two-link IK solver.
    central = [p for p in bucket_points if abs(p.x) < 0.17]
    tooth = min(central, key=lambda p: p.z)
    matrices["Bucket"] = align_yz(tooth, Vector((0, -0.1964, 0.52)))
    joint_ids = {"Undercarriage": 0, "Track_Left": 0, "Track_Right": 0,
                 "UpperBody": 1, "Boom": 2, "Stick": 3, "Bucket": 4}
    track_ground = min((TO_UNITY @ (objects[n].matrix_world @ v.co)).y
                       for n in ("Track_Left", "Track_Right") for v in objects[n].data.vertices)
    packets = []
    for name, obj in objects.items():
        mesh = obj.data
        mesh.calc_loop_triangles()
        groups = {}
        for tri in mesh.loop_triangles:
            mat = mesh.materials[tri.material_index].name
            mat_id = MATERIALS.index(mat)
            group = groups.setdefault(mat_id, {"verts": [], "indices": [], "lookup": {}})
            corner_indices = []
            for loop_index in tri.loops:
                loop = mesh.loops[loop_index]
                original = obj.matrix_world @ mesh.vertices[loop.vertex_index].co
                native = TO_UNITY @ original
                local = native - pivots[name]
                if name in matrices:
                    position = matrices[name] @ local
                    if name == "Bucket":
                        position.x -= tooth.x * 0.5
                    linear = matrices[name] @ TO_UNITY @ obj.matrix_world.to_3x3()
                else:
                    position = native * 0.5 + Vector((0, -0.0375 - track_ground * 0.5, 0))
                    linear = TO_UNITY @ obj.matrix_world.to_3x3() * 0.5
                normal = (linear.inverted().transposed() @ mesh.corner_normals[loop_index].vector).normalized()
                uv = mesh.uv_layers.active.data[loop_index].uv
                values = tuple(round(float(c), 7) for c in (*position, *normal, *uv))
                if values not in group["lookup"]:
                    group["lookup"][values] = len(group["verts"])
                    group["verts"].append(values)
                corner_indices.append(group["lookup"][values])
            # Blender -> Unity swaps Y/Z; reverse winding once.
            group["indices"].extend((corner_indices[0], corner_indices[2], corner_indices[1]))
        for mat_id, group in groups.items():
            packets.append((name, joint_ids[name], mat_id, group))
    DEST.parent.mkdir(parents=True, exist_ok=True)
    with DEST.open("wb") as stream:
        stream.write(b"NGEXC001")
        stream.write(struct.pack("<i", len(packets)))
        for name, joint, mat, group in packets:
            encoded = name.encode("utf-8")
            stream.write(struct.pack("<i", len(encoded)))
            stream.write(encoded)
            stream.write(struct.pack("<4i", joint, mat, len(group["verts"]), len(group["indices"])))
            for values in group["verts"]:
                stream.write(struct.pack("<8f", *values))
            stream.write(struct.pack("<" + "i" * len(group["indices"]), *group["indices"]))
    report = {
        "format": "NGEXC001", "triangles": sum(len(p[3]["indices"]) // 3 for p in packets),
        "vertices": sum(len(p[3]["verts"]) for p in packets),
        "draw_meshes": len(packets), "bytes": DEST.stat().st_size,
        "bucket_contact": [0, -0.1964, 0.52],
        "notes": ["Retargeted to original runtime joint spacings and IK contact point.",
                  "Solid materials, not original Meshy textures.",
                  "Static track surfaces and fused hydraulic visuals remain draft limitations."],
    }
    (DEST.parent / "MeshyExcavatorManifest.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))
    if "--preview" in sys.argv:
        render_preview(packets)


def render_preview(packets):
    """Offline view of derived surfaces in the game's parked joint pose."""
    # Save material references before removing the standalone review geometry.
    materials = [bpy.data.materials[n] for n in MATERIALS]
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    base = Vector((0, 0.0375, 0))
    identity = Matrix.Identity(3)
    boom = Matrix.Rotation(math.radians(15), 3, "X")
    stick = boom @ Matrix.Rotation(math.radians(-62), 3, "X")
    bucket = stick @ Matrix.Rotation(math.radians(24), 3, "X")
    turret_p = base + Vector((0, 0.65, 0))
    boom_p = turret_p + Vector((0.17, 0.34, 0.36))
    stick_p = boom_p + boom @ Vector((0, 1.28, 0.78))
    bucket_p = stick_p + stick @ Vector((0, -1.15, 0.35))
    rotations = [identity, identity, boom, stick, bucket]
    positions = [base, turret_p, boom_p, stick_p, bucket_p]
    for name, joint, material, group in packets:
        vertices = [TO_UNITY @ (positions[joint] + rotations[joint] @ Vector(v[:3]))
                    for v in group["verts"]]
        indices = group["indices"]
        faces = [(indices[i], indices[i+2], indices[i+1]) for i in range(0, len(indices), 3)]
        mesh = bpy.data.meshes.new("Game retarget " + name)
        mesh.from_pydata(vertices, [], faces)
        mesh.materials.append(materials[material])
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
    bpy.ops.mesh.primitive_plane_add(size=200)
    ground = bpy.context.object
    ground.name = "Offline review ground"
    gray = bpy.data.materials.new("Review gray")
    gray.diffuse_color = (0.16, 0.18, 0.21, 1)
    ground.data.materials.append(gray)
    camera_data = bpy.data.cameras.new("Review camera")
    camera = bpy.data.objects.new("Review camera", camera_data)
    bpy.context.collection.objects.link(camera)
    focus = Vector((0, 0.75, 0.95))
    camera.location = (4.5, 5.5, 3.5)
    camera.rotation_euler = (focus - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 4.6
    scene = bpy.context.scene
    scene.camera = camera
    for location, energy, size in [((2, -3, 6), 800, 5), ((-4, 2, 4), 550, 4)]:
        light_data = bpy.data.lights.new("Review area", "AREA")
        light_data.energy, light_data.size = energy, size
        light = bpy.data.objects.new("Review area", light_data)
        bpy.context.collection.objects.link(light)
        light.location = location
        light.rotation_euler = (focus - light.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.render.resolution_x = 1120
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.world.color = (0.3, 0.3, 0.3)
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(ROOT / "exports/meshy_excavator/Meshy-Game-Retarget-Offline.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
