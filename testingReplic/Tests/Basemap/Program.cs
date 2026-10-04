using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            Resources.RootDirectory = AppContext.BaseDirectory;
            CityBasemap map = CityBasemap.LoadFromResources();
            Check(map.schemaVersion == 1 && map.roads.Length == 4032 &&
                map.buildings.Length == 12000 && map.areas.Length == 528,
                "actual OSM basemap schema/counts");
            Check(map.EffectiveOriginLatitude == 31.515 && map.EffectiveOriginLongitude == 34.45 &&
                Math.Abs(map.EffectiveUnitsPerKilometre - 50f) < .001f, "source projection metadata");
            Check(map.EffectiveAttribution.Contains("OpenStreetMap") &&
                map.EffectiveSourceUrl.Contains("openstreetmap.org"), "ODbL attribution/source URL");

            var requests = new List<CityUrbanDistrictRequest>();
            // The final GameCatalog definition is the separate Rashid route presentation;
            // the requested sourced parcels cover its twelve inland project districts.
            Check(GameCatalog.FinalDistrictIndex == 12, "current catalog has twelve project neighborhoods");
            for (int i = 0; i < GameCatalog.FinalDistrictIndex; i++)
            {
                DistrictLocation district = GameGeography.Districts[i];
                Check(GameCatalog.Districts[i].id == district.id, "catalog id matches sourced geography point");
                GeoPoint point = GameGeography.Project(district.latitude, district.longitude);
                requests.Add(new CityUrbanDistrictRequest(district.id,
                    new Vector3(point.x, 0f, point.z), GameCatalog.Districts[i].projects.Length));
                Check(GameCatalog.Districts[i].projects.Length == 9,
                    district.id + " project count comes from GameCatalog");
            }
            var geometry = new CityGeometry();
            var models = new CityModelLibrary();
            for (int i = 0; i < map.buildings.Length; i++)
                models.SourceBuildingByCenter.Add(CityModelLibrary.CenterKey(
                    map.buildings[i].center.x, map.buildings[i].center.z), map.buildings[i].FeatureId);
            var parent = new GameObject("stub parent").transform;
            var context = CityUrbanContext.Build(map, geometry, models, parent,
                new Material { name = "urban" }, new Material { name = "open" },
                new Material { name = "major road" }, new Material { name = "local road" },
                new Material { name = "footprints" }, requests);

            VerifyParcelSites(map, context, requests);
            VerifyUtilitySites(map, context, requests);
            VerifyExtrusions(map, context, models, requests);
            VerifyExtent(map);
            Check(models.ContextModelCopies <= CityUrbanContext.MaxAuthoredModelCopies, "authored low-LOD copy cap");
            Check(models.ContextModelCopies >= 4000, "dense authored destroyed buildings, not merely a raised unused cap");
            for (int i = 0; i < models.ContextPlacements.Count; i++)
                Check(models.ContextPlacements[i].footprintIsLocal,
                    "context placement requests oriented local footprint sizing");
            Check(CityRuinProfiles.ContextModelKeys.All(key => models.ContextModelKeys.Contains(key)) &&
                !models.ContextModelKeys.Contains("apartment_context"),
                "all four destroyed context LODs reachable; no intact starting background");
            Check(context.EstimatedContextTriangles <= CityUrbanContext.MaxContextTriangles,
                "700k estimated context triangle budget");
            Check(context.AllContextBuildings.Count == map.buildings.Length - requests.Count * 9,
                "project parcels removed from static context building list");
            Check(context.GetDistrictContextCandidates("shujaiya", 12).Count == 12,
                "per-district context candidate API");

            VerifyInvalidInputs();
            Vector3 preferredDepot = MeanCenter(requests);
            Vector3 depot = context.GetDepotPosition(preferredDepot, 1.5f);
            VerifyDepotPosition(map, context, depot, 1.5f);
            string exportPath = SaveProofExport(map, context, models, requests, depot);
            Console.WriteLine("Basemap source/math checks passed: " + assertions +
                " assertions; " + requests.Count + " districts, " + (requests.Count * 9) +
                " unique plots, " +
                models.ContextModelCopies + " context LOD copies; proof export " + exportPath + ".");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string SaveProofExport(CityBasemap map, CityUrbanContext context,
        CityModelLibrary models, IList<CityUrbanDistrictRequest> requests, Vector3 depot)
    {
        var districts = new List<ProofDistrict>();
        for (int i = 0; i < requests.Count; i++)
        {
            CityUrbanDistrictRequest request = requests[i];
            CityUrbanDistrictPresentation presentation = context.GetDistrictPresentation(request.id);
            var plots = new List<ProofParcel>();
            for (int p = 0; p < presentation.plots.Count; p++)
            {
                CityUrbanPlot plot = presentation.plots[p];
                plots.Add(new ProofParcel
                {
                    sourceBuildingId = plot.sourceBuildingId,
                    worldPosition = ToProofVector(plot.worldPosition),
                    size = ToProofVector(plot.size),
                    yaw = plot.yaw
                });
            }
            var utilities = new List<ProofUtility>();
            IList<CityUrbanUtilityPosition> placements = context.GetUtilityPositions(request.id);
            for (int u = 0; u < placements.Count; u++)
            {
                CityUrbanUtilityPosition placement = placements[u];
                utilities.Add(new ProofUtility
                {
                    kind = placement.utilityKind,
                    sourceBuildingId = placement.sourceBuildingId,
                    worldPosition = ToProofVector(placement.worldPosition),
                    radius = placement.radius
                });
            }
            districts.Add(new ProofDistrict
            {
                id = request.id,
                derivedCoverageRadius = presentation.derivedCoverageRadius,
                plots = plots,
                utilityPositions = utilities
            });
        }

        var contextModels = new List<ProofModelPlacement>();
        for (int i = 0; i < models.ContextPlacements.Count; i++)
        {
            ContextModelPlacement placement = models.ContextPlacements[i];
            contextModels.Add(new ProofModelPlacement
            {
                sourceBuildingId = placement.sourceBuildingId,
                key = placement.key,
                worldPosition = ToProofVector(placement.worldPosition),
                size = ToProofVector(placement.size),
                yaw = placement.yaw,
                height = placement.height,
                footprintIsLocal = placement.footprintIsLocal
            });
        }
        var volumes = new List<ProofPolygonVolume>();
        for (int i = 0; i < context.ExtrudedBuildingVolumes.Count; i++)
        {
            CityUrbanBuildingVolume volume = context.ExtrudedBuildingVolumes[i];
            var outline = new List<ProofCoordinate>(volume.outline.Length);
            for (int p = 0; p < volume.outline.Length; p++)
                outline.Add(new ProofCoordinate { x = volume.outline[p].x, z = volume.outline[p].z });
            volumes.Add(new ProofPolygonVolume
            {
                sourceBuildingId = volume.sourceBuildingId,
                height = volume.height,
                outline = outline
            });
        }
        var document = new ProofDocument
        {
            schema = 1,
            attribution = map.EffectiveAttribution,
            sourceUrl = map.EffectiveSourceUrl,
            actualBounds = new ProofBounds
            {
                minX = map.actualBounds.minX, maxX = map.actualBounds.maxX,
                minZ = map.actualBounds.minZ, maxZ = map.actualBounds.maxZ
            },
            depotPosition = ToProofVector(depot),
            depotClearanceRadius = 1.5f,
            authoredModelCopyCount = models.ContextModelCopies,
            maxAuthoredModelCopies = CityUrbanContext.MaxAuthoredModelCopies,
            estimatedContextTriangles = context.EstimatedContextTriangles,
            maxContextTriangles = CityUrbanContext.MaxContextTriangles,
            roadCount = map.roads.Length,
            buildingFootprintCount = map.buildings.Length,
            areaPolygonCount = map.areas.Length,
            districts = districts,
            contextModelPlacements = contextModels,
            polygonVolumeIds = volumes
        };
        var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        string json = JsonSerializer.Serialize(document, options);
        string root = FindWorkspaceRoot();
        string directory = Path.Combine(root, "exports", "models");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Gaza-City-Production-Placement.json");
        File.WriteAllText(path, json);
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string FindWorkspaceRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "testingReplic", "Assets",
                "NewGaza", "Resources", "GazaBasemap.json"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the workspace containing GazaBasemap.json.");
    }

    private static ProofVector ToProofVector(Vector3 vector)
    {
        return new ProofVector { x = vector.x, y = vector.y, z = vector.z };
    }

    private static void VerifyParcelSites(CityBasemap map, CityUrbanContext context,
        IList<CityUrbanDistrictRequest> requests)
    {
        var byId = new Dictionary<string, CityBasemapBuilding>(StringComparer.Ordinal);
        for (int i = 0; i < map.buildings.Length; i++) byId.Add(map.buildings[i].FeatureId, map.buildings[i]);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var allPlots = new List<CityUrbanPlot>();
        for (int d = 0; d < requests.Count; d++)
        {
            CityUrbanDistrictRequest request = requests[d];
            CityUrbanDistrictPresentation presentation = context.GetDistrictPresentation(request.id);
            Check(presentation.plots.Count == 9, request.id + " has nine project parcels");
            Check(presentation.derivedCoverageRadius > 0f, request.id + " has derived coverage extent");
            Check(VectorNear(presentation.representativeCenter, request.representativeCenter),
                request.id + " keeps the representative center");
            IList<CityUrbanPlot> plots = context.GetDistrictPlots(request.representativeCenter, request.id, 9);
            for (int p = 0; p < plots.Count; p++)
            {
                CityUrbanPlot plot = plots[p];
                Check(selected.Add(plot.sourceBuildingId), "project parcel id is unique citywide");
                CityBasemapBuilding building = byId[plot.sourceBuildingId];
                Check(IsOwnedBy(requests, d, building), request.id + " parcel remains inside nearest-center Voronoi cell");
                Check(!OverlapsRoad(building, map.roads), request.id + " parcel clears actual road ribbons");
                Check(plot.size.x > 0f && plot.size.z > 0f &&
                    IsFinite(plot.worldPosition.x) && IsFinite(plot.worldPosition.z),
                    request.id + " parcel has finite 3D footprint placement");
                allPlots.Add(plot);
            }
        }
        Check(selected.Count == requests.Count * 9,
            "all catalog project placements use unique sourced footprints");
        for (int i = 0; i < allPlots.Count; i++)
            for (int j = i + 1; j < allPlots.Count; j++)
                Check(!Overlaps(allPlots[i], allPlots[j]),
                    "selected project parcels have non-overlapping oriented footprints");
    }

    private static void VerifyUtilitySites(CityBasemap map, CityUrbanContext context,
        IList<CityUrbanDistrictRequest> requests)
    {
        for (int d = 0; d < requests.Count; d++)
        {
            string id = requests[d].id;
            IList<CityUrbanUtilityPosition> placements = context.GetUtilityPositions(id);
            Check(placements.Count == 3, id + " has three utility positions");
            for (int slot = 0; slot < 3; slot++)
            {
                string kind = slot == 0 ? "salvage" : slot == 1 ? "badge" : "crane";
                float expectedRadius = slot == 0 ? .30f : slot == 1 ? .07f : .30f;
                CityUrbanUtilityPosition utility = null;
                for (int i = 0; i < placements.Count; i++)
                    if (placements[i].utilityKind == kind) utility = placements[i];
                Check(utility != null && Math.Abs(utility.radius - expectedRadius) < .0001f,
                    id + " " + kind + " uses expected utility radius");
                Check(VectorNear(context.GetUtilityPosition(id, slot), utility.worldPosition),
                    id + " main integration utility slot maps to sourced location");
                Check(IsNearSourcedParcel(map, utility),
                    id + " utility is close to its reserved project parcel");
                Check(!UtilityOverlapsRoad(map.roads, utility),
                    id + " utility clears all actual road ribbons");
                Check(!UtilityOverlapsAnyBuilding(map.buildings, utility),
                    id + " utility clears every actual building footprint");
                for (int p = 0; p < placements.Count; p++)
                    if (placements[p].utilityKind != kind)
                        Check(!CircleOverlap(utility, placements[p]),
                            id + " utility circles do not overlap each other");
                for (int otherDistrict = 0; otherDistrict < d; otherDistrict++)
                {
                    IList<CityUrbanUtilityPosition> earlier =
                        context.GetUtilityPositions(requests[otherDistrict].id);
                    for (int p = 0; p < earlier.Count; p++)
                        Check(!CircleOverlap(utility, earlier[p]),
                            "utility circles do not overlap citywide");
                }
            }
        }
    }

    private static bool IsNearSourcedParcel(CityBasemap map, CityUrbanUtilityPosition utility)
    {
        for (int i = 0; i < map.buildings.Length; i++)
        {
            CityBasemapBuilding source = map.buildings[i];
            if (source.FeatureId != utility.sourceBuildingId) continue;
            float dx = utility.worldPosition.x - source.center.x;
            float dz = utility.worldPosition.z - source.center.z;
            float reach = (float)Math.Sqrt(source.size.x * source.size.x +
                source.size.z * source.size.z) + utility.radius + 1.3f;
            return dx * dx + dz * dz <= reach * reach;
        }
        return false;
    }

    private static bool UtilityOverlapsRoad(CityBasemapRoad[] roads, CityUrbanUtilityPosition utility)
    {
        for (int r = 0; r < roads.Length; r++)
            for (int p = 1; p < roads[r].points.Length; p++)
                if (PointSegmentDistance(utility.worldPosition.x, utility.worldPosition.z,
                    roads[r].points[p - 1].x, roads[r].points[p - 1].z,
                    roads[r].points[p].x, roads[r].points[p].z) <
                    utility.radius + roads[r].width * .5f + .015f) return true;
        return false;
    }

    private static bool UtilityOverlapsAnyBuilding(CityBasemapBuilding[] buildings,
        CityUrbanUtilityPosition utility)
    {
        for (int i = 0; i < buildings.Length; i++)
            if (CircleOverlapsFootprint(utility.worldPosition.x, utility.worldPosition.z,
                utility.radius + .015f, buildings[i].outline)) return true;
        return false;
    }

    private static bool CircleOverlapsFootprint(float x, float z, float radius,
        CityBasemapPoint[] polygon)
    {
        bool inside = false;
        float minDistance = float.MaxValue;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            CityBasemapPoint a = polygon[previous], b = polygon[i];
            if ((a.z > z) != (b.z > z) &&
                x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x) inside = !inside;
            float distance = PointSegmentDistance(x, z, a.x, a.z, b.x, b.z);
            minDistance = Math.Min(minDistance, distance);
        }
        return inside || minDistance < radius;
    }

    private static bool CircleOverlap(CityUrbanUtilityPosition a, CityUrbanUtilityPosition b)
    {
        float dx = a.worldPosition.x - b.worldPosition.x;
        float dz = a.worldPosition.z - b.worldPosition.z;
        float required = a.radius + b.radius + .03f;
        return dx * dx + dz * dz < required * required;
    }

    private static void VerifyExtrusions(CityBasemap map, CityUrbanContext context,
        CityModelLibrary models, IList<CityUrbanDistrictRequest> requests)
    {
        var buildingsById = new Dictionary<string, CityBasemapBuilding>(StringComparer.Ordinal);
        for (int i = 0; i < map.buildings.Length; i++)
            buildingsById.Add(map.buildings[i].FeatureId, map.buildings[i]);
        var projectIds = new HashSet<string>(StringComparer.Ordinal);
        for (int d = 0; d < requests.Count; d++)
            foreach (CityUrbanPlot plot in context.GetDistrictPlots(requests[d].id, 9))
                projectIds.Add(plot.sourceBuildingId);
        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < models.ContextPlacements.Count; i++)
            modelIds.Add(models.ContextPlacements[i].sourceBuildingId);
        Check(modelIds.Count == models.ContextPlacements.Count,
            "each selected authored LOD corresponds to a unique source building id");
        var volumeIds = new HashSet<string>(StringComparer.Ordinal);
        IList<CityUrbanBuildingVolume> volumes = context.ExtrudedBuildingVolumes;
        Check(volumes.Count == map.buildings.Length - projectIds.Count - modelIds.Count,
            "every nonproject, non-LOD source building has a polygon volume");
        for (int i = 0; i < volumes.Count; i++)
        {
            CityUrbanBuildingVolume volume = volumes[i];
            CityBasemapBuilding source = buildingsById[volume.sourceBuildingId];
            Check(volumeIds.Add(volume.sourceBuildingId), "polygon volume source id is unique");
            Check(!projectIds.Contains(volume.sourceBuildingId),
                "native parcel is not duplicated by a footprint volume");
            Check(!modelIds.Contains(volume.sourceBuildingId),
                "authored LOD building is not duplicated by a footprint volume");
            Check(Math.Abs(volume.height - source.height) < .000001f,
                 "polygon volume retains exact source height as metadata, not as an intact roof");
            Check(volume.outline.Length == source.outline.Length,
                "polygon volume keeps exact source outline vertex count");
            for (int p = 0; p < source.outline.Length; p++)
                Check(Math.Abs(volume.outline[p].x - source.outline[p].x) < .000001f &&
                    Math.Abs(volume.outline[p].z - source.outline[p].z) < .000001f,
                    "polygon volume outline is the actual OSM outline");
        }
        Check(volumeIds.Count + modelIds.Count + projectIds.Count == map.buildings.Length,
            "source buildings partition into native parcels, authored LODs, and extrusions");

        int roofFaces = 0, wallFaces = 0, footprintMeshes = 0;
        for (int i = 0; i < MeshRenderer.Captured.Count; i++)
        {
            MeshRenderer renderer = MeshRenderer.Captured[i];
            if (renderer.sharedMaterial == null || renderer.sharedMaterial.name != "footprints") continue;
            footprintMeshes++;
            Mesh mesh = renderer.gameObject.GetComponent<MeshFilter>().sharedMesh;
            for (int triangle = 0; triangle < mesh.triangles.Count; triangle += 3)
            {
                Vector3 a = mesh.vertices[mesh.triangles[triangle]];
                Vector3 b = mesh.vertices[mesh.triangles[triangle + 1]];
                Vector3 c = mesh.vertices[mesh.triangles[triangle + 2]];
                float lowY = Math.Min(a.y, Math.Min(b.y, c.y));
                float highY = Math.Max(a.y, Math.Max(b.y, c.y));
                float nx = (b.y - a.y) * (c.z - a.z) - (b.z - a.z) * (c.y - a.y);
                float ny = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);
                float nz = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (highY - lowY < .00001f && lowY > -.0899f)
                {
                    Check(ny > .000001f, "collapsed floor triangles face upward");
                    Check(Math.Abs(highY - (-.09f + .025f)) < .00001f,
                        "every horizontal building surface is a low collapsed slab; no intact elevated roofs");
                    roofFaces++;
                }
                else if (lowY <= -.0899f && highY > lowY + .0001f)
                {
                    Check(Math.Abs(ny) < .0001f && nx * nx + nz * nz > .000000000001f,
                        "extruded building wall triangle is vertical with a real normal");
                    wallFaces++;
                }
            }
        }
        Check(footprintMeshes > 0 && roofFaces > 0 && wallFaces > 0,
             "static chunk meshes contain collapsed slabs and broken walls");
        Check(context.EstimatedContextTriangles <= CityUrbanContext.MaxContextTriangles,
            "source extrusion plus affordable LODs stay under triangle ceiling");
    }

    private static Vector3 MeanCenter(IList<CityUrbanDistrictRequest> requests)
    {
        float x = 0f, z = 0f;
        for (int i = 0; i < requests.Count; i++)
        { x += requests[i].representativeCenter.x; z += requests[i].representativeCenter.z; }
        return new Vector3(x / requests.Count, 0f, z / requests.Count);
    }

    private static void VerifyDepotPosition(CityBasemap map, CityUrbanContext context,
        Vector3 depot, float radius)
    {
        Check(depot.x >= map.actualBounds.minX && depot.x <= map.actualBounds.maxX &&
            depot.z >= map.actualBounds.minZ && depot.z <= map.actualBounds.maxZ,
            "depot placement lies within measured city feature bounds");
        var depotCircle = new CityUrbanUtilityPosition("depot", "depot", "depot",
            depot, radius);
        Check(!UtilityOverlapsRoad(map.roads, depotCircle),
            "depot clearance excludes all actual road ribbons");
        Check(!UtilityOverlapsAnyBuilding(map.buildings, depotCircle),
            "depot clearance excludes all actual source footprints");
        float nearestRoadEdge = float.MaxValue;
        for (int r = 0; r < map.roads.Length; r++)
            for (int p = 1; p < map.roads[r].points.Length; p++)
                nearestRoadEdge = Math.Min(nearestRoadEdge,
                    PointSegmentDistance(depot.x, depot.z,
                        map.roads[r].points[p - 1].x, map.roads[r].points[p - 1].z,
                        map.roads[r].points[p].x, map.roads[r].points[p].z) - map.roads[r].width * .5f);
        Check(nearestRoadEdge >= radius + .019f && nearestRoadEdge <= radius + 1.501f,
            "depot pad is clear and close to an actual road edge");
        Check(context.EstimatedContextTriangles <= CityUrbanContext.MaxContextTriangles,
            "depot placement does not alter context geometry budget");
    }

    private static bool IsOwnedBy(IList<CityUrbanDistrictRequest> requests, int owner,
        CityBasemapBuilding building)
    {
        float nearest = float.MaxValue;
        int winner = -1;
        for (int i = 0; i < requests.Count; i++)
        {
            float dx = building.center.x - requests[i].representativeCenter.x;
            float dz = building.center.z - requests[i].representativeCenter.z;
            float distance = dx * dx + dz * dz;
            if (distance < nearest) { nearest = distance; winner = i; }
        }
        return winner == owner;
    }

    private static bool OverlapsRoad(CityBasemapBuilding building, CityBasemapRoad[] roads)
    {
        float required = (float)Math.Sqrt(building.size.x * building.size.x +
            building.size.z * building.size.z) * .5f + .08f;
        for (int r = 0; r < roads.Length; r++)
            for (int p = 1; p < roads[r].points.Length; p++)
                if (PointSegmentDistance(building.center.x, building.center.z,
                    roads[r].points[p - 1].x, roads[r].points[p - 1].z,
                    roads[r].points[p].x, roads[r].points[p].z) <
                    required + roads[r].width * .5f) return true;
        return false;
    }

    private static float PointSegmentDistance(float x, float z, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float length = dx * dx + dz * dz;
        float t = length < .000001f ? 0f :
            Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / length));
        float px = ax + t * dx - x, pz = az + t * dz - z;
        return (float)Math.Sqrt(px * px + pz * pz);
    }

    private static bool Overlaps(CityUrbanPlot a, CityUrbanPlot b)
    {
        Vector2[] axesA = Axes(a.yaw), axesB = Axes(b.yaw);
        for (int i = 0; i < 4; i++)
        {
            Vector2 axis = i < 2 ? axesA[i] : axesB[i - 2];
            float distance = Math.Abs((a.worldPosition.x - b.worldPosition.x) * axis.x +
                (a.worldPosition.z - b.worldPosition.z) * axis.y);
            float radiusA = ProjectionRadius(a.size, axesA, axis);
            float radiusB = ProjectionRadius(b.size, axesB, axis);
            if (distance >= radiusA + radiusB - .0001f) return false;
        }
        return true;
    }

    private static Vector2[] Axes(float yaw)
    {
        float radians = yaw * (float)Math.PI / 180f;
        float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
        return new[] { new Vector2(c, s), new Vector2(-s, c) };
    }

    private static float ProjectionRadius(Vector3 size, Vector2[] axes, Vector2 axis)
    {
        return size.x * .5f * Math.Abs(Vector2.Dot(axis, axes[0])) +
            size.z * .5f * Math.Abs(Vector2.Dot(axis, axes[1]));
    }

    private static void VerifyExtent(CityBasemap map)
    {
        CityBasemapExtent extent = map.actualBounds;
        Check(IsFinite(extent.minX) && IsFinite(extent.maxX) &&
            IsFinite(extent.minZ) && IsFinite(extent.maxZ), "actual map extent is finite");
        Check(extent.minX < -227f && extent.maxX > 189f &&
            extent.minZ < -222f && extent.maxZ > 261f, "camera framing extent covers complete source bounds");
    }

    private static void VerifyInvalidInputs()
    {
        ExpectFailure(() => CityBasemap.LoadFromResources("missing-basemap"),
            "missing Resources basemap throws");
        ExpectFailure(() => CityBasemap.ParseAndValidate("", "empty fixture"),
            "empty JSON throws");
        ExpectFailure(() => CityBasemap.ParseAndValidate("{", "malformed fixture"),
            "malformed JSON throws");
        ExpectFailure(() => CityBasemap.ParseAndValidate("{}", "missing schema fixture"),
            "missing schema/features throw");
        const string validHeaderWithoutRoads =
            "{\"schemaVersion\":1,\"metadata\":{\"projection\":{\"originLatitude\":31.515," +
            "\"originLongitude\":34.45,\"unitsPerKilometre\":50}},\"buildings\":[],\"areas\":[]}";
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(validHeaderWithoutRoads,
            "missing feature fixture"), "roads is missing", "missing required road array is explicit");
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(
            "{\"schema\":1,\"originLatitude\":31.515,\"originLongitude\":34.45,\"unitsPerKm\":50}",
            "flat origin fixture"), "roads is missing",
            "flat header ignores materialized absent legacy origin");
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(
            "{\"schema\":1,\"origin\":{\"latitude\":31.515,\"longitude\":34.45},\"unitsPerKm\":50}",
            "legacy origin fixture"), "roads is missing", "explicit legacy origin remains supported");
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(
            validHeaderWithoutRoads.Replace("\"projection\":",
                "\"origin\":{\"latitude\":0,\"longitude\":0},\"projection\":"),
            "nested alternate origin fixture"), "roads is missing",
            "nested origin key is not treated as a top-level origin");
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(
            validHeaderWithoutRoads.Insert(1, "\"origin\":{\"latitude\":0,\"longitude\":0},"),
            "explicit wrong origin fixture"), "origin must be",
            "provided invalid origin is not silently repaired by metadata");
        ExpectFailureContains(() => CityBasemap.ParseAndValidate(
            validHeaderWithoutRoads.Replace("31.515", "31.600"),
            "wrong metadata origin fixture"), "origin must be", "wrong metadata origin is rejected");
        string valid = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GazaBasemap.json"));
        int marker = valid.IndexOf("\"schemaVersion\":1", StringComparison.Ordinal);
        Check(marker >= 0, "schema version marker exists");
        string badSchema = valid.Substring(0, marker) +
            valid.Substring(marker).Replace("\"schemaVersion\":1", "\"schemaVersion\":2",
                StringComparison.Ordinal);
        ExpectFailure(() => CityBasemap.ParseAndValidate(badSchema, "wrong schema fixture"),
            "unsupported schema throws");
        string badNumber = valid.Replace("\"unitsPerKilometre\":50.0", "\"unitsPerKilometre\":NaN");
        ExpectFailure(() => CityBasemap.ParseAndValidate(badNumber, "non-finite fixture"),
            "non-finite projection throws");
    }

    private static bool VectorNear(Vector3 a, Vector3 b)
    {
        return Math.Abs(a.x - b.x) < .001f && Math.Abs(a.z - b.z) < .001f;
    }
    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAILED: " + label);
    }
    private static void ExpectFailure(Action action, string label)
    {
        try { action(); }
        catch (Exception) { Check(true, label); return; }
        Check(false, label);
    }
    private static void ExpectFailureContains(Action action, string message, string label)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains(message), label);
            return;
        }
        Check(false, label);
    }

    private sealed class ProofDocument
    {
        public int schema { get; set; }
        public string attribution { get; set; }
        public string sourceUrl { get; set; }
        public ProofBounds actualBounds { get; set; }
        public ProofVector depotPosition { get; set; }
        public float depotClearanceRadius { get; set; }
        public int authoredModelCopyCount { get; set; }
        public int maxAuthoredModelCopies { get; set; }
        public int estimatedContextTriangles { get; set; }
        public int maxContextTriangles { get; set; }
        public int roadCount { get; set; }
        public int buildingFootprintCount { get; set; }
        public int areaPolygonCount { get; set; }
        public List<ProofDistrict> districts { get; set; }
        public List<ProofModelPlacement> contextModelPlacements { get; set; }
        public List<ProofPolygonVolume> polygonVolumeIds { get; set; }
    }
    private sealed class ProofBounds
    {
        public float minX, maxX, minZ, maxZ;
    }
    private sealed class ProofDistrict
    {
        public string id { get; set; }
        public float derivedCoverageRadius { get; set; }
        public List<ProofParcel> plots { get; set; }
        public List<ProofUtility> utilityPositions { get; set; }
    }
    private sealed class ProofParcel
    {
        public string sourceBuildingId { get; set; }
        public ProofVector worldPosition { get; set; }
        public ProofVector size { get; set; }
        public float yaw { get; set; }
    }
    private sealed class ProofUtility
    {
        public string kind { get; set; }
        public string sourceBuildingId { get; set; }
        public ProofVector worldPosition { get; set; }
        public float radius { get; set; }
    }
    private sealed class ProofModelPlacement
    {
        public string sourceBuildingId { get; set; }
        public string key { get; set; }
        public ProofVector worldPosition { get; set; }
        public ProofVector size { get; set; }
        public float yaw { get; set; }
        public float height { get; set; }
        public bool footprintIsLocal { get; set; }
    }
    private sealed class ProofPolygonVolume
    {
        public string sourceBuildingId { get; set; }
        public float height { get; set; }
        public List<ProofCoordinate> outline { get; set; }
    }
    private sealed class ProofCoordinate
    {
        public float x, z;
    }
    private sealed class ProofVector
    {
        public float x, y, z;
    }
}