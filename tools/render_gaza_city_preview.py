#!/usr/bin/env python3
"""Render OSM-sourced Gaza City context using the native worker's exported LOD placements.

The placements file is required: it must be exported by the production gaza-urban-world
selection path, not generated or randomized by this renderer. Example:

  blender --background --factory-startup --python tools/render_gaza_city_preview.py -- \
    --basemap testingReplic/Assets/NewGaza/Resources/GazaBasemap.json \
    --placements exports/models/Gaza-City-Production-Placement.json \
    --out-overview exports/models/Gaza-City-Overview.png \
    --out-neighborhood exports/models/Gaza-Urban-Neighborhood.png

Cycles CPU, 16 samples, four threads. These are offline source-layout previews, not Unity
screenshots, and mapped OSM geometry does not make claims about current building condition.
"""

import argparse
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector


GROUND_Z = -0.09
EXPECTED_COUNTS = {"roads": 4032, "buildings": 12000, "areas": 528}
MODEL_KEYS = {"apartment_context", "ruined_building_context"}
OSM_CAPTION = (
    "© OpenStreetMap contributors · ODbL 1.0 | OSM geometry is not evidence "
    "of current building condition."
)
GAME_GEOGRAPHY = Path("testingReplic/Assets/NewGaza/Core/GameGeography.cs")
MODELS_DIR = Path("testingReplic/Assets/NewGaza/Resources/Models")


def cli_arguments():
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--basemap", default="testingReplic/Assets/NewGaza/Resources/GazaBasemap.json")
    parser.add_argument("--placements", required=True,
                        help="JSON exported by the production gaza-urban-world context selector")
    parser.add_argument("--models-dir", default=str(MODELS_DIR))
    parser.add_argument("--geography", default=str(GAME_GEOGRAPHY),
                        help="GameGeography.cs containing the native 19-point coastline")
    parser.add_argument("--out-overview", default="exports/models/Gaza-City-Overview.png")
    parser.add_argument("--out-neighborhood", default="exports/models/Gaza-Urban-Neighborhood.png")
    return parser.parse_args(arguments)


def require_file(path, description):
    if not path.is_file() or path.stat().st_size == 0:
        raise FileNotFoundError(f"Required {description} is missing or empty: {path}")
    return path


def read_json(path, description):
    require_file(path, description)
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Could not read {description} {path}: {error}") from error


def field_point(value, name):
    if isinstance(value, dict):
        try:
            return float(value["x"]), float(value.get("z", value.get("y")))
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError(f"Invalid {name} point object: {value!r}") from error
    if isinstance(value, (list, tuple)) and len(value) >= 2:
        return float(value[0]), float(value[2] if len(value) >= 3 else value[1])
    raise ValueError(f"Missing or invalid {name} point: {value!r}")


def load_basemap(path):
    data = read_json(path, "OSM Gaza city basemap")
    schema = data.get("schemaVersion", data.get("schema"))
    metadata = data.get("metadata") or {}
    projection = metadata.get("projection") or {}
    if schema != 1:
        raise ValueError(f"Expected GazaBasemap schema 1, got {schema!r}.")
    origin = (float(projection.get("originLatitude", 0)),
              float(projection.get("originLongitude", 0)))
    units = float(projection.get("unitsPerKilometre", 0))
    if abs(origin[0] - 31.515) > 0.00001 or abs(origin[1] - 34.45) > 0.00001 or abs(units - 50) > 0.001:
        raise ValueError(f"Unexpected OSM projection origin/scale: {origin}, {units} units/km.")
    for key, expected in EXPECTED_COUNTS.items():
        count = len(data.get(key, []))
        if count != expected:
            raise ValueError(f"Expected {expected} sourced OSM {key}, found {count}.")
    if any(not data.get(key) for key in ("roads", "buildings", "areas")):
        raise ValueError("OSM basemap requires sourced roads, building outlines, and land areas.")
    return data, origin, units


def project_geo(latitude, longitude, origin, units):
    km_per_degree = 111.32
    return (
        (longitude - origin[1]) * km_per_degree * math.cos(math.radians(origin[0])) * units,
        (latitude - origin[0]) * km_per_degree * units,
    )


def load_native_coast(path, origin, units, bounds):
    source = require_file(path, "native GameGeography.cs")
    text = source.read_text(encoding="utf-8")
    match = re.search(r"CoastCoordinates\s*=\s*\{(.*?)\};", text, re.DOTALL)
    if not match:
        raise ValueError(f"Could not find CoastCoordinates in {source}.")
    coordinates = re.findall(
        r"GeoCoordinate\s*\(\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*\)",
        match.group(1),
    )
    if len(coordinates) != 19:
        raise ValueError(f"Expected the native 19-point coast, found {len(coordinates)} points.")
    points = [project_geo(float(lat), float(lon), origin, units) for lat, lon in coordinates]
    min_x, max_x, min_z, max_z = bounds
    if any(x < min_x - 45 or x > max_x + 45 or z < min_z - 45 or z > max_z + 45
           for x, z in points):
        raise ValueError("Native coast projection is inconsistent with the GazaBasemap world extent.")
    return points


