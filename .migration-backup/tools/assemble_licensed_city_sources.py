#!/usr/bin/env python3
"""Assemble licensed artist-authored city sources into textured GLB + BLEND files.

This is a Blender 4.4 background script. It extracts only selected authored
meshes from the supplied source packs, adds the supplied CC0 concrete albedo as
a box-projected material in the source .blend, and bakes that material to a
UV atlas for the portable GLB.

Example:
  blender --background --factory-startup --python tools/assemble_licensed_city_sources.py -- \
    --concrete-albedo /tmp/gaza-art-assets/concrete_albedo.jpg
"""

import argparse
import csv
import json
import math
import sys
import unicodedata
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


DEFAULT_SOURCE_ROOT = Path("/tmp/gaza-art-assets")
DEFAULT_PACK_ROOT = DEFAULT_SOURCE_ROOT / "Ultimate Textured Building Pack - Dec 2019"
DEFAULT_FBX = DEFAULT_SOURCE_ROOT / "Destroyed_City_Assets.fbx"
DEFAULT_OUTPUT = Path("attached_assets/licensed_models")
ATLAS_SIZE = 1024


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--quaternius-root", type=Path, default=DEFAULT_PACK_ROOT)
    parser.add_argument("--destroyed-city-fbx", type=Path, default=DEFAULT_FBX)
    parser.add_argument("--concrete-albedo", type=Path, default=DEFAULT_SOURCE_ROOT / "concrete_albedo.jpg")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--only",
        choices=("apartment", "ruined_building", "rubble_heap"),
        help="Build only one asset (default builds all three).",
    )
    parser.add_argument("--atlas-size", type=int, default=ATLAS_SIZE)
    parser.add_argument("--wall-repeat", type=float, default=4.0)
    args = parser.parse_args(argv)
    if args.atlas_size < 64 or args.atlas_size > 4096:
        parser.error("--atlas-size must be between 64 and 4096")
    if args.wall_repeat <= 0:
        parser.error("--wall-repeat must be positive")
    return args


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def object_meshes():
    return [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.data.polygons]


def clean_scene(keep):
    keep_ids = {obj.as_pointer() for obj in keep}
    world_matrices = {obj.as_pointer(): obj.matrix_world.copy() for obj in keep}
    for obj in list(bpy.context.scene.objects):
        if obj.as_pointer() not in keep_ids:
            bpy.data.objects.remove(obj, do_unlink=True)
    # Detach kept meshes from deleted empties/non-selected parents while
    # retaining their authored world placement.
    for obj in keep:
        world = world_matrices[obj.as_pointer()]
        obj.parent = None
        obj.matrix_world = world


