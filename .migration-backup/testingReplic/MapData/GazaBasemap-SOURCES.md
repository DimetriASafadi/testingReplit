# Gaza City offline basemap data

## Snapshot and geographic scope

`../Assets/NewGaza/Resources/GazaBasemap.json` is a generated OSM-derived
vector contextual basemap clipped to **31.475–31.562° N, 34.402–34.490° E**.
It covers the requested Gaza City neighborhoods, including Karama and the
coast; it is not a Gaza Strip map. The acquisition used the public OSM 0.6
`/map` endpoint on **2026-10-01 UTC**. The complete source records needed to
rebuild these geometries (OSM way IDs/tags/node references and raw WGS84 node
latitudes and longitudes) are distributed in the reproducibly compressed
`GazaBasemap-source.json.gz`.

The Overpass attempt was unavailable (gateway timeout), so the generator uses
small, sequential OSM API bbox requests. It recursively splits a tile if it
exceeds the API's 50,000-node request limit and stores successful responses in
`cache/` for resumable runs. The compressed source snapshot supports a fully
offline rebuild without distributing those redundant tile-cache files:

```sh
python3 tools/fetch_gaza_basemap.py \
  --source-in testingReplic/MapData/GazaBasemap-source.json.gz \
  --outpath testingReplic/Assets/NewGaza/Resources/GazaBasemap.json
```

To fetch a fresh snapshot instead, omit `--source-in`; use `--refresh` only
when intentionally replacing cached API tiles. New source snapshots default
to deterministic gzip output (`mtime=0`). `MapData/.gitignore` excludes the
uncompressed canonical source JSON and downloaded `cache/` tiles; only the
compressed source archive is distributed. All implementation and geometry
transformation uses the Python standard library. The generated
`GazaBasemap.metadata.json` records the compressed source SHA-256 separately
from the SHA-256 of its original decompressed JSON payload, along with output
checksum, retrieval date, attribution, feature counts, tile count, extents,
projection, network connectivity, and representative-point coverage. The
runtime JSON's `sourceArchive` value names the canonical uncompressed JSON
payload; its distributed gzip file is identified by `sourceFile` in the
sidecar.

The sidecar's `sourceCompressedSha256` hashes the distributed `.json.gz`
bytes. `sourceUncompressedSha256` hashes the exact canonical UTF-8 JSON
payload after decompression (including its final newline); the uncompressed
size and the gzip file size are reported separately. `sourceSha256` is retained
as a compatibility alias for the compressed-file hash.

Snapshot validation: **4,032 clipped road segments from 4,027 OSM ways,
12,000 spatially sampled building footprints, and 528 areas/open spaces**.
The source archive preserves **370,638 referenced OSM nodes and 74,251 tagged
ways**. The clipped vector extents reach the requested bbox edges:
31.475–31.562° N and 34.402–34.490° E (projected x −227.762 to +189.801,
z −222.640 to +261.602). The shared-node highway graph has 36 components;
its largest component contains 3,920 ways (97.3% of retained road ways).
The building sample occupies 48 of 64 cells in an 8×8 bbox coverage
diagnostic. Nearest-road distances to the 12 existing representative points
range from 0 to 45 metres; the nearest distances are recorded individually
in the map JSON. These are point checks, not invented district boundaries.

## Contents and geometry

- Roads: OSM `highway=*` ways, clipped to the exact requested bbox. `id` is
  the original OSM way ID; `points` are connected segments sampled from the
  original way geometry, not invented links. Roads are retained across the
  whole requested area. Measured `width=*` is used if numeric; otherwise
  typical widths by highway class are approximations: motorway 16 m, trunk
  12 m, primary 10 m, secondary 8 m, tertiary 6.5 m, residential/unclassified
  5 m, service 3.5 m, footway 1.8 m, and path 1.5 m. Widths are projected at
  0.05 Unity units per metre; the complete class table is in the generator.
- Buildings: actual closed OSM `building=*` way footprints, clipped to the
  bbox. If more than 12,000 are available, deterministic round-robin
  sampling across a 64×64 spatial grid avoids dropping a whole city sector.
  `outline` is the clipped footprint. `center`, local x/z `size`, and `yaw`
  derive from the minimum-area oriented rectangle tested against every
  footprint edge. For the winning edge, forward=`(dx,dz)/length`,
  right=`(dz,-dx)/length`, and yaw=`atan2(dx,dz)` in degrees; projected
  midpoint ranges reconstruct the world-space center. Height uses OSM
  `height`, then `building:levels × 3 m`, then an explicitly marked
  approximate two-floor / 6 m fallback. Dimensions, width, and height use the
  game projection's world units.
- Areas: clipped closed OSM ways tagged with landuse, selected natural
  categories, leisure parks/open spaces, or `amenity=park`.

This API endpoint returns ways and nodes, not relation multipolygon assembly.
Consequently, relation-only geometries, polygon holes, and buildings mapped
only as relations are not represented. Raw source records preserve original
OSM node latitude/longitude; the Unity-friendly output stores projected
coordinates only.

## Projection and coverage notes

The projection matches `Assets/NewGaza/Core/GameGeography.cs` exactly:
origin **31.515 N, 34.45 E**, 111.32 km per degree, longitude multiplied by
`cos(31.515°)`, 50 Unity units per kilometre, **+X east / +Z north**. No
neighborhood boundaries are asserted. The 12 coverage checks are distances
from OSM roads to the existing representative points only, not district
coverage polygons. Connectivity counts use shared OSM nodes; roads need not
all belong to one component.

OSM mapping is not proof that a mapped object currently exists or of its
condition, damage, or destruction. This data is for contextual display only,
not navigation, surveying, or property decisions.

## OSM identifiers already used by the coast / Rashid source

The pre-existing coast and Rashid generalized paths reference ways
`347996618`, `1223815759`, `1217458515`, `29872441`, `1012764380`,
`1012764378`, `948504169`, `959773881`, `1008649842`, `1008649846`,
`593764276`, `41254253`, `41243303`, `604412505`, `1217398036`, and
`1217398041`. These IDs are retained as provenance metadata whether or not an
individual way is still tagged as a highway in the newly fetched snapshot.

## Attribution

**© OpenStreetMap contributors** — [openstreetmap.org/copyright](https://www.openstreetmap.org/copyright).
The OSM-derived source archive and resulting basemap are subject to the
[Open Database License 1.0 (ODbL)](https://opendatacommons.org/licenses/odbl/1-0/);
see `GazaBasemap-LICENSE.md`. The attribution and license do not change the
license of unrelated game code or assets.