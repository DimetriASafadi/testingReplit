#!/usr/bin/env python3
"""Fetch and build an offline, OSM-derived Gaza City contextual basemap.

Example:
  python3 tools/fetch_gaza_basemap.py \
    --outpath testingReplic/Assets/NewGaza/Resources/GazaBasemap.json

The public OSM API is queried sequentially in small, cached tiles.  Existing
tiles are reused on later runs; --refresh explicitly replaces them.  The
output uses the local projection in GameGeography.cs (+X east, +Z north,
origin 31.515 N, 34.45 E, 50 Unity units/km).
"""

import argparse
import datetime
import gzip
import hashlib
import io
import json
import math
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


DEFAULT_BOUNDS = (31.475, 34.402, 31.562, 34.490)  # south, west, north, east
DEFAULT_OUTPUT = Path("testingReplic/Assets/NewGaza/Resources/GazaBasemap.json")
DEFAULT_SOURCE = Path("testingReplic/MapData/GazaBasemap-source.json.gz")
DEFAULT_METADATA = Path("testingReplic/MapData/GazaBasemap.metadata.json")
DEFAULT_CACHE = Path("testingReplic/MapData/cache")
API_URL = "https://api.openstreetmap.org/api/0.6/map"
USER_AGENT = (
    "NewGazaOfflineBasemap/1.0 "
    "(OSM public API; ODbL attribution: https://www.openstreetmap.org/copyright)"
)
ORIGIN_LAT = 31.515
ORIGIN_LON = 34.45
KM_PER_DEGREE = 111.32
UNITS_PER_KM = 50.0
UNITS_PER_METRE = UNITS_PER_KM / 1000.0
PREEXISTING_WAY_IDS = [
    347996618, 1223815759, 1217458515, 29872441,
    1012764380, 1012764378, 948504169, 959773881,
    1008649842, 1008649846, 593764276, 41254253,
    41243303, 604412505, 1217398036, 1217398041,
]
# These are representative points only; they are not neighbourhood polygons.
DISTRICTS = [
    ("shujaiya", 31.4979729, 34.4726451),
    ("tuffah", 31.5158861, 34.4693028),
    ("sheikh-radwan", 31.5321920, 34.4667695),
    ("daraj", 31.5164710, 34.4655040),
    ("karama", 31.5487639, 34.4648283),
    ("old-city", 31.5050311, 34.4641381),
    ("nasr", 31.5340280, 34.4596056),
    ("sabra", 31.5071510, 34.4519234),
    ("zeitoun", 31.4879035, 34.4445419),
    ("rimal", 31.5200000, 34.4431000),
    ("tel-al-hawa", 31.5046790, 34.4349021),
    ("sheikh-ijlin", 31.5044915, 34.4237360),
]
ROAD_WIDTH_METRES = {
    "motorway": 16.0, "motorway_link": 8.0, "trunk": 12.0,
    "trunk_link": 7.0, "primary": 10.0, "primary_link": 7.0,
    "secondary": 8.0, "secondary_link": 6.0, "tertiary": 6.5,
    "tertiary_link": 5.0, "unclassified": 5.0, "residential": 5.0,
    "living_street": 4.0, "service": 3.5, "pedestrian": 3.0,
    "cycleway": 2.0, "footway": 1.8, "path": 1.5, "track": 2.5,
    "steps": 1.5, "bridleway": 2.0,
}


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--outpath", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--source-out", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--metadata-out", type=Path, default=DEFAULT_METADATA)
    parser.add_argument("--cache-dir", type=Path, default=DEFAULT_CACHE)
    parser.add_argument(
        "--bbox", nargs=4, type=float, metavar=("SOUTH", "WEST", "NORTH", "EAST"),
        default=DEFAULT_BOUNDS, help="WGS84 bounds (defaults to requested Gaza City area).",
    )
    parser.add_argument("--tile-size", type=float, default=0.029)
    parser.add_argument("--request-delay", type=float, default=1.2)
    parser.add_argument("--max-buildings", type=int, default=12000)
    parser.add_argument("--refresh", action="store_true", help="Redownload cached OSM tiles.")
    parser.add_argument(
        "--source-in", type=Path,
        help="Build from a previously written raw source JSON without network requests.",
    )
    args = parser.parse_args()
    south, west, north, east = args.bbox
    if not (south < north and west < east and -90 <= south and north <= 90
            and -180 <= west and east <= 180):
        parser.error("--bbox must be ordered WGS84 south, west, north, east bounds")
    if args.tile_size <= 0 or args.tile_size > 0.05:
        parser.error("--tile-size must be > 0 and <= 0.05 degrees for safe OSM API requests")
    if args.request_delay < 1.0:
        parser.error("--request-delay must be at least 1 second for the public OSM API")
    if args.max_buildings < 1:
        parser.error("--max-buildings must be positive")
    return args


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def tile_bounds(bounds, tile_size):
    south, west, north, east = bounds
    lat = south
    while lat < north - 1e-12:
        top = min(north, lat + tile_size)
        lon = west
        while lon < east - 1e-12:
            right = min(east, lon + tile_size)
            yield (lat, lon, top, right)
            lon = right
        lat = top