def material_principled(material):
    if not material.use_nodes:
        previous_color = tuple(float(value) for value in material.diffuse_color)
        material.use_nodes = True
        material.node_tree.nodes.clear()
        output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
        shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
        material.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        shader.inputs["Base Color"].default_value = previous_color
    else:
        shader = next(
            (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if shader is None:
            shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
            output = next(
                (node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL"),
                None,
            )
            if output is None:
                output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
            material.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return shader


def set_flat_material(material, color, roughness=0.86, metallic=0.0):
    shader = material_principled(material)
    base = shader.inputs.get("Base Color")
    if base is not None:
        for link in list(base.links):
            material.node_tree.links.remove(link)
        base.default_value = (*color[:3], 1.0)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    material.diffuse_color = (*color[:3], 1.0)


def add_box_projected_concrete(material, image, repeat, tint):
    shader = material_principled(material)
    tree = material.node_tree
    base = shader.inputs.get("Base Color")
    if base is None:
        raise RuntimeError(f"Material {material.name!r} has no Principled Base Color input.")
    for link in list(base.links):
        tree.links.remove(link)

    coordinates = tree.nodes.new("ShaderNodeTexCoord")
    coordinates.label = "Object generated coordinates"
    mapping = tree.nodes.new("ShaderNodeMapping")
    mapping.label = f"Concrete scale ×{repeat:g}"
    mapping.inputs["Scale"].default_value = (repeat, repeat, repeat)
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.label = "CC0 scanned concrete (box projection)"
    texture.image = image
    texture.projection = "BOX"
    texture.projection_blend = 0.15
    texture.extension = "REPEAT"
    texture.interpolation = "Linear"
    multiply = tree.nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    multiply.inputs[2].default_value = (*tint, 1.0)
    multiply.label = "Neutral concrete tint"
    tree.links.new(coordinates.outputs["Generated"], mapping.inputs["Vector"])
    tree.links.new(mapping.outputs["Vector"], texture.inputs["Vector"])
    tree.links.new(texture.outputs["Color"], multiply.inputs[1])
    tree.links.new(multiply.outputs["Color"], base)
    shader.inputs["Roughness"].default_value = 0.9
    shader.inputs["Metallic"].default_value = 0.0
    material.diffuse_color = (*tint, 1.0)


def load_concrete_image(filepath):
    if not filepath.is_file():
        raise FileNotFoundError(
            f"Required licensed concrete albedo is not available yet: {filepath}. "
            "Place the acquired CC0 photograph there, then rerun this command."
        )
    image = bpy.data.images.load(str(filepath.resolve()), check_existing=False)
    if image.size[0] < 1 or image.size[1] < 1:
        raise RuntimeError(f"Concrete albedo image is empty: {filepath}")
    image.name = "CC0_Concrete_Albedo"
    image.colorspace_settings.name = "sRGB"
    image.pack()
    return image


def configure_apartment_materials(image, repeat):
    # The Quaternius source is a real author-built, flat-roof four-storey
    # building. Texture facade/surface groups, while retaining dark glazing,
    # window trim, roof details, and the muted wood door palette.
    wall_tints = {
        "bricks": (0.91, 0.89, 0.84),
        "main": (0.87, 0.86, 0.82),
        "light": (0.97, 0.95, 0.90),
        "white": (0.94, 0.93, 0.89),
    }
    material_colors = {
        "windows": (0.27, 0.29, 0.29),
        "glass": (0.055, 0.075, 0.085),
        "dark": (0.095, 0.105, 0.105),
        "darkwood": (0.15, 0.095, 0.065),
        "black": (0.035, 0.04, 0.04),
    }
    for material in bpy.data.materials:
        name = material.name.lower()
        if name in wall_tints:
            add_box_projected_concrete(material, image, repeat, wall_tints[name])
        elif name in material_colors:
            set_flat_material(material, material_colors[name], roughness=0.9)


def configure_ruin_and_rubble_materials(image, repeat):
    concrete_tints = {
        "ciment_brisé": (0.94, 0.92, 0.87),
        "ciment_brisé2": (0.86, 0.84, 0.79),
        "ciment_brisé3": (0.91, 0.90, 0.86),
    }
    for material in bpy.data.materials:
        name = material.name.lower()
        if name in concrete_tints:
            add_box_projected_concrete(material, image, repeat, concrete_tints[name])
        elif "rouillé" in name or "rouille" in name:
            # Keep the authored reinforcing-metal slot distinct and rusty.
            set_flat_material(material, (0.25, 0.115, 0.055), roughness=0.88, metallic=0.18)
        elif name == "rock_apo":
            # The rubble pack's rock pieces are broken masonry/concrete, so
            # keep them on the real scanned albedo rather than a pale flat
            # fill that reads as white after studio lighting.
            add_box_projected_concrete(material, image, repeat, (0.78, 0.76, 0.72))
        elif name == "route_brisée":
            set_flat_material(material, (0.23, 0.23, 0.21), roughness=0.96)


def save_source_blend(filepath, meshes, image):
    clean_scene(meshes)
    if not meshes:
        raise RuntimeError(f"No authored mesh objects were selected for {filepath.name}.")
    for obj in meshes:
        obj.name = obj.name.replace("/", "_")
    image.pack()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(filepath), compress=True)
    if not filepath.is_file() or filepath.stat().st_size == 0:
        raise RuntimeError(f"Could not write source .blend: {filepath}")


def descendants(root):
    found = [root]
    stack = list(root.children)
    while stack:
        obj = stack.pop()
        found.append(obj)
        stack.extend(list(obj.children))
    return found


def build_apartment(pack_root, output_dir, photo_path, repeat):
    source = pack_root / "Models with Materials" / "Blends" / "4Story_Mat.blend"
    if not source.is_file():
        raise FileNotFoundError(f"Quaternius four-storey source blend is missing: {source}")
    reset_scene()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    candidates = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.name == "4Story_Mat"]
    if len(candidates) != 1:
        raise RuntimeError(
            f"Expected one authored 4Story_Mat mesh in {source.name}; found {len(candidates)}."
        )
    meshes = candidates
    clean_scene(meshes)
    image = load_concrete_image(photo_path)
    configure_apartment_materials(image, repeat)
    for obj in meshes:
        obj.name = "Quaternius_4Story_FlatRoof"
    source_blend = output_dir / "apartment_source.blend"
    save_source_blend(source_blend, meshes, image)
    return source_blend, {
        "sourceArtist": "Quaternius",
        "sourcePack": "Ultimate Textured Building Pack - Dec 2019",
        "sourceLicense": "CC0 1.0 Universal (per source pack)",
        "sourceUrl": "https://quaternius.com/packs/ultimatetexturedbuildings.html",
        "sourceMeshObjects": ["4Story_Mat"],
        "sourceFile": str(source),
        "selectionNotes": "One authored flat-roof four-storey building mesh; no dummy geometry, cameras, or lights.",
    }


def import_destroyed_city(filepath):
    if not filepath.is_file():
        raise FileNotFoundError(f"Fleurman destroyed-city FBX is missing: {filepath}")
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(filepath), use_anim=False)


