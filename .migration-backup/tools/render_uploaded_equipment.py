#!/usr/bin/env python3
"""CPU-only proofs rendered from the delivered editable machinery, not substitute art."""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/"exports/uploaded-equipment"
bpy.ops.wm.open_mainfile(filepath=str(OUT/"All_Variants_Articulated.blend"))
roots=[o for o in bpy.context.scene.objects if o.type=="EMPTY" and "VariantKind" in o]
scene=bpy.context.scene
scene.render.engine="CYCLES"
scene.cycles.device="CPU"
scene.cycles.samples=24
scene.render.threads_mode="FIXED"
scene.render.threads=8
scene.render.resolution_x=1500
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
scene.view_settings.view_transform="AgX"
scene.view_settings.look="AgX - Medium High Contrast"
world=bpy.data.worlds.new("Proof studio")
world.use_nodes=True
world.node_tree.nodes["Background"].inputs[0].default_value=(.24,.28,.32,1)
world.node_tree.nodes["Background"].inputs[1].default_value=.65
scene.world=world
meshes=[o for o in scene.objects if o.type=="MESH"]
scene.frame_set(1)
bpy.context.view_layer.update()
points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
low=Vector([min(p[k] for p in points) for k in range(3)])
high=Vector([max(p[k] for p in points) for k in range(3)])
center=(low+high)/2
extent=max(high-low)
bpy.ops.object.camera_add(location=center+Vector((1.4,-1.8,1.1))*extent)
camera=bpy.context.object
camera.rotation_euler=(center-camera.location).to_track_quat("-Z","Y").to_euler()
camera.data.type="ORTHO"
camera.data.ortho_scale=extent*1.4
scene.camera=camera
for offset,power in [((1,-1,2),1200),((-1,.7,1.3),850)]:
    bpy.ops.object.light_add(type="AREA",location=center+Vector(offset)*extent)
    light=bpy.context.object
    light.rotation_euler=(center-light.location).to_track_quat("-Z","Y").to_euler()
    light.data.energy=power*max(extent*extent/25,.02)
    light.data.size=extent
for frame,name in [(1,"All_Variants_Parked.png"),(49,"All_Variants_Articulated.png")]:
    scene.frame_set(frame)
    scene.render.filepath=str(OUT/name)
    bpy.ops.render.render(write_still=True)

# A moving, isolated side view verifies actual joint and shoe channels visually.
scene.render.resolution_x=720
scene.render.resolution_y=540
scene.cycles.samples=12
for root_name in ("Excavator_Large","Bulldozer_Large","Tipper_Loaded_Large"):
    root=bpy.data.objects[root_name]
    selected=set(root.children_recursive)
    for obj in meshes:
        obj.hide_render=obj not in selected
    scene.frame_set(1)
    bpy.context.view_layer.update()
    points=[o.matrix_world@Vector(c) for o in selected if o.type=="MESH" for c in o.bound_box]
    low=Vector([min(p[k] for p in points) for k in range(3)])
    high=Vector([max(p[k] for p in points) for k in range(3)])
    focus=(low+high)/2
    size=max(high-low)
    camera.location=focus+Vector((size*2,-size*.10,size*.25))
    camera.rotation_euler=(focus-camera.location).to_track_quat("-Z","Y").to_euler()
    camera.data.ortho_scale=size*1.55
    for frame in (1,25,49,73,97,121,127,133,139,145):
        scene.frame_set(frame)
        # The payload is deliberately separate; the tipping proof is empty, not a
        # claimed rigid-body unloading simulation.
        for obj in selected:
            if obj.get("Role")=="Cargo_Removable" and 25<=frame<=73:
                obj.hide_render=True
        scene.render.filepath=str(OUT/f"{root_name}_Frame_{frame:03d}.png")
        bpy.ops.render.render(write_still=True)
print("DELIVERY_PROOFS_COMPLETE",flush=True)