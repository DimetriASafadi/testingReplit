using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    public sealed class CityRegionOverlay : MonoBehaviour
    {
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private int lastFocus = -2, lastHover = -2;

        internal void Initialize(CityWorld world)
        {
            var shader = Shader.Find("NewGaza/DistrictOverlay");
            if (shader == null) throw new InvalidOperationException("Region overlay shader is missing");
            for (int d = 0; d < GameCatalog.Districts.Length; d++)
            {
                var polygon = new List<Vector2> { new Vector2(world.MapMinX, world.MapMinZ),
                    new Vector2(world.MapMaxX, world.MapMinZ), new Vector2(world.MapMaxX, world.MapMaxZ),
                    new Vector2(world.MapMinX, world.MapMaxZ) };
                Vector3 centre = world.DistrictPosition(d);
                for (int other = 0; other < GameCatalog.Districts.Length; other++)
                {
                    if (other == d) continue;
                    Vector3 neighbour = world.DistrictPosition(other);
                    Vector2 normal = new Vector2(neighbour.x - centre.x, neighbour.z - centre.z);
                    float offset = (neighbour.x * neighbour.x + neighbour.z * neighbour.z - centre.x * centre.x - centre.z * centre.z) * .5f;
                    polygon = Clip(polygon, normal, offset);
                }
                // Separate z-strips preserve concave coast bends; intersecting all coast
                // half-planes would incorrectly remove playable western land.
                var coast = GameGeography.Coastline;
                var vertices = new List<Vector3>(); var indices = new List<int>();
                for (int i = 1; i < coast.Length; i++)
                {
                    float dz = coast[i].z - coast[i - 1].z;
                    if (Mathf.Abs(dz) < .001f) continue;
                    float slope = (coast[i].x - coast[i - 1].x) / dz;
                    var strip = Clip(polygon, new Vector2(0, -1), -Mathf.Min(coast[i - 1].z, coast[i].z));
                    strip = Clip(strip, new Vector2(0, 1), Mathf.Max(coast[i - 1].z, coast[i].z));
                    strip = Clip(strip, new Vector2(-1, slope), -(coast[i - 1].x - slope * coast[i - 1].z));
                    AddPiece(strip, vertices, indices);
                }
                float lowZ = float.MaxValue, highZ = float.MinValue, lowX = 0, highX = 0;
                foreach (var point in coast)
                {
                    if (point.z < lowZ) { lowZ = point.z; lowX = point.x; }
                    if (point.z > highZ) { highZ = point.z; highX = point.x; }
                }
                AddPiece(Clip(Clip(polygon, new Vector2(0, 1), lowZ), new Vector2(-1, 0), -lowX), vertices, indices);
                AddPiece(Clip(Clip(polygon, new Vector2(0, -1), -highZ), new Vector2(-1, 0), -highX), vertices, indices);
                Color color = Color.HSVToRGB((d * .6180339f) % 1f, .55f, .82f); color.a = .045f;
                var material = new Material(shader) { name = "Region tint " + GameCatalog.Districts[d].id };
                material.SetColor("_Color", color); materials.Add(material);
                if (vertices.Count < 3) continue;
                var mesh = new Mesh { name = "Geographic presentation cell " + d };
                mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); meshes.Add(mesh);
                var root = new GameObject("Tint • " + GameCatalog.Districts[d].name, typeof(MeshFilter), typeof(MeshRenderer));
                root.transform.SetParent(transform, false);
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            }
        }

        private static void AddPiece(List<Vector2> polygon, List<Vector3> vertices, List<int> indices)
        {
            if (polygon.Count < 3) return;
            int start = vertices.Count;
            foreach (var point in polygon) vertices.Add(new Vector3(point.x, -.075f, point.y));
            for (int i = 1; i < polygon.Count - 1; i++)
            { indices.Add(start); indices.Add(start + i); indices.Add(start + i + 1); }
        }

        internal void Highlight(int focus, int hover)
        {
            if (focus == lastFocus && hover == lastHover) return;
            lastFocus = focus; lastHover = hover;
            for (int d = 0; d < materials.Count; d++)
            { Color color = materials[d].GetColor("_Color"); color.a = d == hover ? .16f : d == focus ? .11f : .045f; materials[d].SetColor("_Color", color); }
        }

        public static float CoastX(float z)
        {
            var points = GameGeography.Coastline;
            for (int i = 1; i < points.Length; i++)
                if (z >= Mathf.Min(points[i - 1].z, points[i].z) && z <= Mathf.Max(points[i - 1].z, points[i].z))
                    return Mathf.Lerp(points[i - 1].x, points[i].x, Mathf.InverseLerp(points[i - 1].z, points[i].z, z));
            var closest = points[0];
            foreach (var point in points) if (Mathf.Abs(point.z - z) < Mathf.Abs(closest.z - z)) closest = point;
            return closest.x;
        }

        private static List<Vector2> Clip(List<Vector2> input, Vector2 normal, float offset)
        {
            var result = new List<Vector2>();
            if (input.Count == 0) return result;
            Vector2 previous = input[input.Count - 1]; float prev = Vector2.Dot(normal, previous) - offset;
            foreach (var current in input)
            {
                float distance = Vector2.Dot(normal, current) - offset;
                if ((prev <= 0) != (distance <= 0)) result.Add(Vector2.Lerp(previous, current, prev / (prev - distance)));
                if (distance <= 0) result.Add(current);
                previous = current; prev = distance;
            }
            return result;
        }

        private void OnDestroy()
        {
            foreach (var material in materials) Destroy(material);
            foreach (var mesh in meshes) Destroy(mesh);
        }
    }
}