def write_destroyed_city_inventory(output_dir):
    """Preserve a tabular audit of every imported FBX object and hierarchy."""
    filepath = output_dir / "destroyed_city_source_inventory.csv"
    rows = []
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name.casefold()):
        if obj.type == "MESH":
            world_corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
            minimum = [min(point[axis] for point in world_corners) for axis in range(3)]
            maximum = [max(point[axis] for point in world_corners) for axis in range(3)]
            vertex_count = len(obj.data.vertices)
            polygon_count = len(obj.data.polygons)
            materials = [material.name if material else "" for material in obj.data.materials]
        else:
            minimum = ["", "", ""]
            maximum = ["", "", ""]
            vertex_count = ""
            polygon_count = ""
            materials = []
        rows.append(
            {
                "object": obj.name,
                "type": obj.type,
                "parent": obj.parent.name if obj.parent else "",
                "world_x": obj.matrix_world.translation.x,
                "world_y": obj.matrix_world.translation.y,
                "world_z": obj.matrix_world.translation.z,
                "vertices": vertex_count,
                "polygons": polygon_count,
                "material_slots": ";".join(materials),
                "bounds_min_x": minimum[0],
                "bounds_min_y": minimum[1],
                "bounds_min_z": minimum[2],
                "bounds_max_x": maximum[0],
                "bounds_max_y": maximum[1],
                "bounds_max_z": maximum[2],
            }
        )
    columns = list(rows[0].keys()) if rows else ["object", "type", "parent"]
    with filepath.open("w", newline="", encoding="utf-8") as csv_file:
        writer = csv.DictWriter(csv_file, fieldnames=columns)
        writer.writeheader()
        writer.writerows(rows)
    return filepath


def build_ruined_building(fbx_path, output_dir, photo_path, repeat):
    import_destroyed_city(fbx_path)
    write_destroyed_city_inventory(output_dir)
    root = bpy.data.objects.get("Colonne_1")
    if root is None or root.type != "MESH":
        raise RuntimeError("Could not locate authored Colonne_1 ruined-building group in the FBX.")
    group = descendants(root)
    meshes = [obj for obj in group if obj.type == "MESH" and obj.data.polygons]
    if len(meshes) < 2:
        raise RuntimeError(f"Colonne_1 group unexpectedly contains only {len(meshes)} mesh object(s).")
    clean_scene(meshes)
    image = load_concrete_image(photo_path)
    configure_ruin_and_rubble_materials(image, repeat)
    for obj in meshes:
        obj.name = f"Fleurman_{obj.name}"
    source_blend = output_dir / "ruined_building_source.blend"
    save_source_blend(source_blend, meshes, image)
    return source_blend, {
        "sourceArtist": "Fleurman",
        "sourcePack": "Destroyed City Assets (2017)",
        "sourceLicense": "CC0 (verified for the downloaded OpenGameArt source)",
        "sourceUrl": "https://opengameart.org/content/destroyed-city-assets",
        "sourceFile": str(fbx_path),
        "sourceHierarchyRoot": "Colonne_1",
        "sourceMeshObjects": [obj.name for obj in meshes],
        "selectionNotes": "Selected one physically assembled authored ruin hierarchy only; excluded the two other spaced building variants, roads, debris, rocks, cube dummy, camera, and light.",
    }


