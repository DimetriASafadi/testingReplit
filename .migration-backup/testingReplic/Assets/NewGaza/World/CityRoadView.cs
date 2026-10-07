using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using NewGaza.Core;

namespace NewGaza
{
    /// <summary>
    /// Native, procedural presentation for the sourced gameplay road graph. Static road
    /// surfaces are batched by improvement level and 40-unit world chunks; only a selected
    /// road's lightweight amber edge treatment is rebuilt when selection changes.
    /// </summary>
    internal sealed class CityRoadView : MonoBehaviour, IDisposable
    {
        private const float ChunkSize = 40f;
        private const float GroundY = -.09f;
        private const float SurfaceY = GroundY + .006f;
        private const float MaximumStepLength = 2f;

        private CityRoadNetwork network;
        private CityGeometry geometry;
        private Transform parent;
        private Transform root;
        private Transform staticRoot;
        private Transform selectionRoot;
        private string selectedId;
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Mesh> selectionMeshes = new List<Mesh>();
        private readonly Dictionary<string, int> renderedLevels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> savedLevels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> materials =
            new Dictionary<string, Material>(StringComparer.Ordinal);

        public void Initialize(CityRoadNetwork roadNetwork, CityGeometry cityGeometry, Transform cityParent)
        {
            if (roadNetwork == null) throw new ArgumentNullException(nameof(roadNetwork));
            if (cityGeometry == null) throw new ArgumentNullException(nameof(cityGeometry));
            if (cityParent == null) throw new ArgumentNullException(nameof(cityParent));
            Dispose();

            network = roadNetwork;
            geometry = cityGeometry;
            parent = cityParent;
            var roadRoot = new GameObject("Native road network • 40-unit visual chunks");
            roadRoot.transform.SetParent(parent, false);
            roadRoot.transform.localPosition = Vector3.zero;
            root = roadRoot.transform;
            staticRoot = new GameObject("Road level batches").transform;
            staticRoot.SetParent(root, false);

            CreateMaterials();
            BuildRoadMeshes(null);
            selectionRoot = new GameObject("Selected road • amber edge highlight").transform;
            selectionRoot.SetParent(root, false);
            selectionRoot.localPosition = Vector3.zero;
            selectedId = null;
        }

        public void Refresh(GameState state, string selectedSegmentId)
        {
            if (network == null || geometry == null || root == null)
                throw new InvalidOperationException("CityRoadView.Initialize must be called before Refresh.");
            if (state == null) throw new ArgumentNullException(nameof(state));

            savedLevels.Clear();
            if (state.roadSegments != null)
                for (int i = 0; i < state.roadSegments.Length; i++)
                {
                    RoadSegmentState saved = state.roadSegments[i];
                    if (saved != null && !string.IsNullOrEmpty(saved.id) && !savedLevels.ContainsKey(saved.id))
                        savedLevels.Add(saved.id, saved.level);
                }

            bool levelsChanged = false;
            for (int i = 0; i < network.Segments.Length; i++)
            {
                CityRoadSegment segment = network.Segments[i];
                int level;
                if (!savedLevels.TryGetValue(segment.Definition.id, out level)) level = 0;
                int previous;
                if (renderedLevels.TryGetValue(segment.Definition.id, out previous) && previous != level)
                {
                    // Use the economy's canonical lookup on actual road-state changes, not
                    // once per edge on every world tick.
                    level = RoadEconomy.GetLevel(state, segment.Definition.id);
                    if (previous != level) levelsChanged = true;
                }
            }
            if (levelsChanged)
                BuildRoadMeshes(state, savedLevels);

            if (!string.Equals(selectedId, selectedSegmentId, StringComparison.Ordinal))
            {
                selectedId = selectedSegmentId;
                BuildSelection();
            }
        }

