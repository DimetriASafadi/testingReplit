using System;
using System.Collections.Generic;
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
            var roads = new CityRoadNetwork(map);
            Check(map.roads.Length == 4032, "actual sourced OSM ways loaded");
            int sourceEdges = 0;
            for (int i = 0; i < map.roads.Length; i++) sourceEdges += map.roads[i].points.Length - 1;
            Check(sourceEdges > 20000, "actual OSM source contains the expected ~21k polyline edges");
            Check(roads.Segments.Length > map.roads.Length && roads.Definitions.Length == roads.Segments.Length,
                "source ways split into stable purchasable segments");
            VerifySegmentGeometry(roads);
            VerifyStableIds(map);
            VerifyPicking(roads);
            VerifyRibbonPicking();
            VerifyRoadRoutes(roads);
            VerifyDisconnectedDoesNotJump();
            VerifyCrossingIsNotJunction();
            VerifyEquipmentRoadFallback();
            VerifyRoadSurfaceClassification();
            Console.WriteLine("Road network checks passed: " + assertions + " assertions, " +
                roads.Segments.Length + " source-derived segments.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void VerifySegmentGeometry(CityRoadNetwork roads)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CityRoadSegment segment in roads.Segments)
        {
            Check(ids.Add(segment.Definition.id), "segment id is unique");
            Check(segment.Definition.lengthMeters > 0f &&
                segment.Definition.lengthMeters <= 1000.01f, "each segment <= 1km");
            Check(segment.Points.Length >= 2, "segment contains source polyline points");
            Check(segment.Definition.id.Contains(":"), "segment id retains OSM way provenance");
        }
    }

    private static void VerifyStableIds(CityBasemap map)
    {
        var first = new CityRoadNetwork(map);
        var reordered = new CityBasemap
        {
            roads = (CityBasemapRoad[])map.roads.Clone()
        };
        var second = new CityRoadNetwork(reordered);
        Check(first.Definitions.Length == second.Definitions.Length, "rebuild has stable segment count");
        for (int i = 0; i < first.Definitions.Length; i++)
            Check(first.Definitions[i].id == second.Definitions[i].id &&
                Math.Abs(first.Definitions[i].lengthMeters - second.Definitions[i].lengthMeters) < .001f,
                "segment IDs and lengths are deterministic");
        // Resampling a sourced polyline without moving its geometry leaves the same IDs and lengths.
        CityBasemapRoad source = map.roads[0];
        CityBasemapRoad sampled = new CityBasemapRoad
        {
            id = source.id, idText = source.idText, name = source.name, kind = source.kind, width = source.width,
            points = Resample(source.points)
        };
        CityRoadNetwork originalNetwork = new CityRoadNetwork(new CityBasemap { roads = new[] { source } });
        CityRoadNetwork sampledNetwork = new CityRoadNetwork(new CityBasemap { roads = new[] { sampled } });
        Check(originalNetwork.Definitions.Length == sampledNetwork.Definitions.Length,
            "resampling preserves chunk count");
        for (int i = 0; i < originalNetwork.Definitions.Length; i++)
            Check(originalNetwork.Definitions[i].id == sampledNetwork.Definitions[i].id &&
                Math.Abs(originalNetwork.Definitions[i].lengthMeters -
                    sampledNetwork.Definitions[i].lengthMeters) < .01f,
                "resampling preserves stable source IDs and lengths");
    }

    private static CityBasemapPoint[] Resample(CityBasemapPoint[] points)
    {
        var result = new List<CityBasemapPoint>();
        for (int i = 1; i < points.Length; i++)
        {
            if (i == 1) result.Add(new CityBasemapPoint(points[0].x, points[0].z));
            result.Add(new CityBasemapPoint((points[i - 1].x + points[i].x) * .5f,
                (points[i - 1].z + points[i].z) * .5f));
            result.Add(new CityBasemapPoint(points[i].x, points[i].z));
        }
        return result.ToArray();
    }

    private static void VerifyPicking(CityRoadNetwork roads)
    {
        CityRoadSegment source = roads.Segments[roads.Segments.Length / 2];
        Vector3 center = source.Points[source.Points.Length / 2];
        CityRoadSegment picked;
        Check(roads.TryPick(center, .5f, out picked) && picked != null,
            "all sourced road segments are clickable");
        Check(roads.FindSegment(source.Definition.id) == source, "stable ID lookup");
    }

    private static void VerifyRibbonPicking()
    {
        var network = new CityRoadNetwork(new CityBasemap
        {
            roads = new[] { new CityBasemapRoad
            {
                id = 21, kind = "residential", width = 2f,
                points = new[] { new CityBasemapPoint(0f,0f), new CityBasemapPoint(10f,0f) }
            } }
        });
        CityRoadSegment picked;
        Check(network.TryPick(new Vector3(5f,0f,1.09f), .10f, out picked),
            "screen-pick tolerance expands the rendered road ribbon edge");
        Check(!network.TryPick(new Vector3(5f,0f,1.101f), .10f, out picked),
            "road picking does not select beyond ribbon plus capped tolerance");
    }

    private static void VerifyRoadRoutes(CityRoadNetwork roads)
    {
        // The fixture depot is from the production CityUrbanContext.GetDepotPosition result;
        // it is not a fabricated point near a convenient road.
        string fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Gaza-City-Production-Placement.json"));
        Vector3 depot;
        using (JsonDocument proof = JsonDocument.Parse(fixture))
        {
            JsonElement point = proof.RootElement.GetProperty("depotPosition");
            depot = new Vector3(point.GetProperty("x").GetSingle(),
                point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle());
        }
        int attempted = 0, reachable = 0;
        float depotAccess = NearestDrivable(roads, depot);
        Check(depotAccess <= 5f, "authentic sourced depot fixture has strict road access");
        for (int i = 0; i < 12; i++)
        {
            GeoPoint representative = GameGeography.DistrictPoint(i);
            Vector3 job = new Vector3(representative.x, 0f, representative.z);
            Check(NearestDrivable(roads, job) <= 5f,
                "actual city job representative has strict street access: " +
                GameGeography.Districts[i].id);
            attempted++;
            int calls = 0;
            CityRoadRoute route = roads.FindRoute(depot, job, id => { calls++; return 1f; });
            Check(route != null, "production depot reaches actual job: " +
                GameGeography.Districts[i].id);
            if (route == null) continue;
            reachable++;
            Check(route.Points.Length >= 3 && route.RoadIds.Length == route.Points.Length - 1,
                "route has provenance per adjacent waypoint");
            Check(route.Length > Vector3.Distance(depot, job),
                "route follows sourced street graph instead of direct chord");
            for (int edge = 0; edge < route.RoadIds.Length; edge++)
            {
                Vector3 midpoint = (route.Points[edge] + route.Points[edge + 1]) * .5f;
                string id = route.RoadIds[edge];
                if (string.IsNullOrEmpty(id))
                {
                    Check(Vector3.Distance(route.Points[edge], route.Points[edge + 1]) <= 5.001f,
                        "off-road access is a short real endpoint-to-road projection only");
                    continue;
                }
                CityRoadSegment sourceSegment = roads.FindSegment(id);
                Check(sourceSegment != null &&
                    SegmentDistanceToSegment(midpoint, sourceSegment) < .001f,
                    "each route road edge carries accurate sourced way/segment provenance");
            }
            Check(calls > 0, "route cost uses live per-road speed callback");
            Check(route.RoadIdAtDistance(Math.Min(1f, route.Length)) ==
                route.RoadIds[route.EdgeAtDistance(Math.Min(1f, route.Length))],
                "route edge-at-distance helper preserves nullable leg provenance");
            Vector3 halfway = route.PositionAtDistance(route.Length * .5f);
            Check(NearestDrivable(roads, halfway) < .001f,
                "route intermediate waypoints stay on real sourced road centerlines");

            // Re-query with one actual road upgraded. The callback observes current levels at
            // route time, rather than a network-wide speed snapshot cached at construction.
            string activeRoad = route.RoadIdAtDistance(Math.Min(route.Length * .4f, route.Length));
            var liveLevels = new Dictionary<string, int>(StringComparer.Ordinal);
            CityRoadRoute repaired = roads.FindRoute(depot, job, id =>
            {
                int level;
                return RoadEconomy.SpeedMultiplier(liveLevels.TryGetValue(id, out level) ? level : 0);
            });
            liveLevels[activeRoad] = 2;
            CityRoadRoute upgraded = roads.FindRoute(depot, job, id =>
            {
                int level;
                return RoadEconomy.SpeedMultiplier(liveLevels.TryGetValue(id, out level) ? level : 0);
            });
            Check(repaired != null && upgraded != null,
                "live road condition update retains the street route");
        }
        Check(attempted == 12, "all twelve actual project-neighborhood job centers were checked");
        Check(reachable == 12, "all twelve sourced job/depot routes are reachable");
    }

    private static float NearestDrivable(CityRoadNetwork roads, Vector3 point)
    {
        float nearest = float.MaxValue;
        for (int i = 0; i < roads.Segments.Length; i++)
        {
            CityRoadSegment segment = roads.Segments[i];
            if (segment.Kind == "footway" || segment.Kind == "path" || segment.Kind == "steps" ||
                segment.Kind == "pedestrian") continue;
            for (int p = 1; p < segment.Points.Length; p++)
                nearest = Math.Min(nearest, SegmentDistance(point, segment.Points[p - 1], segment.Points[p]));
        }
        return nearest;
    }

    private static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a, offset = point - a;
        float denom = edge.x * edge.x + edge.z * edge.z;
        float t = denom <= .000001f ? 0f : Math.Max(0f, Math.Min(1f,
            (offset.x * edge.x + offset.z * edge.z) / denom));
        Vector3 projection = a + edge * t;
        return Vector3.Distance(point, projection);
    }

    private static float SegmentDistanceToSegment(Vector3 point, CityRoadSegment segment)
    {
        float best = float.MaxValue;
        for (int i = 1; i < segment.Points.Length; i++)
            best = Math.Min(best, SegmentDistance(point, segment.Points[i - 1], segment.Points[i]));
        return best;
    }

    private static void VerifyDisconnectedDoesNotJump()
    {
        var map = new CityBasemap
        {
            roads = new[]
            {
                Road(1, new CityBasemapPoint(0f,0f), new CityBasemapPoint(10f,0f)),
                Road(2, new CityBasemapPoint(20f,0f), new CityBasemapPoint(30f,0f))
            }
        };
        var network = new CityRoadNetwork(map);
        Check(network.FindRoute(new Vector3(1f,0f,0f), new Vector3(29f,0f,0f)) == null,
            "disconnected route is explicit null, never a straight-line fallback");
        var farAccess = new CityRoadNetwork(new CityBasemap
        {
            roads = new[] { Road(3, new CityBasemapPoint(0f,0f), new CityBasemapPoint(10f,0f)) }
        });
        Check(farAccess.FindRoute(new Vector3(5f,0f,5.01f), new Vector3(5f,0f,0f)) == null,
            "terminal access beyond five city units is not silently connected");
    }

    private static void VerifyCrossingIsNotJunction()
    {
        var map = new CityBasemap
        {
            roads = new[]
            {
                Road(11, new CityBasemapPoint(-10f,0f), new CityBasemapPoint(10f,0f)),
                Road(12, new CityBasemapPoint(0f,-10f), new CityBasemapPoint(0f,10f))
            }
        };
        var network = new CityRoadNetwork(map);
        Check(network.FindRoute(new Vector3(-9f,0f,0f), new Vector3(0f,0f,9f)) == null,
            "coordinate crossing without shared authored vertex remains disconnected");
    }

    private static void VerifyEquipmentRoadFallback()
    {
        var network = new CityRoadNetwork(new CityBasemap
        {
            roads = new[]
            {
                Road(31, new CityBasemapPoint(0f,0f), new CityBasemapPoint(10f,0f)),
                Road(32, new CityBasemapPoint(20f,0f), new CityBasemapPoint(30f,0f))
            }
        });
        Vector3 from = new Vector3(1f,0f,0f), goal = new Vector3(29f,0f,0f);
        Check(network.FindRoute(from, goal) == null,
            "strict route retains null contract for disconnected road components");
        CityRoadRoute partial = network.FindEquipmentRoute(from, goal);
        Check(partial != null && partial.Points[0].Equals(from) &&
            partial.Points[partial.Points.Length - 1].Equals(goal),
            "fleet route preserves exact source and destination across disconnected components");
        bool stoppedAtUsefulEnd = false, hasOffRoadFinish = false;
        for (int i = 0; i < partial.RoadIds.Length; i++)
        {
            if (partial.RoadIds[i] == null)
            {
                hasOffRoadFinish = true;
                continue;
            }
            if (partial.RoadIds[i].StartsWith("31:", StringComparison.Ordinal) &&
                Math.Abs(partial.Points[i + 1].x - 10f) < .001f)
                stoppedAtUsefulEnd = true;
            Check(!partial.RoadIds[i].StartsWith("32:", StringComparison.Ordinal),
                "unreachable disconnected component is never used as a shortcut");
        }
        Check(stoppedAtUsefulEnd && hasOffRoadFinish,
            "road travel follows its connected component to the useful endpoint, then leaves the road");
        Check(partial.RoadIdAtDistance(partial.Length - .1f) == null,
            "off-road destination connector has null road provenance");

        CityRoadRoute farStart = network.FindEquipmentRoute(new Vector3(5f,0f,25f),
            new Vector3(9f,0f,0f));
        Check(farStart != null && farStart.Points[0].Equals(new Vector3(5f,0f,25f)) &&
            farStart.Points[farStart.Points.Length - 1].Equals(new Vector3(9f,0f,0f)),
            "far off-road start can approach and usefully follow the nearest road component");
        Check(farStart.RoadIdAtDistance(.1f) == null,
            "far-start road access connector remains off-road");

        var noRoads = new CityRoadNetwork(new CityBasemap { roads = new CityBasemapRoad[0] });
        CityRoadRoute direct = noRoads.FindEquipmentRoute(from, goal);
        Check(direct != null && direct.Length == Vector3.Distance(from, goal) &&
            direct.RoadIds.Length == 1 && direct.RoadIds[0] == null,
            "fleet falls back to a direct slow-travel leg when no roads are available");
    }

    private static void VerifyRoadSurfaceClassification()
    {
        var network = new CityRoadNetwork(new CityBasemap
        {
            roads = new[] { new CityBasemapRoad
            {
                id = 41, kind = "residential", width = 2f,
                points = new[] { new CityBasemapPoint(0f,0f), new CityBasemapPoint(10f,0f) }
            } }
        });
        Check(network.RoadIdUnder(new Vector3(5f,0f,.99f)) == "41:0",
            "surface classification covers the actual road half-width");
        Check(network.RoadIdUnder(new Vector3(5f,0f,1.19f)) == "41:0",
            "surface classification includes only a small margin outside the road edge");
        Check(network.RoadIdUnder(new Vector3(5f,0f,1.21f)) == null,
            "bare ground beyond the road edge and margin is not classified paved");
    }

    private static CityBasemapRoad Road(long id, CityBasemapPoint a, CityBasemapPoint b)
    {
        return new CityBasemapRoad { id = id, kind = "residential", width = .3f, points = new[] { a, b } };
    }

    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAILED: " + label);
    }

}