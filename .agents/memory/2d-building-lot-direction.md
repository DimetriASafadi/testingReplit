---
name: Building parcel art direction
description: User-approved small-building yards, four faithful building faces, coherent ground seams and one sunlight direction.
---

The buildable square is a land parcel, not a requirement to enlarge every building to fill it. Small buildings need suitably furnished surroundings; larger housing should occupy more of the parcel. Parks, sports fields and farms should use most of their allotted land, rather than appearing as small central stamps.

**Why:** The user repeatedly explained this distinction and explicitly approved the planting around the small house: «شفتك وضعت زرع حوله وطلع جميل جدًا». On 2026-11-02 they requested a consistent parcel-based sizing standard across buildings/farms/gardens, specifically identifying the park and western-food restaurant as wrongly proportioned.

**How to apply:** Preserve that accepted small-house yard approach and adapt accessories to each building's use, rather than putting generic gardens around every industry or shop. Fit the ground footprint in both axes, independently of image resolution and structural height. Judge height above the rear ground edge, not total PNG height (which includes the land diamond). Keep a shared scale across all faces and use that same geometry for construction, shadows and factory entrances.

## Architectural size is separate from parcel coverage

Establish a human-scale reference using visible facade floors and doors. Plot coverage is an upper bound, not the desired scale of every building; smaller structures need landscaped/functional yards using the remainder of the square.

**Why:** The user repeated on 2026-11-02 that the three-floor traditional building must not dwarf higher apartment blocks. Equalizing PNG/platform widths retained wildly different native floor sizes. They explicitly asked to fill the whole lot with either an appropriately large building or a properly furnished courtyard, not inflate small buildings.

**How to apply:** Calibrate new art against neighboring housing using actual floor/door spacing, not source bounds or catalog floor counts alone. Reserve the actual scaled building footprint before placing beds, seating, parking and entry paths. Bake furniture into bounded shared yard textures, not live per-frame scene objects. Inspect mixed types adjacent in the real plot renderer at phone scale; isolated image comparisons missed the original inconsistency.

Use the whole available land parcel around the building, not only the small painted platform attached to its sprite.

**Why:** The user repeated this request after the initial landscaping update, showing an apartment and villa with large empty bands between their furnished platforms and the roads.

**How to apply:** Judge coverage in the actual city against neighboring roads; use parking, planting and service areas appropriate to the building while leaving entrance routes clear.

The user expanded rotation from front/rear to four genuine faces of every building. All views must represent the same architecture, floor count, materials and accessories, not mirrored pictures. Ground must blend into the district, and sunlight must not turn with the building.

**Why:** The user identified inconsistent sizes, detached-looking floor platforms and opposing shadows in screenshots, and specifically asked for all building types, not only those pictured.

**How to apply:** Compare all four poses in the actual city with neighboring roads and lots, not only isolated previews; rotation changes visible faces, not sunlight. Preserve old saved front/rear meanings when extending the orientation format.

**Why preserve saved meanings:** Players already saved rear-facing buildings before quarter-turn views existed. Re-numbering directions sequentially would silently turn their existing buildings to a different face.