def tile_name(tile):
    return "map_s{:.6f}_w{:.6f}_n{:.6f}_e{:.6f}.osm.gz".format(*tile)


def download_tile(tile, cache_dir, refresh, delay, last_request):
    cache_dir.mkdir(parents=True, exist_ok=True)
    path = cache_dir / tile_name(tile)
    legacy_path = path.with_suffix("")
    if not path.is_file() and legacy_path.is_file():
        # Migrate earlier uncompressed cache entries without another API request.
        path.write_bytes(gzip.compress(legacy_path.read_bytes(), compresslevel=6))
        legacy_path.unlink()
    if path.is_file() and path.stat().st_size and not refresh:
        return [path], last_request
    if last_request[0] is not None:
        wait = delay - (time.monotonic() - last_request[0])
        if wait > 0:
            time.sleep(wait)
    south, west, north, east = tile
    query = urllib.parse.urlencode({
        "bbox": "{:.7f},{:.7f},{:.7f},{:.7f}".format(west, south, east, north)
    })
    request = urllib.request.Request(
        API_URL + "?" + query,
        headers={"User-Agent": USER_AGENT, "Accept": "application/xml"},
    )
    last_request[0] = time.monotonic()
    temporary = path.with_suffix(path.suffix + ".part")
    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            payload = response.read()
        # Verify a complete XML response before committing it to the resumable cache.
        ET.fromstring(payload)
        temporary.write_bytes(gzip.compress(payload, compresslevel=6))
        temporary.replace(path)
        print("Downloaded {} ({:,} bytes)".format(path.name, len(payload)), flush=True)
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        temporary.unlink(missing_ok=True)
        if error.code == 400 and "too many nodes" in detail.lower():
            south, west, north, east = tile
            if north - south < 0.003 or east - west < 0.003:
                raise RuntimeError(
                    "OSM API tile is still over its 50,000-node limit at minimum "
                    "tile size: {}".format(tile)
                ) from error
            middle_lat = (south + north) / 2
            middle_lon = (west + east) / 2
            print(
                "OSM API tile exceeds 50,000 nodes; splitting {} into cached subtiles."
                .format(path.name),
                flush=True,
            )
            subtiles = (
                (south, west, middle_lat, middle_lon),
                (south, middle_lon, middle_lat, east),
                (middle_lat, west, north, middle_lon),
                (middle_lat, middle_lon, north, east),
            )
            paths = []
            for subtile in subtiles:
                child_paths, last_request = download_tile(
                    subtile, cache_dir, refresh, delay, last_request
                )
                paths.extend(child_paths)
            return paths, last_request
        raise RuntimeError(
            "OSM API tile request failed for {}: HTTP {} {}. "
            "Cached tiles remain reusable; rerun later to resume.".format(
                path.name, error.code, detail.strip()
            )
        ) from error
    except (urllib.error.URLError, TimeoutError, ET.ParseError, OSError) as error:
        temporary.unlink(missing_ok=True)
        raise RuntimeError(
            "OSM API tile request failed for {}: {}. Cached tiles remain reusable; "
            "rerun later to resume.".format(path.name, error)
        ) from error
    return [path], last_request


