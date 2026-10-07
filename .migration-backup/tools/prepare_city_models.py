#!/usr/bin/env python3
"""Prepare licensed-source or generated city assets as small textured Unity OBJ assets.

Run with Blender 4.4, for example:
  blender --background --factory-startup --python tools/prepare_city_models.py -- \
    --input attached_assets/licensed_models/apartment.glb \
    --key apartment --triangles 2500 \
    --out testingReplic/Assets/NewGaza/Resources/Models

Only mesh geometry from the supplied GLB or curated .blend is exported.
Cameras and lights are added only to the separate preview render and are
never included in the OBJ.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


TRIANGLE_DEFAULTS = {
    "apartment": 2500,
    "ruined_building": 4000,
    "rubble_heap": 1400,
}
TRIANGLE_TOLERANCE = 0.01
MAX_ALBEDO_SIZE = 1024


def cli_arguments():
    """Read only arguments after Blender's optional ``--`` separator."""
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, help="Input GLB or editable Blender .blend source")
    parser.add_argument("--key", required=True, choices=sorted(TRIANGLE_DEFAULTS))
    parser.add_argument(
        "--triangles",
        type=int,
        help="Maximum triangle count (defaults: apartment 2500, ruined_building 4000, rubble_heap 1400)",
    )
    parser.add_argument(
        "--out",
        default="testingReplic/Assets/NewGaza/Resources/Models",
        help="Directory for the Unity OBJ, albedo PNG, and per-asset manifest",
    )
    parser.add_argument(
        "--preview-dir",
        help="Preview output directory (default: <project>/exports/models)",
    )
    options = parser.parse_args(arguments)
    if options.triangles is None:
        options.triangles = TRIANGLE_DEFAULTS[options.key]
    if options.triangles < 1:
        parser.error("--triangles must be a positive integer")
    return options