def read_native_placements(path, basemap):
    document = read_json(path, "production native-context placement export")
    if document.get("schema") != 1:
        raise ValueError(f"Expected production placement export schema 1, got {document.get('schema')!r}.")
    candidates = (
        "contextModelPlacements", "authoredLodPlacements", "authoredPlacements",
        "authoredModels", "contextPlacements", "contextModels", "placements", "models",
    )
    records = next((document[key] for key in candidates if isinstance(document.get(key), list)), None)
    if records is None:
        raise ValueError(
            "Production placement export must contain a contextModelPlacements (or equivalent) array."
        )
    expected_count = document.get("authoredModelCopyCount")
    if expected_count is not None and int(expected_count) != len(records):
        raise ValueError(
            f"Native export claims {expected_count} authored models but contains {len(records)} placements."
        )
    if len(records) > int(document.get("maxAuthoredModelCopies", 1200)):
        raise ValueError("Production export exceeds its authored context model copy cap.")
    estimated_triangles = document.get("estimatedContextTriangles")
    triangle_budget = document.get("maxContextTriangles", 700000)
    if estimated_triangles is not None and int(estimated_triangles) > int(triangle_budget):
        raise ValueError("Production export exceeds its declared static context triangle budget.")

    districts = document.get("districts")
    if not isinstance(districts, list) or not districts:
        raise ValueError("Production placement export must include its selected native districts and parcels.")
    project_ids = set()
    shujaiya_plots = None
    total_plots = 0
    total_utilities = 0
    for district in districts:
        district_id = district.get("id")
        plots = district.get("plots")
        utilities = district.get("utilityPositions", [])
        if not district_id or not isinstance(plots, list):
            raise ValueError("Malformed district/plot data in production placement export.")
        total_plots += len(plots)
        total_utilities += len(utilities)
        for plot in plots:
            plot_id = str(plot.get("sourceBuildingId", ""))
            if not plot_id or plot_id in project_ids:
                raise ValueError(f"Missing or duplicate native project parcel id {plot_id!r}.")
            project_ids.add(plot_id)
        if district_id == "shujaiya":
            shujaiya_plots = plots
    if len(project_ids) != total_plots:
        raise ValueError("Native plot ids are not unique in the placement export.")
    if total_plots != 108 or total_utilities != 36:
        raise ValueError(
            f"Expected native exports for 108 plots and 36 utilities, got {total_plots} and {total_utilities}."
        )
    if not shujaiya_plots or len(shujaiya_plots) != 9:
        raise ValueError("Production fixture must contain the nine native Shuja'iyya project parcels.")

    source_buildings = {}
    for item in basemap["buildings"]:
        source_id = str(item.get("idText") or item.get("id"))
        if source_id in source_buildings:
            raise ValueError(f"Duplicate OSM building id {source_id} in basemap.")
        source_buildings[source_id] = item

    placements = []
    seen = set()
    for index, record in enumerate(records):
        if not isinstance(record, dict):
            raise ValueError(f"Native placement {index} is not a JSON object.")
        source_id = str(record.get("sourceBuildingId", record.get("buildingId", record.get("id", ""))))
        source = source_buildings.get(source_id)
        if source is None:
            raise ValueError(f"Native placement {index} refers to unknown OSM building {source_id!r}.")
        if source_id in seen:
            raise ValueError(f"Native placement export repeats OSM building {source_id!r}.")
        seen.add(source_id)
        model = record.get("model", record.get("modelKey", record.get("key")))
        if model not in MODEL_KEYS:
            raise ValueError(f"Native placement {index} has unsupported context model {model!r}.")
        if record.get("footprintIsLocal") is not True:
            raise ValueError(
                f"Native placement {source_id} must preserve its exported footprintIsLocal=true transform."
            )
        source_center = source["center"]
        source_size = source["size"]
        position_value = record.get("worldPosition", record.get("position"))
        position = ((float(source_center["x"]), GROUND_Z, float(source_center["z"]))
                    if position_value is None else parse_xyz(position_value, "position"))
        size_value = record.get("size", record.get("footprint"))
        size = ((float(source_size["x"]), 0.0, float(source_size["z"]))
                if size_value is None else parse_xyz(size_value, "size"))
        yaw = float(record.get("yawDegrees", record.get("yaw", source["yaw"])))
        height = float(record.get("height", source["height"]))
        expected_position = (float(source_center["x"]), GROUND_Z, float(source_center["z"]))
        expected_size = (float(source_size["x"]), 0.0, float(source_size["z"]))
        if any(abs(a - b) > 0.02 for a, b in zip(position, expected_position)):
            raise ValueError(f"Native placement {source_id} is not at its sourced OSM building center.")
        if any(abs(a - b) > 0.02 for a, b in zip(size, expected_size)):
            raise ValueError(f"Native placement {source_id} does not use its sourced OBB footprint.")
        if abs(yaw - float(source["yaw"])) > 0.02 or abs(height - float(source["height"])) > 0.02:
            raise ValueError(f"Native placement {source_id} does not match its sourced yaw/height.")
        placements.append({
            "id": source_id, "model": model, "position": position, "size": size,
            "yaw": yaw, "height": height,
        })
    if not placements:
        raise ValueError("Production context placement export contains no authored LOD placements.")
    if len(placements) > 1200:
        raise ValueError(f"Production export has {len(placements)} placements; native cap is 1200.")
    for plot in shujaiya_plots:
        source_id = str(plot.get("sourceBuildingId", ""))
        source = source_buildings.get(source_id)
        if source is None:
            raise ValueError(f"Shuja'iyya parcel references unknown OSM building {source_id!r}.")
        center = parse_xyz(plot["worldPosition"], "Shuja'iyya parcel position")
        size = parse_xyz(plot["size"], "Shuja'iyya parcel size")
        yaw = float(plot["yaw"])
        expected_center = (float(source["center"]["x"]), GROUND_Z, float(source["center"]["z"]))
        expected_size = (float(source["size"]["x"]), 0.0, float(source["size"]["z"]))
        if any(abs(a - b) > 0.02 for a, b in zip(center, expected_center)):
            raise ValueError(f"Shuja'iyya parcel {source_id} is not at its OSM building center.")
        if any(abs(a - b) > 0.02 for a, b in zip(size, expected_size)):
            raise ValueError(f"Shuja'iyya parcel {source_id} has changed its OSM oriented footprint.")
        if abs(yaw - float(source["yaw"])) > 0.02:
            raise ValueError(f"Shuja'iyya parcel {source_id} has changed its OSM yaw.")
        plot["previewFootprint"] = (
            size[0] * 0.82 * 0.76,
            0.0,
            size[2] * 0.8 * 0.72,
        )
        plot["previewMaxHeight"] = max(
            1.0, min(size[0] * 0.82, size[2] * 0.8) * 1.2
        )
    if seen & project_ids:
        raise ValueError("Native context LOD placements duplicate native project parcel ids.")

    volume_keys = (
        "polygonVolumeIds", "contextBuildingVolumes", "buildingVolumes", "volumeBuildings",
        "extrudedBuildings", "contextVolumes", "volumePlacements",
    )
    volume_records = next(
        (document[key] for key in volume_keys if isinstance(document.get(key), list)), None
    )
    if volume_records is None:
        raise ValueError(
            "Production export is missing its native actual-outline building-volume records."
        )
    expected_volume_ids = set(source_buildings) - seen - project_ids
    volumes, volume_ids = [], set()
    for index, record in enumerate(volume_records):
        source_id = str(record.get("sourceBuildingId", record.get("buildingId", record.get("id", ""))))
        source = source_buildings.get(source_id)
        if source is None:
            raise ValueError(f"Native building volume {index} references unknown OSM id {source_id!r}.")
        if source_id in volume_ids or source_id in seen or source_id in project_ids:
            raise ValueError(f"OSM building {source_id} is duplicated between native geometry classes.")
        volume_ids.add(source_id)
        height = float(record["height"])
        outline_value = record.get("outline", record.get("worldOutline", record.get("points")))
        if not isinstance(outline_value, list) or len(outline_value) < 3:
            raise ValueError(f"Native building volume {source_id} is missing its actual OSM outline.")
        outline = [field_point(point, f"volume {source_id} outline") for point in outline_value]
        source_outline = projected(source["outline"])
        if not matching_outline(outline, source_outline):
            raise ValueError(f"Native building volume {source_id} outline differs from GazaBasemap.")
        if not math.isfinite(height) or height <= 0 or abs(height - float(source["height"])) > 0.02:
            raise ValueError(f"Native building volume {source_id} has changed its OSM source height.")
        volumes.append({"id": source_id, "height": height, "outline": outline})
    if volume_ids != expected_volume_ids:
        missing = sorted(expected_volume_ids - volume_ids)
        unexpected = sorted(volume_ids - expected_volume_ids)
        raise ValueError(
            f"Native volume ID coverage mismatch: {len(missing)} missing, "
            f"{len(unexpected)} unexpected; examples missing={missing[:4]}, extra={unexpected[:4]}."
        )
    for key in ("buildingVolumeCount", "contextVolumeCount", "extrudedBuildingCount"):
        if key in document and int(document[key]) != len(volumes):
            raise ValueError(f"Production export {key} does not match its volume records.")
    return placements, project_ids, shujaiya_plots, volumes, document