def parse_tiles(paths):
    nodes = {}
    ways = {}
    for path in paths:
        opener = gzip.open if path.suffix == ".gz" else open
        with opener(path, "rb") as stream:
            root = ET.parse(stream).getroot()
        for element in root:
            if element.tag == "node":
                node_id = int(element.attrib["id"])
                record = {
                    "id": node_id,
                    "lat": float(element.attrib["lat"]),
                    "lon": float(element.attrib["lon"]),
                }
                if node_id in nodes and nodes[node_id] != record:
                    raise ValueError("Conflicting OSM node coordinates for {}".format(node_id))
                nodes[node_id] = record
            elif element.tag == "way":
                way_id = int(element.attrib["id"])
                nds = [int(nd.attrib["ref"]) for nd in element.findall("nd")]
                tags = {tag.attrib["k"]: tag.attrib["v"] for tag in element.findall("tag")}
                previous = ways.get(way_id)
                record = {"id": way_id, "nodes": nds, "tags": tags}
                if previous and previous != record:
                    # Tiled snapshots can straddle an edit. Preserve the first complete record.
                    if len(record["nodes"]) > len(previous["nodes"]):
                        ways[way_id] = record
                else:
                    ways[way_id] = record
    return nodes, ways


def source_json_bytes(source):
    return (
        json.dumps(source, ensure_ascii=False, separators=(",", ":")) + "\n"
    ).encode("utf-8")


def write_source(path, payload):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.suffix == ".gz":
        # GzipFile with a fixed mtime and empty filename yields reproducible
        # headers independent of the source path and invocation time.
        target = io.BytesIO()
        with gzip.GzipFile(
            filename="", mode="wb", fileobj=target, compresslevel=9, mtime=0
        ) as compressed:
            compressed.write(payload)
        path.write_bytes(target.getvalue())
    else:
        path.write_bytes(payload)


def read_source(path):
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rb") as stream:
        payload = stream.read()
    raw = json.loads(payload)
    nodes = {int(node["id"]): node for node in raw["nodes"]}
    ways = {int(way["id"]): way for way in raw["ways"]}
    return raw, nodes, ways, payload


def relevant_ways(ways):
    selected = {}
    for way_id, way in ways.items():
        tags = way["tags"]
        if (
            "highway" in tags
            or ("building" in tags and tags["building"] not in ("no", "false"))
            or "landuse" in tags
            or "natural" in tags
            or "leisure" in tags
            or tags.get("amenity") == "park"
        ):
            selected[way_id] = way
    return selected


def make_source_snapshot(nodes, ways, bounds, retrieval_date, source_url, tile_paths, tile_size):
    selected = relevant_ways(ways)
    referenced = {
        node_id for way in selected.values() for node_id in way["nodes"]
    }
    missing = referenced.difference(nodes)
    if missing:
        raise ValueError(
            "OSM map tiles did not include {:,} referenced nodes (e.g. {}). "
            "Retry with smaller --tile-size.".format(len(missing), min(missing))
        )
    return {
        "source": "OpenStreetMap API 0.6 /map",
        "sourceUrl": source_url,
        "requestTiling": {
            "strategy": "sequential cached bbox tiles; tiles split recursively when OSM's 50,000-node limit is reached",
            "maximumInitialTileDegrees": tile_size,
            "successfulTileCount": len(tile_paths),
            "cacheFiles": [path.name for path in tile_paths],
        },
        "retrievalDateUtc": retrieval_date,
        "license": "ODbL-1.0",
        "attribution": "© OpenStreetMap contributors",
        "bbox": {"south": bounds[0], "west": bounds[1], "north": bounds[2], "east": bounds[3]},
        "coordinates": "WGS84 latitude/longitude; raw node coordinates retained",
        "nodes": [nodes[node_id] for node_id in sorted(referenced)],
        "ways": [selected[way_id] for way_id in sorted(selected)],
    }


def project(lat, lon):
    x = (lon - ORIGIN_LON) * KM_PER_DEGREE * math.cos(math.radians(ORIGIN_LAT)) * UNITS_PER_KM
    z = (lat - ORIGIN_LAT) * KM_PER_DEGREE * UNITS_PER_KM
    return float(x), float(z)


