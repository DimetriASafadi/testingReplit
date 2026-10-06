"""Prepare a separate excavator asset; run using Blender's background Python.

Does not modify the supplied FBX or any Unity assets.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector


SOURCE = Path("attached_assets/Meshy_AI_Steel_Excavator_Sketc_1006082101_generate_1791275580506.fbx")
OUTPUT = Path("exports/meshy_excavator")
SCALE = 4.0
SOURCE_ORIGIN = Vector((0.35, 0, -0.6216909885406494))
ORIENTATION = Matrix.Rotation(-math.pi / 2, 3, "Z")
PART_BUDGETS = {
    "Track_Left": 2500, "Track_Right": 2500, "Undercarriage": 1000,
    "UpperBody": 3500, "Boom": 3000, "Stick": 2500, "Bucket": 2000,
}
SOURCE_PIVOTS = {
    "Undercarriage": SOURCE_ORIGIN,
    "Track_Left": Vector((0.35, -0.235, -0.50)),
    "Track_Right": Vector((0.35, 0.235, -0.50)),
    "UpperBody": Vector((0.36, 0, -0.373)),
    "Boom": Vector((0.195, 0.035, -0.195)),
    "Stick": Vector((-0.837, 0.035, 0.442)),
    "Bucket": Vector((-0.811, 0.035, -0.332)),
}


def asset_point(co):
    return ORIENTATION @ ((Vector(co) - SOURCE_ORIGIN) * SCALE)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def load_source():
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=str(SOURCE.resolve()))
    obj = max((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: len(o.data.polygons))
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def setup_render(light_scale=1):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 4
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.35, 0.35, 0.35, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.view_settings.view_transform = "AgX"
    for name, pos, energy, size in [
        ("Key", (0, -3, 4), 450, 4),
        ("Fill", (2, 3, 2), 250, 3),
        ("Rim", (-2, 0, 3), 200, 2),
    ]:
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.shape, data.size = energy * light_scale ** 2, "DISK", size * light_scale
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = Vector(pos) * light_scale
        obj.rotation_euler = (-obj.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", data)
    scene.collection.objects.link(camera)
    data.type = "ORTHO"
    scene.camera = camera
    return camera


def material(name, color, metallic=0, roughness=0.6):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return mat


def view(camera, name, pos, scale=2.3, width=1600, height=1100):
    scene = bpy.context.scene
    camera.location = pos
    camera.rotation_euler = (-Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = scale
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.filepath = str((OUTPUT / name).resolve())
    bpy.ops.render.render(write_still=True)


def inspect():
    obj = load_source()
    obj.data.materials.clear()
    obj.data.materials.append(material("Inspection_Clay", (0.6, 0.6, 0.6)))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    stats = {
        "source": str(SOURCE),
        "vertices": len(obj.data.vertices),
        "polygons": len(obj.data.polygons),
        "triangles": sum(len(p.vertices) - 2 for p in obj.data.polygons),
        "uv_layers": [u.name for u in obj.data.uv_layers],
        "bounds": [list(v) for v in obj.bound_box],
    }
    (OUTPUT / "source_inspection.json").write_text(json.dumps(stats, indent=2))
    camera = setup_render()
    view(camera, "inspection_side.png", (0, -4, 0))
    view(camera, "inspection_top.png", (0, 0, 4))


def classify(co):
    """Semantic cut regions measured against the source's orthographic views."""
    x, y, z = co
    if x < -0.4 and z < -0.356:
        return "Bucket"
    if x >= -0.4 and z < -0.373:
        if y < -0.14:
            return "Track_Left"
        if y > 0.14:
            return "Track_Right"
        return "Undercarriage"
    if x < -0.775 or (x < -0.72 and z < 0.30):
        return "Stick"
    if x < 0.065 or (x < 0.295 and -0.035 < y < 0.155 and z > -0.21):
        return "Boom"
    return "UpperBody"