def matching_outline(candidate, source):
    if len(candidate) != len(source):
        return False
    count = len(source)
    for reverse in (False, True):
        for start in range(count):
            if all(
                abs(candidate[index][0] - source[(start + (-index if reverse else index)) % count][0]) < 0.02
                and abs(candidate[index][1] - source[(start + (-index if reverse else index)) % count][1]) < 0.02
                for index in range(count)
            ):
                return True
    return False


def parse_xyz(value, name):
    if isinstance(value, dict):
        try:
            return float(value["x"]), float(value["y"]), float(value["z"])
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError(f"Invalid {name}: {value!r}") from error
    if isinstance(value, (list, tuple)) and len(value) == 3:
        return tuple(float(component) for component in value)
    raise ValueError(f"Invalid {name}: expected an XYZ triple, got {value!r}.")


def make_material(name, color, roughness=0.92, emission=False):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeEmission" if emission else "ShaderNodeBsdfPrincipled")
    if emission:
        shader.inputs["Color"].default_value = (*color, 1.0)
        shader.inputs["Strength"].default_value = 1.0
    else:
        shader.inputs["Base Color"].default_value = (*color, 1.0)
        shader.inputs["Roughness"].default_value = roughness
        shader.inputs["Metallic"].default_value = 0.0
    material.node_tree.links.new(shader.outputs[0], output.inputs["Surface"])
    return material