def bounds_center(objects):
    points = [
        obj.matrix_world @ vertex.co
        for obj in objects
        for vertex in obj.data.vertices
    ]
    if not points:
        raise RuntimeError("Cannot find bounds for selected source meshes.")
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return (minimum + maximum) * 0.5, minimum, maximum


def object_world_bounds(obj):
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return minimum, maximum


def rotate_mesh_about_local_center(obj, rotation_degrees):
    coordinates = [vertex.co.copy() for vertex in obj.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in coordinates) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in coordinates) for axis in range(3)))
    center = (minimum + maximum) * 0.5
    rotation = Matrix.Identity(4)
    for axis, degrees in zip(("X", "Y", "Z"), rotation_degrees):
        if degrees:
            rotation = Matrix.Rotation(math.radians(degrees), 4, axis) @ rotation
    rotation_3x3 = rotation.to_3x3()
    for vertex in obj.data.vertices:
        vertex.co = center + rotation_3x3 @ (vertex.co - center)
    obj.data.update()


def arrange_rubble_heap(objects):
    # Detach from the source library's presentation empties but retain every
    # authored world transform; arrange the actual shards as a compact heap.
    for obj in objects:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        if obj.data.users > 1:
            obj.data = obj.data.copy()
    center, _minimum, _maximum = bounds_center(objects)
    layout = {
        "debri_1": ((-0.08, 0.02), (0.0, 0.0, 9.0), 0.00),
        "debri_2": ((0.06, 0.06), (78.0, 0.0, -12.0), 0.42),
        "debri_3": ((0.46, -0.26), (74.0, 0.0, 21.0), 0.30),
        "rock_1": ((-0.48, 0.30), (0.0, 0.0, -18.0), 0.38),
        "rock_2": ((0.35, 0.36), (0.0, 0.0, 14.0), 0.40),
        "rock_3": ((-0.24, -0.40), (76.0, 0.0, 28.0), 0.34),
    }
    for obj in objects:
        asset_name = normalized_name(obj.name).replace("fleurman_", "")
        placement, rotation, target_ground = layout[asset_name]
        rotate_mesh_about_local_center(obj, rotation)
        minimum, maximum = object_world_bounds(obj)
        fragment_center = (minimum + maximum) * 0.5
        translation = Vector(
            (
                center.x + placement[0] - fragment_center.x,
                center.y + placement[1] - fragment_center.y,
                target_ground - minimum.z,
            )
        )
        world = obj.matrix_world.copy()
        world.translation += translation
        obj.matrix_world = world


def normalized_name(value):
    decomposed = unicodedata.normalize("NFKD", value)
    return "".join(char for char in decomposed if not unicodedata.combining(char)).lower()


def build_rubble_heap(fbx_path, output_dir, photo_path, repeat):
    import_destroyed_city(fbx_path)
    write_destroyed_city_inventory(output_dir)
    mesh_by_name = {
        normalized_name(obj.name): obj
        for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.data.polygons
    }
    wanted = ("debri_1", "debri_2", "debri_3", "rock_1", "rock_2", "rock_3")
    missing = [name for name in wanted if name not in mesh_by_name]
    if missing:
        raise RuntimeError(
            "Fleurman source is missing required authored rubble fragments: " + ", ".join(missing)
        )
    meshes = [mesh_by_name[name] for name in wanted]
    arrange_rubble_heap(meshes)
    clean_scene(meshes)
    image = load_concrete_image(photo_path)
    configure_ruin_and_rubble_materials(image, repeat)
    for obj in meshes:
        obj.name = f"Fleurman_{obj.name}"
    source_blend = output_dir / "rubble_heap_source.blend"
    save_source_blend(source_blend, meshes, image)
    return source_blend, {
        "sourceArtist": "Fleurman",
        "sourcePack": "Destroyed City Assets (2017)",
        "sourceLicense": "CC0 (verified for the downloaded OpenGameArt source)",
        "sourceUrl": "https://opengameart.org/content/destroyed-city-assets",
        "sourceFile": str(fbx_path),
        "sourceMeshObjects": [obj.name for obj in meshes],
        "selectedOriginalNames": ["Débri_1", "Débri_2", "Débri_3", "Rock_1", "Rock_2", "Rock_3"],
        "selectionNotes": "Only authored Débri_1-3 broken concrete pieces and Rock_1-3 fragments; the upright long shards were rigidly rotated and the six meshes repositioned into a low, overlapping heap. No geometry was added or removed. Excluded road pieces, building groups, cube dummy, camera, and light.",
    }