def partition(source):
    bm = bmesh.new()
    bm.from_mesh(source.data)
    original_boundary = sum(e.is_boundary for e in bm.edges)
    # Slice first, then classify. This produces planar, shared cuts rather than
    # centroid-selected jagged borders through the source's tiny triangles.
    planes = [
        (0, -0.4), (0, -0.775), (0, -0.72), (0, 0.065), (0, 0.295),
        (1, -0.14), (1, 0.14), (1, -0.035), (1, 0.155),
        (2, -0.373), (2, -0.356), (2, 0.30), (2, -0.21),
    ]
    for axis, offset in planes:
        point, normal = Vector((0, 0, 0)), Vector((0, 0, 0))
        point[axis], normal[axis] = offset, 1
        bmesh.ops.bisect_plane(
            bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
            dist=1e-7, plane_co=point, plane_no=normal,
            clear_inner=False, clear_outer=False,
        )
    bm.verts.index_update()
    grouped = {name: [] for name in PART_BUDGETS}
    for face in bm.faces:
        grouped[classify(face.calc_center_median())].append(face)
    assert sum(len(faces) for faces in grouped.values()) == len(bm.faces)
    result = {}
    for name, faces in grouped.items():
        indices, verts, polygons = {}, [], []
        for face in faces:
            polygon = []
            for vertex in face.verts:
                if vertex.index not in indices:
                    indices[vertex.index] = len(verts)
                    verts.append(tuple(vertex.co))
                polygon.append(indices[vertex.index])
            polygons.append(polygon)
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(verts, [], polygons)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        # Close newly opened cut surfaces. These are repair caps, not original
        # detailed bearing/cylinder geometry.
        part_bm = bmesh.new()
        part_bm.from_mesh(mesh)
        boundary = [e for e in part_bm.edges if e.is_boundary]
        capped = bmesh.ops.holes_fill(part_bm, edges=boundary, sides=0).get("faces", [])
        bmesh.ops.triangulate(part_bm, faces=list(part_bm.faces))
        bmesh.ops.recalc_face_normals(part_bm, faces=list(part_bm.faces))
        part_bm.to_mesh(mesh)
        part_bm.free()
        obj["repaired_cut_faces"] = len(capped)
        result[name] = obj
    source_faces_after_cuts = len(bm.faces)
    bm.free()
    bpy.data.objects.remove(source, do_unlink=True)
    print("PARTITION", json.dumps({
        "source_boundary_edges": original_boundary,
        "source_faces_after_cuts": source_faces_after_cuts,
        "parts": {n: len(o.data.polygons) for n, o in result.items()},
    }))
    return result


def optimize(parts):
    for name, obj in parts.items():
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        if name in {"UpperBody", "Boom"}:
            # These fused junctions have intersecting caps. A small voxel pass
            # rebuilds their closed surfaces before reduction, rather than
            # retaining coincident inward-facing cap triangles.
            obj.data.remesh_voxel_size = 0.0035
            obj.data.remesh_voxel_adaptivity = 0
            bpy.ops.object.voxel_remesh()
        triangles = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        modifier = obj.modifiers.new("Mobile_Reduction", "DECIMATE")
        modifier.ratio = min(1, PART_BUDGETS[name] / triangles)
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        # Filling intersecting semantic cuts can create a duplicate cap triangle.
        # Remove coincident topology before closing any remaining small holes.
        bm.verts.index_update()
        seen, duplicate_faces = set(), []
        for face in bm.faces:
            key = tuple(sorted(v.index for v in face.verts))
            if key in seen:
                duplicate_faces.append(face)
            else:
                seen.add(key)
        if duplicate_faces:
            bmesh.ops.delete(bm, geom=duplicate_faces, context="FACES_ONLY")
        isolated_faces = [f for f in bm.faces if all(len(e.link_faces) == 1 for e in f.edges)]
        if isolated_faces:
            bmesh.ops.delete(bm, geom=isolated_faces, context="FACES")
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        if name == "UpperBody":
            # Material borders must have real edges: assigning a window by a
            # large decimated triangle's centroid creates stray blue patches.
            for axis, offset in [(0, 0.105), (0, 0.276), (0, 0.333), (0, 0.396),
                                 (1, -0.19), (2, -0.085), (2, 0.116)]:
                point, normal = Vector((0, 0, 0)), Vector((0, 0, 0))
                point[axis], normal[axis] = offset, 1
                bmesh.ops.bisect_plane(
                    bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                    dist=1e-7, plane_co=point, plane_no=normal,
                    clear_inner=False, clear_outer=False,
                )
            bmesh.ops.triangulate(bm, faces=list(bm.faces))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(obj.data)
        bm.free()
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
        bpy.ops.object.mode_set(mode="OBJECT")
        # Source has no UVs: this is a fresh per-part layout, not a preserved atlas.
        obj.data.uv_layers.active.name = "PreparedUV"
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
        obj["triangle_budget"] = PART_BUDGETS[name]