class SurfaceBatch:
    def __init__(self):
        self.vertices = []
        self.faces = []
        self.material_indices = []
        self.materials = []

    def add(self, points, material_index):
        if len(points) < 3:
            return
        area = sum(
            points[i][0] * points[(i + 1) % len(points)][1] -
            points[(i + 1) % len(points)][0] * points[i][1]
            for i in range(len(points))
        )
        if abs(area) < 0.00002:
            return
        ordered = list(reversed(points)) if area > 0 else points
        offset = len(self.vertices)
        self.vertices.extend((float(x), -float(z), float(height)) for x, z, height in ordered)
        self.faces.append(tuple(range(offset, offset + len(ordered))))
        self.material_indices.append(material_index)

    def add_line_ribbon(self, line, half_width, material_index, height):
        if len(line) < 2:
            return
        left, right = [], []
        for index, point in enumerate(line):
            before = line[max(0, index - 1)]
            after = line[min(len(line) - 1, index + 1)]
            dx, dz = after[0] - before[0], after[1] - before[1]
            length = math.hypot(dx, dz)
            if length < 1e-6:
                continue
            # Native CityUrbanContext uses a bounded miter at OSM road vertices.
            nx, nz = -dz / length, dx / length
            left.append((point[0] + nx * half_width, point[1] + nz * half_width))
            right.append((point[0] - nx * half_width, point[1] - nz * half_width))
        for index in range(1, len(left)):
            self.add(
                [(*left[index - 1], height), (*left[index], height),
                 (*right[index], height), (*right[index - 1], height)],
                material_index,
            )

    def add_coastal_slope(self, line, width, material_index):
        offset_line = []
        for index, point in enumerate(line):
            before = line[max(0, index - 1)]
            after = line[min(len(line) - 1, index + 1)]
            dx, dz = after[0] - before[0], after[1] - before[1]
            length = math.hypot(dx, dz)
            if length < 1e-6:
                offset_line.append(point)
                continue
            # With the native south-to-north coastline order, this right-hand normal points inland.
            offset_line.append((point[0] + dz / length * width,
                                point[1] - dx / length * width))
        for index in range(1, len(line)):
            self.add([
                (line[index - 1][0], line[index - 1][1], -0.10),
                (line[index][0], line[index][1], -0.10),
                (offset_line[index][0], offset_line[index][1], GROUND_Z),
                (offset_line[index - 1][0], offset_line[index - 1][1], GROUND_Z),
            ], material_index)

    def add_coastal_water_band(self, line, width, material_index, height):
        sea_line = []
        for index, point in enumerate(line):
            before = line[max(0, index - 1)]
            after = line[min(len(line) - 1, index + 1)]
            dx, dz = after[0] - before[0], after[1] - before[1]
            length = math.hypot(dx, dz)
            if length < 1e-6:
                sea_line.append(point)
                continue
            sea_line.append((point[0] - dz / length * width,
                             point[1] + dx / length * width))
        for index in range(1, len(line)):
            self.add([
                (*line[index - 1], height), (*line[index], height),
                (*sea_line[index], height), (*sea_line[index - 1], height),
            ], material_index)

    def object(self, name, materials):
        mesh = bpy.data.meshes.new(f"{name}_Mesh")
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.materials.clear()
        for material in materials:
            mesh.materials.append(material)
        mesh.update()
        for polygon, index in zip(mesh.polygons, self.material_indices):
            polygon.material_index = index
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        # CityUrbanContext's native static meshes are built without shadow casting.
        obj.visible_shadow = False
        return obj


