#!/usr/bin/env python3
"""Render the production Shuja'iyya district layout and optimized city meshes offline.

First export the real C# layout through the lightweight fixture:
  dotnet run --project testingReplic/Tests/Layout/LayoutTests.csproj -- \
    --export-preview /tmp/district-layout.json

Then render it with Blender 4.4+ (Cycles CPU only; no GPU context required):
  blender --background --factory-startup --python tools/render_city_layout_preview.py -- \
    --layout /tmp/district-layout.json \
    --models-dir testingReplic/Assets/NewGaza/Resources/Models \
    --output exports/models/district_layout.png

The preview shows the production layout with every plot at the game's stage-0
ruins state. It is an offline optimized-mesh preview; it does not prove a GPU
device or Unity runtime rendering.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


GROUND_Z = -0.09
EXPECTED_DISTRICT = "shujaiya"
EXPECTED_PLOTS = 9
MODEL_KEYS = ("apartment", "ruined_building", "rubble_heap")
PREVIEW_CAPTION = (
    "Offline optimized-mesh + production DistrictLayout preview "
    "(Cycles CPU; no GPU device proven)."
)


def cli_arguments():
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--layout", required=True, help="JSON exported by LayoutTests --export-preview")
    parser.add_argument(
        "--models-dir",
        default="testingReplic/Assets/NewGaza/Resources/Models",
        help="Directory containing the three Unity OBJ meshes and matching albedo PNGs",
    )
    parser.add_argument(
        "--output",
        default="exports/models/district_layout.png",
        help="Output PNG (default: exports/models/district_layout.png)",
    )
    return parser.parse_args(arguments)


def require_file(path, description):
    if not path.is_file() or path.stat().st_size == 0:
        raise FileNotFoundError(f"Required {description} is missing or empty: {path}")
    return path


def read_layout(path):
    require_file(path, "production DistrictLayout JSON")
    layout = json.loads(path.read_text(encoding="utf-8"))
    if layout.get("formatVersion") != 1:
        raise ValueError("Unsupported preview layout formatVersion; expected 1.")
    if layout.get("districtId") != EXPECTED_DISTRICT:
        raise ValueError(f"Expected district {EXPECTED_DISTRICT!r}, got {layout.get('districtId')!r}.")
    if layout.get("requestedPlotCount") != EXPECTED_PLOTS:
        raise ValueError(f"Expected {EXPECTED_PLOTS} stage-0 plots, got {layout.get('requestedPlotCount')!r}.")
    if len(layout.get("plots", [])) != EXPECTED_PLOTS:
        raise ValueError("Production layout JSON does not contain exactly nine plots.")
    if not layout.get("streets"):
        raise ValueError("Production layout JSON has no street segments.")
    return layout


def unity_point_to_blender(point, base_height=GROUND_Z):
    """Map Unity x/y/z (Y-up) into Blender x/y/z (Z-up), including ground elevation."""
    return Vector((float(point[0]), -float(point[2]), float(point[1]) + base_height))


def rotation_yaw(yaw_degrees):
    return math.radians(float(yaw_degrees))


def make_material(name, color, roughness=0.9):
    material = bpy.data.materials.new(name=name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    principled = next(node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Roughness"].default_value = roughness
    principled.inputs["Metallic"].default_value = 0.0
    return material


def create_albedo_material(key, albedo_path):
    image = bpy.data.images.load(str(albedo_path), check_existing=False)
    image.pack()
    material = make_material(f"New Gaza • imported {key} albedo", (1.0, 1.0, 1.0), 0.92)
    nodes = material.node_tree.nodes
    principled = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = f"{key}_albedo_png"
    texture.label = "Imported Unity albedo PNG"
    texture.image = image
    material.node_tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    return material


def import_city_model(key, models_dir):
    obj_path = require_file(models_dir / f"{key}.obj", f"{key} OBJ model")
    albedo_path = require_file(models_dir / f"{key}_albedo.png", f"{key} albedo PNG")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.wm.obj_import(
        filepath=str(obj_path),
        forward_axis="NEGATIVE_Z",
        up_axis="Y",
    )
    imported = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if not imported:
        raise RuntimeError(f"OBJ importer found no mesh geometry in {obj_path}.")
    for obj in imported:
        obj.select_set(True)
    active = imported[0]
    bpy.context.view_layer.objects.active = active
    if len(imported) > 1:
        bpy.ops.object.join()
    model_obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    model_obj.name = f"Imported_{key}"
    model_obj.data.name = f"Imported_{key}_UVNormalsMesh"
    if not model_obj.data.vertices or not model_obj.data.polygons:
        raise RuntimeError(f"Imported {key} OBJ has no usable vertices or faces.")

    # The OBJ axes are the same Y-up/forward convention used by the Unity importer.
    # Blender's import-axis conversion places these local vertices in Z-up scene space.
    coordinates = [vertex.co.copy() for vertex in model_obj.data.vertices]
    minimum = Vector((
        min(point.x for point in coordinates),
        min(point.y for point in coordinates),
        min(point.z for point in coordinates),
    ))
    maximum = Vector((
        max(point.x for point in coordinates),
        max(point.y for point in coordinates),
        max(point.z for point in coordinates),
    ))
    blender_size = maximum - minimum
    # Convert imported Blender bounds back to the Unity source-model axes. Unity's
    # Y is Blender Z, and Unity's Z is negative Blender Y.
    unity_bounds = {
        "min": Vector((minimum.x, minimum.z, -maximum.y)),
        "max": Vector((maximum.x, maximum.z, -minimum.y)),
    }
    unity_bounds["size"] = unity_bounds["max"] - unity_bounds["min"]
    if min(unity_bounds["size"]) <= 1e-7 or min(blender_size) <= 1e-7:
        raise RuntimeError(f"Imported {key} model has zero-sized source bounds: {unity_bounds['size']}.")

    material = create_albedo_material(key, albedo_path)
    model_obj.data.materials.clear()
    model_obj.data.materials.append(material)
    for polygon in model_obj.data.polygons:
        polygon.material_index = 0
    model_obj.hide_render = True
    model_obj.hide_viewport = True
    return {
        "key": key,
        "mesh": model_obj.data,
        "bounds": unity_bounds,
        "material": material,
    }


def model_placement(model, available_width, available_depth, max_height, yaw_degrees):
    bounds = model["bounds"]
    size = bounds["size"]
    yaw = rotation_yaw(yaw_degrees)
    cosine = abs(math.cos(yaw))
    sine = abs(math.sin(yaw))
    rotated_width = cosine * size.x + sine * size.z
    rotated_depth = sine * size.x + cosine * size.z
    scale = min(available_width / rotated_width, available_depth / rotated_depth, max_height / size.y)
    if not math.isfinite(scale) or scale <= 0.0:
        raise ValueError(f"Could not fit imported {model['key']} mesh to the supplied footprint.")

    # CityModelLibrary.AddTo centers the source footprint, places its lowest Y at
    # ground, applies a uniform scale, then rotates around Unity Y.
    center_x = (bounds["min"].x + bounds["max"].x) * 0.5
    center_z = (bounds["min"].z + bounds["max"].z) * 0.5
    local_shift = Vector((-center_x * scale, center_z * scale, -bounds["min"].y * scale))
    cos_yaw, sin_yaw = math.cos(yaw), math.sin(yaw)
    rotated_shift = Vector((
        local_shift.x * cos_yaw - local_shift.y * sin_yaw,
        local_shift.x * sin_yaw + local_shift.y * cos_yaw,
        local_shift.z,
    ))
    return scale, yaw, rotated_shift


def create_model_instance(model, parent, unity_position, yaw_degrees, footprint_x,
                          footprint_z, max_height, name):
    scale, yaw, local_shift = model_placement(
        model, footprint_x, footprint_z, max_height, yaw_degrees
    )
    instance = bpy.data.objects.new(name, model["mesh"])
    bpy.context.scene.collection.objects.link(instance)
    instance.parent = parent
    local_position = Vector((
        float(unity_position[0]),
        -float(unity_position[2]),
        float(unity_position[1]),
    ))
    instance.location = local_position + local_shift
    instance.rotation_euler = (0.0, 0.0, yaw)
    instance.scale = (scale, scale, scale)
    return instance


def make_plot_anchor(plot, index):
    anchor = bpy.data.objects.new(f"PlotAnchor_{index + 1}", None)
    bpy.context.scene.collection.objects.link(anchor)
    anchor.empty_display_type = "PLAIN_AXES"
    anchor.empty_display_size = 0.0
    anchor.location = unity_point_to_blender(plot["position"])
    anchor.rotation_euler.z = rotation_yaw(plot["yawDegrees"])
    return anchor


def make_box_mesh():
    mesh = bpy.data.meshes.new("DistrictBoxMesh")
    mesh.from_pydata(
        [
            (-0.5, -0.5, -0.5), (0.5, -0.5, -0.5),
            (0.5, 0.5, -0.5), (-0.5, 0.5, -0.5),
            (-0.5, -0.5, 0.5), (0.5, -0.5, 0.5),
            (0.5, 0.5, 0.5), (-0.5, 0.5, 0.5),
        ],
        [],
        [
            (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
            (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
        ],
    )
    mesh.update()
    return mesh


def make_box(name, mesh, material, unity_center, unity_size, yaw_degrees=0.0):
    object_mesh = mesh.copy()
    object_mesh.name = f"{name}_Mesh"
    obj = bpy.data.objects.new(name, object_mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = unity_point_to_blender(unity_center)
    obj.rotation_euler.z = rotation_yaw(yaw_degrees)
    obj.scale = (float(unity_size[0]), float(unity_size[2]), float(unity_size[1]))
    obj.data.materials.clear()
    obj.data.materials.append(material)
    return obj


def make_asphalt_cap(mesh, material, point, width, index):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=0.5,
        depth=1.0,
        location=unity_point_to_blender((point[0], 0.04, point[2])),
    )
    cap = bpy.context.object
    cap.name = f"StreetJunctionAsphalt_{index}"
    cap.scale = (width, width, 0.08)
    cap.data.materials.clear()
    cap.data.materials.append(material)
    for polygon in cap.data.polygons:
        polygon.material_index = 0


def build_streets(layout, asphalt, sidewalk, curb):
    box_mesh = make_box_mesh()
    cap_index = 0
    for index, street in enumerate(layout["streets"]):
        start = Vector(street["start"])
        end = Vector(street["end"])
        delta = end - start
        length = delta.length
        if length < 0.1:
            continue
        tangent = delta / length
        normal = Vector((tangent.z, 0.0, -tangent.x))
        midpoint = (start + end) * 0.5
        yaw = math.degrees(math.atan2(tangent.x, tangent.z))
        road_half_width = street["width"] * 0.5
        sidewalk_width = street["sidewalkWidth"]
        sidewalk_offset = road_half_width + sidewalk_width * 0.5

        # Match CityWorld.BuildInlandStreets: source carriageway, curb, and
        # sidewalk dimensions/offsets are read from each exported real segment.
        make_box(
            f"Street_{index + 1}_Asphalt", box_mesh, asphalt,
            (midpoint.x, 0.04, midpoint.z),
            (street["width"], 0.08, length + 0.12), yaw,
        )
        for side in (-1, 1):
            curb_center = midpoint + normal * side * (road_half_width + 0.055)
            make_box(
                f"Street_{index + 1}_Curb_{side}", box_mesh, curb,
                (curb_center.x, 0.07, curb_center.z),
                (0.11, 0.14, length + 0.08), yaw,
            )
            sidewalk_center = midpoint + normal * side * sidewalk_offset
            make_box(
                f"Street_{index + 1}_Sidewalk_{side}", box_mesh, sidewalk,
                (sidewalk_center.x, 0.035, sidewalk_center.z),
                (sidewalk_width, 0.07, length + 0.14), yaw,
            )
        for endpoint in (street["start"], street["end"]):
            make_asphalt_cap(box_mesh, asphalt, endpoint, street["width"], cap_index)
            cap_index += 1


class DotNetRandom:
    """Seeded System.Random compatibility for the game's three per-plot rubble placements."""

    MBIG = 2147483647
    MSEED = 161803398

    def __init__(self, seed):
        subtraction = abs(int(seed))
        if subtraction == 2147483648:
            subtraction = self.MBIG
        mj = self.MSEED - subtraction
        self.seed_array = [0] * 56
        self.seed_array[55] = mj
        mk = 1
        for index in range(1, 55):
            target = (21 * index) % 55
            self.seed_array[target] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = self.seed_array[target]
        for _ in range(4):
            for index in range(1, 56):
                self.seed_array[index] -= self.seed_array[1 + (index + 30) % 55]
                if self.seed_array[index] < 0:
                    self.seed_array[index] += self.MBIG
        self.inext = 0
        self.inextp = 21

    def next_double(self):
        self.inext += 1
        if self.inext >= 56:
            self.inext = 1
        self.inextp += 1
        if self.inextp >= 56:
            self.inextp = 1
        value = self.seed_array[self.inext] - self.seed_array[self.inextp]
        if value == self.MBIG:
            value -= 1
        if value < 0:
            value += self.MBIG
        self.seed_array[self.inext] = value
        return value * (1.0 / self.MBIG)