def assign_materials(parts):
    paint = material("Construction_Yellow", (0.82, 0.49, 0.025), metallic=0.15)
    rubber = material("Track_Rubber", (0.025, 0.031, 0.038), roughness=0.85)
    steel = material("Dark_Steel", (0.11, 0.14, 0.17), metallic=0.65)
    glass = material("Opaque_Tinted_Cab", (0.045, 0.11, 0.15), metallic=0.15, roughness=0.28)
    for name, obj in parts.items():
        obj.data.materials.clear()
        if name.startswith("Track"):
            obj.data.materials.append(rubber)
            obj.data.materials.append(steel)
            for face in obj.data.polygons:
                x, y, z = face.center
                if -0.565 < z < -0.43 and -0.10 < x < 0.76:
                    face.material_index = 1
        elif name in {"Bucket", "Undercarriage"}:
            obj.data.materials.append(steel)
        else:
            obj.data.materials.append(paint)
            if name == "UpperBody":
                obj.data.materials.append(glass)
                for face in obj.data.polygons:
                    x, y, z = face.center
                    side_window = y < -0.19 and (
                        (0.105 < x < 0.276 and -0.085 < z < 0.116)
                        or (0.333 < x < 0.396 and -0.085 < z < 0.116)
                    )
                    if side_window:
                        face.material_index = 1


def parent_keep_world(obj, parent):
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()
    obj.matrix_world = world
    bpy.context.view_layer.update()


def make_hierarchy(parts):
    root = bpy.data.objects.new("Excavator_Root", None)
    bpy.context.scene.collection.objects.link(root)
    root["preparation_status"] = "Articulation draft; no hydraulic or track animation"
    root["source_triangle_count"] = 253448
    root["units"] = "meters; Blender Z up and +Y forward"
    for name, obj in parts.items():
        pivot = asset_point(SOURCE_PIVOTS[name])
        for vertex in obj.data.vertices:
            vertex.co = asset_point(vertex.co) - pivot
        obj.location = pivot
        obj.data.update()
        obj["part_role"] = name
    bpy.context.view_layer.update()
    parent_keep_world(parts["Undercarriage"], root)
    for name in ("Track_Left", "Track_Right", "UpperBody"):
        parent_keep_world(parts[name], parts["Undercarriage"])
    parent_keep_world(parts["Boom"], parts["UpperBody"])
    parent_keep_world(parts["Stick"], parts["Boom"])
    parent_keep_world(parts["Bucket"], parts["Stick"])
    return root