def clip_segment(a, b, bounds):
    """Liang-Barsky clip, returning exact interpolated WGS84 endpoint pairs."""
    south, west, north, east = bounds
    x0, y0 = a[1], a[0]
    dx, dy = b[1] - x0, b[0] - y0
    t0, t1 = 0.0, 1.0
    for p, q in ((-dx, x0 - west), (dx, east - x0), (-dy, y0 - south), (dy, north - y0)):
        if abs(p) < 1e-15:
            if q < 0:
                return None
            continue
        ratio = q / p
        if p < 0:
            if ratio > t1:
                return None
            t0 = max(t0, ratio)
        else:
            if ratio < t0:
                return None
            t1 = min(t1, ratio)
    return (
        (y0 + t0 * dy, x0 + t0 * dx),
        (y0 + t1 * dy, x0 + t1 * dx),
    )


def clip_line(coords, bounds):
    parts = []
    current = []
    for first, second in zip(coords, coords[1:]):
        clipped = clip_segment(first, second, bounds)
        if clipped is None:
            if len(current) >= 2:
                parts.append(current)
            current = []
            continue
        start, end = clipped
        if not current or abs(current[-1][0] - start[0]) > 1e-11 or abs(current[-1][1] - start[1]) > 1e-11:
            if len(current) >= 2:
                parts.append(current)
            current = [start]
        if abs(current[-1][0] - end[0]) > 1e-11 or abs(current[-1][1] - end[1]) > 1e-11:
            current.append(end)
    if len(current) >= 2:
        parts.append(current)
    return parts


def clip_polygon(points, bounds):
    """Sutherland-Hodgman rectangle clipping in the source WGS84 plane."""
    south, west, north, east = bounds
    polygon = list(points)
    if len(polygon) > 1 and polygon[0] == polygon[-1]:
        polygon.pop()
    boundaries = (
        (lambda point: point[1] >= west, lambda a, b: intersect_lon(a, b, west)),
        (lambda point: point[1] <= east, lambda a, b: intersect_lon(a, b, east)),
        (lambda point: point[0] >= south, lambda a, b: intersect_lat(a, b, south)),
        (lambda point: point[0] <= north, lambda a, b: intersect_lat(a, b, north)),
    )
    for inside, intersection in boundaries:
        if not polygon:
            break
        output = []
        previous = polygon[-1]
        previous_inside = inside(previous)
        for current in polygon:
            current_inside = inside(current)
            if current_inside:
                if not previous_inside:
                    output.append(intersection(previous, current))
                output.append(current)
            elif previous_inside:
                output.append(intersection(previous, current))
            previous, previous_inside = current, current_inside
        polygon = output
    if len(polygon) < 3:
        return []
    return polygon


def intersect_lon(a, b, lon):
    if abs(b[1] - a[1]) < 1e-15:
        return (a[0], lon)
    ratio = (lon - a[1]) / (b[1] - a[1])
    return (a[0] + ratio * (b[0] - a[0]), lon)


def intersect_lat(a, b, lat):
    if abs(b[0] - a[0]) < 1e-15:
        return (lat, a[1])
    ratio = (lat - a[0]) / (b[0] - a[0])
    return (lat, a[1] + ratio * (b[1] - a[1]))


def oriented_footprint(points):
    """Return the minimum-area edge-oriented OBB for projected x/z vertices.

    Local +Z is forward and local +X is right, matching Unity's yaw rotation:
    forward=(sin(yaw), cos(yaw)); right=(cos(yaw), -sin(yaw)).
    """
    best = None
    for first, second in zip(points, points[1:] + points[:1]):
        dx = second[0] - first[0]
        dz = second[1] - first[1]
        length = math.hypot(dx, dz)
        if length <= 1e-12:
            continue
        forward = (dx / length, dz / length)
        right = (dz / length, -dx / length)
        right_projections = [point[0] * right[0] + point[1] * right[1] for point in points]
        forward_projections = [point[0] * forward[0] + point[1] * forward[1] for point in points]
        min_right, max_right = min(right_projections), max(right_projections)
        min_forward, max_forward = min(forward_projections), max(forward_projections)
        width = max_right - min_right
        depth = max_forward - min_forward
        area = width * depth
        if best is None or area < best["area"] - 1e-12:
            right_mid = (min_right + max_right) / 2
            forward_mid = (min_forward + max_forward) / 2
            best = {
                "area": area,
                "center": {
                    "x": right_mid * right[0] + forward_mid * forward[0],
                    "z": right_mid * right[1] + forward_mid * forward[1],
                },
                "size": {"x": width, "z": depth},
                "yaw": math.degrees(math.atan2(dx, dz)),
            }
    if best is None:
        # Degenerate outlines have no nonzero edge; keep their point geometry
        # usable without manufacturing a rotation.
        return {
            "center": {
                "x": sum(point[0] for point in points) / len(points),
                "z": sum(point[1] for point in points) / len(points),
            },
            "size": {"x": 0.0, "z": 0.0},
            "yaw": 0.0,
        }
    return best


