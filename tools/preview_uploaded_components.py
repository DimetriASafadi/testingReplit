#!/usr/bin/env python3
"""Prepare isolated source components and side-view evidence for articulation."""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "exports/uploaded-equipment-inspection"
bpy.ops.wm.open_mainfile(filepath=str(OUT / "source-inspection.blend"))
source = next(o for o in bpy.context.scene.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = source
source.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")
components = sorted((o for o in bpy.context.scene.objects if o.type == "MESH"),
                    key=lambda o: len(o.data.vertices), reverse=True)
for i, obj in enumerate(components):
    obj.name = f"SourceComponent_{i:02d}"
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "source-components.blend"))
for image in bpy.data.images:
    if image.packed_file:
        image.filepath_raw = str(OUT / (image.name + ".png"))
        image.file_format = "PNG"
        image.save()
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 12
scene.render.threads_mode = "FIXED"
scene.render.threads = 8
scene.render.resolution_x = 720
scene.render.resolution_y = 540
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("Component world")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[1].default_value = .7
scene.world = world
bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.type = "ORTHO"
scene.camera = camera
bpy.ops.object.light_add(type="AREA")
light = bpy.context.object
light.data.size = 1
light.data.energy = 90
for i in range(12):
    shown = {i}
    if i == 1:
        shown.update(range(12, 18))
    if i == 2:
        shown.add(18)
    for j, obj in enumerate(components):
        obj.hide_render = j not in shown
    points = [o.matrix_world @ Vector(c) for j, o in enumerate(components)
              if j in shown for c in o.bound_box]
    low = Vector([min(p[k] for p in points) for k in range(3)])
    high = Vector([max(p[k] for p in points) for k in range(3)])
    center = (low + high) / 2
    extent = max(high - low)
    camera.location = center + Vector((extent * 2, 0, extent * .10))
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = extent * 1.28
    light.location = center + Vector((extent, -extent, extent * 2))
    light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = str(OUT / f"component-{i:02d}-side.png")
    bpy.ops.render.render(write_still=True)
print("COMPONENT_PREVIEWS_COMPLETE", len(components))