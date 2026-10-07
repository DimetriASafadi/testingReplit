#!/usr/bin/env python3
"""Numerical and FBX round-trip checks; not a Unity import or engine test."""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/"exports/uploaded-equipment"
bpy.ops.wm.open_mainfile(filepath=str(OUT/"All_Variants_Articulated.blend"))
report=json.loads((OUT/"Rig_Manifest.json").read_text())
covered=[i for variant in report["variants"] for i in variant["source_component_ids"]]
assert sorted(covered)==list(range(19)),"Source component omitted or duplicated"
roots=[o for o in bpy.context.scene.objects if o.type=="EMPTY" and "VariantKind" in o]
assert len(roots)==12
scene=bpy.context.scene
mesh_checks=[]
for obj in scene.objects:
    if obj.type!="MESH":
        continue
    points=np.empty(len(obj.data.vertices)*3,dtype=np.float64)
    obj.data.vertices.foreach_get("co",points)
    assert np.isfinite(points).all(),obj.name
    assert len(obj.data.polygons)>0 and len(obj.data.vertices)>0,obj.name
    bad=sum(1 for face in obj.data.polygons if face.area<=1e-16)
    assert bad==0,f"{obj.name}: {bad} degenerate faces"
    mesh_checks.append({"object":obj.name,"vertices":len(obj.data.vertices),
                        "polygons":len(obj.data.polygons),"degenerate_faces":bad})
    if obj.data.uv_layers.active:
        uv=np.empty(len(obj.data.uv_layers.active.data)*2,dtype=np.float64)
        obj.data.uv_layers.active.data.foreach_get("uv",uv)
        assert np.isfinite(uv).all(),obj.name
clearances={}
for root in roots:
    if root.get("VariantKind")!="excavator":
        continue
    variant=next(v for v in report["variants"] if v["name"]==root.name)
    low=np.array(variant["source_bounds"]["min"])
    high=np.array(variant["source_bounds"]["max"])
    boom=next(o for o in root.children_recursive if o.get("Role")=="Boom")
    limit_y=boom["source_pivot"][1]-(high[1]-low[1])*.23
    limit_z=low[2]+(high[2]-low[2])*.34
    for obj in root.children_recursive:
        if obj.get("Role") not in ("Boom","Stick","Bucket"):
            continue
        coords=np.empty(len(obj.data.vertices)*3,dtype=np.float64)
        obj.data.vertices.foreach_get("co",coords)
        points=coords.reshape(-1,3)+np.array(obj["source_pivot"])
        assert not ((points[:,1]>limit_y+1e-6)&(points[:,2]<limit_z-1e-6)).any(), \
            f"{obj.name}: protected undercarriage vertices assigned to the arm"
for frame in np.arange(1,97.5,.5):
    scene.frame_set(int(frame), subframe=float(frame)%1)
    bpy.context.view_layer.update()
    for root in roots:
        for obj in root.children_recursive:
            assert np.isfinite(np.array(obj.matrix_world)).all(),obj.name
            if obj.get("Role")=="Bucket":
                points=np.empty(len(obj.data.vertices)*3,dtype=np.float64)
                obj.data.vertices.foreach_get("co",points)
                matrix=np.array(obj.matrix_world)
                z=points.reshape(-1,3)@matrix[2,:3]+matrix[2,3]-root.location.z
                minimum=float(z.min())
                clearances[root.name]=min(clearances.get(root.name,math.inf),minimum)
for name,value in clearances.items():
    assert value>=-1e-5,f"{name}: bucket crosses the support plane: {value}"
scene.frame_set(1)
roundtrips=[]
from io_scene_fbx import parse_fbx
for variant in report["variants"]:
    path=OUT/"FBX"/(variant["name"]+".fbx")
    raw,version=parse_fbx.parse(str(path))
    objects=next(e for e in raw.elems if e.id==b"Objects")
    stacks=[e for e in objects.elems if e.id==b"AnimationStack"]
    assert len(stacks)==1,f"{path.name}: expected one synchronized scene take"
    assert any(e.id==b"AnimationCurve" for e in objects.elems),path.name
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    imported=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    expected=sum(n["type"]=="MESH" for n in variant["nodes"])
    assert len(imported)==expected,(path.name,len(imported),expected)
    bpy.context.scene.frame_set(1)
    for obj in imported:
        assert np.isfinite(np.array(obj.matrix_world)).all(),obj.name
        assert obj.data.materials,obj.name
    controls=[o for o in bpy.context.scene.objects if o.animation_data and o.animation_data.action]
    assert controls,path.name
    first={o.name:np.array(o.matrix_world).copy() for o in controls}
    changed=set()
    for frame in (49,133):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for obj in controls:
            matrix=np.array(obj.matrix_world)
            assert np.isfinite(matrix).all(),obj.name
            if np.max(np.abs(matrix-first[obj.name]))>1e-5:
                changed.add(obj.name)
    assert changed,f"{path.name}: exported animation does not move its geometry"
    roundtrips.append({"file":path.name,"mesh_count":len(imported),
                      "synchronized_takes":len(stacks),"animated_nodes":len(controls),
                      "nodes_with_measured_motion":len(changed),
                      "fbx_version":version})
evidence={"source_components_covered":sorted(covered),"variant_count":len(roots),
          "mesh_checks":mesh_checks,"minimum_bucket_clearance_source_units":clearances,
          "roundtrip":roundtrips,
          "unity_editor":"NOT RUN","device_rendering":"NOT RUN"}
(OUT/"Verification.json").write_text(json.dumps(evidence,indent=2))
print("FBX_CHECKS_PASSED",json.dumps({"variants":12,"source_components":19,
      "roundtrips":len(roundtrips),"bucket_clearances":clearances,
      "degenerate_faces":sum(m["degenerate_faces"] for m in mesh_checks)}),flush=True)