def width_for(tags):
    highway = tags.get("highway", "residential")
    width_tag = tags.get("width", "")
    match = re.match(r"^\s*(\d+(?:\.\d+)?)\s*(?:m|metres?)?\s*$", width_tag, re.I)
    metres = float(match.group(1)) if match else ROAD_WIDTH_METRES.get(highway, 4.0)
    return metres * UNITS_PER_METRE


def building_height(tags):
    height = tags.get("height", "")
    match = re.match(r"^\s*(\d+(?:\.\d+)?)\s*(?:m|metres?)?\s*$", height, re.I)
    levels_value = tags.get("building:levels") or tags.get("levels") or ""
    level_match = re.match(r"^\s*(\d+(?:\.\d+)?)", levels_value)
    levels = max(1, int(round(float(level_match.group(1))))) if level_match else 2
    if match:
        metres = float(match.group(1))
        if not level_match:
            levels = max(1, int(round(metres / 3.0)))
        return metres * UNITS_PER_METRE, levels, "OSM height tag"
    if level_match:
        return float(levels) * 3.0 * UNITS_PER_METRE, levels, "OSM levels tag × 3 m/floor"
    # Approximation only; footprint geometry remains authentic OSM geometry.
    return 6.0 * UNITS_PER_METRE, levels, "fallback 2 floors × 3 m/floor"


def sample_buildings(buildings, max_count, bounds):
    if len(buildings) <= max_count:
        return buildings, False
    south, west, north, east = bounds
    columns = rows = 64
    buckets = defaultdict(list)
    for building in buildings:
        lat, lon = building["_sampleLatLon"]
        column = min(columns - 1, max(0, int((lon - west) / (east - west) * columns)))
        row = min(rows - 1, max(0, int((lat - south) / (north - south) * rows)))
        buckets[(row, column)].append(building)
    for bucket in buckets.values():
        bucket.sort(key=lambda item: item["id"])
    selected = []
    # Round-robin cells by depth: sampling is spatially even, not city-sector biased.
    depth = 0
    while len(selected) < max_count:
        advanced = False
        for key in sorted(buckets):
            if depth < len(buckets[key]):
                selected.append(buckets[key][depth])
                advanced = True
                if len(selected) == max_count:
                    break
        if not advanced:
            break
        depth += 1
    return selected, True


def way_latlon(way, nodes):
    try:
        return [(nodes[node_id]["lat"], nodes[node_id]["lon"]) for node_id in way["nodes"]]
    except KeyError as error:
        raise ValueError("Missing node {} needed by way {}".format(error.args[0], way["id"]))


def point_segment_distance(point, a, b):
    px, pz = point
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    denominator = dx * dx + dz * dz
    t = 0.0 if denominator == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / denominator))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)