class VolumeChunk:
    """Chunk-batched OSM roof and wall polygons; all building geometry remains source-shaped."""

    def __init__(self):
        self.vertices, self.faces, self.material_indices = [], [], []

    def add(self, outline, height, wall_material=0, roof_material=1):
        area = sum(
            outline[i][0] * outline[(i + 1) % len(outline)][1]
            - outline[i][1] * outline[(i + 1) % len(outline)][0]
            for i in range(len(outline))
        )
        order = list(reversed(range(len(outline)))) if area > 0 else list(range(len(outline)))
        offset = len(self.vertices)
        # Match CityUrbanContext.AddExtrudedBuilding: walls start at native GroundY.
        # The former footprint-decal lift made these volumes float above the sourced
        # ground/roads and cast long detached black shadows in the close preview.
        base = GROUND_Z
        for x, z in outline:
            self.vertices.extend(((x, -z, base), (x, -z, base + height)))
        for i, a in enumerate(order):
            b = order[(i + 1) % len(order)]
            self.faces.append((offset + a * 2, offset + b * 2,
                               offset + b * 2 + 1, offset + a * 2 + 1))
            self.material_indices.append(wall_material)
        self.faces.append(tuple(offset + i * 2 + 1 for i in order))
        self.material_indices.append(roof_material)


def add_osm_building_volumes(volumes):
    chunk_size = 40.0
    chunks = {}
    for volume in volumes:
        outline, height = volume["outline"], volume["height"]
        center_x = sum(point[0] for point in outline) / len(outline)
        center_z = sum(point[1] for point in outline) / len(outline)
        key = (math.floor(center_x / chunk_size), math.floor(center_z / chunk_size))
        chunks.setdefault(key, VolumeChunk()).add(outline, height)
    building_concrete = (0x85 / 255.0, 0x87 / 255.0, 0x80 / 255.0)
    wall = make_material("OSM outline walls • source concrete #858780", building_concrete)
    roof = make_material("OSM outline roofs • source concrete #858780", building_concrete)
    for (x, z), chunk in chunks.items():
        mesh = bpy.data.meshes.new(f"OSMVolumeChunk_{x}_{z}_ActualOutlines")
        mesh.from_pydata(chunk.vertices, [], chunk.faces)
        mesh.materials.append(wall)
        mesh.materials.append(roof)
        mesh.update()
        for polygon, material_index in zip(mesh.polygons, chunk.material_indices):
            polygon.material_index = material_index
        obj = bpy.data.objects.new(f"OSM actual-outline volumes • chunk {x},{z}", mesh)
        bpy.context.scene.collection.objects.link(obj)
        # Match the production context batches: receive scene light, but do not cast
        # individual city-volume shadows onto roads or adjacent land polygons.
        obj.visible_shadow = False


def projected(points):
    return [(float(point["x"]), float(point["z"])) for point in points]


def skipped_area(kind):
    value = (kind or "").lower()
    return any(term in value for term in ("water", "river", "basin", "beach", "shore", "sea", "military"))


def open_land(kind):
    value = (kind or "").lower()
    return any(term in value for term in ("park", "garden", "grass", "recreation", "green"))


def major_road(kind):
    value = (kind or "").lower()
    return any(term in value for term in ("primary", "secondary", "tertiary", "trunk", "motorway"))


def actual_bounds(data):
    extent = data.get("actualBounds") or {}
    keys = ("minX", "maxX", "minZ", "maxZ")
    if all(key in extent for key in keys):
        return tuple(float(extent[key]) for key in keys)
    points = []
    for road in data["roads"]:
        points.extend(projected(road["points"]))
    for building in data["buildings"]:
        points.extend(projected(building["outline"]))
    for area in data["areas"]:
        points.extend(projected(area["points"]))
    return (min(p[0] for p in points), max(p[0] for p in points),
            min(p[1] for p in points), max(p[1] for p in points))


def add_backdrop(bounds, coast, material):
    min_x, max_x, min_z, max_z = bounds
    # The backdrop follows the native coastline and the projected data extent; no rectangular
    # ocean-side floor or extruded map plinth is introduced.
    padding = 1.0
    batch = SurfaceBatch()
    land_boundary = [(*point, GROUND_Z) for point in coast]
    land_boundary.extend([
        (max_x + padding, coast[-1][1], GROUND_Z),
        (max_x + padding, min_z - padding, GROUND_Z),
    ])
    batch.add(land_boundary, 0)
    batch.object("Coastline-bounded matte terrain", [material])