def build_stage_zero_plots(layout, models):
    ruin = models["ruined_building"]
    rubble = models["rubble_heap"]
    for index, plot in enumerate(layout["plots"]):
        anchor = make_plot_anchor(plot, index)
        footprint_x = float(plot["size"][0]) * 0.82
        footprint_z = float(plot["size"][2]) * 0.8
        max_height = max(1.0, min(footprint_x, footprint_z) * 1.2)
        create_model_instance(
            ruin, anchor, (0.0, 0.0, 0.0), 0.0,
            footprint_x * 0.76, footprint_z * 0.72, max_height,
            f"Stage0RuinedBuilding_{index + 1}",
        )

        # Mirror CityWorld.AddImportedRubbleScatter and its stable .NET seed.
        random = DotNetRandom(layout["districtIndex"] * 17 + index * 31)
        for cluster in range(3):
            width = footprint_x * (0.2 + random.next_double() * 0.12)
            depth = footprint_z * (0.2 + random.next_double() * 0.12)
            max_x = max(0.0, footprint_x * 0.5 - width * 0.5 - 0.12)
            max_z = max(0.0, footprint_z * 0.5 - depth * 0.5 - 0.12)
            x = (random.next_double() * 2.0 - 1.0) * max_x
            z = (random.next_double() * 2.0 - 1.0) * max_z
            yaw = random.next_double() * 360.0
            create_model_instance(
                rubble, anchor, (x, 0.0, z), yaw, width, depth,
                min(width, depth), f"Stage0Rubble_{index + 1}_{cluster + 1}",
            )