def setup_atlas_bake(obj, materials, atlas_size):
    missing_material_faces = [
        poly.index
        for poly in obj.data.polygons
        if poly.material_index >= len(obj.data.materials)
        or obj.data.materials[poly.material_index] is None
    ]
    if missing_material_faces:
        raise RuntimeError(
            f"{obj.name} has {len(missing_material_faces)} faces without a source material; "
            "refusing to invent an albedo."
        )
    atlas_name = "LicensedAlbedoAtlas"
    original_names = {layer.name for layer in obj.data.uv_layers}
    index = 1
    while atlas_name in original_names:
        atlas_name = f"LicensedAlbedoAtlas{index}"
        index += 1
    atlas = obj.data.uv_layers.new(name=atlas_name)
    obj.data.uv_layers.active = atlas
    for layer in obj.data.uv_layers:
        layer.active_render = layer == atlas

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    try:
        bpy.ops.uv.lightmap_pack(
            PREF_CONTEXT="ALL_FACES",
            PREF_PACK_IN_ONE=True,
            PREF_NEW_UVLAYER=False,
            PREF_BOX_DIV=12,
            PREF_MARGIN_DIV=0.035,
        )
    except Exception:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.uv_layers.active = atlas
    for layer in obj.data.uv_layers:
        layer.active_render = layer == atlas

    image = bpy.data.images.new(
        name="LicensedCityDiffuseAtlas",
        width=atlas_size,
        height=atlas_size,
        alpha=False,
        float_buffer=False,
    )
    for material in materials:
        if material is None:
            raise RuntimeError("Source geometry has a material-less face; refusing to invent a fallback albedo.")
        shader = material_principled(material)
        nodes = material.node_tree.nodes
        for node in nodes:
            node.select = False
        target = nodes.new("ShaderNodeTexImage")
        target.name = "DiffuseAtlasBakeTarget"
        target.label = "Bake target (not a source material)"
        target.image = image
        target.select = True
        nodes.active = target
        atlas_coordinates = nodes.new("ShaderNodeUVMap")
        atlas_coordinates.label = "Bake atlas UVs"
        atlas_coordinates.uv_map = atlas.name
        material.node_tree.links.new(
            atlas_coordinates.outputs["UV"], target.inputs["Vector"]
        )

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.cycles.bake_type = "DIFFUSE"
    bake = scene.render.bake
    bake.target = "IMAGE_TEXTURES"
    bake.use_clear = True
    bake.use_pass_color = True
    bake.use_pass_direct = False
    bake.use_pass_indirect = False
    bake.margin = 10
    bpy.ops.object.bake(type="DIFFUSE")
    return image, atlas