def mesh_stats(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    result = {
        "vertices": len(obj.data.vertices),
        "triangles": sum(len(p.vertices) - 2 for p in obj.data.polygons),
        "boundary_edges": sum(e.is_boundary for e in bm.edges),
        "nonmanifold_edges": sum(not e.is_manifold for e in bm.edges),
        "degenerate_faces": sum(f.calc_area() < 1e-10 for f in bm.faces),
        "uv_layers": [u.name for u in obj.data.uv_layers],
        "materials": [m.name for m in obj.data.materials],
        "parent": obj.parent.name if obj.parent else None,
        "pivot_world_meters": list(obj.matrix_world.translation),
    }
    coords = [obj.matrix_world @ v.co for v in obj.data.vertices]
    result["world_bounds_meters"] = {
        "min": [min(co[i] for co in coords) for i in range(3)],
        "max": [max(co[i] for co in coords) for i in range(3)],
    }
    bm.free()
    return result


def prepare():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    source = load_source()
    parts = partition(source)
    optimize(parts)
    assign_materials(parts)
    root = make_hierarchy(parts)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    stats = {name: mesh_stats(obj) for name, obj in parts.items()}
    print("PREPARED", json.dumps(stats, indent=2))
    total = sum(s["triangles"] for s in stats.values())
    # Small additional faces preserve straight color borders after reduction.
    assert total <= 20000, total
    assert all(s["triangles"] > 0 and s["uv_layers"] for s in stats.values())
    assert all(s["nonmanifold_edges"] == 0 and s["degenerate_faces"] == 0 for s in stats.values()), stats
    (OUTPUT / "preparation_report.json").write_text(json.dumps({
        "source": str(SOURCE), "source_triangles": 253448,
        "source_uv_layers": [], "source_materials": [],
        "prepared_triangles": total, "parts": stats,
        "notes": [
            "Original file is unchanged. This is a separate review asset, not integrated into Unity.",
            "Closed repair caps replace fused joint surfaces; pivots are visually estimated.",
            "Boom and upper body were resurfaced to repair intersecting fused junctions.",
            "Four solid-color PBR materials and new per-part UVs; no original textures.",
            "No rigged cylinders, dynamic hoses, cycling track belt, skinning or animation clips.",
            "Test device performance and working-range poses before production integration.",
        ],
    }, indent=2))
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in parts.values():
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=str((OUTPUT / "Meshy_Excavator_MobileSplit.fbx").resolve()),
        use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", global_scale=1.0,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=True, mesh_smooth_type="FACE",
        bake_anim=False, add_leaf_bones=False, path_mode="AUTO",
    )
    camera = setup_render(light_scale=SCALE)
    # Asset's world center is elevated above its ground-level origin.
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    camera.location = (-13, -9, 10)
    target = Vector((0, 1.6, 2.8))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = 11
    scene.render.filepath = str((OUTPUT / "prepared_preview.png").resolve())
    bpy.ops.render.render(write_still=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str((OUTPUT / "Meshy_Excavator_MobileSplit.blend").resolve()))
    # Separate diagnostic pose: never bake these trial rotations into the FBX.
    parts["UpperBody"].rotation_euler.z = math.radians(20)
    parts["Boom"].rotation_euler.x = math.radians(8)
    parts["Stick"].rotation_euler.x = math.radians(-12)
    parts["Bucket"].rotation_euler.x = math.radians(18)
    scene.render.filepath = str((OUTPUT / "articulation_check.png").resolve())
    bpy.ops.render.render(write_still=True)


def verify():
    expected = json.loads((OUTPUT / "preparation_report.json").read_text())
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=str((OUTPUT / "Meshy_Excavator_MobileSplit.fbx").resolve()))
    meshes = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
    assert set(meshes) == set(PART_BUDGETS), set(meshes)
    results = {}
    for name, obj in meshes.items():
        actual = mesh_stats(obj)
        original = expected["parts"][name]
        assert actual["triangles"] == original["triangles"], name
        assert actual["parent"] == original["parent"], name
        assert actual["uv_layers"], name
        assert actual["boundary_edges"] == actual["nonmanifold_edges"] == actual["degenerate_faces"] == 0, (name, actual)
        for i in range(3):
            assert abs(actual["pivot_world_meters"][i] - original["pivot_world_meters"][i]) < 0.001, name
            for boundary in ("min", "max"):
                assert abs(actual["world_bounds_meters"][boundary][i] -
                           original["world_bounds_meters"][boundary][i]) < 0.001, (name, boundary)
        assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co), name
        assert all(math.isfinite(c) for u in obj.data.uv_layers.active.data for c in u.uv), name
        assert all(p.material_index < len(obj.data.materials) for p in obj.data.polygons), name
        results[name] = actual
    assert not any(o.animation_data and o.animation_data.action for o in bpy.context.scene.objects)
    report = {
        "status": "PASS", "method": "Independent FBX re-import into Blender; not a Unity/device test",
        "mesh_count": len(meshes),
        "triangle_count": sum(r["triangles"] for r in results.values()),
        "checks": ["part names", "triangle counts", "hierarchy", "pivots within 1 mm",
                   "bounds within 1 mm", "closed manifold surfaces", "UVs",
                   "finite vertex/UV data", "material slots", "no baked animation"],
    }
    (OUTPUT / "verification_report.json").write_text(json.dumps(report, indent=2))
    print("VERIFICATION", json.dumps(report, indent=2))
    camera = setup_render(light_scale=SCALE)
    camera.location = (-13, -9, 10)
    target = Vector((0, 1.6, 2.8))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.ortho_scale = 11
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.render.filepath = str((OUTPUT / "fbx_roundtrip_preview.png").resolve())
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--inspect", action="store_true")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.inspect:
        inspect()
    if args.prepare:
        prepare()
    if args.verify:
        verify()