def add_source_surfaces(data, bounds, coast):
    urban = make_material("Sourced urban ground • native limestone #ADADA6",
                          (0xAD / 255.0, 0xAD / 255.0, 0xA6 / 255.0))
    green = make_material("Sourced mapped open land • native sage #87917C",
                          (0x87 / 255.0, 0x91 / 255.0, 0x7C / 255.0))
    local_road = make_material("OSM local roads • native asphalt #636663",
                               (0x63 / 255.0, 0x66 / 255.0, 0x63 / 255.0))
    main_road = make_material("OSM major routes • native asphalt #474744",
                              (0x47 / 255.0, 0x47 / 255.0, 0x44 / 255.0))
    water = make_material("Coastal sea • blue #205A78", (0.125, 0.353, 0.471))
    shallow_water = make_material("Coastal shallows • blue-green #4E969F", (0.306, 0.588, 0.624))
    foam = make_material("Coastline foam", (0.73, 0.81, 0.81))
    sand = make_material("Narrow native-coast margin", (0.59, 0.53, 0.42))
    background = make_material("Coastline-bounded native limestone #ADADA6",
                               (0xAD / 255.0, 0xAD / 255.0, 0xA6 / 255.0))
    add_backdrop(bounds, coast, background)

    area_batch = SurfaceBatch()
    area_batch.materials = [urban, green]
    for area in data["areas"]:
        if skipped_area(area.get("kind")):
            continue
        area_batch.add([(*point, GROUND_Z + 0.006) for point in projected(area["points"])],
                       1 if open_land(area.get("kind")) else 0)
    area_batch.object("OSM_sourced_land_area_polygons", [urban, green])

    road_batch = SurfaceBatch()
    road_batch.materials = [local_road, main_road]
    for road in data["roads"]:
        width = float(road["width"])
        if width <= 0 or not math.isfinite(width):
            raise ValueError(f"Invalid OSM road width for way {road.get('id')!r}.")
        road_batch.add_line_ribbon(
            projected(road["points"]), width * 0.5,
            1 if width >= 0.55 or major_road(road.get("kind")) else 0,
            GROUND_Z + 0.055,
        )
    road_batch.object("OSM_road_ribbons • sourced widths", [local_road, main_road])

    min_x, max_x, min_z, max_z = bounds
    west_edge = min_x - 45.0
    sea_level = -0.12
    sea_polygon = [(*point, sea_level) for point in coast]
    sea_polygon.extend([(west_edge, coast[-1][1], sea_level),
                        (west_edge, coast[0][1], sea_level)])
    sea_batch = SurfaceBatch()
    sea_batch.add(sea_polygon, 0)
    sea_batch.object("Sea west of native projected coastline", [water])

    shallows_batch = SurfaceBatch()
    shallows_batch.add_coastal_water_band(coast, 4.0, 0, sea_level + 0.001)
    shallows_batch.object("Shallow-water coastal band from native coastline", [shallow_water])

    coast_batch = SurfaceBatch()
    coast_batch.add_line_ribbon(coast, 0.11, 0, -0.098)
    coast_batch.object("Native coastline • soft foam edge", [foam])

    shore_batch = SurfaceBatch()
    shore_batch.add_coastal_slope(coast, 2.0, 0)
    shore_batch.object("Native coastline • 2-unit sand transition", [sand])


def import_city_model(key, models_dir):
    obj_path = require_file(models_dir / f"{key}.obj", f"{key} LOD OBJ")
    albedo_path = require_file(models_dir / f"{key.split('_context')[0]}_albedo.png",
                               f"{key} original albedo PNG")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.wm.obj_import(filepath=str(obj_path), forward_axis="NEGATIVE_Z", up_axis="Y")
    imported = [obj for obj in bpy.context.selected_objects if obj.type == "MESH"]
    if not imported:
        raise RuntimeError(f"OBJ importer found no mesh geometry in {obj_path}.")
    bpy.context.view_layer.objects.active = imported[0]
    if len(imported) > 1:
        bpy.ops.object.join()
    model = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    model.name = f"Imported_{key}_UVNormals"
    if not model.data.vertices or not model.data.polygons:
        raise RuntimeError(f"Imported {key} model has no usable geometry.")
    model.data.calc_loop_triangles()
    if key in MODEL_KEYS and len(model.data.loop_triangles) > 450:
        raise RuntimeError(
            f"Imported {key} source LOD has {len(model.data.loop_triangles)} triangles; "
            "the native context budget is 450."
        )
    coords = [vertex.co.copy() for vertex in model.data.vertices]
    minimum = Vector((min(point.x for point in coords), min(point.y for point in coords),
                      min(point.z for point in coords)))
    maximum = Vector((max(point.x for point in coords), max(point.y for point in coords),
                      max(point.z for point in coords)))
    unity_min = Vector((minimum.x, minimum.z, -maximum.y))
    unity_max = Vector((maximum.x, maximum.z, -minimum.y))
    size = unity_max - unity_min
    if min(size) <= 1e-7:
        raise RuntimeError(f"Imported {key} has invalid source bounds {tuple(size)}.")
    image = bpy.data.images.load(str(albedo_path), check_existing=True)
    image.pack()
    material = make_material(f"{key} • original mapped albedo", (1.0, 1.0, 1.0), 0.92)
    nodes = material.node_tree.nodes
    principled = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = f"{key}_original_albedo_UV"
    texture.image = image
    material.node_tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    model.data.materials.clear()
    model.data.materials.append(material)
    for polygon in model.data.polygons:
        polygon.material_index = 0
    model.hide_render = True
    model.hide_viewport = True
    return {
        "key": key, "mesh": model.data, "material": material,
        "min": unity_min, "size": size, "triangle_count": len(model.data.loop_triangles),
    }