def set_only_active(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def imported_mesh_objects():
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    meshes = [obj for obj in meshes if len(obj.data.polygons) > 0]
    if not meshes:
        raise RuntimeError("The GLB imported successfully but contains no non-empty mesh geometry.")
    return meshes


def join_imported_meshes(meshes):
    for obj in meshes:
        obj.select_set(True)
    active = meshes[0]
    bpy.context.view_layer.objects.active = active
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = "PreparedCityModel"
    joined.data.name = "PreparedCityModelMesh"
    # Joining retains each object's transform relative to the active object.
    # Applying the joined transform bakes all imported placement into vertices.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return joined


def mesh_bounds(mesh):
    if not mesh.vertices:
        raise RuntimeError("Imported mesh has no vertices.")
    coords = [vertex.co.copy() for vertex in mesh.vertices]
    minimum = Vector((min(p.x for p in coords), min(p.y for p in coords), min(p.z for p in coords)))
    maximum = Vector((max(p.x for p in coords), max(p.y for p in coords), max(p.z for p in coords)))
    return minimum, maximum


def triangulate(obj):
    set_only_active(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.update()


def triangle_count(mesh):
    # The exported mesh is triangulated; use polygon topology rather than a
    # decimator's requested ratio so the manifest reports real mesh triangles.
    return sum(max(0, len(poly.vertices) - 2) for poly in mesh.polygons)


def reduce_triangles(obj, target):
    triangulate(obj)
    count = triangle_count(obj.data)
    if count <= target:
        return count

    attempts = 0
    while count > target and attempts < 14:
        ratio = max(0.001, min(0.999, (target / float(count)) * 0.995))
        modifier = obj.modifiers.new(name="CityModelTriangleBudget", type="DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = ratio
        if hasattr(modifier, "use_collapse_triangulate"):
            modifier.use_collapse_triangulate = True
        set_only_active(obj)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        triangulate(obj)
        new_count = triangle_count(obj.data)
        attempts += 1
        if new_count >= count:
            # Ratios are approximate around constrained topology. Make a
            # progressively stronger retry against the actual result.
            modifier = obj.modifiers.new(name="CityModelTriangleBudgetRetry", type="DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = max(0.001, ratio * 0.8)
            if hasattr(modifier, "use_collapse_triangulate"):
                modifier.use_collapse_triangulate = True
            set_only_active(obj)
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            triangulate(obj)
            new_count = triangle_count(obj.data)
        count = new_count

    maximum_allowed = int(math.ceil(target * (1.0 + TRIANGLE_TOLERANCE)))
    if count > maximum_allowed:
        raise RuntimeError(
            "Decimation did not meet the triangle budget: "
            f"{count} actual triangles, target {target}, allowed maximum {maximum_allowed}."
        )
    return count


def normalize_bottom_center(obj):
    minimum, maximum = mesh_bounds(obj.data)
    original_minimum = tuple(float(value) for value in minimum)
    original_maximum = tuple(float(value) for value in maximum)
    width_x = maximum.x - minimum.x
    width_y = maximum.y - minimum.y
    width_xy = max(width_x, width_y)
    if not math.isfinite(width_xy) or width_xy <= 1e-8:
        raise RuntimeError("Cannot normalize model: its imported XY width is zero.")

    scale = 1.0 / width_xy
    center_x = (minimum.x + maximum.x) * 0.5
    center_y = (minimum.y + maximum.y) * 0.5
    for vertex in obj.data.vertices:
        vertex.co.x = (vertex.co.x - center_x) * scale
        vertex.co.y = (vertex.co.y - center_y) * scale
        vertex.co.z = (vertex.co.z - minimum.z) * scale
    obj.data.update()
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    return {
        "originalWorldBoundsMin": list(original_minimum),
        "originalWorldBoundsMax": list(original_maximum),
        "uniformScale": scale,
        "normalizedOriginBlender": [0.0, 0.0, 0.0],
        "pivot": "bottom-center",
    }


def used_materials(obj):
    used_indices = {poly.material_index for poly in obj.data.polygons}
    materials = []
    for index in sorted(used_indices):
        if index < len(obj.data.materials):
            material = obj.data.materials[index]
            if material is not None and material not in materials:
                materials.append(material)
    if not materials:
        raise RuntimeError(
            "The imported mesh has no material assigned to its polygons; refusing to invent a flat albedo."
        )
    return materials


def principled_base_color(material):
    if not material.use_nodes or material.node_tree is None:
        return None
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node, node.inputs.get("Base Color")
    return None


def direct_base_color_image(material, obj, allow_implicit_uv=False):
    result = principled_base_color(material)
    if result is None:
        return None
    _principled, base_color = result
    if base_color is None or not base_color.is_linked:
        return None
    link = base_color.links[0]
    node = link.from_node
    if node.type != "TEX_IMAGE" or node.image is None:
        return None
    # Direct copying is safe only for a simple UV image. Generated-coordinate,
    # box-projected, Mapping-node, and other procedural graphs must be baked:
    # copying their source image straight onto the OBJ's UVs changes its look.
    if getattr(node, "projection", "FLAT") != "FLAT":
        return None
    vector = node.inputs.get("Vector")
    if vector is None:
        return None
    active = obj.data.uv_layers.active or (
        obj.data.uv_layers[0] if obj.data.uv_layers else None
    )
    if not vector.is_linked:
        # Blender's glTF importer leaves the default TEXCOORD_0 link implicit.
        # It is safe to copy only for a GLB/Gltf input; an unlinked image in a
        # curated .blend samples Generated coordinates instead.
        if not allow_implicit_uv or active is None:
            return None
        mapped_name = active.name
    elif len(vector.links) != 1:
        return None
    else:
        vector_link = vector.links[0]
        coordinate_node = vector_link.from_node
        if coordinate_node.type == "UVMAP":
            mapped_name = coordinate_node.uv_map or (active.name if active else None)
            if mapped_name is None or obj.data.uv_layers.get(mapped_name) is None:
                return None
        elif coordinate_node.type == "TEX_COORD":
            if vector_link.from_socket.name != "UV":
                return None
            mapped_name = active.name if active else None
            if mapped_name is None:
                return None
        else:
            return None
    image = node.image
    if image.size[0] < 1 or image.size[1] < 1:
        return None
    return image, mapped_name


def has_base_color_image(material):
    result = principled_base_color(material)
    if result is None:
        return False
    _principled, base_color = result
    if base_color is None:
        return False
    stack = [link.from_node for link in base_color.links]
    seen = set()
    while stack:
        node = stack.pop()
        if node in seen:
            continue
        seen.add(node)
        if node.type == "TEX_IMAGE" and node.image is not None:
            if node.image.size[0] > 0 and node.image.size[1] > 0:
                return True
        for socket in node.inputs:
            stack.extend(link.from_node for link in socket.links)
    return False


def save_image_png(image, filepath):
    filepath.parent.mkdir(parents=True, exist_ok=True)
    image.file_format = "PNG"
    image.filepath_raw = str(filepath)
    image.save()
    if not filepath.is_file() or filepath.stat().st_size == 0:
        raise RuntimeError(f"Blender failed to write albedo texture: {filepath}")


def save_direct_albedo(image, material, filepath):
    width, height = image.size[:]
    scale = min(1.0, MAX_ALBEDO_SIZE / float(max(width, height)))
    target_width = max(1, int(round(width * scale)))
    target_height = max(1, int(round(height * scale)))

    copied = image.copy()
    try:
        if (target_width, target_height) != tuple(copied.size[:]):
            copied.scale(target_width, target_height)

        # For a direct image -> Principled connection, a linked factor is
        # already represented in the graph (and would not be a "simple"
        # direct connection). Preserve an unlinked constant factor if present.
        result = principled_base_color(material)
        factor = (1.0, 1.0, 1.0, 1.0)
        if result is not None:
            _principled, base_color = result
            if base_color is not None and not base_color.is_linked:
                factor = tuple(float(value) for value in base_color.default_value)
        if any(abs(value - 1.0) > 1e-6 for value in factor):
            pixels = list(copied.pixels[:])
            for offset in range(0, len(pixels), 4):
                pixels[offset] *= factor[0]
                pixels[offset + 1] *= factor[1]
                pixels[offset + 2] *= factor[2]
                pixels[offset + 3] *= factor[3]
            copied.pixels[:] = pixels
        save_image_png(copied, filepath)
        return copied.size[:]
    finally:
        bpy.data.images.remove(copied)


def set_source_uvs_for_bake(obj, materials):
    source_layer = (obj.data.uv_layers.active or obj.data.uv_layers[0]) if obj.data.uv_layers else None
    source_name = source_layer.name if source_layer is not None else None

    # GLB image nodes commonly have implicit UV coordinates. Make their
    # source-UV dependency explicit before the atlas becomes the bake UV.
    for material in materials:
        if not material.use_nodes or material.node_tree is None:
            continue
        tree = material.node_tree
        for node in list(tree.nodes):
            if source_name and node.type == "UVMAP" and not node.uv_map:
                node.uv_map = source_name
            elif not source_name and node.type == "UVMAP":
                raise RuntimeError(
                    f"Material {material.name!r} uses UV coordinates, but the imported mesh has no source UV layer."
                )
            if node.type == "TEX_IMAGE" and node.image is not None:
                vector_input = node.inputs.get("Vector")
                if source_name and vector_input is not None and not vector_input.is_linked:
                    uv_node = tree.nodes.new("ShaderNodeUVMap")
                    uv_node.uv_map = source_name
                    uv_node.location = (node.location.x - 220, node.location.y)
                    tree.links.new(uv_node.outputs["UV"], vector_input)
                elif not source_name and vector_input is not None and vector_input.is_linked:
                    if any(link.from_node.type == "UVMAP" for link in vector_input.links):
                        raise RuntimeError(
                            f"Material {material.name!r} samples a missing source UV map."
                        )
                elif vector_input is not None and vector_input.is_linked:
                    for link in vector_input.links:
                        if link.from_node.type == "UVMAP" and not link.from_node.uv_map:
                            if source_name:
                                link.from_node.uv_map = source_name
    return source_layer


def create_bake_atlas(obj, materials, filepath):
    textured = [material for material in materials if has_base_color_image(material)]
    if not textured:
        raise RuntimeError(
            "The imported materials contain no base-color image. Refusing to silently replace the model with flat color."
        )
    source_uv = set_source_uvs_for_bake(obj, materials)

    # Preserve original texture coordinates and build a second, packed UV map
    # for the shared albedo atlas.
    atlas_name = "BakedAlbedoUV"
    suffix = 1
    existing = {layer.name for layer in obj.data.uv_layers}
    while atlas_name in existing:
        atlas_name = f"BakedAlbedoUV{suffix}"
        suffix += 1
    atlas = obj.data.uv_layers.new(name=atlas_name)
    obj.data.uv_layers.active = atlas

    set_only_active(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    try:
        bpy.ops.uv.lightmap_pack(
            PREF_CONTEXT="ALL_FACES",
            PREF_PACK_IN_ONE=True,
            PREF_NEW_UVLAYER=False,
            PREF_BOX_DIV=12,
            PREF_MARGIN_DIV=0.03,
        )
    except Exception as error:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"Could not create the shared albedo UV atlas: {error}") from error
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.uv_layers.active = atlas

    # Ensure image textures still sample their original GLB UVs after packing.
    for material in materials:
        if not material.use_nodes or material.node_tree is None:
            continue
        for node in material.node_tree.nodes:
            if source_uv is not None and node.type == "UVMAP" and node.uv_map == "":
                node.uv_map = source_uv.name

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.cycles.bake_type = "DIFFUSE"
    bake = scene.render.bake
    bake.use_pass_color = True
    bake.use_pass_direct = False
    bake.use_pass_indirect = False
    bake.use_clear = True
    bake.margin = 8
    bake.target = "IMAGE_TEXTURES"

    atlas_image = bpy.data.images.new(
        name="CityModelBakedAlbedo",
        width=MAX_ALBEDO_SIZE,
        height=MAX_ALBEDO_SIZE,
        alpha=False,
        float_buffer=False,
    )
    for material in materials:
        if not material.use_nodes or material.node_tree is None:
            raise RuntimeError(
                f"Material {material.name!r} is not node-based; refusing to guess its diffuse color."
            )
        nodes = material.node_tree.nodes
        for node in nodes:
            node.select = False
        image_node = nodes.new("ShaderNodeTexImage")
        image_node.name = "BakedAlbedoTarget"
        image_node.label = "Baked albedo export target"
        image_node.image = atlas_image
        image_node.select = True
        nodes.active = image_node

    set_only_active(obj)
    try:
        bpy.ops.object.bake(type="DIFFUSE")
    except Exception as error:
        bpy.data.images.remove(atlas_image)
        raise RuntimeError(f"Cycles CPU diffuse-color bake failed: {error}") from error

    save_image_png(atlas_image, filepath)
    return atlas_image, atlas_name


def create_export_material(obj, image, key):
    material = bpy.data.materials.new(name=f"{key}_albedo")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    principled.inputs["Roughness"].default_value = 0.9
    principled.inputs["Metallic"].default_value = 0.0
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = "RuntimeAlbedoTexture"
    texture.label = "Runtime albedo"
    texture.image = image
    coordinates = nodes.new("ShaderNodeUVMap")
    coordinates.name = "RuntimeAlbedoUV"
    coordinates.uv_map = (
        obj.data.uv_layers.active.name if obj.data.uv_layers.active else obj.data.uv_layers[0].name
    )
    material.node_tree.links.new(coordinates.outputs["UV"], texture.inputs["Vector"])
    material.node_tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    return material


def albedo_for_model(obj, key, output_dir, allow_implicit_uv=False):
    materials = used_materials(obj)
    albedo_path = output_dir / f"{key}_albedo.png"
    simple_texture = (
        direct_base_color_image(materials[0], obj, allow_implicit_uv)
        if len(materials) == 1
        else None
    )
    if simple_texture is not None:
        simple_image, source_uv_name = simple_texture
        source_uv = obj.data.uv_layers.get(source_uv_name)
        if source_uv is None:
            raise RuntimeError(
                f"Base-color image uses UV layer {source_uv_name!r}, which is absent from the mesh."
            )
        obj.data.uv_layers.active = source_uv
        for layer in obj.data.uv_layers:
            layer.active_render = layer == source_uv
        save_direct_albedo(simple_image, materials[0], albedo_path)
        # Use a copy loaded from the written PNG for a standalone render-ready
        # material (also avoids relying on an embedded GLB image path).
        exported_image = bpy.data.images.load(str(albedo_path), check_existing=False)
        atlas_name = obj.data.uv_layers.active.name if obj.data.uv_layers.active else None
        baked = False
    else:
        exported_image, atlas_name = create_bake_atlas(obj, materials, albedo_path)
        baked = True
    create_export_material(obj, exported_image, key)
    return albedo_path, atlas_name, len(materials), baked


def uv_coverage(mesh):
    if not mesh.uv_layers:
        return None
    layer = mesh.uv_layers.active or mesh.uv_layers[0]
    values = [tuple(float(value) for value in item.uv) for item in layer.data]
    finite = [(u, v) for u, v in values if math.isfinite(u) and math.isfinite(v)]
    if not finite:
        return {
            "layer": layer.name,
            "loopCount": len(values),
            "finiteLoopCount": 0,
            "bounds": None,
        }
    return {
        "layer": layer.name,
        "loopCount": len(values),
        "finiteLoopCount": len(finite),
        "bounds": {
            "min": [min(point[0] for point in finite), min(point[1] for point in finite)],
            "max": [max(point[0] for point in finite), max(point[1] for point in finite)],
        },
    }


def export_obj(obj, filepath):
    set_only_active(obj)
    bpy.ops.wm.obj_export(
        filepath=str(filepath),
        export_selected_objects=True,
        export_uv=True,
        export_normals=True,
        export_materials=False,
        export_triangulated_mesh=True,
        forward_axis="NEGATIVE_Z",
        up_axis="Y",
        global_scale=1.0,
        path_mode="AUTO",
    )
    if not filepath.is_file() or filepath.stat().st_size == 0:
        raise RuntimeError(f"Blender failed to write Unity OBJ: {filepath}")


def point_camera_at(camera, point):
    direction = Vector(point) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def render_preview(obj, filepath):
    filepath.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    center_min, center_max = mesh_bounds(obj.data)
    center = (center_min + center_max) * 0.5
    dimensions = center_max - center_min
    span = max(dimensions.x, dimensions.y, dimensions.z, 0.2)

    camera_data = bpy.data.cameras.new("CityModelPreviewCamera")
    camera = bpy.data.objects.new("CityModelPreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = center + Vector((1.4 * span, -2.0 * span, 1.15 * span))
    camera_data.lens = 52
    point_camera_at(camera, center)
    scene.camera = camera

    def area_light(name, offset, energy, size):
        light_data = bpy.data.lights.new(name=name, type="AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size * span
        light = bpy.data.objects.new(name, light_data)
        scene.collection.objects.link(light)
        light.location = center + Vector(offset) * span
        point_camera_at(light, center)
        return light

    area_light("PreviewKey", (2.5, -3.0, 4.2), 450, 3.0)
    area_light("PreviewFill", (-3.0, -1.0, 2.2), 240, 3.5)
    area_light("PreviewRim", (0.0, 3.0, 3.2), 300, 2.5)

    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "Medium High Contrast"
    if scene.world is not None:
        scene.world.use_nodes = True
        background = scene.world.node_tree.nodes.get("Background")
        if background is not None:
            background.inputs["Color"].default_value = (0.35, 0.35, 0.35, 1.0)
            background.inputs["Strength"].default_value = 0.18
    scene.camera.data.dof.use_dof = False
    scene.render.filepath = str(filepath)

    # Never initialize Eevee/OpenGL in this headless pipeline: some Blender
    # builds abort inside epoxy before Python can catch a missing GLX/EGL context.
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 4
    bpy.ops.render.render(write_still=True)
    if not filepath.is_file() or filepath.stat().st_size == 0:
        raise RuntimeError(f"Blender did not produce a preview PNG: {filepath}")


def manifest_for(obj, key, input_path, target, actual, origin, albedo_path, atlas_uv, material_count, baked):
    minimum, maximum = mesh_bounds(obj.data)
    dimensions = maximum - minimum
    coverage = uv_coverage(obj.data)
    return {
        "formatVersion": 1,
        "key": key,
        "sourceFile": str(input_path),
        "targetTriangles": target,
        "actualTriangles": actual,
        "triangleTolerance": TRIANGLE_TOLERANCE,
        "verifiedWithinBudget": actual <= int(math.ceil(target * (1.0 + TRIANGLE_TOLERANCE))),
        "vertexCount": len(obj.data.vertices),
        "dimensionsNormalizedBlenderXYZ": [
            float(dimensions.x),
            float(dimensions.y),
            float(dimensions.z),
        ],
        "origin": origin,
        "uvCoverage": coverage,
        "albedo": {
            "file": albedo_path.name,
            "size": list(bpy.data.images.get("CityModelBakedAlbedo").size[:])
            if baked and bpy.data.images.get("CityModelBakedAlbedo") is not None
            else None,
            "bakedDiffuseColorOnly": baked,
            "sourceMaterialCount": material_count,
            "uvLayer": atlas_uv,
        },
    }


def main():
    options = cli_arguments()
    input_path = Path(options.input).expanduser().resolve()
    output_dir = Path(options.out).expanduser().resolve()
    project_root = Path(__file__).resolve().parent.parent
    preview_dir = (
        Path(options.preview_dir).expanduser().resolve()
        if options.preview_dir
        else project_root / "exports" / "models"
    )
    if not input_path.is_file():
        raise FileNotFoundError(f"Input GLB does not exist: {input_path}")
    output_dir.mkdir(parents=True, exist_ok=True)

    # Start clean so only geometry imported from the supplied GLB or the
    # explicitly curated source blend is eligible.
    suffix = input_path.suffix.lower()
    if suffix == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(input_path))
    elif suffix in (".glb", ".gltf"):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=str(input_path))
    else:
        raise ValueError(f"Unsupported input file type {suffix!r}; use .blend, .glb, or .gltf.")
    imported = imported_mesh_objects()
    obj = join_imported_meshes(imported)
    actual_triangles = reduce_triangles(obj, options.triangles)
    origin = normalize_bottom_center(obj)
    albedo_path, atlas_uv, material_count, baked = albedo_for_model(
        obj, options.key, output_dir, allow_implicit_uv=suffix in (".glb", ".gltf")
    )

    obj_path = output_dir / f"{options.key}.obj"
    export_obj(obj, obj_path)
    manifest = manifest_for(
        obj,
        options.key,
        input_path,
        options.triangles,
        actual_triangles,
        origin,
        albedo_path,
        atlas_uv,
        material_count,
        baked,
    )
    manifest_path = output_dir / f"{options.key}_manifest.json"
    preview_path = preview_dir / f"{options.key}.png"
    manifest["preview"] = {"file": str(preview_path), "status": "pending"}
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    try:
        render_preview(obj, preview_path)
        manifest["preview"]["status"] = "rendered"
    except Exception as error:
        manifest["preview"].update(
            {
                "status": "failed",
                "error": f"{type(error).__name__}: {error}",
            }
        )
        print(
            f"WARNING: preview render failed ({error}); OBJ, albedo, and manifest remain ready.",
            file=sys.stderr,
        )
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(
        "Prepared city model:"
        f" key={options.key}, triangles={actual_triangles}/{options.triangles},"
        f" obj={obj_path}, albedo={albedo_path}, preview={preview_path},"
        f" manifest={manifest_path}"
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"prepare_city_models.py: ERROR: {error}", file=sys.stderr)
        raise