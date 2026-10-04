using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Static, sourced city fabric. Presentation radii are derived from building coverage in
    /// nearest-representative-point Voronoi cells; they are not official neighborhood limits.
    /// </summary>
    internal sealed class CityUrbanContext
    {
        internal const int ChunkSize = 40; // ~800m chunks at the source's 50 units/km scale.
        internal const int MaxAuthoredModelCopies = 1200;
        internal const int MaxSourceLodTriangles = 450;
        internal const int MaxContextTriangles = 700000;
        private const float GroundY = -.09f;
        private readonly Dictionary<string, CityUrbanDistrictPresentation> districts =
            new Dictionary<string, CityUrbanDistrictPresentation>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CityUrbanBuildingPresentation>> candidates =
            new Dictionary<string, List<CityUrbanBuildingPresentation>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CityUrbanUtilityPosition>> utilities =
            new Dictionary<string, List<CityUrbanUtilityPosition>>(StringComparer.Ordinal);
        private readonly List<CityUrbanBuildingPresentation> allContext =
            new List<CityUrbanBuildingPresentation>();
        private readonly List<CityUrbanBuildingVolume> extrudedVolumes =
            new List<CityUrbanBuildingVolume>();
        private readonly HashSet<string> projectBuildingIds = new HashSet<string>(StringComparer.Ordinal);
        private CityBasemap sourceMap;
        private Dictionary<ChunkKey, List<RoadSegment>> roadIndex;
        private Dictionary<ChunkKey, List<CityBasemapBuilding>> footprintIndex;
        private int triangleCount;

        internal IList<CityUrbanBuildingPresentation> AllContextBuildings
        {
            get { return allContext.AsReadOnly(); }
        }

        internal int EstimatedContextTriangles { get { return triangleCount; } }

        internal IList<CityUrbanBuildingVolume> ExtrudedBuildingVolumes
        {
            get { return extrudedVolumes.AsReadOnly(); }
        }

        internal CityUrbanDistrictPresentation GetDistrictPresentation(string districtId)
        {
            CityUrbanDistrictPresentation result;
            if (districtId == null || !districts.TryGetValue(districtId, out result))
                throw new ArgumentException("No basemap presentation exists for district id '" +
                    districtId + "'.", nameof(districtId));
            return result;
        }

        /// <summary>Returns deterministic sourced parcels for the caller's unchanged-scale district root.</summary>
        internal IList<CityUrbanPlot> GetDistrictPlots(string districtId, int count)
        {
            CityUrbanDistrictPresentation presentation = GetDistrictPresentation(districtId);
            if (count < 0 || count > presentation.plots.Count)
                throw new ArgumentOutOfRangeException(nameof(count),
                    "Requested project count exceeds the road-clear basemap parcels selected for this district.");
            var result = new List<CityUrbanPlot>(count);
            for (int i = 0; i < count; i++) result.Add(presentation.plots[i]);
            return result.AsReadOnly();
        }

        internal IList<CityUrbanPlot> GetDistrictPlots(Vector3 districtCenter, string districtId, int count)
        {
            CityUrbanDistrictPresentation presentation = GetDistrictPresentation(districtId);
            if (Mathf.Abs(presentation.representativeCenter.x - districtCenter.x) > .001f ||
                Mathf.Abs(presentation.representativeCenter.z - districtCenter.z) > .001f)
                throw new ArgumentException("District center does not match its sourced representative point.",
                    nameof(districtCenter));
            return GetDistrictPlots(districtId, count);
        }

        internal IList<CityUrbanBuildingPresentation> GetDistrictContextCandidates(string districtId,
            int maximum = 12)
        {
            List<CityUrbanBuildingPresentation> result;
            if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (districtId == null || !candidates.TryGetValue(districtId, out result))
                throw new ArgumentException("No basemap context exists for district id '" +
                    districtId + "'.", nameof(districtId));
            int count = Math.Min(maximum, result.Count);
            return result.GetRange(0, count).AsReadOnly();
        }

        /// <summary>Road/building-clear utility circles beside sourced project parcels.</summary>
        internal IList<CityUrbanUtilityPosition> GetUtilityPositions(string districtId)
        {
            List<CityUrbanUtilityPosition> result;
            if (districtId == null || !utilities.TryGetValue(districtId, out result))
                throw new ArgumentException("No basemap utility positions exist for district id '" +
                    districtId + "'.", nameof(districtId));
            return result.AsReadOnly();
        }

        /// <summary>World-local utility point: slot 0 salvage, 1 badge, 2 crane.</summary>
        internal Vector3 GetUtilityPosition(string districtId, int slot)
        {
            string kind = slot == 0 ? "salvage" : slot == 1 ? "badge" :
                slot == 2 ? "crane" : null;
            if (kind == null)
                throw new ArgumentOutOfRangeException(nameof(slot), "Utility slot must be 0, 1, or 2.");
            IList<CityUrbanUtilityPosition> placements = GetUtilityPositions(districtId);
            for (int i = 0; i < placements.Count; i++)
                if (placements[i].utilityKind == kind) return placements[i].worldPosition;
            throw new InvalidOperationException("Missing " + kind +
                " location for district '" + districtId + "'.");
        }

        /// <summary>Finds an unoccupied, road-adjacent industrial pad in actual sourced land.</summary>
        internal Vector3 GetDepotPosition(Vector3 preferredPosition, float clearanceRadius = 1.5f)
        {
            if (sourceMap == null || roadIndex == null || footprintIndex == null)
                throw new InvalidOperationException("Sourced depot placement requires a built city context.");
            if (float.IsNaN(clearanceRadius) || float.IsInfinity(clearanceRadius) || clearanceRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(clearanceRadius));
            return FindDepotPosition(sourceMap, preferredPosition, clearanceRadius, roadIndex,
                footprintIndex, utilities);
        }

        private static Vector3 FindDepotPosition(CityBasemap map, Vector3 preferred, float radius,
            Dictionary<ChunkKey, List<RoadSegment>> roads,
            Dictionary<ChunkKey, List<CityBasemapBuilding>> footprints,
            Dictionary<string, List<CityUrbanUtilityPosition>> utilitySets)
        {
            const float sampleSpacing = 2f;
            var candidates = new List<DepotCandidate>();
            var occupiedSamples = new HashSet<long>();
            AddDepotCandidate(candidates, occupiedSamples, preferred.x, preferred.z, preferred, sampleSpacing);
            for (int r = 0; r < map.roads.Length; r++)
            {
                CityBasemapRoad road = map.roads[r];
                for (int p = 1; p < road.points.Length; p++)
                {
                    CityBasemapPoint a = road.points[p - 1], b = road.points[p];
                    float dx = b.x - a.x, dz = b.z - a.z;
                    float length = Mathf.Sqrt(dx * dx + dz * dz);
                    if (length < .001f) continue;
                    float offset = road.width * .5f + radius + .18f;
                    float nx = -dz / length, nz = dx / length;
                    float mx = (a.x + b.x) * .5f, mz = (a.z + b.z) * .5f;
                    AddDepotCandidate(candidates, occupiedSamples, mx + nx * offset,
                        mz + nz * offset, preferred, sampleSpacing);
                    AddDepotCandidate(candidates, occupiedSamples, mx - nx * offset,
                        mz - nz * offset, preferred, sampleSpacing);
                }
            }
            for (int i = 0; i < map.areas.Length; i++)
            {
                CityBasemapArea area = map.areas[i];
                if (!IsOpenLand(area.kind)) continue;
                float x = 0f, z = 0f;
                for (int p = 0; p < area.points.Length; p++)
                { x += area.points[p].x; z += area.points[p].z; }
                AddDepotCandidate(candidates, occupiedSamples,
                    x / area.points.Length, z / area.points.Length, preferred, sampleSpacing);
            }
            // Road offsets prefer authentic frontage; a coarse full-extent pass guarantees
            // that unusual cul-de-sac/park geometry is not missed.
            for (float x = map.actualBounds.minX; x <= map.actualBounds.maxX; x += sampleSpacing)
                for (float z = map.actualBounds.minZ; z <= map.actualBounds.maxZ; z += sampleSpacing)
                    AddDepotCandidate(candidates, occupiedSamples, x, z, preferred, sampleSpacing);
            candidates.Sort((a, b) =>
            {
                int compare = a.distanceSqr.CompareTo(b.distanceSqr);
                if (compare != 0) return compare;
                compare = a.x.CompareTo(b.x);
                return compare != 0 ? compare : a.z.CompareTo(b.z);
            });

            for (int i = 0; i < candidates.Count; i++)
            {
                DepotCandidate candidate = candidates[i];
                if (candidate.x < map.actualBounds.minX || candidate.x > map.actualBounds.maxX ||
                    candidate.z < map.actualBounds.minZ || candidate.z > map.actualBounds.maxZ ||
                    IsInsideWaterOrShore(map, candidate.x, candidate.z) ||
                    !BuildingFootprintsClear(candidate.x, candidate.z, radius, footprints) ||
                    !RoadRibbonClear(candidate.x, candidate.z, radius, roads) ||
                    !NearRoadEdge(candidate.x, candidate.z, radius, roads) ||
                    DepotOverlapsUtility(candidate.x, candidate.z, radius, utilitySets))
                    continue;
                return new Vector3(candidate.x, 0f, candidate.z);
            }
            throw new InvalidOperationException("No land depot pad with " + radius +
                " world-unit clearance from every sourced road and building was found near the preferred site.");
        }

        private static void AddDepotCandidate(List<DepotCandidate> candidates, HashSet<long> seen,
            float x, float z, Vector3 preferred, float spacing)
        {
            int qx = (int)Math.Round(x / spacing), qz = (int)Math.Round(z / spacing);
            long key = ((long)(uint)qx << 32) | (uint)qz;
            if (!seen.Add(key)) return;
            float dx = x - preferred.x, dz = z - preferred.z;
            candidates.Add(new DepotCandidate(x, z, dx * dx + dz * dz));
        }

        private static bool IsInsideWaterOrShore(CityBasemap map, float x, float z)
        {
            for (int i = 0; i < map.areas.Length; i++)
            {
                CityBasemapArea area = map.areas[i];
                if (IsWaterOrShore(area.kind) && PointInsidePolygon(x, z, area.points)) return true;
            }
            return false;
        }

        private static bool PointInsidePolygon(float x, float z, CityBasemapPoint[] polygon)
        {
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                CityBasemapPoint a = polygon[previous], b = polygon[i];
                if ((a.z > z) != (b.z > z) &&
                    x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            }
            return inside;
        }

        private static bool NearRoadEdge(float x, float z, float radius,
            Dictionary<ChunkKey, List<RoadSegment>> index)
        {
            int cx = Mathf.FloorToInt(x / ChunkSize), cz = Mathf.FloorToInt(z / ChunkSize);
            float nearestEdge = float.MaxValue;
            var examined = new HashSet<RoadSegment>();
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    List<RoadSegment> bucket;
                    if (!index.TryGetValue(new ChunkKey(gx, gz), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        RoadSegment segment = bucket[i];
                        if (!examined.Add(segment)) continue;
                        float edge = Mathf.Sqrt(segment.DistanceSqr(x, z)) - segment.halfWidth;
                        nearestEdge = Mathf.Min(nearestEdge, edge);
                    }
                }
            return nearestEdge >= radius + .02f && nearestEdge <= radius + 1.5f;
        }

        private static bool DepotOverlapsUtility(float x, float z, float radius,
            Dictionary<string, List<CityUrbanUtilityPosition>> utilities)
        {
            foreach (KeyValuePair<string, List<CityUrbanUtilityPosition>> pair in utilities)
                if (UtilityCirclesOverlap(x, z, radius, pair.Value)) return true;
            return false;
        }

        /// <summary>
        /// Selects native plot parcels first, then draws every other sourced footprint and
        /// connected road in ~40-unit frustum-cullable static chunks.
        /// </summary>
        internal static CityUrbanContext Build(CityBasemap map, CityGeometry geometry,
            CityModelLibrary models, Transform parent, Material urbanGround, Material openGround,
            Material majorRoad, Material localRoad, Material buildingFootprints,
            IList<CityUrbanDistrictRequest> requests)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (models == null) throw new ArgumentNullException(nameof(models));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (urbanGround == null || openGround == null || majorRoad == null ||
                localRoad == null || buildingFootprints == null)
                throw new ArgumentNullException("Sourced context requires all ground, road, and footprint materials.");
            if (requests == null || requests.Count == 0)
                throw new ArgumentException("At least one sourced district representative is required.", nameof(requests));

            var result = new CityUrbanContext();
            result.SelectDistricts(map, requests);
            var chunks = new Dictionary<ChunkKey, ContextChunk>();

            for (int i = 0; i < map.areas.Length; i++)
            {
                CityBasemapArea area = map.areas[i];
                if (IsWaterOrShore(area.kind)) continue;
                Material material = IsOpenLand(area.kind) ? openGround : urbanGround;
                try
                {
                    AddPolygon(mapPointList(area.points), GroundY + .006f, material, chunks,
                        geometry, result);
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException("Could not render sourced OSM area " +
                        area.FeatureId + ".", error);
                }
            }

            var renderable = new List<CityBasemapBuilding>(map.buildings.Length);
            for (int i = 0; i < map.buildings.Length; i++)
            {
                CityBasemapBuilding building = map.buildings[i];
                if (result.projectBuildingIds.Contains(building.FeatureId)) continue;
                renderable.Add(building);
                int districtIndex = result.NearestDistrict(building.center.x, building.center.z, requests);
                CityUrbanBuildingPresentation presentation = MakeBuildingPresentation(building,
                    requests[districtIndex].id, false);
                result.allContext.Add(presentation);
                List<CityUrbanBuildingPresentation> districtCandidates =
                    result.candidates[requests[districtIndex].id];
                districtCandidates.Add(presentation);
            }

            // Decide candidate model IDs before constructing any building volumes. Reserve
            // the full source-outline volume budget first, including outlines that may be
            // suppressed beneath an affordable authored LOD.
            List<CityBasemapBuilding> authoredCandidates = SelectSpatiallyDistributed(renderable,
                MaxAuthoredModelCopies);
            int reservedOutlineTriangles = 0;
            for (int i = 0; i < renderable.Count; i++)
                reservedOutlineTriangles += EstimateExtrudedBuildingTriangles(renderable[i]);
            if (result.triangleCount + reservedOutlineTriangles > MaxContextTriangles)
                throw new InvalidOperationException("Actual OSM building volumes exceed the 700000-triangle context budget.");
            int affordableCopies = Math.Min(authoredCandidates.Count,
                (MaxContextTriangles - result.triangleCount - reservedOutlineTriangles) /
                    MaxSourceLodTriangles);
            var authoredIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < affordableCopies; i++)
                authoredIds.Add(authoredCandidates[i].FeatureId);

            for (int i = 0; i < renderable.Count; i++)
            {
                CityBasemapBuilding building = renderable[i];
                if (authoredIds.Contains(building.FeatureId)) continue;
                try
                {
                    AddExtrudedBuilding(building, buildingFootprints, chunks, geometry, result);
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException("Could not extrude sourced OSM building " +
                        building.FeatureId + ".", error);
                }
            }

            string[] destroyedContextModels = CityRuinProfiles.ContextModelKeys;
            for (int i = 0; i < affordableCopies; i++)
            {
                CityBasemapBuilding building = authoredCandidates[i];
                ChunkKey key = Cell(building.center.x, building.center.z);
                ContextChunk chunk = GetChunk(chunks, key, geometry);
                // Every starting background parcel is destroyed. Original imported
                // intact models remain available for earned construction, never this layer.
                string contextModel = destroyedContextModels[StableHash(building.FeatureId) % destroyedContextModels.Length];
                models.AddTo(chunk.models, contextModel,
                    new Vector3(building.center.x, GroundY, building.center.z),
                    new Vector3(building.size.x, 0f, building.size.z), building.yaw,
                    building.height, true);
            }
            result.triangleCount += affordableCopies * MaxSourceLodTriangles;
            if (result.triangleCount > MaxContextTriangles)
                throw new InvalidOperationException("Sourced city context exceeds its 700000-triangle static budget.");

            foreach (KeyValuePair<ChunkKey, ContextChunk> pair in chunks)
                pair.Value.Build(pair.Key, parent, geometry);
            for (int i = 0; i < requests.Count; i++)
                result.candidates[requests[i].id].Sort((a, b) =>
                {
                    float da = HorizontalSqr(a.worldPosition, requests[i].representativeCenter);
                    float db = HorizontalSqr(b.worldPosition, requests[i].representativeCenter);
                    int compare = da.CompareTo(db);
                    return compare != 0 ? compare : string.CompareOrdinal(a.sourceBuildingId, b.sourceBuildingId);
                });
            return result;
        }

        private void SelectDistricts(CityBasemap map, IList<CityUrbanDistrictRequest> requests)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < requests.Count; i++)
            {
                CityUrbanDistrictRequest request = requests[i];
                if (request == null || string.IsNullOrWhiteSpace(request.id) ||
                    !ids.Add(request.id) || request.projectCount < 0)
                    throw new ArgumentException("District requests require unique ids and nonnegative project counts.");
                candidates.Add(request.id, new List<CityUrbanBuildingPresentation>());
            }

            var roads = BuildRoadSegmentIndex(map.roads);
            var buildingFootprints = BuildBuildingFootprintIndex(map.buildings);
            sourceMap = map;
            roadIndex = roads;
            footprintIndex = buildingFootprints;
            var sourceBuildings = new Dictionary<string, CityBasemapBuilding>(StringComparer.Ordinal);
            for (int i = 0; i < map.buildings.Length; i++)
                sourceBuildings.Add(map.buildings[i].FeatureId, map.buildings[i]);
            var alreadyPlacedUtilities = new List<CityUrbanUtilityPosition>();
            for (int d = 0; d < requests.Count; d++)
            {
                CityUrbanDistrictRequest request = requests[d];
                var owned = new List<CityBasemapBuilding>();
                float radius = 0f;
                for (int i = 0; i < map.buildings.Length; i++)
                {
                    CityBasemapBuilding building = map.buildings[i];
                    if (NearestDistrict(building.center.x, building.center.z, requests) != d) continue;
                    owned.Add(building);
                    radius = Mathf.Max(radius, Mathf.Sqrt(HorizontalSqr(
                        new Vector3(building.center.x, 0f, building.center.z),
                        request.representativeCenter)));
                }
                if (owned.Count < request.projectCount)
                    throw new InvalidOperationException("Basemap Voronoi cell for district '" + request.id +
                        "' has only " + owned.Count + " building footprints for " +
                        request.projectCount + " native projects.");

                owned.Sort((a, b) =>
                {
                    float da = HorizontalSqr(new Vector3(a.center.x, 0f, a.center.z), request.representativeCenter);
                    float db = HorizontalSqr(new Vector3(b.center.x, 0f, b.center.z), request.representativeCenter);
                    int compare = da.CompareTo(db);
                    return compare != 0 ? compare : string.CompareOrdinal(a.FeatureId, b.FeatureId);
                });
                var plots = new List<CityUrbanPlot>(request.projectCount);
                for (int i = 0; i < owned.Count && plots.Count < request.projectCount; i++)
                {
                    CityBasemapBuilding building = owned[i];
                    if (building.size.x < .25f || building.size.z < .25f ||
                        !RoadClear(building, roads)) continue;
                    bool overlaps = false;
                    for (int p = 0; p < plots.Count; p++)
                    {
                        float separation = Mathf.Max(1f,
                            FootprintRadius(building.size.x, building.size.z) +
                            FootprintRadius(plots[p].size.x, plots[p].size.z) + .08f);
                        if (HorizontalSqr(new Vector3(building.center.x, 0f, building.center.z),
                            plots[p].worldPosition) < separation * separation)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;
                    plots.Add(new CityUrbanPlot(building.FeatureId,
                        new Vector3(building.center.x, GroundY, building.center.z),
                        new Vector3(building.size.x, 0f, building.size.z), building.yaw));
                    projectBuildingIds.Add(building.FeatureId);
                }
                if (plots.Count != request.projectCount)
                    throw new InvalidOperationException("Basemap Voronoi cell for district '" + request.id +
                        "' provides only " + plots.Count + " road-clear, spaced parcels for " +
                        request.projectCount + " native projects.");
                districts.Add(request.id, new CityUrbanDistrictPresentation(request.id,
                    request.representativeCenter, radius, plots));
                var districtUtilities = PlaceUtilities(request.id, plots, sourceBuildings,
                    buildingFootprints, roads, alreadyPlacedUtilities);
                utilities.Add(request.id, districtUtilities);
                alreadyPlacedUtilities.AddRange(districtUtilities);
            }
        }

        private static List<CityUrbanUtilityPosition> PlaceUtilities(string districtId,
            List<CityUrbanPlot> plots, Dictionary<string, CityBasemapBuilding> sourceBuildings,
            Dictionary<ChunkKey, List<CityBasemapBuilding>> footprintIndex,
            Dictionary<ChunkKey, List<RoadSegment>> roadIndex,
            List<CityUrbanUtilityPosition> alreadyPlaced)
        {
            // Larger cranes reserve their clear frontage first; all positions remain close to
            // real project parcels and outside every sourced footprint and road ribbon.
            var roles = new[]
            {
                new CityUrbanUtilityKind("crane", .30f),
                new CityUrbanUtilityKind("salvage", .30f),
                new CityUrbanUtilityKind("badge", .07f)
            };
            var result = new List<CityUrbanUtilityPosition>(roles.Length);
            for (int r = 0; r < roles.Length; r++)
            {
                CityUrbanUtilityKind role = roles[r];
                CityUrbanUtilityPosition placed = FindUtility(districtId, role, plots,
                    sourceBuildings, footprintIndex, roadIndex, alreadyPlaced, result);
                if (placed == null)
                    throw new InvalidOperationException("No road-clear, building-clear " + role.name +
                        " utility location was found near a sourced project parcel in district '" +
                        districtId + "'.");
                result.Add(placed);
            }
            return result;
        }

        private static CityUrbanUtilityPosition FindUtility(string districtId, CityUrbanUtilityKind role,
            List<CityUrbanPlot> plots, Dictionary<string, CityBasemapBuilding> sourceBuildings,
            Dictionary<ChunkKey, List<CityBasemapBuilding>> footprintIndex,
            Dictionary<ChunkKey, List<RoadSegment>> roadIndex,
            List<CityUrbanUtilityPosition> alreadyPlaced, List<CityUrbanUtilityPosition> placedThisDistrict)
        {
            const float frontageGap = .06f;
            for (int plotIndex = 0; plotIndex < plots.Count; plotIndex++)
            {
                CityUrbanPlot plot = plots[plotIndex];
                CityBasemapBuilding source = sourceBuildings[plot.sourceBuildingId];
                float radians = source.yaw * (float)(Math.PI / 180.0);
                float axisXx = (float)Math.Cos(radians), axisXz = (float)Math.Sin(radians);
                float axisZx = -axisXz, axisZz = axisXx;
                for (int ring = 0; ring <= 12; ring++)
                {
                    float extra = ring * .1f;
                    for (int directionIndex = 0; directionIndex < 16; directionIndex++)
                    {
                        float angle = directionIndex * (float)(Math.PI / 8.0);
                        float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                        float support = Mathf.Abs(dx * axisXx + dz * axisXz) * source.size.x * .5f +
                            Mathf.Abs(dx * axisZx + dz * axisZz) * source.size.z * .5f;
                        float distance = support + role.radius + frontageGap + extra;
                        float x = source.center.x + dx * distance;
                        float z = source.center.z + dz * distance;
                        if (!BuildingFootprintsClear(x, z, role.radius, footprintIndex) ||
                            !RoadRibbonClear(x, z, role.radius, roadIndex) ||
                            UtilityCirclesOverlap(x, z, role.radius, alreadyPlaced) ||
                            UtilityCirclesOverlap(x, z, role.radius, placedThisDistrict))
                            continue;
                        return new CityUrbanUtilityPosition(districtId, role.name,
                            plot.sourceBuildingId, new Vector3(x, GroundY, z), role.radius);
                    }
                }
            }
            return null;
        }

        private static Dictionary<ChunkKey, List<CityBasemapBuilding>> BuildBuildingFootprintIndex(
            CityBasemapBuilding[] buildings)
        {
            var result = new Dictionary<ChunkKey, List<CityBasemapBuilding>>();
            for (int i = 0; i < buildings.Length; i++)
            {
                CityBasemapBuilding building = buildings[i];
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int p = 0; p < building.outline.Length; p++)
                {
                    minX = Mathf.Min(minX, building.outline[p].x);
                    maxX = Mathf.Max(maxX, building.outline[p].x);
                    minZ = Mathf.Min(minZ, building.outline[p].z);
                    maxZ = Mathf.Max(maxZ, building.outline[p].z);
                }
                int minCellX = Mathf.FloorToInt((minX - .5f) / ChunkSize);
                int maxCellX = Mathf.FloorToInt((maxX + .5f) / ChunkSize);
                int minCellZ = Mathf.FloorToInt((minZ - .5f) / ChunkSize);
                int maxCellZ = Mathf.FloorToInt((maxZ + .5f) / ChunkSize);
                for (int x = minCellX; x <= maxCellX; x++)
                    for (int z = minCellZ; z <= maxCellZ; z++)
                    {
                        ChunkKey key = new ChunkKey(x, z);
                        List<CityBasemapBuilding> bucket;
                        if (!result.TryGetValue(key, out bucket))
                        {
                            bucket = new List<CityBasemapBuilding>();
                            result.Add(key, bucket);
                        }
                        bucket.Add(building);
                    }
            }
            return result;
        }

        private static bool BuildingFootprintsClear(float x, float z, float radius,
            Dictionary<ChunkKey, List<CityBasemapBuilding>> index)
        {
            int cx = Mathf.FloorToInt(x / ChunkSize), cz = Mathf.FloorToInt(z / ChunkSize);
            var examined = new HashSet<CityBasemapBuilding>();
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    List<CityBasemapBuilding> bucket;
                    if (!index.TryGetValue(new ChunkKey(gx, gz), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        CityBasemapBuilding building = bucket[i];
                        if (!examined.Add(building)) continue;
                        if (CircleOverlapsPolygon(x, z, radius + .015f, building.outline))
                            return false;
                    }
                }
            return true;
        }

        private static bool CircleOverlapsPolygon(float x, float z, float radius, CityBasemapPoint[] polygon)
        {
            bool inside = false;
            float nearestSqr = float.MaxValue;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                CityBasemapPoint a = polygon[previous], b = polygon[i];
                if (((a.z > z) != (b.z > z)) &&
                    x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x)
                    inside = !inside;
                float distance = PointSegmentDistanceSqr(x, z, a.x, a.z, b.x, b.z);
                nearestSqr = Mathf.Min(nearestSqr, distance);
            }
            return inside || nearestSqr < radius * radius;
        }

        private static bool RoadRibbonClear(float x, float z, float radius,
            Dictionary<ChunkKey, List<RoadSegment>> index)
        {
            int cx = Mathf.FloorToInt(x / ChunkSize), cz = Mathf.FloorToInt(z / ChunkSize);
            var examined = new HashSet<RoadSegment>();
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    List<RoadSegment> bucket;
                    if (!index.TryGetValue(new ChunkKey(gx, gz), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        RoadSegment segment = bucket[i];
                        if (examined.Add(segment) &&
                            segment.DistanceSqr(x, z) < (radius + segment.halfWidth + .015f) *
                            (radius + segment.halfWidth + .015f)) return false;
                    }
                }
            return true;
        }

        private static float PointSegmentDistanceSqr(float x, float z,
            float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float length = dx * dx + dz * dz;
            float t = length < .0001f ? 0f : Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / length);
            float px = ax + t * dx - x, pz = az + t * dz - z;
            return px * px + pz * pz;
        }

        private static bool UtilityCirclesOverlap(float x, float z, float radius,
            List<CityUrbanUtilityPosition> existing)
        {
            for (int i = 0; i < existing.Count; i++)
            {
                float dx = x - existing[i].worldPosition.x, dz = z - existing[i].worldPosition.z;
                float clearance = radius + existing[i].radius + .03f;
                if (dx * dx + dz * dz < clearance * clearance) return true;
            }
            return false;
        }

        private int NearestDistrict(float x, float z, IList<CityUrbanDistrictRequest> requests)
        {
            int best = 0;
            float distance = float.MaxValue;
            for (int i = 0; i < requests.Count; i++)
            {
                float next = HorizontalSqr(new Vector3(x, 0f, z), requests[i].representativeCenter);
                if (next < distance) { distance = next; best = i; }
            }
            return best;
        }

        private static List<CityBasemapBuilding> SelectSpatiallyDistributed(
            List<CityBasemapBuilding> buildings, int maximum)
        {
            if (buildings.Count <= maximum) return buildings;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < buildings.Count; i++)
            {
                minX = Mathf.Min(minX, buildings[i].center.x); maxX = Mathf.Max(maxX, buildings[i].center.x);
                minZ = Mathf.Min(minZ, buildings[i].center.z); maxZ = Mathf.Max(maxZ, buildings[i].center.z);
            }
            const int strata = 40;
            var buckets = new List<CityBasemapBuilding>[strata * strata];
            for (int i = 0; i < buildings.Count; i++)
            {
                CityBasemapBuilding building = buildings[i];
                int x = Mathf.Clamp((int)((building.center.x - minX) / Mathf.Max(.001f, maxX - minX) * strata),
                    0, strata - 1);
                int z = Mathf.Clamp((int)((building.center.z - minZ) / Mathf.Max(.001f, maxZ - minZ) * strata),
                    0, strata - 1);
                int index = z * strata + x;
                if (buckets[index] == null) buckets[index] = new List<CityBasemapBuilding>();
                buckets[index].Add(building);
            }
            var result = new List<CityBasemapBuilding>(maximum);
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] == null) continue;
                buckets[i].Sort((a, b) => string.CompareOrdinal(a.FeatureId, b.FeatureId));
                result.Add(buckets[i][buckets[i].Count / 2]);
            }
            // At most one authored LOD per ~800m stratum bucket, capped below the 700k budget.
            if (result.Count > maximum)
            {
                var reduced = new List<CityBasemapBuilding>(maximum);
                for (int i = 0; i < maximum; i++)
                    reduced.Add(result[(int)((long)i * result.Count / maximum)]);
                result = reduced;
            }
            return result;
        }

        private static CityUrbanBuildingPresentation MakeBuildingPresentation(
            CityBasemapBuilding building, string districtId, bool isProject)
        {
            return new CityUrbanBuildingPresentation(building.FeatureId, districtId, isProject,
                new Vector3(building.center.x, GroundY, building.center.z),
                new Vector3(building.size.x, 0f, building.size.z), building.yaw,
                building.height, building.levels);
        }

        private static Dictionary<ChunkKey, List<RoadSegment>> BuildRoadSegmentIndex(CityBasemapRoad[] roads)
        {
            var result = new Dictionary<ChunkKey, List<RoadSegment>>();
            for (int r = 0; r < roads.Length; r++)
                for (int p = 1; p < roads[r].points.Length; p++)
                {
                    CityBasemapPoint a = roads[r].points[p - 1], b = roads[r].points[p];
                    var segment = new RoadSegment(a.x, a.z, b.x, b.z, roads[r].width * .5f);
                    int minX = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - segment.halfWidth - 5f) / ChunkSize);
                    int maxX = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + segment.halfWidth + 5f) / ChunkSize);
                    int minZ = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - segment.halfWidth - 5f) / ChunkSize);
                    int maxZ = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + segment.halfWidth + 5f) / ChunkSize);
                    for (int x = minX; x <= maxX; x++)
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            ChunkKey key = new ChunkKey(x, z);
                            List<RoadSegment> bucket;
                            if (!result.TryGetValue(key, out bucket))
                            {
                                bucket = new List<RoadSegment>();
                                result.Add(key, bucket);
                            }
                            bucket.Add(segment);
                        }
                }
            return result;
        }

        private static bool RoadClear(CityBasemapBuilding building,
            Dictionary<ChunkKey, List<RoadSegment>> roadIndex)
        {
            int cx = Mathf.FloorToInt(building.center.x / ChunkSize);
            int cz = Mathf.FloorToInt(building.center.z / ChunkSize);
            var examined = new HashSet<RoadSegment>();
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    List<RoadSegment> segments;
                    if (!roadIndex.TryGetValue(new ChunkKey(x, z), out segments)) continue;
                    for (int i = 0; i < segments.Count; i++)
                    {
                        RoadSegment segment = segments[i];
                        if (!examined.Add(segment)) continue;
                        float clearance = segment.halfWidth +
                            FootprintRadius(building.size.x, building.size.z) + .08f;
                        if (segment.DistanceSqr(building.center.x, building.center.z) <
                            clearance * clearance) return false;
                    }
                }
            return true;
        }

        private static void AddRoad(CityBasemapRoad road, Material material,
            Dictionary<ChunkKey, ContextChunk> chunks, CityGeometry geometry, CityUrbanContext owner)
        {
            int count = road.points.Length;
            var left = new Vector2[count];
            var right = new Vector2[count];
            float half = road.width * .5f;
            for (int i = 0; i < count; i++)
            {
                CityBasemapPoint current = road.points[i];
                Vector2 before = i > 0 ? Direction(road.points[i - 1], current) : Direction(current, road.points[1]);
                Vector2 after = i + 1 < count ? Direction(current, road.points[i + 1]) : before;
                Vector2 n1 = new Vector2(-before.y, before.x), n2 = new Vector2(-after.y, after.x);
                Vector2 miter = (n1 + n2).normalized;
                float divisor = Mathf.Max(.35f, Mathf.Abs(Vector2.Dot(miter, n2)));
                float length = Mathf.Min(half / divisor, half * 2f);
                left[i] = new Vector2(current.x, current.z) + miter * length;
                right[i] = new Vector2(current.x, current.z) - miter * length;
            }
            for (int i = 1; i < count; i++)
            {
                var quad = new List<Vector2> { left[i - 1], left[i], right[i], right[i - 1] };
                AddClippedPolygon(quad, GroundY + .055f, material, chunks, geometry, owner);
            }
        }

        private static Vector2 Direction(CityBasemapPoint a, CityBasemapPoint b)
        {
            Vector2 delta = new Vector2(b.x - a.x, b.z - a.z);
            return delta.sqrMagnitude < .0001f ? Vector2.up : delta.normalized;
        }

        private static void AddPolygon(List<Vector2> polygon, float y, Material material,
            Dictionary<ChunkKey, ContextChunk> chunks, CityGeometry geometry, CityUrbanContext owner)
        {
            float signedArea = 0f;
            for (int i = 1; i < polygon.Count - 1; i++)
                signedArea += Cross(polygon[0], polygon[i], polygon[i + 1]);
            // Sub-centimetre source slivers near the bbox edge are retained in the DTO but
            // have no stable rasterizable area at the map's float precision.
            if (Mathf.Abs(signedArea) < .00002f) return;
            List<int> triangles = Triangulate(polygon);
            for (int t = 0; t < triangles.Count; t += 3)
            {
                var triangle = new List<Vector2>
                {
                    polygon[triangles[t]], polygon[triangles[t + 1]], polygon[triangles[t + 2]]
                };
                AddClippedPolygon(triangle, y, material, chunks, geometry, owner);
            }
        }

        private static int EstimateExtrudedBuildingTriangles(CityBasemapBuilding building)
        {
            List<Vector2> polygon = mapPointList(building.outline);
            int result = 0;
            float signedArea = PolygonDoubleArea(polygon);
            if (Mathf.Abs(signedArea) >= .00002f)
            {
                var roof = new List<Vector2>(polygon);
                List<int> triangles = Triangulate(roof);
                for (int t = 0; t < triangles.Count; t += 3)
                    result += CountClippedTriangleTriangles(roof[triangles[t]],
                        roof[triangles[t + 1]], roof[triangles[t + 2]]);
            }
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                if ((b - a).sqrMagnitude < .00000001f) continue;
                result += 2 * SegmentChunkCuts(a.x, a.y, b.x, b.y).Count - 2;
            }
            return result;
        }

        private static int CountClippedTriangleTriangles(Vector2 a, Vector2 b, Vector2 c)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxZ = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            int count = 0;
            for (int x = Mathf.FloorToInt(minX / ChunkSize); x <= Mathf.FloorToInt(maxX / ChunkSize); x++)
                for (int z = Mathf.FloorToInt(minZ / ChunkSize); z <= Mathf.FloorToInt(maxZ / ChunkSize); z++)
                {
                    List<Vector2> clipped = Clip(new List<Vector2> { a, b, c },
                        x * ChunkSize, (x + 1) * ChunkSize, z * ChunkSize, (z + 1) * ChunkSize);
                    if (clipped.Count >= 3) count += clipped.Count - 2;
                }
            return count;
        }

        private static void AddExtrudedBuilding(CityBasemapBuilding building, Material material,
            Dictionary<ChunkKey, ContextChunk> chunks, CityGeometry geometry, CityUrbanContext owner)
        {
            List<Vector2> outline = mapPointList(building.outline);
            float area = PolygonDoubleArea(outline);
            // This is a collapsed ground slab, NOT a complete roof at source height.
            AddPolygon(new List<Vector2>(outline), GroundY + .025f,
                material, chunks, geometry, owner);
            int seed = StableHash(building.FeatureId);
            float ruinHeight = building.height * (.3f + (seed % 4) * .12f);
            bool counterClockwise = area > 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                // Omit whole bays, shorten remaining walls and vary their torn upper
                // edges. Keep true OSM parcel outlines and chunk clipping; no closed boxes.
                if ((seed + i) % 3 == 0) continue;
                Vector2 originalA = a;
                a = Vector2.LerpUnclamped(originalA, b, .06f + ((seed + i) % 4) * .035f);
                b = Vector2.LerpUnclamped(originalA, b, .72f + ((seed + i) % 3) * .07f);
                if ((b - a).sqrMagnitude < .00000001f) continue;
                List<float> cuts = SegmentChunkCuts(a.x, a.y, b.x, b.y);
                for (int cut = 1; cut < cuts.Count; cut++)
                {
                    float t0 = cuts[cut - 1], t1 = cuts[cut];
                    Vector2 low = Vector2.LerpUnclamped(a, b, t0);
                    Vector2 high = Vector2.LerpUnclamped(a, b, t1);
                    ChunkKey key = Cell((low.x + high.x) * .5f, (low.y + high.y) * .5f);
                    ContextChunk chunk = GetChunk(chunks, key, geometry);
                    Vector3 bottomA = new Vector3(low.x, GroundY, low.y);
                    Vector3 bottomB = new Vector3(high.x, GroundY, high.y);
                    float heightA = ruinHeight * (.42f + ((seed + i * 7) % 5) * .12f);
                    float heightB = ruinHeight * (.37f + ((seed + i * 11) % 6) * .1f);
                    Vector3 topA = new Vector3(low.x, GroundY + heightA + (heightB - heightA) * t0, low.y);
                    Vector3 topB = new Vector3(high.x, GroundY + heightA + (heightB - heightA) * t1, high.y);
                    if (counterClockwise)
                    {
                        chunk.AddTriangle(material, bottomA, topA, topB);
                        chunk.AddTriangle(material, bottomA, topB, bottomB);
                    }
                    else
                    {
                        chunk.AddTriangle(material, bottomA, bottomB, topB);
                        chunk.AddTriangle(material, bottomA, topB, topA);
                    }
                    owner.triangleCount += 2;
                }
            }
            owner.extrudedVolumes.Add(new CityUrbanBuildingVolume(building.FeatureId,
                building.height, building.outline));
        }

        private static List<float> SegmentChunkCuts(float ax, float az, float bx, float bz)
        {
            var cuts = new List<float> { 0f, 1f };
            float dx = bx - ax, dz = bz - az;
            if (Mathf.Abs(dx) > .000001f)
            {
                int first = Mathf.FloorToInt(Mathf.Min(ax, bx) / ChunkSize) + 1;
                int last = Mathf.FloorToInt(Mathf.Max(ax, bx) / ChunkSize);
                for (int cell = first; cell <= last; cell++)
                {
                    float t = (cell * ChunkSize - ax) / dx;
                    if (t > .000001f && t < .999999f) cuts.Add(t);
                }
            }
            if (Mathf.Abs(dz) > .000001f)
            {
                int first = Mathf.FloorToInt(Mathf.Min(az, bz) / ChunkSize) + 1;
                int last = Mathf.FloorToInt(Mathf.Max(az, bz) / ChunkSize);
                for (int cell = first; cell <= last; cell++)
                {
                    float t = (cell * ChunkSize - az) / dz;
                    if (t > .000001f && t < .999999f) cuts.Add(t);
                }
            }
            cuts.Sort();
            for (int i = cuts.Count - 1; i > 0; i--)
                if (Mathf.Abs(cuts[i] - cuts[i - 1]) < .000001f) cuts.RemoveAt(i);
            return cuts;
        }

        private static float PolygonDoubleArea(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
                area += polygon[i].x * polygon[(i + 1) % polygon.Count].y -
                    polygon[(i + 1) % polygon.Count].x * polygon[i].y;
            return area;
        }

        private static void AddClippedPolygon(List<Vector2> polygon, float y, Material material,
            Dictionary<ChunkKey, ContextChunk> chunks, CityGeometry geometry, CityUrbanContext owner)
        {
            float minX = polygon[0].x, maxX = minX, minZ = polygon[0].y, maxZ = minZ;
            for (int i = 1; i < polygon.Count; i++)
            {
                minX = Mathf.Min(minX, polygon[i].x); maxX = Mathf.Max(maxX, polygon[i].x);
                minZ = Mathf.Min(minZ, polygon[i].y); maxZ = Mathf.Max(maxZ, polygon[i].y);
            }
            int minCellX = Mathf.FloorToInt(minX / ChunkSize), maxCellX = Mathf.FloorToInt(maxX / ChunkSize);
            int minCellZ = Mathf.FloorToInt(minZ / ChunkSize), maxCellZ = Mathf.FloorToInt(maxZ / ChunkSize);
            for (int x = minCellX; x <= maxCellX; x++)
                for (int z = minCellZ; z <= maxCellZ; z++)
                {
                    List<Vector2> clipped = Clip(polygon, x * ChunkSize, (x + 1) * ChunkSize,
                        z * ChunkSize, (z + 1) * ChunkSize);
                    if (clipped.Count < 3) continue;
                    ContextChunk chunk = GetChunk(chunks, new ChunkKey(x, z), geometry);
                    for (int i = 1; i < clipped.Count - 1; i++)
                    {
                        chunk.AddTopTriangle(material, clipped[0], clipped[i], clipped[i + 1], y);
                        owner.triangleCount++;
                    }
                }
        }

        private static List<Vector2> Clip(List<Vector2> source, float minX, float maxX, float minZ, float maxZ)
        {
            List<Vector2> result = source;
            result = ClipEdge(result, 0, minX, true);
            result = ClipEdge(result, 0, maxX, false);
            result = ClipEdge(result, 1, minZ, true);
            result = ClipEdge(result, 1, maxZ, false);
            return result;
        }

        private static List<Vector2> ClipEdge(List<Vector2> input, int axis, float boundary, bool greater)
        {
            var output = new List<Vector2>();
            if (input.Count == 0) return output;
            Vector2 previous = input[input.Count - 1];
            bool previousInside = greater ? previous[axis] >= boundary : previous[axis] <= boundary;
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 current = input[i];
                bool currentInside = greater ? current[axis] >= boundary : current[axis] <= boundary;
                if (currentInside != previousInside)
                {
                    float denominator = current[axis] - previous[axis];
                    float t = Mathf.Abs(denominator) < .000001f ? 0f : (boundary - previous[axis]) / denominator;
                    output.Add(Vector2.LerpUnclamped(previous, current, t));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            return output;
        }

        private static List<int> Triangulate(List<Vector2> polygon)
        {
            var result = new List<int>();
            SimplifyPolygon(polygon);
            if (polygon.Count < 3) return result;
            var remaining = new List<int>(polygon.Count);
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++) remaining.Add(i);
            for (int i = 1; i < polygon.Count - 1; i++)
                area += Cross(polygon[0], polygon[i], polygon[i + 1]);
            bool ccw = area > 0f;
            int guard = polygon.Count * polygon.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int ib = remaining[i];
                    int ic = remaining[(i + 1) % remaining.Count];
                    float cross = Cross(polygon[ia], polygon[ib], polygon[ic]);
                    if (ccw ? cross <= .00001f : cross >= -.00001f) continue;
                    bool contains = false;
                    for (int p = 0; p < remaining.Count; p++)
                    {
                        int test = remaining[p];
                        if (test == ia || test == ib || test == ic) continue;
                        if (InsideTriangle(polygon[test], polygon[ia], polygon[ib], polygon[ic]))
                        { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound) break;
            }
            if (remaining.Count == 3)
            {
                result.Add(remaining[0]); result.Add(remaining[1]); result.Add(remaining[2]);
            }
            if (result.Count != (polygon.Count - 2) * 3)
                throw new InvalidOperationException("A sourced city footprint could not be triangulated safely; " +
                    "emitted " + (result.Count / 3) + " of " + (polygon.Count - 2) +
                    " triangles; check its OSM ring for a self-intersection or invalid vertex order.");
            return result;
        }

        private static void SimplifyPolygon(List<Vector2> polygon)
        {
            const float duplicateEpsilonSqr = .0000000001f;
            for (int i = polygon.Count - 1; i >= 0; i--)
            {
                int previous = (i + polygon.Count - 1) % polygon.Count;
                if ((polygon[i] - polygon[previous]).sqrMagnitude < duplicateEpsilonSqr)
                    polygon.RemoveAt(i);
            }
            bool changed = true;
            while (changed && polygon.Count > 3)
            {
                changed = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 a = polygon[(i + polygon.Count - 1) % polygon.Count];
                    Vector2 b = polygon[i];
                    Vector2 c = polygon[(i + 1) % polygon.Count];
                    if (Mathf.Abs(Cross(a, b, c)) < .000001f)
                    {
                        polygon.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(a, b, point), bc = Cross(b, c, point), ca = Cross(c, a, point);
            return (ab >= -.00001f && bc >= -.00001f && ca >= -.00001f) ||
                (ab <= .00001f && bc <= .00001f && ca <= .00001f);
        }

        private static bool IsWaterOrShore(string kind)
        {
            string value = kind.ToLowerInvariant();
            return value.Contains("water") || value.Contains("river") || value.Contains("basin") ||
                value.Contains("beach") || value.Contains("shore") || value.Contains("sea") ||
                value.Contains("military");
        }

        private static bool IsOpenLand(string kind)
        {
            string value = kind.ToLowerInvariant();
            return value.Contains("park") || value.Contains("garden") || value.Contains("grass") ||
                value.Contains("recreation") || value.Contains("green");
        }

        private static bool IsMajorRoad(string kind)
        {
            string value = kind.ToLowerInvariant();
            return value.Contains("primary") || value.Contains("secondary") ||
                value.Contains("tertiary") || value.Contains("trunk") || value.Contains("motorway");
        }

        private static List<Vector2> mapPointList(CityBasemapPoint[] points)
        {
            var result = new List<Vector2>(points.Length);
            for (int i = 0; i < points.Length; i++) result.Add(new Vector2(points[i].x, points[i].z));
            return result;
        }

        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        private static float FootprintRadius(float width, float depth)
        {
            return Mathf.Sqrt(width * width + depth * depth) * .5f;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 16777619;
                return (int)(hash & 0x7fffffff);
            }
        }

        private static ChunkKey Cell(float x, float z)
        {
            return new ChunkKey(Mathf.FloorToInt(x / ChunkSize), Mathf.FloorToInt(z / ChunkSize));
        }

        private static ContextChunk GetChunk(Dictionary<ChunkKey, ContextChunk> chunks, ChunkKey key,
            CityGeometry geometry)
        {
            ContextChunk result;
            if (!chunks.TryGetValue(key, out result))
            {
                result = new ContextChunk(geometry);
                chunks.Add(key, result);
            }
            return result;
        }

        private sealed class ContextChunk
        {
            internal readonly CityMeshBatch models;
            private readonly Dictionary<Material, List<Vector3>> vertices =
                new Dictionary<Material, List<Vector3>>();
            private readonly Dictionary<Material, List<int>> indices =
                new Dictionary<Material, List<int>>();

            internal ContextChunk(CityGeometry geometry) { models = new CityMeshBatch(geometry); }

            internal void AddTopTriangle(Material material, Vector2 a, Vector2 b, Vector2 c, float y)
            {
                if (Cross(a, b, c) >= 0f)
                    AddTriangle(material, new Vector3(a.x, y, a.y),
                        new Vector3(c.x, y, c.y), new Vector3(b.x, y, b.y));
                else
                    AddTriangle(material, new Vector3(a.x, y, a.y),
                        new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y));
            }

            internal void AddTriangle(Material material, Vector3 a, Vector3 b, Vector3 c)
            {
                List<Vector3> points;
                List<int> triangles;
                if (!vertices.TryGetValue(material, out points))
                {
                    points = new List<Vector3>();
                    triangles = new List<int>();
                    vertices.Add(material, points);
                    indices.Add(material, triangles);
                }
                else triangles = indices[material];
                int first = points.Count;
                points.Add(a);
                points.Add(b);
                points.Add(c);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }

            internal void Build(ChunkKey key, Transform parent, CityGeometry geometry)
            {
                var root = new GameObject("Sourced city context chunk " + key.x + "," + key.z);
                root.transform.SetParent(parent, false);
                foreach (KeyValuePair<Material, List<Vector3>> pair in vertices)
                {
                    var mesh = geometry.Own(new Mesh
                    {
                        name = "Sourced city context static merged",
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                    });
                    mesh.SetVertices(pair.Value);
                    mesh.SetTriangles(indices[pair.Key], 0, true);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    var child = new GameObject(pair.Key.name);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = pair.Key;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                models.Build("Sourced city buildings / road ribbons", root.transform, Vector3.zero);
            }
        }

        private struct ChunkKey : IEquatable<ChunkKey>
        {
            internal readonly int x, z;
            internal ChunkKey(int x, int z) { this.x = x; this.z = z; }
            public bool Equals(ChunkKey other) { return x == other.x && z == other.z; }
            public override bool Equals(object obj) { return obj is ChunkKey && Equals((ChunkKey)obj); }
            public override int GetHashCode() { unchecked { return (x * 397) ^ z; } }
        }

        private sealed class RoadSegment
        {
            internal readonly float ax, az, bx, bz, halfWidth;
            internal RoadSegment(float ax, float az, float bx, float bz, float halfWidth)
            { this.ax = ax; this.az = az; this.bx = bx; this.bz = bz; this.halfWidth = halfWidth; }
            internal float DistanceSqr(float x, float z)
            {
                float dx = bx - ax, dz = bz - az;
                float length = dx * dx + dz * dz;
                float t = length < .0001f ? 0f : Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / length);
                float px = ax + t * dx - x, pz = az + t * dz - z;
                return px * px + pz * pz;
            }
        }

        private struct DepotCandidate
        {
            internal readonly float x, z, distanceSqr;
            internal DepotCandidate(float x, float z, float distanceSqr)
            { this.x = x; this.z = z; this.distanceSqr = distanceSqr; }
        }
    }

    internal sealed class CityUrbanDistrictRequest
    {
        internal readonly string id;
        internal readonly Vector3 representativeCenter;
        internal readonly int projectCount;
        internal CityUrbanDistrictRequest(string id, Vector3 representativeCenter, int projectCount)
        { this.id = id; this.representativeCenter = representativeCenter; this.projectCount = projectCount; }
    }

    internal sealed class CityUrbanDistrictPresentation
    {
        internal readonly string districtId;
        internal readonly Vector3 representativeCenter;
        internal readonly float derivedCoverageRadius;
        internal readonly IList<CityUrbanPlot> plots;
        internal CityUrbanDistrictPresentation(string districtId, Vector3 center, float radius,
            List<CityUrbanPlot> plots)
        {
            this.districtId = districtId;
            representativeCenter = center;
            derivedCoverageRadius = radius;
            this.plots = plots.AsReadOnly();
        }
    }

    internal sealed class CityUrbanPlot
    {
        internal readonly string sourceBuildingId;
        internal readonly Vector3 worldPosition;
        internal readonly Vector3 size;
        internal readonly float yaw;
        internal CityUrbanPlot(string id, Vector3 position, Vector3 size, float yaw)
        { sourceBuildingId = id; worldPosition = position; this.size = size; this.yaw = yaw; }
    }

    internal sealed class CityUrbanBuildingPresentation
    {
        internal readonly string sourceBuildingId;
        internal readonly string districtId;
        internal readonly bool isNativeProjectParcel;
        internal readonly Vector3 worldPosition;
        internal readonly Vector3 size;
        internal readonly float yaw, height;
        internal readonly int levels;
        internal CityUrbanBuildingPresentation(string id, string district, bool project,
            Vector3 position, Vector3 size, float yaw, float height, int levels)
        {
            sourceBuildingId = id; districtId = district; isNativeProjectParcel = project;
            worldPosition = position; this.size = size; this.yaw = yaw;
            this.height = height; this.levels = levels;
        }
    }

    internal sealed class CityUrbanUtilityKind
    {
        internal readonly string name;
        internal readonly float radius;
        internal CityUrbanUtilityKind(string name, float radius) { this.name = name; this.radius = radius; }
    }

    internal sealed class CityUrbanUtilityPosition
    {
        internal readonly string districtId, utilityKind, sourceBuildingId;
        internal readonly Vector3 worldPosition;
        internal readonly float radius;
        internal CityUrbanUtilityPosition(string districtId, string utilityKind, string sourceBuildingId,
            Vector3 worldPosition, float radius)
        {
            this.districtId = districtId;
            this.utilityKind = utilityKind;
            this.sourceBuildingId = sourceBuildingId;
            this.worldPosition = worldPosition;
            this.radius = radius;
        }
    }

    internal sealed class CityUrbanBuildingVolume
    {
        internal readonly string sourceBuildingId;
        internal readonly float height;
        internal readonly CityBasemapPoint[] outline;
        internal CityUrbanBuildingVolume(string sourceBuildingId, float height, CityBasemapPoint[] outline)
        {
            this.sourceBuildingId = sourceBuildingId;
            this.height = height;
            this.outline = outline;
        }
    }
}