def instantiate(model, name, position, footprint, yaw, max_height,
                footprint_is_local, footprint_yaw=None):
    bounds = model["size"]
    if footprint_yaw is None:
        footprint_yaw = yaw
    local_yaw = math.radians(footprint_yaw)
    cosine, sine = abs(math.cos(local_yaw)), abs(math.sin(local_yaw))
    rotated_width = bounds.x if footprint_is_local else cosine * bounds.x + sine * bounds.z
    rotated_depth = bounds.z if footprint_is_local else sine * bounds.x + cosine * bounds.z
    scale = min(footprint[0] / rotated_width, footprint[2] / rotated_depth,
                max_height / bounds.y)
    if not math.isfinite(scale) or scale <= 0:
        raise ValueError(f"Invalid native scale for {name}.")
    center = model["min"] + bounds * 0.5
    yaw_radians = math.radians(yaw)
    local_shift = Vector((-center.x * scale, center.z * scale, -model["min"].y * scale))
    rotated_shift = Vector((local_shift.x * math.cos(yaw_radians) - local_shift.y * math.sin(yaw_radians),
                            local_shift.x * math.sin(yaw_radians) + local_shift.y * math.cos(yaw_radians),
                            local_shift.z))
    instance = bpy.data.objects.new(name, model["mesh"])
    bpy.context.scene.collection.objects.link(instance)
    instance.location = (position[0] + rotated_shift.x,
                         -position[2] + rotated_shift.y,
                         position[1] + rotated_shift.z)
    instance.rotation_euler.z = yaw_radians
    instance.scale = (scale, scale, scale)
    instance.visible_shadow = False
    return instance


class DotNetRandom:
    """Unity/Mono System.Random subtractive sequence used by native stage-0 rubble scatter."""

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
        self.inext, self.inextp = 0, 21

    def next_double(self):
        self.inext = (self.inext + 1) % 56
        if self.inext == 0:
            self.inext = 1
        self.inextp = (self.inextp + 1) % 56
        if self.inextp == 0:
            self.inextp = 1
        value = self.seed_array[self.inext] - self.seed_array[self.inextp]
        if value == self.MBIG:
            value -= 1
        if value < 0:
            value += self.MBIG
        self.seed_array[self.inext] = value
        return value / self.MBIG


def add_stage_zero_shuja_models(models, plots):
    ruin = models["ruined_building"]
    rubble = models["rubble_heap"]
    for index, plot in enumerate(plots):
        position = parse_xyz(plot["worldPosition"], "Shuja'iyya plot position")
        size = parse_xyz(plot["previewFootprint"], "stage-zero ruin footprint")
        yaw = float(plot["yaw"])
        instantiate(
            ruin, f"Native_shuja_stage0_ruin_{index + 1}_{plot['sourceBuildingId']}",
            position, size, yaw, float(plot["previewMaxHeight"]),
            False, footprint_yaw=0.0,
        )
        parent_yaw = math.radians(yaw)
        footprint_x = float(parse_xyz(plot["size"], "native Shuja'iyya parcel size")[0]) * 0.82
        footprint_z = float(parse_xyz(plot["size"], "native Shuja'iyya parcel size")[2]) * 0.8
        random = DotNetRandom(index * 31)
        for cluster in range(3):
            width = footprint_x * (0.2 + random.next_double() * 0.12)
            depth = footprint_z * (0.2 + random.next_double() * 0.12)
            max_x = max(0.0, footprint_x * 0.5 - width * 0.5 - 0.12)
            max_z = max(0.0, footprint_z * 0.5 - depth * 0.5 - 0.12)
            local_x = (random.next_double() * 2.0 - 1.0) * max_x
            local_z = (random.next_double() * 2.0 - 1.0) * max_z
            rubble_yaw = random.next_double() * 360.0
            world_position = (
                position[0] + local_x * math.cos(parent_yaw) + local_z * math.sin(parent_yaw),
                position[1],
                position[2] - local_x * math.sin(parent_yaw) + local_z * math.cos(parent_yaw),
            )
            instantiate(
                rubble, f"Native_shuja_stage0_rubble_{index + 1}_{cluster + 1}",
                world_position, (width, 0.0, depth), yaw + rubble_yaw,
                min(width, depth), False, footprint_yaw=rubble_yaw,
            )


