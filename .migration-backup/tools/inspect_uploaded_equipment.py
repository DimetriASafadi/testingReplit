#!/usr/bin/env python3
"""Inspect an uploaded FBX non-destructively and make a CPU-only source preview."""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def bounds(points):
    points = list(points)
    return {
        "min": [min(p[i] for p in points) for i in range(3)],
        "max": [max(p[i] for p in points) for i in range(3)],
    } if points else None


def inspect_components(obj):
    mesh = obj.data
    parents = list(range(len(mesh.vertices)))

    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i

    for edge in mesh.edges:
        a, b = (root(i) for i in edge.vertices)
        if a != b:
            parents[b] = a
    groups = {}
    for vertex in mesh.vertices:
        groups.setdefault(root(vertex.index), []).append(vertex.index)
    components = []
    for indices in groups.values():
        box = bounds(obj.matrix_world @ mesh.vertices[i].co for i in indices)
        components.append({"vertices": len(indices), "bounds": box})
    return sorted(components, key=lambda c: c["vertices"], reverse=True)


def main():
    cli = sys.argv[sys.argv.index("--") + 1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(cli)
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.input.resolve()))
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    report = {
        "source": args.input.name,
        "unit_scale": bpy.context.scene.unit_settings.scale_length,
        "objects": [],
        "actions": [a.name for a in bpy.data.actions],
        "images": [],
    }
    all_points = []
    for obj in bpy.context.scene.objects:
        item = {"name": obj.name, "type": obj.type, "parent": obj.parent.name if obj.parent else None}
        if obj.type == "MESH":
            points = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
            all_points.extend(points)
            components = inspect_components(obj)
            item.update({
                "vertices": len(obj.data.vertices),
                "polygons": len(obj.data.polygons),
                "bounds": bounds(points),
                "materials": [m.name if m else None for m in obj.data.materials],
                "uv_layers": [u.name for u in obj.data.uv_layers],
                "component_count": len(components),
                "largest_components": components[:40],
            })
        elif obj.type == "ARMATURE":
            item["bones"] = [b.name for b in obj.data.bones]
        report["objects"].append(item)
    for image in bpy.data.images:
        report["images"].append({
            "name": image.name, "filepath": image.filepath,
            "size": list(image.size), "packed": bool(image.packed_file),
        })
    report["bounds"] = box = bounds(all_points)
    (args.output / "source-inspection.json").write_text(json.dumps(report, indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str((args.output / "source-inspection.blend").resolve()))
    print("SOURCE_REPORT", json.dumps({
        "objects": [{k: v for k, v in o.items() if k not in ("largest_components",)}
                    for o in report["objects"]],
        "actions": report["actions"], "images": report["images"], "bounds": box,
    }))
    if not box:
        raise RuntimeError("The FBX has no mesh geometry.")
    center = Vector([(box["min"][i] + box["max"][i]) / 2 for i in range(3)])
    extent = max(box["max"][i] - box["min"][i] for i in range(3))
    bpy.ops.object.camera_add(location=center + Vector((1.4, -1.8, 1.1)) * extent)
    camera = bpy.context.object
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = extent * 1.4
    scene = bpy.context.scene
    scene.camera = camera
    world = bpy.data.worlds.new("Inspection world")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (.32, .37, .43, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = .8
    scene.world = world
    for offset, power in [((1, -1, 2), 1200), ((-1, .7, 1.3), 900)]:
        bpy.ops.object.light_add(type="AREA", location=center + Vector(offset) * extent)
        light = bpy.context.object
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
        light.data.energy = power * max(extent * extent / 25, .02)
        light.data.shape = "DISK"
        light.data.size = extent
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 8
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 740
    scene.render.resolution_percentage = 100
    scene.render.filepath = str((args.output / "source-preview.png").resolve())
    scene.view_settings.view_transform = "Standard"
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()