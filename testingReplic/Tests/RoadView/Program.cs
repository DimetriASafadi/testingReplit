using System;
using System.Collections.Generic;
using System.IO;
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
            CityRoadNetwork productionNetwork = VerifyProductionBasemapAndNetwork();
            VerifyProductionNativeView(productionNetwork);
            VerifyNativeRoadView();
            Console.WriteLine("Road view checks passed: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static CityRoadNetwork VerifyProductionBasemapAndNetwork()
    {
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "GazaBasemap.json")),
            "production source fixture is copied");
        CityBasemap map = CityBasemap.LoadFromResources();
        CityRoadNetwork network = new CityRoadNetwork(map);
        Check(map.roads.Length == 4032, "native view fixture is the real 4032-way Gaza map");
        Check(network.Segments.Length > map.roads.Length &&
            network.Definitions.Length == network.Segments.Length,
            "all source roads are split into individually persisted road segments");
        Check(map.actualBounds != null && IsFinite(map.actualBounds.minX) &&
            IsFinite(map.actualBounds.maxX) && IsFinite(map.actualBounds.minZ) &&
            IsFinite(map.actualBounds.maxZ) && map.actualBounds.maxX > map.actualBounds.minX &&
            map.actualBounds.maxZ > map.actualBounds.minZ,
            "actual 4032-way fixture has finite nonzero measured bounds");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (RoadSegmentDefinition definition in network.Definitions)
        {
            Check(ids.Add(definition.id), "production source segment IDs are unique");
            Check(definition.lengthMeters > 0f && definition.lengthMeters <= 1000.01f,
                "production segment is within a priced kilometre");
            Check(IsCanonicalId(definition.id), "production OSM feature/ordinal ID is canonical");
        }
        return network;
    }

    private static void VerifyProductionNativeView(CityRoadNetwork network)
    {
        Application.isPlaying = false;
        var parent = new GameObject("Actual Gaza production-road scene");
        using (var geometry = new CityGeometry())
        {
            var view = new GameObject("Actual 4032-way CityRoadView").AddComponent<CityRoadView>();
            view.Initialize(network, geometry, parent.transform);
            var state = new GameState();
            view.Refresh(state, null);
            Mesh[] firstStaticMeshes = StaticMeshes(parent);
            int drawCalls = StaticRenderers(parent).Length;
            Check(firstStaticMeshes.Length > 0 && drawCalls > 0,
                "actual Gaza road graph builds native meshes, not just route data");
            Check(drawCalls < network.Segments.Length,
                "actual map surface draw calls are spatial/material batches, not per-road renderers");
            VerifyMeshes(parent);

            view.Refresh(state, null);
            Check(SameMeshes(firstStaticMeshes, StaticMeshes(parent)) &&
                drawCalls == StaticRenderers(parent).Length,
                "actual 4032-way view refresh is cached without hierarchy/mesh rebuild");

            string pickedId = network.Segments[network.Segments.Length / 2].Definition.id;
            view.Refresh(state, pickedId);
            Check(SameMeshes(firstStaticMeshes, StaticMeshes(parent)) &&
                MaterialCount(SelectionRoot(parent).gameObject, "selected amber edge") > 0,
                "actual source polyline selection adds only an amber overlay");
            VerifyAmberPointsNearRoad(SelectionRoot(parent).gameObject,
                network.FindSegment(pickedId));

            state.roadSegments = new[]
            {
                new RoadSegmentState { id = pickedId, level = 1 }
            };
            view.Refresh(state, pickedId);
            Mesh[] repaired = StaticMeshes(parent);
            Check(!AnySameMesh(firstStaticMeshes, repaired),
                "actual source road repair replaces previous static material batches");
            Check(MaterialCount(StaticRoot(parent).gameObject, "graded compact earth") > 0 &&
                MaterialCount(StaticRoot(parent).gameObject, "sandy dust") > 0,
                "actual map mixes repaired earth and unrepaired dusty source segments");
            VerifyMeshes(parent);
            view.Dispose();
        }
    }

    private static void VerifyNativeRoadView()
    {
        // A 150-unit L way is exactly three 1km purchase segments. It exercises actual native
        // chunking and rendering with a tiny predictable fixture, leaving the 4032-way fixture
        // available for broad source-network assertions without spending time on full-city meshes.
        var map = new CityBasemap
        {
            roads = new[]
            {
                new CityBasemapRoad
                {
                    id = 987654,
                    kind = "primary",
                    name = "Synthetic production-view test L",
                    width = .65f,
                    points = new[]
                    {
                        new CityBasemapPoint(0f, 0f),
                        new CityBasemapPoint(75f, 0f),
                        new CityBasemapPoint(75f, 75f)
                    }
                }
            }
        };
        CityRoadNetwork network = new CityRoadNetwork(map);
        Check(network.Segments.Length == 3, "three-kilometre L road produces three improvement segments");
        for (int i = 0; i < network.Segments.Length; i++)
        {
            Check(network.Segments[i].Definition.id == "987654:" + i,
                "L-road split keeps stable source identity and kilometer ordinal");
            Check(Math.Abs(network.Segments[i].Definition.lengthMeters - 1000f) < .02f,
                "each synthetic purchase segment represents one actual kilometre");
        }

        Application.isPlaying = false;
        var parent = new GameObject("Road view test city");
        using (var geometry = new CityGeometry())
        {
            var viewObject = new GameObject("Native production road view");
            var view = viewObject.AddComponent<CityRoadView>();
            view.Initialize(network, geometry, parent.transform);
            var state = new GameState();
            view.Refresh(state, null);
            Mesh[] initialMeshes = StaticMeshes(parent);
            int initialRendererCount = StaticRenderers(parent).Length;
            Check(initialMeshes.Length > 0 && initialRendererCount > 0,
                "native view builds batched meshes from road geometry");
            VerifyMeshes(parent);
            Check(MaterialCount(parent, "road • sandy dust") > 0,
                "unrepaired road uses dusty surface material");

            view.Refresh(state, null);
            Check(SameMeshes(initialMeshes, StaticMeshes(parent)) &&
                initialRendererCount == StaticRenderers(parent).Length,
                "unchanged levels refresh without rebuilding hierarchy or allocating meshes");

            string selectedId = network.Segments[1].Definition.id;
            view.Refresh(state, selectedId);
            Mesh[] selectedMeshes = StaticMeshes(parent);
            Check(SameMeshes(initialMeshes, selectedMeshes),
                "selection does not rebuild static surface meshes");
            Check(MaterialCount(SelectionRoot(parent).gameObject, "selected amber edge") > 0,
                "selection builds a native amber edge mesh over the actual L-road points");
            VerifyAmberPointsNearRoad(SelectionRoot(parent).gameObject, network.Segments[1]);

            state.roadSegments = new[]
            {
                new RoadSegmentState { id = network.Segments[0].Definition.id, level = 1 },
                new RoadSegmentState { id = network.Segments[1].Definition.id, level = 2 }
            };
            view.Refresh(state, selectedId);
            Mesh[] mixedMeshes = StaticMeshes(parent);
            Check(!AnySameMesh(initialMeshes, mixedMeshes),
                "one road-level update removes old meshes and refreshes the static level batches");
            VerifyMeshes(parent);
            Check(MaterialCount(parent, "road • sandy dust") > 0 &&
                MaterialCount(parent, "road • graded compact earth") > 0 &&
                MaterialCount(parent, "road • repaired asphalt") > 0,
                "levels zero, one, and two use dust, earth, and asphalt respectively");
            Check(MaterialCount(parent, "faded white markings") > 0 &&
                MaterialCount(parent, "faded yellow centerline") > 0 &&
                MaterialCount(parent, "pale curb") > 0,
                "level two draws white/yellow markings and raised curb geometry");
            Check(MaterialCount(SelectionRoot(parent).gameObject, "selected amber edge") > 0,
                "selection highlight survives a road repair refresh");
            VerifyAmberPointsNearRoad(SelectionRoot(parent).gameObject, network.Segments[1]);

            state.roadSegments = new[]
            {
                new RoadSegmentState { id = network.Segments[0].Definition.id, level = 2 },
                new RoadSegmentState { id = network.Segments[1].Definition.id, level = 2 },
                new RoadSegmentState { id = network.Segments[2].Definition.id, level = 2 }
            };
            view.Refresh(state, selectedId);
            Mesh[] asphaltMeshes = StaticMeshes(parent);
            Check(MaterialCount(parent, "road • sandy dust") == 0 &&
                MaterialCount(parent, "road • graded compact earth") == 0,
                "level changes remove old sand/earth overlays after one refresh callback");
            Check(MaterialCount(parent, "road • repaired asphalt") > 0,
                "all repaired segments render asphalt");
            Check(!AnySameMesh(mixedMeshes, asphaltMeshes),
                "refresh releases the previous mixed-level meshes instead of retaining stale overlays");
            view.Refresh(state, selectedId);
            Check(SameMeshes(asphaltMeshes, StaticMeshes(parent)),
                "settled asphalt levels retain the same active meshes without accumulating allocations");
            // The L spans a maximum 3x3 chunk cells, including ribbons crossing the
            // origin. Five paving materials can legitimately need more batches than
            // a mixed scene with only one paved segment; equal counts are not a leak check.
            Check(Renderers(parent).Length <= 9 * 5 + 1,
                "three-segment road is bounded by chunk/material batches plus one selection draw");
            VerifyMeshes(parent);

            view.Refresh(state, null);
            Check(MaterialCount(SelectionRoot(parent).gameObject, "selected amber edge") == 0,
                "deselection removes the stale amber overlay in one callback");
            Check(SameMeshes(asphaltMeshes, StaticMeshes(parent)),
                "selection clearing leaves static asphalt batches intact");
            view.Dispose();
            Check(Meshes(parent).Length == 0, "disposing view removes all road mesh objects");
        }
    }

    private static MeshRenderer[] Renderers(GameObject root)
    {
        return root.GetComponentsInChildren<MeshRenderer>(false);
    }

    private static Mesh[] Meshes(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(false);
        var result = new List<Mesh>(filters.Length);
        for (int i = 0; i < filters.Length; i++)
            if (filters[i].sharedMesh != null) result.Add(filters[i].sharedMesh);
        return result.ToArray();
    }

    private static Transform StaticRoot(GameObject city)
    {
        return city.transform.GetChild(0).GetChild(0);
    }

    private static Transform SelectionRoot(GameObject city)
    {
        return city.transform.GetChild(0).GetChild(1);
    }

    private static MeshRenderer[] StaticRenderers(GameObject city)
    {
        return StaticRoot(city).gameObject.GetComponentsInChildren<MeshRenderer>(false);
    }

    private static Mesh[] StaticMeshes(GameObject city)
    {
        return Meshes(StaticRoot(city).gameObject);
    }

    private static bool SameMeshes(Mesh[] first, Mesh[] second)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++)
            if (!ReferenceEquals(first[i], second[i])) return false;
        return true;
    }

    private static bool AnySameMesh(Mesh[] first, Mesh[] second)
    {
        foreach (Mesh a in first)
        foreach (Mesh b in second)
            if (ReferenceEquals(a, b)) return true;
        return false;
    }

    private static int MaterialCount(GameObject root, string namePart)
    {
        int count = 0;
        foreach (MeshRenderer renderer in Renderers(root))
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.Contains(namePart))
                count++;
        return count;
    }

    private static void VerifyMeshes(GameObject root)
    {
        foreach (Mesh mesh in Meshes(root))
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            bool completeTriangles = vertices.Length > 0 && triangles.Length > 0 &&
                triangles.Length % 3 == 0;
            bool finiteVertices = true, finiteNormals = normals.Length == vertices.Length;
            bool validIndices = true, upwardWinding = true;
            int badWinding = 0;
            float minCross = float.MaxValue, maxCross = float.MinValue;
            string firstBadTriangle = string.Empty;
            foreach (Vector3 vertex in vertices)
                if (!IsFinite(vertex.x) || !IsFinite(vertex.y) || !IsFinite(vertex.z))
                    finiteVertices = false;
            foreach (Vector3 normal in normals)
                if (!IsFinite(normal.x) || !IsFinite(normal.y) || !IsFinite(normal.z))
                    finiteNormals = false;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a < 0 || a >= vertices.Length || b < 0 || b >= vertices.Length ||
                    c < 0 || c >= vertices.Length)
                {
                    validIndices = false;
                    upwardWinding = false;
                    continue;
                }
                Vector3 cross = Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                minCross = Math.Min(minCross, cross.y);
                maxCross = Math.Max(maxCross, cross.y);
                if (!IsFinite(cross.y) || cross.y <= 0f)
                {
                    upwardWinding = false;
                    badWinding++;
                    if (firstBadTriangle.Length == 0)
                        firstBadTriangle = vertices[a].x + "," + vertices[a].z + " / " +
                            vertices[b].x + "," + vertices[b].z + " / " +
                            vertices[c].x + "," + vertices[c].z;
                }
            }
            Check(completeTriangles, "each native road mesh contains complete triangles");
            Check(finiteVertices && finiteNormals, "road mesh positions and normals are finite");
            Check(validIndices, "triangle indices remain within each mesh");
            Check(upwardWinding, "road triangles have upward-facing surface winding (" +
                mesh.name + ", " + MaterialFor(root, mesh) + ", " + badWinding + " bad of " +
                triangles.Length / 3 + ", y-cross " + minCross + ".." + maxCross + ", tri " +
                firstBadTriangle + ")");
        }
    }

    private static string MaterialFor(GameObject root, Mesh mesh)
    {
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            if (ReferenceEquals(filter.sharedMesh, mesh))
            {
                MeshRenderer renderer = filter.gameObject.GetComponent<MeshRenderer>();
                return renderer == null || renderer.sharedMaterial == null
                    ? "<no material>" : renderer.sharedMaterial.name;
            }
        return "<not found>";
    }

    private static void VerifyAmberPointsNearRoad(GameObject root, CityRoadSegment road)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(false);
        int checkedVertices = 0;
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null || filter.gameObject.GetComponent<MeshRenderer>() == null)
                continue;
            Material material = filter.gameObject.GetComponent<MeshRenderer>().sharedMaterial;
            if (material == null || !material.name.Contains("selected amber edge")) continue;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Vector3 point in road.Points)
                {
                    minX = Math.Min(minX, point.x);
                    maxX = Math.Max(maxX, point.x);
                    minZ = Math.Min(minZ, point.z);
                    maxZ = Math.Max(maxZ, point.z);
                }
                Check(Math.Sqrt(road.DistanceSquared(vertex)) < 1f &&
                    vertex.x >= minX - 1f && vertex.x <= maxX + 1f &&
                    vertex.z >= minZ - 1f && vertex.z <= maxZ + 1f,
                    "amber overlay vertices remain near the selected real road segment");
                checkedVertices++;
            }
        }
        Check(checkedVertices > 0, "selected road contributes real amber edge vertices");
    }

    private static Vector3 Cross(Vector3 a, Vector3 b)
    {
        return new Vector3(a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }

    private static bool IsCanonicalId(string id)
    {
        int separator = id.IndexOf(':');
        if (separator <= 0 || separator != id.LastIndexOf(':') || separator == id.Length - 1)
            return false;
        long feature;
        int ordinal;
        return long.TryParse(id.Substring(0, separator), out feature) && feature > 0 &&
            feature.ToString() == id.Substring(0, separator) &&
            int.TryParse(id.Substring(separator + 1), out ordinal) && ordinal >= 0 &&
            ordinal.ToString() == id.Substring(separator + 1);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }
}