#!/usr/bin/env python3
"""Render honest CPU-only Blender proof images from production equipment exports.

Run with Blender 4.4+:
  blender --background --factory-startup --python tools/render_equipment_proof.py -- \
    --equipment-json exports/equipment/Equipment-Production-Meshes.json \
    --output-dir exports/equipment

The PNGs use the captured production vertices, normals, UVs, material constants,
texture PNGs, and node world transforms. They are offline external renders, not
Unity screenshots or proof of hardware performance.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


ROOT = Path(__file__).resolve().parents[1]
EQUIPMENT_KINDS = ("excavator", "truck", "bulldozer")
STAGE_LABELS = (
    ("Parked", "baseline-parked"),
    ("Bucket load", "clearing-bucket-load-0.8"),
    ("Haul", "haul-loaded-5s"),
    ("Full tip", "tipper-full-dump"),
    ("Empty return", "tipper-returned-empty"),
)


def cli_arguments():
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--equipment-json",
        type=Path,
        default=ROOT / "exports/equipment/Equipment-Production-Meshes.json",
        help="Production C# fixture export",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=ROOT / "exports/equipment",
        help="Directory for the two offline PNG proof renders",
    )
    parser.add_argument("--hero-name", default="Equipment-Offline-Hero.png")
    parser.add_argument("--poses-name", default="Equipment-Offline-Poses.png")
    return parser.parse_args(arguments)


def require_file(path, description):
    if not path.is_file() or path.stat().st_size == 0:
        raise FileNotFoundError(f"Required {description} is missing or empty: {path}")
    return path


def read_export(path):
    require_file(path, "production Equipment-Production-Meshes.json")
    data = json.loads(path.read_text(encoding="utf-8"))
    summary = data.get("Summary", {})
    meshes = data.get("Meshes", [])
    if not meshes or len(meshes) != summary.get("UniqueMeshCount"):
        raise ValueError("Production mesh inventory does not match its measured summary.")
    measured_vertices = sum(len(mesh["Vertices"]) // 3 for mesh in meshes)
    if measured_vertices != summary.get("UniqueMeshVertices"):
        raise ValueError("Production vertex inventory does not match its measured summary.")
    if len(data.get("Poses", [])) != len(STAGE_LABELS):
        raise ValueError(f"Expected {len(STAGE_LABELS)} exported production poses.")
    return data


def make_material(material_source, texture_paths):
    name = material_source["Name"]
    material = bpy.data.materials.new(name=name)
    base_color = tuple(float(value) for value in material_source["BaseColorRgba"])
    smoothness = float(material_source["Smoothness"])
    material["SourceTextureScale"] = tuple(
        float(value) for value in material_source.get("TextureScale", (1.0, 1.0))
    )
    material.diffuse_color = base_color
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    principled.inputs["Base Color"].default_value = base_color
    principled.inputs["Metallic"].default_value = float(material_source["Metallic"])
    principled.inputs["Roughness"].default_value = max(0.0, min(1.0, 1.0 - smoothness))
    texture_id = material_source.get("BaseTexture")
    if texture_id:
        image = bpy.data.images.load(str(texture_paths[texture_id]), check_existing=True)
        image.pack()
        image_node = nodes.new("ShaderNodeTexImage")
        image_node.name = f"{texture_id} • captured production texture"
        image_node.image = image
        image_node.interpolation = "Linear"
        image_node.extension = "REPEAT"
        multiply = nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs["Fac"].default_value = 1.0
        multiply.inputs["Color2"].default_value = base_color
        material.node_tree.links.new(image_node.outputs["Color"], multiply.inputs["Color1"])
        material.node_tree.links.new(multiply.outputs["Color"], principled.inputs["Base Color"])
    return material


def unity_world_point(point, matrix):
    """System.Numerics row-vector transform, then Unity-Y-up to Blender-Z-up once."""
    x, y, z = point
    world_x = x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12]
    world_y = x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13]
    world_z = x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14]
    return (world_x, -world_z, world_y)


def normal_transform(matrix):
    linear = Matrix(
        (
            (matrix[0], matrix[1], matrix[2]),
            (matrix[4], matrix[5], matrix[6]),
            (matrix[8], matrix[9], matrix[10]),
        )
    )
    try:
        return linear.inverted()
    except ValueError as error:
        raise ValueError("A captured production node has a singular world transform.") from error


def unity_world_normal(normal, inverse_matrix):
    # Source points use System.Numerics row-vector transforms, so authored normals
    # use n * inverse(A)^T before the Unity-Y-up to Blender-Z-up axis conversion.
    x, y, z = normal
    world_x = x * inverse_matrix[0][0] + y * inverse_matrix[0][1] + z * inverse_matrix[0][2]
    world_y = x * inverse_matrix[1][0] + y * inverse_matrix[1][1] + z * inverse_matrix[1][2]
    world_z = x * inverse_matrix[2][0] + y * inverse_matrix[2][1] + z * inverse_matrix[2][2]
    blender_normal = Vector((world_x, -world_z, world_y))
    if blender_normal.length_squared < 1e-16:
        return Vector((0.0, 0.0, 1.0))
    return blender_normal.normalized()


def node_mesh_object(node, mesh_source, material, label):
    mesh_name = mesh_source["Name"]
    source_vertices = mesh_source["Vertices"]
    source_normals = mesh_source["Normals"]
    source_uv = mesh_source["Uv"]
    source_indices = mesh_source["Triangles"]
    world_transform = node["WorldTransform"]
    if len(world_transform) != 16:
        raise ValueError(f"Expected a 4x4 world matrix for production node {node['Path']}.")
    vertex_count = len(source_vertices) // 3
    if len(source_vertices) % 3 or len(source_normals) != len(source_vertices):
        raise ValueError(f"Production vertex/normal arrays are inconsistent for {mesh_name}.")
    if len(source_uv) % 2 or len(source_uv) // 2 > vertex_count:
        raise ValueError(f"Production UV channel is inconsistent for {mesh_name}.")
    if len(source_indices) % 3:
        raise ValueError(f"Production triangle indices are inconsistent for {mesh_name}.")
    if any(index < 0 or index >= vertex_count for index in source_indices):
        raise ValueError(f"Production triangle index is out of bounds for {mesh_name}.")

    inverse_normal_transform = normal_transform(world_transform)
    vertices = [
        unity_world_point(source_vertices[index:index + 3], world_transform)
        for index in range(0, len(source_vertices), 3)
    ]
    faces = [
        tuple(source_indices[index:index + 3])
        for index in range(0, len(source_indices), 3)
    ]
    mesh = bpy.data.meshes.new(f"{label} • {mesh_name}")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.material_index = 0
        polygon.use_smooth = True

    uv_layer = mesh.uv_layers.new(name="Captured production UV0")
    uv_scale = material.get("SourceTextureScale", (1.0, 1.0))
    for loop in mesh.loops:
        vertex_index = loop.vertex_index
        uv_offset = vertex_index * 2
        u = float(source_uv[uv_offset]) if uv_offset < len(source_uv) else 0.0
        v = float(source_uv[uv_offset + 1]) if uv_offset + 1 < len(source_uv) else 0.0
        uv_layer.data[loop.index].uv = (u * float(uv_scale[0]), v * float(uv_scale[1]))

    custom_normals = []
    for loop in mesh.loops:
        offset = loop.vertex_index * 3
        normal = unity_world_normal(source_normals[offset:offset + 3], inverse_normal_transform)
        custom_normals.append(normal)
    if custom_normals:
        mesh.normals_split_custom_set(custom_normals)
    obj = bpy.data.objects.new(f"{label} • {node['Path'].split('/')[-1]}", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj, vertices


def create_equipment_group(pose, kinds, mesh_by_id, material_by_id, parent_name):
    root = bpy.data.objects.new(parent_name, None)
    bpy.context.scene.collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.0
    bounds_min = [float("inf"), float("inf"), float("inf")]
    bounds_max = [float("-inf"), float("-inf"), float("-inf")]
    selected_machines = [machine for machine in pose["Machines"] if machine["Kind"] in kinds]
    if len(selected_machines) != len(kinds):
        raise ValueError(f"Pose {pose['Label']} does not contain all expected equipment kinds {kinds}.")

    created_objects = []
    for machine in selected_machines:
        for node in machine["Nodes"]:
            mesh_id = node.get("Mesh")
            if not node.get("Active", True) or not mesh_id:
                continue
            if mesh_id not in mesh_by_id:
                raise ValueError(f"Production node references unknown mesh {mesh_id!r}.")
            material_id = node.get("Material")
            if material_id not in material_by_id:
                raise ValueError(f"Production node references unknown material {material_id!r}.")
            obj, vertices = node_mesh_object(
                node, mesh_by_id[mesh_id], material_by_id[material_id], machine["Kind"]
            )
            obj.parent = root
            created_objects.append(obj)
            for vertex in vertices:
                for axis in range(3):
                    bounds_min[axis] = min(bounds_min[axis], vertex[axis])
                    bounds_max[axis] = max(bounds_max[axis], vertex[axis])
    if not created_objects:
        return root, created_objects, None, None
    return root, created_objects, Vector(bounds_min), Vector(bounds_max)


def make_surface_material():
    material = bpy.data.materials.new("Neutral construction pad • presentation only")
    material.diffuse_color = (0.20, 0.21, 0.20, 1.0)
    material.use_nodes = True
    shader = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = material.diffuse_color
    shader.inputs["Roughness"].default_value = 0.92
    shader.inputs["Metallic"].default_value = 0.0
    return material


def emission_material(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1.0)
    emission.inputs["Strength"].default_value = 1.0
    output = nodes.new("ShaderNodeOutputMaterial")
    material.node_tree.links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return material


def camera_facing_text(camera, text, x, y, size, material, name, align="LEFT"):
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = text
    curve.size = size
    curve.align_x = align
    curve.align_y = "CENTER"
    curve.extrude = 0.0
    obj = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = camera
    obj.location = (x, y, -1.0)
    obj.data.materials.append(material)
    obj.visible_shadow = False
    return obj


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def configure_cpu_scene(resolution_x, resolution_y, ortho_width, center):
    scene = bpy.context.scene
    # Headless workspace guidance: choose Cycles CPU directly. Never initialize
    # Eevee or attempt GPU context creation before this fallback.
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 8
    scene.render.resolution_x = resolution_x
    scene.render.resolution_y = resolution_y
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.image_settings.color_depth = "8"
    scene.world.color = (0.36, 0.39, 0.41)
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.26, 0.30, 0.33, 1.0)
    background.inputs["Strength"].default_value = 0.30
    scene.render.filepath = ""

    bpy.ops.mesh.primitive_plane_add(size=200.0, location=(0.0, 0.0, -0.002))
    plane = bpy.context.object
    plane.name = "Plain neutral construction pad • presentation only"
    plane.data.materials.append(make_surface_material())

    bpy.ops.object.light_add(type="AREA", location=(2.7, -3.8, 5.4))
    key = bpy.context.object
    key.name = "Large soft key light"
    key.data.energy = 500.0
    key.data.shape = "DISK"
    key.data.size = 4.5
    key.rotation_euler = (Vector((0.0, 0.0, 0.0)) - key.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.light_add(type="AREA", location=(-3.4, 1.6, 3.4))
    fill = bpy.context.object
    fill.name = "Soft neutral fill light"
    fill.data.energy = 140.0
    fill.data.shape = "DISK"
    fill.data.size = 5.0
    fill.rotation_euler = (Vector((0.0, 0.0, 0.0)) - fill.location).to_track_quat("-Z", "Y").to_euler()

    camera_data = bpy.data.cameras.new("Orthographic equipment proof camera")
    camera = bpy.data.objects.new("Orthographic equipment proof camera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = center + Vector((4.8, -7.5, 5.0))
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = ortho_width
    scene.camera = camera
    bpy.context.view_layer.update()
    right = camera.matrix_world.to_3x3() @ Vector((1.0, 0.0, 0.0))
    right.z = 0.0
    right.normalize()
    screen_up_ground = camera.matrix_world.to_3x3() @ Vector((0.0, 1.0, 0.0))
    screen_up_ground.z = 0.0
    screen_up_ground.normalize()
    return scene, camera, right, screen_up_ground


def align_group_to_ground_target(root, bounds_min, bounds_max, right, ground_up, x, y):
    center = (bounds_min + bounds_max) * 0.5
    desired = right * x + ground_up * y
    root.location = Vector((desired.x - center.x, desired.y - center.y, -bounds_min.z))


def create_material_libraries(data, export_path):
    mesh_by_id = {mesh["Id"]: mesh for mesh in data["Meshes"]}
    source_materials = {material["Id"]: material for material in data["Materials"]}
    texture_paths = {}
    for texture in data["Textures"]:
        relative_path = texture.get("Png")
        if not relative_path:
            raise ValueError(f"Production texture {texture['Id']} has no captured PNG path.")
        texture_path = require_file(export_path.parent / relative_path, f"captured production PNG {relative_path}")
        texture_paths[texture["Id"]] = texture_path
    material_by_id = {
        material_id: make_material(material, texture_paths)
        for material_id, material in source_materials.items()
    }
    return mesh_by_id, material_by_id


def render_hero(data, mesh_by_id, material_by_id, output_path):
    clear_scene()
    pose = next(pose for pose in data["Poses"] if pose["Label"] == "clearing-bucket-load-0.8")
    groups = []
    for kind in EQUIPMENT_KINDS:
        root, _, minimum, maximum = create_equipment_group(
            pose, (kind,), mesh_by_id, material_by_id, f"Hero presentation • {kind}"
        )
        if minimum is None:
            raise ValueError(f"The production comparison pose has no active geometry for {kind}.")
        groups.append((root, minimum, maximum))
    scene, camera, right, _ = configure_cpu_scene(
        2400, 1100, 1.8, Vector((0.0, 0.0, 0.12))
    )
    for index, (root, minimum, maximum) in enumerate(groups):
        align_group_to_ground_target(root, minimum, maximum, right, Vector((0.0, 0.0, 0.0)),
                                     (index - 1) * 0.51, 0.0)
    title = emission_material("Proof caption • soft ivory", (0.93, 0.94, 0.91))
    secondary = emission_material("Proof note • muted grey", (0.75, 0.80, 0.79))
    camera_facing_text(camera, "Production geometry • external Blender proof",
                       0.0, 0.39, 0.034, title, "Hero honesty caption", "CENTER")
    camera_facing_text(camera, "Clearing pose • per-machine translation only • exact source transforms, UVs and PBR values",
                       0.0, 0.345, 0.018, secondary, "Hero offline rendering note", "CENTER")
    for x, name in zip((-0.51, 0.0, 0.51), ("Tracked excavator", "Six-wheel tipper", "Crawler dozer")):
        camera_facing_text(camera, name, x, -0.36, 0.027, title, f"Hero row label • {name}", "CENTER")
    camera_facing_text(camera, "Cycles CPU • procedural texture PNGs are fixture-math substitutes • not Unity or hardware playback",
                       0.0, -0.405, 0.017, secondary, "Hero proof limitation note", "CENTER")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)


def render_pose_sheet(data, mesh_by_id, material_by_id, output_path):
    clear_scene()
    pose_by_label = {pose["Label"]: pose for pose in data["Poses"]}
    entries = []
    for caption, pose_label in STAGE_LABELS:
        pose = pose_by_label.get(pose_label)
        if pose is None:
            raise ValueError(f"Production fixture is missing expected pose {pose_label!r}.")
        for kind in ("excavator", "truck"):
            root, _, minimum, maximum = create_equipment_group(
                pose, (kind,), mesh_by_id, material_by_id, f"Pose proof • {pose_label} • {kind}"
            )
            entries.append((caption, kind, root, minimum, maximum))

    scene, camera, right, ground_up = configure_cpu_scene(
        3200, 1600, 3.5, Vector((0.0, 0.0, 0.12))
    )
    stage_count = len(STAGE_LABELS)
    column_pitch = 0.60
    row_distance = 0.40
    inactive_cells = []
    for caption, kind, root, minimum, maximum in entries:
        stage_index = next(index for index, (label, _) in enumerate(STAGE_LABELS) if label == caption)
        column_x = (stage_index - (stage_count - 1) * 0.5) * column_pitch
        row_y = row_distance if kind == "excavator" else -row_distance
        if minimum is None:
            inactive_cells.append((column_x, 0.24 if kind == "excavator" else -0.24, caption, kind))
        else:
            align_group_to_ground_target(root, minimum, maximum, right, ground_up, column_x, row_y)

    title = emission_material("Pose caption • soft ivory", (0.93, 0.94, 0.91))
    secondary = emission_material("Pose note • muted grey", (0.75, 0.80, 0.79))
    camera_facing_text(camera, "Production pose samples • independently centered for comparison",
                       -1.63, 0.75, 0.043, title, "Pose sheet honesty caption")
    camera_facing_text(camera, "Source rig matrices, articulation and mesh vertices • per-machine translation only • offline Cycles CPU",
                       -1.63, 0.68, 0.023, secondary, "Pose sheet transform note")
    for index, (caption, _) in enumerate(STAGE_LABELS):
        x = (index - (stage_count - 1) * 0.5) * column_pitch
        camera_facing_text(camera, caption, x, 0.55, 0.030, title, f"Stage label • {caption}", "CENTER")
    camera_facing_text(camera, "EXCAVATOR", -1.63, 0.24, 0.023, title, "Excavator row label")
    camera_facing_text(camera, "TIPPER", -1.63, -0.24, 0.023, title, "Tipper row label")
    for x, y, caption, kind in inactive_cells:
        camera_facing_text(camera, "Inactive in source capture", x, y, 0.022, secondary,
                           f"Inactive state • {caption} • {kind}", "CENTER")
    camera_facing_text(camera, "Inactive hierarchy nodes are omitted; source fleet spacing intentionally not represented.",
                       -1.63, -0.78, 0.019, secondary, "Pose sheet comparison limitation")
    camera_facing_text(camera, "Not a Unity screenshot or hardware-playback test",
                       -1.63, -0.83, 0.019, secondary, "Pose sheet proof limitation")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)


def main():
    arguments = cli_arguments()
    data = read_export(arguments.equipment_json)
    mesh_by_id, material_by_id = create_material_libraries(data, arguments.equipment_json)
    output_dir = arguments.output_dir
    render_hero(data, mesh_by_id, material_by_id, output_dir / arguments.hero_name)
    render_pose_sheet(data, mesh_by_id, material_by_id, output_dir / arguments.poses_name)
    print(
        f"Rendered production proof PNGs from {arguments.equipment_json}: "
        f"{output_dir / arguments.hero_name} and {output_dir / arguments.poses_name}. "
        "Cycles CPU external render only; Unity/hardware playback was not run."
    )


if __name__ == "__main__":
    main()