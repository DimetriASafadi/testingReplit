#!/usr/bin/env python3
"""Render the delivered FBX files with Blender Cycles CPU, never a Unity screenshot.

blender --background --factory-startup --python tools/render_destroyed_assets.py
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
FBX_ROOT = ROOT / "testingReplic/Assets/NewGaza/Art/DestroyedFBX"
OUTPUT = ROOT / "exports/destroyed-assets/previews"
KEYS = (
    "shujaiya", "tuffah", "sheikh_radwan", "daraj", "karama", "old_city",
    "nasr", "sabra", "zeitoun", "rimal", "tel_al_hawa", "sheikh_ijlin", "rashid",
    "mosque", "school", "clinic", "civic", "wall", "car", "crater", "debris",
)


def material(name, color, roughness=.88):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = roughness
    return result


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def bounds(objects):
    points = [obj.matrix_world @ Vector(point) for obj in objects for point in obj.bound_box]
    return (
        Vector(tuple(min(point[i] for point in points) for i in range(3))),
        Vector(tuple(max(point[i] for point in points) for i in range(3))),
    )


def render(key, samples):
    source = FBX_ROOT / (key + ".fbx")
    if not source.is_file():
        raise FileNotFoundError(source)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)
    objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not objects:
        raise RuntimeError("FBX has no visible meshes: " + key)
    minimum, maximum = bounds(objects)
    extent = maximum - minimum
    if not all(math.isfinite(value) for value in (*minimum, *maximum)) or min(extent) <= 0:
        raise RuntimeError("Invalid delivered FBX bounds: " + key)
    for obj in objects:
        for slot in obj.material_slots:
            if slot.material is None:
                raise RuntimeError("FBX material is missing: " + key)
            if slot.material.use_nodes:
                for node in slot.material.node_tree.nodes:
                    if node.type == "TEX_IMAGE" and (
                        node.image is None or
                        (node.image.source == "FILE" and not node.image.has_data)
                    ):
                        raise RuntimeError("FBX texture is missing: " + key)
    scene = bpy.context.scene
    # Starting Cycles directly avoids native EGL/GLX initialization in this workspace.
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 2
    scene.render.resolution_x = 600
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.world = bpy.data.worlds.new("Offline Mediterranean daylight")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (.68, .74, .8, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .55
    span = max(extent)
    center = (minimum + maximum) * .5
    bpy.ops.mesh.primitive_plane_add(size=span * 5, location=(center.x, center.y, minimum.z - .025))
    bpy.context.object.name = "Preview ground — not included in the delivered FBX"
    bpy.context.object.data.materials.append(material("Preview sandy ground", (.43, .37, .28)))
    bpy.ops.object.light_add(type="SUN", location=(center.x + span, center.y - span, maximum.z + span))
    sun = bpy.context.object
    sun.data.energy = 2.2
    sun.data.angle = .12
    aim(sun, center)
    bpy.ops.object.light_add(type="AREA", location=(center.x - span, center.y - span * .3, maximum.z + span * .6))
    fill = bpy.context.object
    fill.data.energy = 15 * span * span
    fill.data.shape = "DISK"
    fill.data.size = span * 1.2
    aim(fill, center)
    target = Vector((center.x, center.y, minimum.z + extent.z * .43))
    bpy.ops.object.camera_add(location=target + Vector((1.1, -1.6, 1.25)) * span)
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = span * 1.62
    camera.data.clip_end = max(300, span * 20)
    aim(camera, target)
    scene.camera = camera
    OUTPUT.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(OUTPUT / (key + ".png"))
    bpy.ops.render.render(write_still=True)
    return {
        "key": key, "source": str(source.relative_to(ROOT)),
        "boundsMin": list(minimum), "boundsMax": list(maximum),
        "preview": str(Path(scene.render.filepath).relative_to(ROOT)),
        "renderer": "Blender Cycles CPU", "isUnityScreenshot": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=int, default=20)
    parser.add_argument("--keys", nargs="*")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    results = []
    for short in args.keys or KEYS:
        key = short if short.startswith("ruin_") else "ruin_" + short
        print("OFFLINE FBX PREVIEW", key, flush=True)
        results.append(render(key, args.samples))
    (OUTPUT / "Preview-Audit.json").write_text(json.dumps(results, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()