def build_basemap(source, nodes, ways, bounds, max_buildings):
    south, west, north, east = bounds
    roads = []
    buildings = []
    areas = []
    graph = defaultdict(set)
    raw_roads = []
    for way_id in sorted(ways):
        way = ways[way_id]
        tags = way["tags"]
        coords = way_latlon(way, nodes)
        if "highway" in tags and len(coords) >= 2:
            projected_parts = []
            for part in clip_line(coords, bounds):
                points = [{"x": project(lat, lon)[0], "z": project(lat, lon)[1]} for lat, lon in part]
                if len(points) >= 2:
                    projected_parts.append(points)
                    roads.append({
                        "id": way_id,
                        "name": tags.get("name", ""),
                        "kind": tags["highway"],
                        "width": width_for(tags),
                        "points": points,
                    })
            if projected_parts:
                raw_roads.append(way)
                inside_nodes = [
                    node_id for node_id in way["nodes"]
                    if south <= nodes[node_id]["lat"] <= north and west <= nodes[node_id]["lon"] <= east
                ]
                for node_id in inside_nodes:
                    graph[node_id].add(way_id)
        if (
            "building" in tags and tags["building"] not in ("no", "false")
            and tags.get("building:part") is None
            and len(coords) >= 4 and way["nodes"][0] == way["nodes"][-1]
        ):
            clipped = clip_polygon(coords, bounds)
            if len(clipped) >= 3:
                outline = [project(lat, lon) for lat, lon in clipped]
                oriented = oriented_footprint(outline)
                height, levels, height_source = building_height(tags)
                buildings.append({
                    "id": way_id,
                    "center": oriented["center"],
                    "size": oriented["size"],
                    "yaw": oriented["yaw"],
                    "height": height,
                    "levels": levels,
                    "outline": [
                        {"x": point[0], "z": point[1]} for point in outline
                    ],
                    "_sampleLatLon": (
                        sum(point[0] for point in clipped) / len(clipped),
                        sum(point[1] for point in clipped) / len(clipped),
                    ),
                    "_heightSource": height_source,
                })
        area_kind = None
        if "landuse" in tags:
            area_kind = "landuse:" + tags["landuse"]
        elif tags.get("natural") in ("beach", "water", "wood", "scrub", "grassland", "wetland"):
            area_kind = "natural:" + tags["natural"]
        elif tags.get("leisure") in ("park", "garden", "pitch", "playground", "nature_reserve"):
            area_kind = "leisure:" + tags["leisure"]
        elif tags.get("amenity") == "park":
            area_kind = "amenity:park"
        if area_kind and len(coords) >= 4 and way["nodes"][0] == way["nodes"][-1]:
            clipped = clip_polygon(coords, bounds)
            if len(clipped) >= 3:
                areas.append({
                    "id": way_id,
                    "kind": area_kind,
                    "points": [
                        {"x": project(lat, lon)[0], "z": project(lat, lon)[1]}
                        for lat, lon in clipped
                    ],
                })

    buildings, buildings_sampled = sample_buildings(buildings, max_buildings, bounds)
    height_fallback_count = sum(
        1 for building in buildings
        if building["_heightSource"].startswith("fallback")
    )
    for building in buildings:
        building.pop("_sampleLatLon", None)
        building.pop("_heightSource", None)

    # Count components of the real, clipped OSM highway-way graph at OSM nodes.
    way_nodes = defaultdict(set)
    for node_id, attached in graph.items():
        for way_id in attached:
            way_nodes[way_id].add(node_id)
    adjacency = defaultdict(set)
    for attached in graph.values():
        for way_id in attached:
            adjacency[way_id].update(attached - {way_id})
    components = 0
    unseen = set(way_nodes)
    largest_component = 0
    while unseen:
        components += 1
        queue = [unseen.pop()]
        count = 0
        while queue:
            current = queue.pop()
            count += 1
            for neighbor in adjacency[current] & unseen:
                unseen.remove(neighbor)
                queue.append(neighbor)
        largest_component = max(largest_component, count)

    coverage = []
    segments = [
        (projected_a, projected_b)
        for road in roads
        for projected_a, projected_b in zip(
            [(point["x"], point["z"]) for point in road["points"]],
            [(point["x"], point["z"]) for point in road["points"]][1:],
        )
    ]
    for district_id, lat, lon in DISTRICTS:
        target = project(lat, lon)
        distance = min(
            (point_segment_distance(target, a, b) for a, b in segments),
            default=None,
        )
        coverage.append({
            "id": district_id,
            "representativePoint": {"latitude": lat, "longitude": lon},
            "nearestRoadMeters": None if distance is None else round(distance / UNITS_PER_METRE, 1),
            "interpretation": "nearest OSM road to representative point; not a district boundary",
        })

    projected_bounds = [
        project(south, west), project(south, east),
        project(north, west), project(north, east),
    ]
    extent = {
        "minX": min(p[0] for p in projected_bounds),
        "maxX": max(p[0] for p in projected_bounds),
        "minZ": min(p[1] for p in projected_bounds),
        "maxZ": max(p[1] for p in projected_bounds),
    }
    def feature_extent(feature_type):
        points = []
        if feature_type == "roads":
            points = [point for road in roads for point in road["points"]]
        elif feature_type == "buildings":
            points = [point for building in buildings for point in building["outline"]]
        else:
            points = [point for area in areas for point in area["points"]]
        if not points:
            return None
        xs = [point["x"] for point in points]
        zs = [point["z"] for point in points]
        return {
            "projected": {
                "minX": min(xs), "maxX": max(xs), "minZ": min(zs), "maxZ": max(zs),
            },
            "wgs84": {
                "minLatitude": ORIGIN_LAT + min(zs) / (KM_PER_DEGREE * UNITS_PER_KM),
                "maxLatitude": ORIGIN_LAT + max(zs) / (KM_PER_DEGREE * UNITS_PER_KM),
                "minLongitude": ORIGIN_LON + min(xs) / (
                    KM_PER_DEGREE * math.cos(math.radians(ORIGIN_LAT)) * UNITS_PER_KM
                ),
                "maxLongitude": ORIGIN_LON + max(xs) / (
                    KM_PER_DEGREE * math.cos(math.radians(ORIGIN_LAT)) * UNITS_PER_KM
                ),
            },
        }
    building_cells = set()
    for building in buildings:
        lat, lon = building["_sampleLatLon"] if "_sampleLatLon" in building else (
            ORIGIN_LAT + building["center"]["z"] / (KM_PER_DEGREE * UNITS_PER_KM),
            ORIGIN_LON + building["center"]["x"] / (
                KM_PER_DEGREE * math.cos(math.radians(ORIGIN_LAT)) * UNITS_PER_KM
            ),
        )
        building_cells.add((
            min(7, max(0, int((lat - south) / (north - south) * 8))),
            min(7, max(0, int((lon - west) / (east - west) * 8))),
        ))
    metadata = {
        "schemaVersion": 1,
        "attribution": "© OpenStreetMap contributors",
        "license": "Open Database License 1.0 (ODbL)",
        "licenseUrl": "https://opendatacommons.org/licenses/odbl/1-0/",
        "copyrightUrl": "https://www.openstreetmap.org/copyright",
        "source": source["source"],
        "sourceUrl": source["sourceUrl"],
        "retrievalDateUtc": source["retrievalDateUtc"],
        "sourceArchive": "testingReplic/MapData/GazaBasemap-source.json",
        "rawCoordinatesPreservedInSourceArchive": True,
        "bbox": source["bbox"],
        "projection": {
            "type": "local equirectangular",
            "originLatitude": ORIGIN_LAT,
            "originLongitude": ORIGIN_LON,
            "kmPerDegree": KM_PER_DEGREE,
            "longitudeScale": "cos(originLatitude)",
            "unitsPerKilometre": UNITS_PER_KM,
            "handedness": "+X east; +Z north",
        },
        "projectedExtent": extent,
        "featureExtents": {
            "roads": feature_extent("roads"),
            "buildings": feature_extent("buildings"),
            "areas": feature_extent("areas"),
        },
        "featureCounts": {
            "roads": len(roads),
            "uniqueRoadWays": len(raw_roads),
            "buildings": len(buildings),
            "areas": len(areas),
            "sourceNodes": len(source["nodes"]),
            "sourceWays": len(source["ways"]),
        },
        "roadNetwork": {
            "connectedComponentsBySharedOSMNode": components,
            "largestComponentWayCount": largest_component,
            "largestComponentShare": (
                largest_component / len(raw_roads) if raw_roads else 0.0
            ),
            "widthApproximation": (
                "OSM width tag in metres when numeric; otherwise per-highway-class "
                "typical widths listed in fetch_gaza_basemap.py; converted at 0.05 units/metre."
            ),
        },
        "buildingApproximation": (
            "OSM closed-way footprint geometry retained/clipped to bbox. Minimum-area "
            "oriented bounding rectangle is derived by testing each outline edge. Its "
            "forward=(dx,dz)/length, right=(dz,-dx)/length; local x/z sizes are projected "
            "ranges and yaw=atan2(dx,dz) in degrees. Center is reconstructed from projection "
            "midpoints; source outline is preserved. Height uses OSM height, then levels×3m, "
            "otherwise an explicitly approximate 2-floor/6m fallback. Height is Unity world units."
        ),
        "buildingHeightFallbackCount": height_fallback_count,
        "buildingCap": {
            "limit": max_buildings,
            "spatiallySampled": buildings_sampled,
            "method": "deterministic 64×64 bbox cells, round-robin by cell and ascending OSM way ID",
            "occupiedCellsIn8x8CoverageGrid": len(building_cells),
            "coverageGridNote": "bbox coverage diagnostic only; not neighbourhood boundaries",
        },
        "districtRepresentativePointCoverage": coverage,
        "preexistingCoastAndRashidWayIds": PREEXISTING_WAY_IDS,
        "destructionCaveat": (
            "OSM features describe mapped geographic objects and are not proof of current "
            "existence, current condition, damage, or destruction."
        ),
        "limitations": (
            "OSM /map supplies ways and nodes, not relation multipolygon assembly. Closed-way "
            "footprints and areas are included; polygon holes/relation-only geometries are not."
        ),
    }
    result = {
        "schemaVersion": 1,
        "metadata": metadata,
        "roads": roads,
        "buildings": buildings,
        "areas": areas,
    }
    return result