        private void CreateMaterials()
        {
            materials["sand"] = geometry.Material("road • sandy dust", Hex(0x8D8065), .08f,
                0f, SurfaceKind.Stone);
            materials["earth"] = geometry.Material("road • graded compact earth", Hex(0xA08A68), .1f,
                0f, SurfaceKind.Stone);
            materials["asphalt"] = geometry.Material("road • repaired asphalt", Hex(0x474B4B), .16f,
                0f, SurfaceKind.Asphalt);
            materials["crack"] = geometry.Material("road • illustrative survey cracks", Hex(0x5B5445), .05f);
            materials["pothole"] = geometry.Material("road • shallow survey patches", Hex(0x665D4B), .04f);
            materials["sidewalk"] = geometry.Material("road • limestone sidewalk", Hex(0xB9AD96), .12f,
                0f, SurfaceKind.Concrete);
            materials["curb"] = geometry.Material("road • pale curb", Hex(0xD0C3A8), .1f);
            materials["whiteLine"] = geometry.Material("road • faded white markings", Hex(0xE7E1D1), .12f);
            materials["yellowLine"] = geometry.Material("road • faded yellow centerline", Hex(0xD9B958), .12f);
            materials["selection"] = geometry.Material("road • selected amber edge", Hex(0xF3A62F), .22f,
                .12f);
        }

        private void BuildRoadMeshes(GameState state, Dictionary<string, int> savedLevels = null)
        {
            ReleaseRoadMeshes();
            DestroyChildren(staticRoot);
            renderedLevels.Clear();
            var chunks = new Dictionary<ChunkKey, RoadChunk>();
            CityRoadSegment[] segments = network.Segments;
            for (int i = 0; i < segments.Length; i++)
            {
                CityRoadSegment segment = segments[i];
                if (segment == null || segment.Definition == null ||
                    string.IsNullOrWhiteSpace(segment.Definition.id) || segment.Points == null ||
                    segment.Points.Length < 2)
                    throw new InvalidOperationException("CityRoadNetwork contains a road segment with an invalid definition or polyline.");

                int level = 0;
                if (state != null && savedLevels != null)
                    savedLevels.TryGetValue(segment.Definition.id, out level);
                if (level < 0 || level > 2)
                    throw new InvalidOperationException("Road '" + segment.Definition.id +
                        "' has unsupported improvement level " + level + ".");
                renderedLevels[segment.Definition.id] = level;
                AddSegment(segment, level, chunks);
            }

            foreach (KeyValuePair<ChunkKey, RoadChunk> pair in chunks)
                pair.Value.Build(pair.Key, staticRoot, ownedMeshes);
        }