def point_at(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_lighting(scene):
    sun_data = bpy.data.lights.new("Soft late-morning daylight", "SUN")
    sun_data.energy = 1.35
    sun_data.color = (1.0, 0.985, 0.96)
    sun_data.angle = math.radians(50)
    sun = bpy.data.objects.new("Soft late-morning daylight", sun_data)
    scene.collection.objects.link(sun)
    sun.location = (50, -35, 180)
    point_at(sun, (0, 0, 0))

    for name, color, energy, location, size in (
        ("Cool open-sky fill", (0.6, 0.62, 0.62), 26000, (-30, 10, 420), 380),
        ("Warm ground bounce", (0.4, 0.39, 0.37), 12000, (180, -60, 260), 300),
    ):
        fill_data = bpy.data.lights.new(name, "AREA")
        fill_data.energy = energy
        fill_data.color = color
        fill_data.shape = "DISK"
        fill_data.size = size
        fill = bpy.data.objects.new(name, fill_data)
        scene.collection.objects.link(fill)
        fill.location = location
        point_at(fill, (0, 0, 0))

    world = bpy.data.worlds.new("Neutral soft daylight") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.76, 0.82, 0.89, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55


def setup_camera(scene, name, target, ortho_scale, position_offset):
    data = bpy.data.cameras.new(name)
    data.type = "ORTHO"
    data.sensor_fit = "HORIZONTAL"
    data.ortho_scale = ortho_scale
    camera = bpy.data.objects.new(name, data)
    scene.collection.objects.link(camera)
    camera.location = (target[0] + position_offset[0], target[1] + position_offset[1],
                       target[2] + position_offset[2])
    point_at(camera, target)
    scene.camera = camera
    return camera


def configure_render(scene):
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
    scene["preview_caption"] = "OSM-sourced offline city layout • " + OSM_CAPTION
    scene["preview_source"] = "GazaBasemap schema 1; native production context placement export"


def render_one(scene, camera, output_path):
    output_path = output_path.expanduser().resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene.camera = camera
    scene.render.filepath = str(output_path)
    bpy.ops.render.render(write_still=True)
    require_file(output_path, "rendered city preview PNG")
    print(f"Rendered {output_path}")


def render(options):
    basemap_path = Path(options.basemap).expanduser().resolve()
    fixture_path = Path(options.placements).expanduser().resolve()
    models_dir = Path(options.models_dir).expanduser().resolve()
    data, origin, units = load_basemap(basemap_path)
    placements, project_ids, shujaiya_plots, volumes, native_export = read_native_placements(
        fixture_path, data
    )
    bounds = actual_bounds(data)
    coast = load_native_coast(Path(options.geography).expanduser().resolve(), origin, units, bounds)
    overview_path = Path(options.out_overview)
    neighborhood_path = Path(options.out_neighborhood)
    if overview_path.resolve() == neighborhood_path.resolve():
        raise ValueError("Overview and neighborhood previews must have separate output paths.")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    configure_render(scene)
    add_source_surfaces(data, bounds, coast)
    add_osm_building_volumes(volumes)
    model_keys = sorted(MODEL_KEYS | {"ruined_building", "rubble_heap"})
    models = {key: import_city_model(key, models_dir) for key in model_keys}
    for placement in placements:
        instantiate(
            models[placement["model"]],
            f"Native_context_LOD_{placement['model']}_{placement['id']}",
            placement["position"], placement["size"], placement["yaw"], placement["height"],
            True,
        )
    add_stage_zero_shuja_models(models, shujaiya_plots)
    add_lighting(scene)

    min_x, max_x, min_z, max_z = bounds
    view_min_x = min(min_x, min(point[0] for point in coast) - 45.0)
    center_x, center_z = (view_min_x + max_x) * 0.5, (min_z + max_z) * 0.5
    width, depth = max_x - view_min_x, max_z - min_z
    overview_scale = max(width, depth * (4.0 / 3.0)) * 1.08
    overview_camera = setup_camera(
        scene, "GazaCityOverview_Camera", (center_x, -center_z, 0.0),
        overview_scale, (-width * 0.52, -depth * 0.60, max(width, depth) * 1.35),
    )
    render_one(scene, overview_camera, overview_path)

    site_centers = [parse_xyz(plot["worldPosition"], "Shuja'iyya plot position")
                    for plot in shujaiya_plots]
    neighborhood_x = sum(position[0] for position in site_centers) / len(site_centers)
    neighborhood_z = sum(position[2] for position in site_centers) / len(site_centers)
    site_radius = max(
        math.hypot(position[0] - neighborhood_x, position[2] - neighborhood_z)
        for position in site_centers
    )
    frame_radius = max(6.0, min(42.5, site_radius + 2.5))
    neighborhood_scale = frame_radius * 2.0
    close_camera = setup_camera(
        scene, "ShujaiyaUrbanContext_Camera", (neighborhood_x, -neighborhood_z, 0.0),
        neighborhood_scale, (-neighborhood_scale * 0.62, -neighborhood_scale * 0.78,
                             neighborhood_scale * 1.35),
    )
    render_one(scene, close_camera, neighborhood_path)
    print(
        f"Source: schema-1 OSM GazaBasemap ({len(data['roads'])} roads, "
        f"{len(volumes)} chunk-batched source-height OSM outline volumes, "
        f"{len(data['areas'])} land areas); "
        f"{len(placements)} exact native context LOD placements and "
        f"{len(project_ids)} selected native project parcels from {fixture_path}."
    )
    print(
        f"Shuja'iyya frame radius {frame_radius:.1f}u (orthographic width "
        f"{neighborhood_scale:.1f}u); nine production stage-0 high-detail ruins and native rubble."
    )
    print(
        f"Production context estimate: {native_export.get('estimatedContextTriangles')} triangles; "
        f"authored copies: {native_export.get('authoredModelCopyCount')}; "
        f"nonduplicated building volumes: {len(volumes)}."
    )
    print("Render: Cycles CPU, 16 samples, four threads, 1024x768; " + OSM_CAPTION)


def main():
    render(cli_arguments())


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"render_gaza_city_preview.py: ERROR: {error}", file=sys.stderr)
        raise