def bake_source_blend_to_glb(source_blend, output_glb, output_dir, atlas_size):
    bpy.ops.wm.open_mainfile(filepath=str(source_blend))
    meshes = object_meshes()
    if not meshes:
        raise RuntimeError(f"Source blend contains no authored mesh objects: {source_blend}")

    bpy.ops.object.select_all(action="DESELECT")
    for mesh in meshes:
        mesh.select_set(True)
    active = meshes[0]
    bpy.context.view_layer.objects.active = active
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = source_blend.stem.replace("_source", "")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    materials = list(dict.fromkeys(material for material in obj.data.materials if material is not None))
    if not materials:
        raise RuntimeError(f"Selected source mesh has no assigned materials: {source_blend}")

    image, atlas = setup_atlas_bake(obj, materials, atlas_size)
    bake_png = output_dir / f"{obj.name}_source_albedo.png"
    image.file_format = "PNG"
    image.filepath_raw = str(bake_png)
    image.save()

    # The baked asset becomes one ordinary UV-mapped base-color material so
    # glTF has no dependency on Blender's generated-coordinate or box mapping.
    baked_material = bpy.data.materials.new(name=f"{obj.name}_BakedConcreteAlbedo")
    baked_material.use_nodes = True
    nodes = baked_material.node_tree.nodes
    shader = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Roughness"].default_value = 0.9
    shader.inputs["Metallic"].default_value = 0.0
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = "BaseColorAtlas"
    texture.label = "Baked source albedo"
    texture.image = image
    baked_material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    atlas_coordinates = nodes.new("ShaderNodeUVMap")
    atlas_coordinates.name = "BaseColorAtlasUV"
    atlas_coordinates.uv_map = atlas.name
    baked_material.node_tree.links.new(
        atlas_coordinates.outputs["UV"], texture.inputs["Vector"]
    )
    obj.data.materials.clear()
    obj.data.materials.append(baked_material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    atlas_name = atlas.name
    for layer in list(obj.data.uv_layers):
        if layer.name != atlas_name:
            obj.data.uv_layers.remove(layer)
    atlas = obj.data.uv_layers.get(atlas_name)
    if atlas is None:
        raise RuntimeError("Bake atlas UV layer was lost while removing stale source UVs.")
    obj.data.uv_layers.active = atlas
    atlas.active_render = True

    image.pack()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=str(output_glb),
        export_format="GLB",
        use_selection=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_image_format="AUTO",
        export_yup=True,
        export_apply=True,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
    )
    if not output_glb.is_file() or output_glb.stat().st_size == 0:
        raise RuntimeError(f"Blender glTF exporter failed to create {output_glb}")
    return bake_png, sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)


def asset_info(source_blend, glb, bake_png, details, triangle_count, key):
    return {
        "key": key,
        "sourceBlend": source_blend.name,
        "glb": glb.name,
        "bakedSourceAlbedo": bake_png.name,
        "actualArtistAuthoredGeometry": True,
        "sourceKind": "human-authored static meshes",
        "sourceMaterialsBakedDiffuseColorOnly": True,
        "sourceTriangleCountBeforeOptimization": triangle_count,
        "sourceProvenance": details,
    }


def main():
    args = parse_args()
    output_dir = args.out.expanduser().resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    pack_root = args.quaternius_root.expanduser().resolve()
    fbx_path = args.destroyed_city_fbx.expanduser().resolve()
    photo_path = args.concrete_albedo.expanduser().resolve()
    if not photo_path.is_file():
        raise FileNotFoundError(
            f"Required CC0 photo albedo is not available yet: {photo_path}. "
            "The assembly deliberately will not substitute a synthetic or flat-color texture."
        )

    builders = {
        "apartment": lambda: build_apartment(pack_root, output_dir, photo_path, args.wall_repeat),
        "ruined_building": lambda: build_ruined_building(
            fbx_path, output_dir, photo_path, args.wall_repeat
        ),
        "rubble_heap": lambda: build_rubble_heap(fbx_path, output_dir, photo_path, args.wall_repeat),
    }
    if args.only:
        builders = {args.only: builders[args.only]}

    manifest_path = output_dir / "source_manifest.json"
    existing_records = {}
    if args.only and manifest_path.is_file():
        try:
            previous_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            existing_records = {
                record.get("key"): record
                for record in previous_manifest.get("assets", [])
                if record.get("key") and record.get("key") != args.only
            }
        except (OSError, ValueError, TypeError):
            existing_records = {}
    asset_records = []
    for key, build in builders.items():
        source_blend, details = build()
        glb_path = output_dir / f"{key}.glb"
        bake_png, triangles = bake_source_blend_to_glb(
            source_blend, glb_path, output_dir, args.atlas_size
        )
        record = asset_info(source_blend, glb_path, bake_png, details, triangles, key)
        asset_records.append(record)
        print(
            f"Prepared licensed source {key}: {triangles} source triangles, "
            f"GLB={glb_path}, editable source={source_blend}"
        )

    existing_records.update({record["key"]: record for record in asset_records})
    manifest = {
        "formatVersion": 1,
        "sourcePreparation": "Blender 4.4; authored mesh extraction, box-projected scanned concrete source in editable BLEND, diffuse-color-only atlas bake for GLB.",
        "concreteAlbedoSource": str(photo_path),
        "concreteAlbedoLicense": "CC0 (provided as verified by the asset curator)",
        "generatedByAI": False,
        "assets": [existing_records[key] for key in sorted(existing_records)],
    }
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Source manifest: {manifest_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"assemble_licensed_city_sources.py: ERROR: {error}", file=sys.stderr)
        raise