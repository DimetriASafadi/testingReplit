#!/usr/bin/env python3
"""Export the captured production equipment hierarchy as editable metre-scale FBX.

Run using Blender 4.4+:
  blender --background --factory-startup --python tools/export_equipment_fbx.py -- \
    --equipment-json exports/equipment/Equipment-Production-Meshes.json

This deliberately refuses the older pose-only fixture: production AnimationClips
with captured local-TRS samples are required for deliverable animated FBX files.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
OUT_ASSETS = ROOT / "testingReplic/Assets/NewGaza/Art/EquipmentFBX"
OUT_AUDIT = ROOT / "exports/equipment/Equipment-FBX-Audit.json"
KINDS = {
    "excavator": "YellowExcavator",
    "truck": "GreenTipper",
    "bulldozer": "YellowBulldozer",
}
# Proper, determinant-positive basis conversion: Unity (x,y,z) -> Blender (x,-z,y).
BASIS = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))


def arguments():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--equipment-json", type=Path,
                        default=ROOT / "exports/equipment/Equipment-Production-Meshes.json")
    parser.add_argument("--asset-dir", type=Path, default=OUT_ASSETS)
    parser.add_argument("--audit", type=Path, default=OUT_AUDIT)
    parser.add_argument("--proof-dir", type=Path,
                        default=ROOT / "exports/equipment/Equipment-FBX-Proofs")
    return parser.parse_args(args)


def vec3(values):
    return Vector(tuple(float(x) for x in values))


def unity_quat(values):
    # Input is Unity/System.Numerics x,y,z,w; conjugating by B preserves handedness.
    q = Quaternion((float(values[3]), float(values[0]), float(values[1]), float(values[2])))
    m = BASIS @ q.to_matrix() @ BASIS.transposed()
    return m.to_quaternion()


def read_fixture(path):
    if not path.is_file():
        raise FileNotFoundError(f"Production equipment export is missing: {path}")
    data = json.loads(path.read_text(encoding="utf-8"))
    clips = data.get("AnimationClips")
    if not isinstance(clips, list) or not clips:
        raise ValueError("AnimationClips are absent. Refusing stale pose-only fixture; recapture production animation first.")
    for clip in clips:
        frames = clip.get("Frames")
        if not isinstance(frames, list) or len(frames) < 2:
            raise ValueError(f"Animation clip {clip.get('Name')!r} needs at least two captured frames.")
        times = [float(frame["TimeSeconds"]) for frame in frames]
        if times != sorted(times) or len(set(times)) < 2:
            raise ValueError(f"Animation clip {clip.get('Name')!r} has invalid sample times.")
    if not data.get("Meshes") or not data.get("Materials"):
        raise ValueError("Production fixture must include meshes and materials.")
    return data


def make_material(source, texture_lookup, texture_paths, output_dir, baked_cache):
    source_id = source["Id"]
    if source_id in baked_cache:
        return baked_cache[source_id]
    rgba = tuple(float(v) for v in source["BaseColorRgba"])
    material = bpy.data.materials.new(source["Name"])
    material.diffuse_color = rgba
    material.use_nodes = True
    principled = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    principled.inputs["Base Color"].default_value = rgba
    principled.inputs["Metallic"].default_value = float(source.get("Metallic", 0))
    principled.inputs["Roughness"].default_value = max(0.0, min(1.0, 1 - float(source.get("Smoothness", 0.5))))
    baked_path = None
    texture_id = source.get("BaseTexture")
    if texture_id:
        src_path = texture_paths[texture_id]
        if not src_path.is_file():
            raise FileNotFoundError(f"Production texture {texture_id} is missing: {src_path}")
        image = bpy.data.images.load(str(src_path), check_existing=True)
        image.colorspace_settings.name = "sRGB"
        # Keep the exported albedo as a single image texture. UV repeat scale below
        # reproduces Unity's tiling without relying on unsupported shader nodes.
        baked_path = output_dir / f"{texture_id}-{source_id}-fbx-albedo.png"
        baked = image.copy()
        baked.name = f"{source_id} baked albedo"
        pixels = list(baked.pixels[:])
        for i in range(0, len(pixels), 4):
            for c in range(3):
                pixels[i + c] *= rgba[c]
            pixels[i + 3] *= rgba[3]
        baked.pixels[:] = pixels
        baked.filepath_raw = str(baked_path)
        baked.file_format = "PNG"
        baked.save()
        baked.pack()
        tex = material.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = baked
        tex.label = "Baked production albedo × source BaseColor"
        material.node_tree.links.new(tex.outputs["Color"], principled.inputs["Base Color"])
        material["BakedAlbedo"] = str(baked_path)
        material["ProductionTextureScale"] = tuple(float(v) for v in source.get("TextureScale", [1, 1]))
        material["ProductionBaseColor"] = rgba
    baked_cache[source_id] = material
    return material


def mesh_data(source, material, texture_scale=(1, 1)):
    verts = source.get("Vertices", [])
    norms = source.get("Normals", [])
    uv = source.get("Uv", [])
    indices = source.get("Triangles", [])
    count = len(verts) // 3
    if len(verts) % 3 or len(norms) != len(verts) or len(indices) % 3:
        raise ValueError(f"Invalid production mesh arrays: {source.get('Name')}")
    if any(int(i) < 0 or int(i) >= count for i in indices):
        raise ValueError(f"Out-of-range triangle index in {source.get('Name')}")
    points = [BASIS @ vec3(verts[i:i + 3]) for i in range(0, len(verts), 3)]
    faces = [tuple(int(i) for i in indices[j:j + 3]) for j in range(0, len(indices), 3)]
    mesh = bpy.data.meshes.new(source.get("Name", source["Id"]))
    mesh.from_pydata(points, [], faces)
    mesh.materials.append(material)
    mesh.update()
    layer = mesh.uv_layers.new(name="Production UV0")
    sx, sy = texture_scale
    for loop in mesh.loops:
        ix = loop.vertex_index * 2
        u = float(uv[ix]) if ix < len(uv) else 0.0
        v = float(uv[ix + 1]) if ix + 1 < len(uv) else 0.0
        layer.data[loop.index].uv = (u * sx, v * sy)
    # Proper basis conversion has positive determinant, so production winding stays intact.
    custom = []
    for loop in mesh.loops:
        off = loop.vertex_index * 3
        custom.append((BASIS @ vec3(norms[off:off + 3])).normalized())
    if custom:
        mesh.normals_split_custom_set(custom)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return mesh


def node_props(node):
    rotation = node.get("LocalRotationQuat", node.get("LocalRotation"))
    if rotation is None:
        raise ValueError(f"Node {node.get('Path')} lacks captured local rotation.")
    position = BASIS @ vec3(node.get("LocalPosition", (0, 0, 0)))
    quat = unity_quat(rotation)
    scale = vec3(node.get("LocalScale", (1, 1, 1)))
    # Basis permutation applies to scale axes too.
    scale = Vector((scale.x, scale.z, scale.y))
    # FBX does not reliably round-trip animated visibility. Animate only the
    # geometry-bearing node's scale, preserving its rig/joint ancestors and
    # pivots even when a machine wrapper is inactive in a captured snapshot.
    if node.get("Mesh") and not node.get("Active", True):
        scale = Vector((0, 0, 0))
    return position, quat, scale


def node_path(node):
    return node.get("Path", node.get("Name", "Node"))


def create_machine(machine, data, mesh_lookup, material_lookup, texture_paths, output_dir, material_cache):
    bypath = {}
    nodes = machine.get("Nodes", [])
    if not nodes:
        raise ValueError(f"Machine {machine.get('Kind')} contains no hierarchy nodes.")
    for node in nodes:
        path = node_path(node)
        if path in bypath:
            raise ValueError(f"Duplicate node path: {path}")
        position, rotation, scale = node_props(node)
        obj = bpy.data.objects.new(node.get("Name", path.split("/")[-1]), None)
        bpy.context.scene.collection.objects.link(obj)
        obj.empty_display_type = "PLAIN_AXES"
        obj.empty_display_size = 0.06
        # The generated vehicle wrapper alone carries the city-scale .07; remove
        # it and its scene placement to preserve model-space metre dimensions.
        if len(path.split("/")) == 1:
            obj.location = (0, 0, 0)
            obj.rotation_mode = "QUATERNION"
            obj.rotation_quaternion = rotation
            obj.scale = (1, 1, 1)
            obj["RootVehicleScaleNeutralized"] = True
        else:
            obj.location = position
            obj.rotation_mode = "QUATERNION"
            obj.rotation_quaternion = rotation
            obj.scale = scale
        obj["ProductionNodePath"] = path
        obj["ProductionActive"] = bool(node.get("Active", True))
        if node.get("Mesh"):
            obj["MeshActive"] = bool(node.get("Active", True))
        bypath[path] = (obj, node)
    for path, (obj, node) in bypath.items():
        parent_path = path.rsplit("/", 1)[0] if "/" in path else None
        parent = None
        if parent_path and parent_path in bypath:
            parent = bypath[parent_path][0]
        elif parent_path:
            # Production paths sometimes omit non-exported hierarchy intermediates.
            candidates = [p for p in bypath if path.startswith(p + "/")]
            if candidates:
                parent = bypath[max(candidates, key=len)][0]
        if parent is not None:
            obj.parent = parent
            obj.matrix_parent_inverse = Matrix.Identity(4)
            # Setting parent preserves the old world matrix; restore the authored
            # local transform explicitly so pivots remain true hierarchy pivots.
            if len(path.split("/")) != 1:
                position, rotation, scale = node_props(node)
                obj.location = position
                obj.rotation_mode = "QUATERNION"
                obj.rotation_quaternion = rotation
                obj.scale = scale
        mesh_id = node.get("Mesh")
        if mesh_id:
            if mesh_id not in mesh_lookup:
                raise ValueError(f"Unknown production mesh {mesh_id!r} at {path}")
            material_id = node.get("Material")
            if material_id not in material_lookup:
                raise ValueError(f"Unknown production material {material_id!r} at {path}")
            mat_src = material_lookup[material_id]
            mat = make_material(mat_src, data.get("TexturesById", {}), texture_paths,
                                output_dir, material_cache)
            geom = mesh_data(mesh_lookup[mesh_id], mat, mat_src.get("TextureScale", (1, 1)))
            mesh_obj = bpy.data.objects.new(node.get("Name", path.split("/")[-1]) + " • mesh", geom)
            bpy.context.scene.collection.objects.link(mesh_obj)
            mesh_obj.parent = obj
            mesh_obj.matrix_parent_inverse = Matrix.Identity(4)
            mesh_obj.location = (0, 0, 0)
            mesh_obj.rotation_mode = "QUATERNION"
            mesh_obj.rotation_quaternion = (1, 0, 0, 0)
            mesh_obj.scale = (1, 1, 1)
            mesh_obj["ProductionNodePath"] = path
            mesh_obj["ProductionActive"] = bool(node.get("Active", True))
    return bypath


def pose_nodes(frame, kind):
    for machine in frame.get("Machines", []):
        if machine.get("Kind") == kind:
            return {node_path(n): n for n in machine.get("Nodes", [])}
    return {}


def add_clips(clips, kind, node_objects, baseline_machine, scene_fps, city_units_to_meters):
    """Make one synchronized active action per object across a single FBX scene take.

    Blender's FBX exporter explicitly creates a separate AnimStack for every
    NLA strip. These clips therefore share one consecutive scene timeline and
    use active actions with NLA/all-actions export disabled.
    """
    first_nodes = {node_path(n): n for n in baseline_machine.get("Nodes", [])}
    timeline = []
    cursor = 1.0
    for clip in clips:
        frames = clip["Frames"]
        source_rate = float(clip.get("FramesPerSecond", 0))
        if source_rate <= 0:
            raise ValueError(f"Clip {clip.get('Name')!r} has invalid FramesPerSecond.")
        first_time = float(frames[0]["TimeSeconds"])
        last_time = float(frames[-1]["TimeSeconds"])
        duration_frames = (last_time - first_time) * scene_fps
        if duration_frames <= 0 or abs(duration_frames - round(duration_frames)) > 1e-3:
            raise ValueError(f"Clip {clip.get('Name')!r} times do not map to the {scene_fps} fps export timeline.")
        duration_frames = round(duration_frames)
        start_frame = cursor
        end_frame = start_frame + duration_frames
        timeline.append({
            "Clip": clip, "FirstSeconds": first_time, "StartFrame": start_frame,
            "EndFrame": end_frame, "SourceRate": source_rate,
            "Frames": frames,
        })
        cursor = end_frame + 1.0

    root_paths = [path for path in node_objects if "/" not in path]
    if len(root_paths) != 1:
        raise ValueError(f"Expected one machine wrapper/root node, found {root_paths}.")
    root_path = root_paths[0]
    root_offset = Vector((0, 0, 0))
    root_rotation_prefix = None
    root_fallback = first_nodes.get(root_path)
    for item in timeline:
        root_samples = [
            node for frame in item["Frames"]
            for node in [pose_nodes(frame, kind).get(root_path)] if node is not None
        ]
        base_node = root_samples[0] if root_samples else root_fallback
        end_node = root_samples[-1] if root_samples else base_node
        item["RootTranslationOffset"] = root_offset.copy()
        item["RootRotationPrefix"] = root_rotation_prefix.copy() if root_rotation_prefix else None
        item["RootBaseline"] = base_node
        if base_node is not None and end_node is not None:
            base_position = BASIS @ vec3(base_node.get("LocalPosition", (0, 0, 0)))
            end_position = BASIS @ vec3(end_node.get("LocalPosition", (0, 0, 0)))
            root_offset += (end_position - base_position) * city_units_to_meters
            base_rotation = unity_quat(base_node.get("LocalRotationQuat", base_node.get("LocalRotation")))
            end_rotation = unity_quat(end_node.get("LocalRotationQuat", end_node.get("LocalRotation")))
            relative_end = base_rotation.inverted() @ end_rotation
            root_rotation_prefix = (end_rotation if root_rotation_prefix is None
                                    else root_rotation_prefix @ relative_end)
        root_fallback = end_node

    scene = bpy.context.scene
    scene.timeline_markers.clear()
    report = []
    for item in timeline:
        name = str(item["Clip"]["Name"])
        start, end = item["StartFrame"], item["EndFrame"]
        scene.timeline_markers.new(f"{name}:start", frame=round(start))
        scene.timeline_markers.new(f"{name}:end", frame=round(end))
        report.append({
            "Name": name, "Frames": len(item["Frames"]),
            "SourceFramesPerSecond": item["SourceRate"],
            "DurationSeconds": round(item["EndFrame"] - item["StartFrame"], 6) / scene_fps,
            "SceneFrameStart": round(start), "SceneFrameEnd": round(end),
        })

    for path, (obj, _node_source) in node_objects.items():
        action = bpy.data.actions.new(f"{KINDS[kind]} Unified Scene Timeline | {path}")
        obj.animation_data_create()
        obj.animation_data.action = action
        curves = {}
        for data_path, width in (("location", 3), ("rotation_quaternion", 4), ("scale", 3)):
            for component in range(width):
                curves[data_path, component] = action.fcurves.new(
                    data_path=data_path, index=component, action_group="Unified machine timeline")
        # Blender 4.4 uses layered actions: assigning the Action alone leaves
        # its curves inert until the target object's compatible slot is chosen.
        if len(action.slots) != 1:
            raise ValueError(f"Expected one Blender action slot for {path}; got {len(action.slots)}.")
        obj.animation_data.action_slot = action.slots[0]
        last_values = None
        for item in timeline:
            frames = item["Frames"]
            first_time = item["FirstSeconds"]
            start = item["StartFrame"]
            fallback_node = first_nodes.get(path)
            captured_samples = []
            for record in frames:
                node = pose_nodes(record, kind).get(path)
                if node is not None:
                    captured_samples.append((float(record["TimeSeconds"]), node))
            clip_root_baseline = item["RootBaseline"] if path == root_path else None
            if not captured_samples:
                if fallback_node is None and last_values is None:
                    continue
                values = last_values or _animation_values(
                    fallback_node, path,
                    fallback_node if len(path.split("/")) == 1 else None,
                    city_units_to_meters,
                    item["RootTranslationOffset"], item["RootRotationPrefix"])
                for scene_frame in (start, item["EndFrame"]):
                    _insert_transform_sample(curves, scene_frame, values)
                last_values = values
                continue
            # Hold the preceding stage's endpoint until this clip's first
            # captured sample if the source timeline has a nonzero first time.
            clip_samples = []
            for seconds, node in captured_samples:
                offset_frames = (seconds - first_time) * scene_fps
                if abs(offset_frames - round(offset_frames)) > 1e-3:
                    raise ValueError(f"Clip {item['Clip'].get('Name')!r} sample at {seconds:g}s "
                                     f"does not land on an export frame.")
                scene_frame = start + round(offset_frames)
                values = _animation_values(
                    node, path, clip_root_baseline, city_units_to_meters,
                    item["RootTranslationOffset"], item["RootRotationPrefix"])
                clip_samples.append((scene_frame, values))
                last_values = values
            if clip_samples[0][0] > start:
                _insert_transform_sample(curves, start, clip_samples[0][1])
            for scene_frame, values in clip_samples:
                _insert_transform_sample(curves, scene_frame, values)
            if clip_samples[-1][0] < item["EndFrame"]:
                _insert_transform_sample(curves, item["EndFrame"], clip_samples[-1][1])
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
            curve.update()
        if action.frame_range[1] + 1e-3 < cursor - 1:
            raise ValueError(f"Unified action for {path} ends at {action.frame_range[1]:g}; "
                             f"staged timeline ends at {cursor - 1:g}.")
    scene.frame_start = 1
    scene.frame_end = round(cursor - 1)
    scene.frame_set(scene.frame_start)
    # Confirm Blender 4.4's layered action slots actually drive the hierarchy;
    # some machines are intentionally static during another machine's clip.
    changed = False
    for probe in timeline:
        probe_a = round(probe["StartFrame"])
        probe_b = round((probe["StartFrame"] + probe["EndFrame"]) * 0.5)
        scene.frame_set(probe_a)
        before = {path: obj.matrix_basis.copy() for path, (obj, _node) in node_objects.items()}
        scene.frame_set(probe_b)
        for path, (obj, _node) in node_objects.items():
            delta = sum(abs(obj.matrix_basis[row][col] - before[path][row][col])
                        for row in range(4) for col in range(4))
            changed |= delta > 1e-5
        if changed:
            break
    if not changed:
        raise ValueError(f"Captured {kind} timeline curves do not evaluate on the Blender hierarchy.")
    scene.frame_set(scene.frame_start)
    return report


def _animation_values(node, path, root_baseline, city_units_to_meters,
                      root_translation_offset=Vector((0, 0, 0)),
                      root_rotation_prefix=None):
    pos, quat, scale = node_props(node)
    if len(path.split("/")) == 1:
        # Keep captured root motion in model metres and append normalized
        # clip offsets consecutively, avoiding teleports at clip boundaries.
        origin = Vector((0, 0, 0))
        if root_baseline is not None:
            origin = BASIS @ vec3(root_baseline.get("LocalPosition", (0, 0, 0)))
        pos = (pos - origin) * city_units_to_meters + root_translation_offset
        if root_baseline is not None and root_rotation_prefix is not None:
            base_rotation = unity_quat(
                root_baseline.get("LocalRotationQuat", root_baseline.get("LocalRotation")))
            quat = root_rotation_prefix @ (base_rotation.inverted() @ quat)
    return {
        "location": tuple(pos),
        "rotation_quaternion": tuple(quat),
        "scale": (1, 1, 1) if len(path.split("/")) == 1 else tuple(scale),
    }


def _insert_transform_sample(curves, frame, values):
    for (data_path, component), curve in curves.items():
        curve.keyframe_points.insert(float(frame), float(values[data_path][component]), options={"FAST"})


def render_reference(kind, args, pose_frame):
    args.proof_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.frame_set(round(pose_frame))
    bpy.context.view_layer.update()
    renderables = [o for o in bpy.context.scene.objects
                   if o.type == "MESH" and o.matrix_world.to_scale().length_squared > 1e-12]
    if not renderables:
        raise ValueError(f"No renderable mesh geometry for {kind} offline reference.")
    points = [o.matrix_world @ Vector(corner) for o in renderables for corner in o.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (low + high) * 0.5
    extent = max((high - low).length, 0.8)
    camdata = bpy.data.cameras.new("Offline reference camera")
    camera = bpy.data.objects.new("Offline reference camera", camdata)
    scene.collection.objects.link(camera)
    camdata.lens = 55
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    # A side-biased front three-quarter reveals the excavator's full boom
    # length instead of looking down the arm from the rear/cab axis.
    camera_direction = Vector((1.50, -0.55, 0.55)).normalized()
    camera_rotation = (-camera_direction).to_track_quat("-Z", "Y")
    camera.rotation_euler = camera_rotation.to_euler()
    horizontal_tangent = camdata.sensor_width / (2 * camdata.lens)
    aspect = scene.render.resolution_x / scene.render.resolution_y
    vertical_tangent = horizontal_tangent / aspect
    camera_right = camera_rotation @ Vector((1, 0, 0))
    camera_up = camera_rotation @ Vector((0, 1, 0))
    # Frame the whole production hierarchy, including the extended arm, rather
    # than fitting only the machine body. Target roughly 70% image occupancy.
    camera_distance = max(
        max(abs((point - center).dot(camera_right)) / (horizontal_tangent * 0.55)
            + (point - center).dot(camera_direction) for point in points),
        max(abs((point - center).dot(camera_up)) / (vertical_tangent * 0.55)
            + (point - center).dot(camera_direction) for point in points),
        max(extent * 0.25, 0.5))
    camera.location = center + camera_direction * camera_distance
    scene.camera = camera
    lightdata = bpy.data.lights.new("Neutral softbox", "AREA")
    light = bpy.data.objects.new("Neutral softbox", lightdata)
    scene.collection.objects.link(light)
    light.location = center + Vector((extent, -extent, extent * 2.2))
    lightdata.energy = 1100
    lightdata.shape = "DISK"
    lightdata.size = extent * 2.5
    plane_mesh = bpy.data.meshes.new("Neutral proof ground mesh")
    plane_mesh.from_pydata([
        (center.x - extent * 3, center.y - extent * 3, low.z - 0.015),
        (center.x + extent * 3, center.y - extent * 3, low.z - 0.015),
        (center.x + extent * 3, center.y + extent * 3, low.z - 0.015),
        (center.x - extent * 3, center.y + extent * 3, low.z - 0.015),
    ], [], [(0, 1, 2, 3)])
    plane = bpy.data.objects.new("Neutral proof ground", plane_mesh)
    scene.collection.objects.link(plane)
    ground_mat = bpy.data.materials.new("Neutral grey proof ground")
    ground_mat.diffuse_color = (0.16, 0.18, 0.19, 1)
    plane_mesh.materials.append(ground_mat)
    world = scene.world or bpy.data.worlds.new("Equipment proof world")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.19, 0.22, 0.26, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = str(args.proof_dir / f"{KINDS[kind]}-Offline-Reference.png")
    bpy.ops.render.render(write_still=True)
    scene.camera = None
    bpy.data.objects.remove(camera, do_unlink=True)
    bpy.data.cameras.remove(camdata)
    bpy.data.objects.remove(light, do_unlink=True)
    bpy.data.lights.remove(lightdata)
    bpy.data.objects.remove(plane, do_unlink=True)
    bpy.data.meshes.remove(plane_mesh)
    bpy.data.materials.remove(ground_mat)
    return scene.render.filepath


def bounds_for(objects):
    points = []
    for obj in objects:
        if obj.type != "MESH" or obj.matrix_world.to_scale().length_squared <= 1e-12:
            continue
        points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    if not points:
        raise ValueError("FBX round-trip contains no mesh bounds.")
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return tuple(high - low)


def verify_fbx_round_trip(path, source_objects, expected_end_frame, source_frame):
    bpy.context.view_layer.update()
    expected = bounds_for(source_objects)
    before = {obj.as_pointer() for obj in bpy.context.scene.objects}
    source_frame_start = bpy.context.scene.frame_start
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    imported = [obj for obj in bpy.context.scene.objects if obj.as_pointer() not in before]
    actions = []
    for obj in imported:
        animation = obj.animation_data
        if animation is None:
            continue
        if animation.action:
            actions.append(animation.action)
        for track in animation.nla_tracks:
            actions.extend(strip.action for strip in track.strips if strip.action)
    actions = list(dict.fromkeys(actions))
    import_frame_offset = min((float(action.frame_range[0]) for action in actions),
                              default=source_frame_start) - source_frame_start
    bpy.context.scene.frame_set(round(source_frame + import_frame_offset))
    bpy.context.view_layer.update()
    actual = bounds_for(imported)
    deltas = [abs(a - e) for a, e in zip(actual, expected)]
    allowed = [max(0.025, e * 0.04) for e in expected]
    if any(delta > limit for delta, limit in zip(deltas, allowed)):
        raise ValueError(f"FBX metre dimensions changed on reimport: source={expected}, imported={actual}")
    varying = []
    for action in actions:
        curves = getattr(action, "fcurves", ())
        if any(len(curve.keyframe_points) >= 2 and
               max(key.co.y for key in curve.keyframe_points)
               - min(key.co.y for key in curve.keyframe_points) > 1e-5
               for curve in curves):
            varying.append(action.name)
    if not varying:
        raise ValueError("FBX reimport found no non-constant captured transform animation.")
    imported_range_end = max((float(action.frame_range[1]) for action in actions), default=0.0)
    if imported_range_end < expected_end_frame - 1.0:
        raise ValueError(
            f"FBX reimport action ends at frame {imported_range_end:g}, before unified timeline "
            f"frame {expected_end_frame:g}; clips may have been split/lost.")
    imported_meshes = sum(obj.type == "MESH" for obj in imported)
    imported_empties = sum(obj.type == "EMPTY" for obj in imported)
    imported_mesh_objects = [obj for obj in imported if obj.type == "MESH"]
    missing_uv = [obj.name for obj in imported_mesh_objects if len(obj.data.uv_layers) == 0]
    missing_material = [obj.name for obj in imported_mesh_objects if len(obj.data.materials) == 0]
    if missing_uv or missing_material:
        raise ValueError(f"FBX lost mesh UV/material assignments: UV={missing_uv}, materials={missing_material}")
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    return {"ReimportedDimensionsMeters": [round(v, 5) for v in actual],
            "DimensionToleranceMeters": [round(v, 5) for v in allowed],
            "ReimportedMeshObjects": imported_meshes,
            "ReimportedHierarchyNodes": imported_empties,
            "UVMappedMeshObjects": imported_meshes - len(missing_uv),
            "MaterialAssignedMeshObjects": imported_meshes - len(missing_material),
            "ReimportedVaryingActions": varying,
            "UnifiedActionRangeEndFrame": round(imported_range_end, 3),
            "ExpectedUnifiedRangeEndFrame": expected_end_frame}


def export_one(kind, machine, data, args, meshes, materials, textures):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.meshes:
        if block.users == 0:
            bpy.data.meshes.remove(block)
    for block in bpy.data.actions:
        bpy.data.actions.remove(block)
    args.asset_dir.mkdir(parents=True, exist_ok=True)
    cache = {}
    node_objects = create_machine(machine, data, meshes, materials, textures, args.asset_dir, cache)
    rates = [int(round(float(c["FramesPerSecond"]))) for c in data["AnimationClips"]]
    scene_fps = math.lcm(*rates)
    city_units_to_meters = float(data.get("Motion", {}).get("CityUnitsToMeters", 20.0))
    clips_report = add_clips(data["AnimationClips"], kind, node_objects, machine,
                             scene_fps, city_units_to_meters)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.render.fps = scene_fps
    args.asset_dir.mkdir(parents=True, exist_ok=True)
    work_clip = next((clip for clip in clips_report if clip["Name"] == "WorkingCycle"),
                     clips_report[0])
    proof_seconds = min(4.9, float(work_clip["DurationSeconds"]) * 0.5)
    proof_frame = work_clip["SceneFrameStart"] + round(proof_seconds * scene_fps)
    proof_path = render_reference(kind, args, proof_frame)
    path = args.asset_dir / f"{KINDS[kind]}.fbx"
    source_objects = list(bpy.context.scene.objects)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"EMPTY", "MESH"},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z", axis_up="Y", use_space_transform=True,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, add_leaf_bones=False,
        path_mode="COPY", embed_textures=True, use_custom_props=True)
    round_trip = verify_fbx_round_trip(path, source_objects, bpy.context.scene.frame_end,
                                       bpy.context.scene.frame_current)
    return {"File": str(path.relative_to(ROOT)), "Bytes": path.stat().st_size,
            "HierarchyNodes": len(node_objects),
            "MeshObjects": sum(1 for o in bpy.context.scene.objects if o.type == "MESH"),
            "Triangles": sum(len(o.data.polygons) for o in bpy.context.scene.objects if o.type == "MESH"),
            "AnimationClips": clips_report,
            "SceneFrameRate": scene_fps,
            "OfflineProofPose": {"Clip": work_clip["Name"],
                                 "ClipTimeSeconds": proof_seconds,
                                 "SceneFrame": proof_frame},
            "RoundTripCheck": round_trip,
            "OfflinePNGProof": str(Path(proof_path).relative_to(ROOT))}


def main():
    args = arguments()
    data = read_fixture(args.equipment_json)
    meshes = {m["Id"]: m for m in data["Meshes"]}
    materials = {m["Id"]: m for m in data["Materials"]}
    textures = {}
    for texture in data.get("Textures", []):
        p = Path(texture["Png"])
        if not p.is_absolute():
            p = args.equipment_json.parent / p
        textures[texture["Id"]] = p
    records = {}
    kinds_seen = set()
    for clip in data["AnimationClips"]:
        for frame in clip["Frames"]:
            kinds_seen.update(m.get("Kind") for m in frame.get("Machines", []))
    for kind, filename in KINDS.items():
        if kind not in kinds_seen:
            raise ValueError(f"Captured animation is missing required equipment kind {kind}.")
    first = data["AnimationClips"][0]["Frames"][0]
    for kind in KINDS:
        machines = [m for m in first.get("Machines", []) if m.get("Kind") == kind]
        if len(machines) != 1:
            raise ValueError(f"Expected one {kind} machine in clip baseline; got {len(machines)}.")
        records[KINDS[kind]] = export_one(kind, machines[0], data, args, meshes, materials, textures)
    args.audit.parent.mkdir(parents=True, exist_ok=True)
    args.audit.write_text(json.dumps({
        "SchemaVersion": 1, "Source": str(args.equipment_json),
        "CoordinateConversion": "Unity (x,y,z) -> Blender (x,-z,y), proper basis conjugation; metre scale; wrapper scale/translation neutralized.",
        "AnimationSource": "Captured AnimationClips Frames local TRS; clips are consecutive ranges in one synchronized whole-scene FBX take, with clip range labels in the scene timeline and audit. Exporter uses active actions with NLA strips and all-actions disabled.",
        "RootMotion": "Root scale is neutralized; each clip root translation is rebased to its first captured position, converted from city units to model metres, and offset from the prior clip endpoint to keep the staged timeline continuous. Captured root rotations/deltas are retained.",
        "InactiveGeometry": "Only mesh-bearing nodes key local scale to zero when their captured Active flag is false; hierarchy/root activity never disables the whole machine.",
        "TrackAnimationLimitation": "Captured track-shoe geometry is static per snapshot in this JSON and is not continuously deformed by FBX transform clips; native gameplay track motion remains procedural/canonical.",
        "Exports": records,
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(records)} production equipment FBX assets; audit: {args.audit}")


if __name__ == "__main__":
    main()