def main():
    args = parse_args()
    bounds = tuple(args.bbox)
    if args.source_in:
        source, nodes, ways, source_payload = read_source(args.source_in)
        bounds = (
            source["bbox"]["south"], source["bbox"]["west"],
            source["bbox"]["north"], source["bbox"]["east"],
        )
        print("Building from source snapshot {}.".format(args.source_in))
    else:
        tiles = list(tile_bounds(bounds, args.tile_size))
        paths = []
        last_request = [None]
        for tile in tiles:
            tile_paths, last_request = download_tile(
                tile, args.cache_dir, args.refresh, args.request_delay, last_request
            )
            paths.extend(tile_paths)
        nodes, ways = parse_tiles(paths)
        retrieval_date = datetime.datetime.now(datetime.timezone.utc).date().isoformat()
        source = make_source_snapshot(
            nodes, ways, bounds, retrieval_date,
            API_URL, paths, args.tile_size,
        )
        source_payload = source_json_bytes(source)
        write_source(args.source_out, source_payload)
    result = build_basemap(source, nodes, ways, bounds, args.max_buildings)
    args.outpath.parent.mkdir(parents=True, exist_ok=True)
    args.outpath.write_text(
        json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    source_path = args.source_in if args.source_in else args.source_out
    sidecar = {
        "basemapFile": str(args.outpath),
        "basemapSha256": sha256_file(args.outpath),
        "sourceFile": str(source_path),
        # sourceSha256 is retained as an alias for the distributed file hash.
        "sourceSha256": sha256_file(source_path),
        "sourceCompression": (
            "gzip (mtime=0)" if source_path.suffix == ".gz" else "none"
        ),
        "sourceCompressedSha256": (
            sha256_file(source_path) if source_path.suffix == ".gz" else None
        ),
        "sourceCompressedBytes": (
            source_path.stat().st_size if source_path.suffix == ".gz" else None
        ),
        "sourceUncompressedFile": "GazaBasemap-source.json (canonical decompressed JSON)",
        "sourceUncompressedSha256": hashlib.sha256(source_payload).hexdigest(),
        "sourceUncompressedBytes": len(source_payload),
        "retrievalDateUtc": source["retrievalDateUtc"],
        "attribution": "© OpenStreetMap contributors",
        "license": "ODbL-1.0",
        "sourceWays": len(source["ways"]),
        "sourceNodes": len(source["nodes"]),
        "featureCounts": result["metadata"]["featureCounts"],
    }
    args.metadata_out.parent.mkdir(parents=True, exist_ok=True)
    args.metadata_out.write_text(
        json.dumps(sidecar, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    counts = result["metadata"]["featureCounts"]
    print(
        "Wrote {}: {:,} road segments ({} OSM ways), {:,} buildings, {:,} areas; "
        "{} connected road components.".format(
            args.outpath, counts["roads"], counts["uniqueRoadWays"],
            counts["buildings"], counts["areas"],
            result["metadata"]["roadNetwork"]["connectedComponentsBySharedOSMNode"],
        )
    )
    print("Metadata/checksums: {}".format(args.metadata_out))
    for item in result["metadata"]["districtRepresentativePointCoverage"]:
        print("  {}: nearest road {:.0f} m".format(item["id"], item["nearestRoadMeters"]))


if __name__ == "__main__":
    main()