def build_roadside_rubble(layout, models):
    for utility in layout["utilities"]:
        if utility.get("model") != "rubble_heap":
            continue
        # DistrictView uses this exact 2 x 1.8 footprint, 1.5 max height,
        # and reserved utility transform for its salvage heap.
        anchor = bpy.data.objects.new("RoadsideSalvageAnchor", None)
        bpy.context.scene.collection.objects.link(anchor)
        anchor.empty_display_size = 0.0
        anchor.location = unity_point_to_blender(utility["position"])
        anchor.rotation_euler.z = rotation_yaw(utility["yawDegrees"])
        create_model_instance(
            models["rubble_heap"], anchor, (0.0, 0.0, 0.0), 0.0,
            2.0, 1.8, 1.5, "RoadsideSalvage_RubbleHeap",
        )


def point_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_lighting_and_camera(scene):
    camera_data = bpy.data.cameras.new("DistrictLayoutPreviewCamera")
    camera = bpy.data.objects.new("DistrictLayoutPreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (37.0, -39.0, 47.0)
    camera_data.lens = 42.0
    point_at(camera, (0.0, 0.0, 2.0))
    scene.camera = camera

    sun_data = bpy.data.lights.new("DaylightSun", type="SUN")
    sun_data.energy = 3.0
    sun_data.angle = math.radians(50.0)
    sun = bpy.data.objects.new("DaylightSun", sun_data)
    scene.collection.objects.link(sun)
    sun.location = (-20.0, -28.0, 34.0)
    point_at(sun, (0.0, 0.0, 0.0))

    fill_data = bpy.data.lights.new("SoftSkyFill", type="AREA")
    fill_data.energy = 600.0
    fill_data.shape = "DISK"
    fill_data.size = 24.0
    fill = bpy.data.objects.new("SoftSkyFill", fill_data)
    scene.collection.objects.link(fill)
    fill.location = (-5.0, 12.0, 26.0)
    point_at(fill, (0.0, 0.0, 0.0))


def configure_render(scene, output_path):
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 4
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = str(output_path)
    scene["preview_caption"] = PREVIEW_CAPTION
    if scene.world is None:
        scene.world = bpy.data.worlds.new("NeutralDaylightWorld")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.48, 0.50, 0.48, 1.0)
    background.inputs["Strength"].default_value = 0.6