        private void AddSegment(CityRoadSegment segment, int level,
            Dictionary<ChunkKey, RoadChunk> chunks)
        {
            Vector3[] points = segment.Points;
            bool footway = IsFootway(segment.Kind);
            float width = Mathf.Max(.12f, segment.Width);
            float halfWidth = width * .5f;
            float sidewalk = level == 2 && !footway && width >= .34f ? .075f : 0f;
            float curb = sidewalk > 0f ? .018f : 0f;
            Material surface = materials[level == 0 ? "sand" : level == 1 ? "earth" : "asphalt"];
            var miters = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 before = i > 0
                    ? new Vector2(points[i].x - points[i - 1].x, points[i].z - points[i - 1].z).normalized
                    : new Vector2(points[1].x - points[0].x, points[1].z - points[0].z).normalized;
                Vector2 after = i + 1 < points.Length
                    ? new Vector2(points[i + 1].x - points[i].x, points[i + 1].z - points[i].z).normalized
                    : before;
                Vector2 n1 = new Vector2(-before.y, before.x);
                Vector2 n2 = new Vector2(-after.y, after.x);
                Vector2 miter = (n1 + n2).sqrMagnitude < .0001f ? n2 : (n1 + n2).normalized;
                float divisor = Mathf.Max(.35f, Mathf.Abs(Vector2.Dot(miter, n2)));
                miters[i] = miter * Mathf.Min(1f / divisor, 2f);
            }
            float traveled = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = new Vector2(points[i - 1].x, points[i - 1].z);
                Vector2 b = new Vector2(points[i].x, points[i].z);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < .001f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x) / length;
                if (level == 0 && !footway && StableHash(segment.Definition.id + ":" + i) % 4 == 0)
                    AddIllustrativeWear(segment.Definition.id, a, b, normal, halfWidth, chunks);
                float distanceAlong = traveled;
                Vector2 miterStart = miters[i - 1];
                Vector2 miterEnd = miters[i];
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / MaximumStepLength));
                for (int step = 0; step < steps; step++)
                {
                    float t0 = step / (float)steps;
                    float t1 = (step + 1) / (float)steps;
                    Vector2 start = Vector2.Lerp(a, b, t0);
                    Vector2 end = Vector2.Lerp(a, b, t1);
                    Vector2 startMiter = Vector2.Lerp(miterStart, miterEnd, t0);
                    Vector2 endMiter = Vector2.Lerp(miterStart, miterEnd, t1);
                    AddStrip(start, end, startMiter, endMiter, halfWidth, -halfWidth,
                        surface, chunks, SurfaceY);

                    if (sidewalk > 0f)
                    {
                        AddStrip(start, end, startMiter, endMiter, halfWidth + curb,
                            halfWidth + curb + sidewalk, materials["sidewalk"], chunks, SurfaceY + .001f);
                        AddStrip(start, end, startMiter, endMiter,
                            halfWidth + curb + sidewalk - .014f, halfWidth + curb + sidewalk,
                            materials["curb"], chunks, SurfaceY + .009f);
                        AddStrip(start, end, startMiter, endMiter, -halfWidth - curb,
                            -halfWidth - curb - sidewalk, materials["sidewalk"], chunks, SurfaceY + .001f);
                        AddStrip(start, end, startMiter, endMiter,
                            -halfWidth - curb - sidewalk, -halfWidth - curb - sidewalk + .014f,
                            materials["curb"], chunks, SurfaceY + .009f);
                    }

                    if (level == 2 && !footway)
                        AddMarkings(start, end, startMiter, endMiter, halfWidth, width,
                            chunks, distanceAlong + length * t0);
                }
                traveled += length;
            }
        }

        private static void AddStrip(Vector2 start, Vector2 end, Vector2 startMiter,
            Vector2 endMiter, float leftDistance, float rightDistance, Material material,
            Dictionary<ChunkKey, RoadChunk> chunks, float y)
        {
            Vector2 a = start + startMiter * leftDistance;
            Vector2 b = end + endMiter * leftDistance;
            Vector2 c = end + endMiter * rightDistance;
            Vector2 d = start + startMiter * rightDistance;
            AddPolygon(chunks, material, y, a, b, c, d);
        }

        private void AddMarkings(Vector2 start, Vector2 end, Vector2 startMiter,
            Vector2 endMiter, float halfWidth, float width, Dictionary<ChunkKey, RoadChunk> chunks,
            float distanceAlong)
        {
            float edgeOffset = Mathf.Max(.045f, halfWidth * .72f);
            const float stripeWidth = .012f;
            AddStrip(start, end, startMiter, endMiter, edgeOffset + stripeWidth * .5f,
                edgeOffset - stripeWidth * .5f,
                materials["whiteLine"], chunks, SurfaceY + .002f);
            AddStrip(start, end, startMiter, endMiter, -edgeOffset + stripeWidth * .5f,
                -edgeOffset - stripeWidth * .5f,
                materials["whiteLine"], chunks, SurfaceY + .002f);

            if (width < .48f) return;
            float length = Vector2.Distance(start, end);
            if (length < .01f) return;
            const float dash = .82f;
            const float gap = .62f;
            const float period = dash + gap;
            float endDistance = distanceAlong + length;
            float dashStart = Mathf.Floor(distanceAlong / period) * period;
            for (; dashStart < endDistance; dashStart += period)
            {
                float fromDistance = Mathf.Max(distanceAlong, dashStart);
                float toDistance = Mathf.Min(endDistance, dashStart + dash);
                if (toDistance <= fromDistance) continue;
                Vector2 from = Vector2.Lerp(start, end, (fromDistance - distanceAlong) / length);
                Vector2 to = Vector2.Lerp(start, end, (toDistance - distanceAlong) / length);
                float fromT = (fromDistance - distanceAlong) / length;
                float toT = (toDistance - distanceAlong) / length;
                AddStrip(from, to, Vector2.Lerp(startMiter, endMiter, fromT),
                    Vector2.Lerp(startMiter, endMiter, toT), .007f, -.007f,
                    materials["yellowLine"], chunks, SurfaceY + .003f);
            }
        }

        private void AddIllustrativeWear(string id, Vector2 start, Vector2 end, Vector2 normal,
            float halfWidth, Dictionary<ChunkKey, RoadChunk> chunks)
        {
            float length = Vector2.Distance(start, end);
            if (length < .45f) return;
            int seed = StableHash(id);
            float phase = (seed % 31) * .09f;
            float center = length * .5f + phase;
            if (center < .1f || center > length - .1f) return;
            Vector2 point = Vector2.Lerp(start, end, center / length);
            float width = Mathf.Min(.045f, halfWidth * .22f);
            Vector2 along = new Vector2(normal.y, -normal.x);
            AddStrip(point - normal * width, point + normal * width, along, along, .006f,
                -.006f, materials["pothole"], chunks, SurfaceY + .001f);

            // A small broken zig-zag line suggests maintenance survey wear without
            // making rubble obstacles or treating the road as impassable.
            along = (end - start).normalized;
            Vector2 crackStart = point - normal * halfWidth * .55f;
            Vector2 crackEnd = point + normal * halfWidth * .55f;
            Vector2 bend = crackStart + (crackEnd - crackStart) * .52f + along * .025f;
            AddThinLine(chunks, materials["crack"], crackStart, bend, .006f, SurfaceY + .002f);
            AddThinLine(chunks, materials["crack"], bend, crackEnd, .006f, SurfaceY + .002f);
        }

        private void BuildSelection()
        {
            if (selectionRoot == null) return;
            ReleaseSelectionMeshes();
            for (int i = selectionRoot.childCount - 1; i >= 0; i--)
                DestroyObject(selectionRoot.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(selectedId)) return;
            CityRoadSegment segment = network.FindSegment(selectedId);
            if (segment == null) return;
            if (segment.Points == null || segment.Points.Length < 2)
                throw new InvalidOperationException("Selected road '" + selectedId + "' has no usable polyline.");

            var builder = new MeshBuilder();
            float halfWidth = Mathf.Max(.12f, segment.Width) * .5f;
            const float borderWidth = .022f;
            Vector3[] points = segment.Points;
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 start = new Vector2(points[i - 1].x, points[i - 1].z);
                Vector2 end = new Vector2(points[i].x, points[i].z);
                Vector2 delta = end - start;
                float length = delta.magnitude;
                if (length < .001f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x) / length;
                float inset = .018f;
                AddBuilderQuad(builder, start + normal * (halfWidth + inset + borderWidth),
                    end + normal * (halfWidth + inset + borderWidth),
                    end + normal * (halfWidth + inset), start + normal * (halfWidth + inset),
                    SurfaceY + .016f);
                AddBuilderQuad(builder, start - normal * (halfWidth + inset + borderWidth),
                    end - normal * (halfWidth + inset + borderWidth),
                    end - normal * (halfWidth + inset), start - normal * (halfWidth + inset),
                    SurfaceY + .016f);
            }
            if (builder.VertexCount == 0) return;
            Mesh mesh = builder.Build("Selected road amber borders");
            selectionMeshes.Add(mesh);
            var filter = new GameObject("Amber road borders").AddComponent<MeshFilter>();
            filter.transform.SetParent(selectionRoot, false);
            filter.sharedMesh = mesh;
            var renderer = filter.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials["selection"];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void AddPolygon(Dictionary<ChunkKey, RoadChunk> chunks, Material material,
            float y, params Vector2[] polygon)
        {
            Vector2 center = Vector2.zero;
            for (int i = 0; i < polygon.Length; i++) center += polygon[i];
            center /= polygon.Length;
            var key = Cell(center.x, center.y);
            RoadChunk chunk;
            if (!chunks.TryGetValue(key, out chunk))
            {
                chunk = new RoadChunk();
                chunks.Add(key, chunk);
            }
            chunk.AddPolygon(material, polygon, y);
        }

        private static void AddThinLine(Dictionary<ChunkKey, RoadChunk> chunks, Material material,
            Vector2 start, Vector2 end, float width, float y)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude < .000001f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
            AddPolygon(chunks, material, y,
                start + normal, end + normal, end - normal, start - normal);
        }

        private static void AddBuilderQuad(MeshBuilder builder, Vector2 a, Vector2 b,
            Vector2 c, Vector2 d, float y)
        {
            builder.AddPolygon(new[] { a, b, c, d }, y);
        }

        private void ReleaseRoadMeshes()
        {
            if (geometry != null)
                for (int i = 0; i < ownedMeshes.Count; i++)
                    if (ownedMeshes[i] != null) geometry.Release(ownedMeshes[i]);
            ownedMeshes.Clear();
        }

        private void ReleaseSelectionMeshes()
        {
            if (geometry != null)
                for (int i = 0; i < selectionMeshes.Count; i++)
                    if (selectionMeshes[i] != null) geometry.Release(selectionMeshes[i]);
            selectionMeshes.Clear();
        }

        private static void DestroyChildren(Transform transform)
        {
            if (transform == null) return;
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyObject(transform.GetChild(i).gameObject);
        }

        private static void DestroyObject(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }

        private static bool IsFootway(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            string normalized = kind.ToLowerInvariant();
            return normalized.Contains("footway") || normalized.Contains("pedestrian") ||
                normalized.Contains("steps") || normalized.Contains("path");
        }

        private static ChunkKey Cell(float x, float z)
        {
            return new ChunkKey(Mathf.FloorToInt(x / ChunkSize), Mathf.FloorToInt(z / ChunkSize));
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash & 0x7fffffff;
            }
        }

        private static Color Hex(uint value)
        {
            return new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f,
                (value & 255) / 255f, 1f);
        }

        public void Dispose()
        {
            ReleaseRoadMeshes();
            ReleaseSelectionMeshes();
            if (root != null) DestroyObject(root.gameObject);
            root = null;
            staticRoot = null;
            selectionRoot = null;
            network = null;
            geometry = null;
            parent = null;
            selectedId = null;
            renderedLevels.Clear();
            materials.Clear();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private struct ChunkKey : IEquatable<ChunkKey>
        {
            private readonly int x;
            private readonly int z;

            internal ChunkKey(int x, int z) { this.x = x; this.z = z; }
            public bool Equals(ChunkKey other) { return x == other.x && z == other.z; }
            public override bool Equals(object obj) { return obj is ChunkKey && Equals((ChunkKey)obj); }
            public override int GetHashCode() { unchecked { return x * 397 ^ z; } }
        }

        private sealed class RoadChunk
        {
            private readonly Dictionary<Material, MeshBuilder> batches =
                new Dictionary<Material, MeshBuilder>();

            internal void AddPolygon(Material material, Vector2[] points, float y)
            {
                MeshBuilder builder;
                if (!batches.TryGetValue(material, out builder))
                {
                    builder = new MeshBuilder();
                    batches.Add(material, builder);
                }
                builder.AddPolygon(points, y);
            }

            internal void Build(ChunkKey key, Transform parent, List<Mesh> owned)
            {
                foreach (KeyValuePair<Material, MeshBuilder> pair in batches)
                {
                    Mesh mesh = pair.Value.Build("Road chunk " + key.GetHashCode());
                    if (mesh == null) continue;
                    owned.Add(mesh);
                    var child = new GameObject(pair.Key.name);
                    child.transform.SetParent(parent, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = pair.Key;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
            }
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            internal int VertexCount { get { return vertices.Count; } }

            internal void AddPolygon(Vector2[] polygon, float y)
            {
                if (polygon == null || polygon.Length < 3) return;
                int validTriangles = 0;
                for (int i = 1; i < polygon.Length - 1; i++)
                {
                    double area = UpwardArea(polygon[0], polygon[i], polygon[i + 1]);
                    if (Math.Abs(area) > 1e-8d) validTriangles++;
                }
                if (validTriangles == 0) return;

                int first = vertices.Count;
                for (int i = 0; i < polygon.Length; i++)
                {
                    vertices.Add(new Vector3(polygon[i].x, y, polygon[i].y));
                    uv.Add(new Vector2(polygon[i].x * .65f, polygon[i].y * .65f));
                }
                // Evaluate every fan triangle independently in translated double
                // precision. At city-scale coordinates a polygon-wide float shoelace
                // sum can lose the sign of centimetre-wide road strips.
                for (int i = 1; i < polygon.Length - 1; i++)
                {
                    double area = UpwardArea(polygon[0], polygon[i], polygon[i + 1]);
                    if (Math.Abs(area) <= 1e-8d) continue;
                    triangles.Add(first);
                    if (area > 0d)
                    {
                        triangles.Add(first + i);
                        triangles.Add(first + i + 1);
                    }
                    else
                    {
                        triangles.Add(first + i + 1);
                        triangles.Add(first + i);
                    }
                }
            }

            private static double UpwardArea(Vector2 a, Vector2 b, Vector2 c)
            {
                double firstX = (double)b.x - a.x;
                double firstZ = (double)b.y - a.y;
                double secondX = (double)c.x - a.x;
                double secondZ = (double)c.y - a.y;
                // Vector3.Cross(b-a,c-a).y in Unity's x/y/z coordinate system.
                return firstZ * secondX - firstX * secondZ;
            }

            internal Mesh Build(string name)
            {
                if (vertices.Count == 0) return null;
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}