def render(layout_path, models_dir, output_path):
    layout = read_layout(layout_path)
    models_dir = models_dir.expanduser().resolve()
    output_path = output_path.expanduser().resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    models = {key: import_city_model(key, models_dir) for key in MODEL_KEYS}

    ground = make_material("Continuous neutral ground", (0.48, 0.445, 0.39), 0.96)
    asphalt = make_material("Worn neutral asphalt", (0.065, 0.063, 0.058), 0.96)
    sidewalk = make_material("Dusty limestone sidewalk", (0.48, 0.445, 0.38), 0.92)
    curb = make_material("Limestone curb", (0.40, 0.36, 0.30), 0.94)

    ground_mesh = bpy.data.meshes.new("ContinuousGroundPlaneMesh")
    ground_mesh.from_pydata(
        [(-40.0, -40.0, GROUND_Z), (40.0, -40.0, GROUND_Z),
         (40.0, 40.0, GROUND_Z), (-40.0, 40.0, GROUND_Z)],
        [], [(0, 1, 2, 3)],
    )
    ground_mesh.update()
    ground_obj = bpy.data.objects.new("Continuous neutral terrain • no plot pads", ground_mesh)
    bpy.context.scene.collection.objects.link(ground_obj)
    ground_obj.data.materials.append(ground)

    build_streets(layout, asphalt, sidewalk, curb)
    build_stage_zero_plots(layout, models)
    build_roadside_rubble(layout, models)

    scene = bpy.context.scene
    add_lighting_and_camera(scene)
    configure_render(scene, output_path)
    bpy.ops.render.render(write_still=True)
    require_file(output_path, "rendered district layout preview")
    print(f"Rendered: {output_path}")
    print(f"Caption: {PREVIEW_CAPTION}")
    print(
        f"Source: DistrictLayout.Create({layout['districtId']!r}, {layout['requestedPlotCount']}); "
        f"{len(layout['streets'])} actual street segments; 9 stage-0 imported ruins; "
        "27 deterministically seeded rubble clusters; imported apartment mesh validated."
    )


def main():
    options = cli_arguments()
    render(
        Path(options.layout),
        Path(options.models_dir),
        Path(options.output),
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"render_city_layout_preview.py: ERROR: {error}", file=sys.stderr)
        raise