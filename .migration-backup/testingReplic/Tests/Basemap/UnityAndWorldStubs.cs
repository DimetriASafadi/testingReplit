using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Globalization;

namespace UnityEngine
{
    public class Object { }
    public sealed class TextAsset : Object
    {
        public readonly string text;
        public TextAsset(string text) { this.text = text; }
    }
    public sealed class Material : Object { public string name; }
    public sealed class Transform
    {
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }
    public sealed class GameObject : Object
    {
        public readonly Transform transform = new Transform();
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public string name;
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : new()
        {
            var result = new T();
            components[typeof(T)] = result;
            if (result is MeshFilter filter) filter.gameObject = this;
            if (result is MeshRenderer renderer) renderer.gameObject = this;
            return result;
        }
        public T GetComponent<T>() where T : class
        {
            object value;
            return components.TryGetValue(typeof(T), out value) ? value as T : null;
        }
    }
    public sealed class Mesh : Object
    {
        public static readonly List<Mesh> Captured = new List<Mesh>();
        public string name;
        public UnityEngine.Rendering.IndexFormat indexFormat;
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public Mesh() { Captured.Add(this); }
        public void SetVertices(List<Vector3> vertices) { this.vertices.AddRange(vertices); }
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds)
        { this.triangles.AddRange(triangles); }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }
    public sealed class MeshFilter { public Mesh sharedMesh; public GameObject gameObject; }
    public sealed class MeshRenderer
    {
        public static readonly List<MeshRenderer> Captured = new List<MeshRenderer>();
        public Material sharedMaterial;
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public GameObject gameObject;
        public MeshRenderer() { Captured.Add(this); }
    }
    public static class Resources
    {
        public static string RootDirectory;
        public static T Load<T>(string resourcePath) where T : class
        {
            if (typeof(T) != typeof(TextAsset) || string.IsNullOrEmpty(RootDirectory)) return null;
            string path = Path.Combine(RootDirectory, resourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
            return File.Exists(path) ? new TextAsset(File.ReadAllText(path)) as T : null;
        }
    }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };
        public static T FromJson<T>(string json)
        {
            T value = JsonSerializer.Deserialize<T>(json, Options);
            // Exercise the Unity inline-serialization case absent from the .NET
            // serializer: an omitted optional origin exists but contains zeros.
            if (value is NewGaza.CityBasemap map && map.origin == null)
                map.origin = new NewGaza.CityBasemapOrigin();
            return value;
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 up { get { return new Vector2(0f, 1f); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector2 normalized { get { float m = magnitude; return m < 1e-20f ? new Vector2() : this / m; } }
        public float this[int index] { get { return index == 0 ? x : y; } }
        public static float Dot(Vector2 a, Vector2 b) { return a.x * b.x + a.y * b.y; }
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) { return a + (b - a) * t; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float scale) { return new Vector2(a.x * scale, a.y * scale); }
        public static Vector2 operator /(Vector2 a, float scale) { return new Vector2(a.x / scale, a.y / scale); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
    }
    public struct Quaternion
    {
        private float yaw;
        public static Quaternion Euler(float x, float y, float z)
        {
            if (x != 0 || z != 0) throw new NotSupportedException("Basemap fixture only uses ground-plane yaw");
            return new Quaternion { yaw = y };
        }
        public static Vector3 operator *(Quaternion q, Vector3 p)
        {
            double radians = q.yaw * Math.PI / 180;
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vector3(p.x * c + p.z * s, p.y, p.z * c - p.x * s);
        }
    }
    public static class Mathf
    {
        public static float Abs(float v) { return Math.Abs(v); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }
        public static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
        public static int FloorToInt(float v) { return (int)Math.Floor(v); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On }
}

namespace NewGaza
{
    using UnityEngine;

    internal sealed class CityGeometry
    {
        internal Mesh Own(Mesh mesh) { return mesh; }
    }

    internal sealed class CityMeshBatch
    {
        internal CityMeshBatch(CityGeometry geometry) { }
        internal GameObject Build(string name, Transform parent, Vector3 position)
        {
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            return result;
        }
    }

    internal sealed class CityModelLibrary
    {
        internal int ContextModelCopies { get; private set; }
        internal readonly HashSet<string> ContextModelKeys = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<ContextModelPlacement> ContextPlacements =
            new List<ContextModelPlacement>();
        internal readonly Dictionary<string, string> SourceBuildingByCenter =
            new Dictionary<string, string>(StringComparer.Ordinal);
        internal float AddTo(CityMeshBatch batch, string key, Vector3 position,
            Vector3 availableFootprint, float yaw, float maxHeight, bool footprintIsLocal)
        {
            ContextModelCopies++;
            ContextModelKeys.Add(key);
            string centerKey = CenterKey(position.x, position.z);
            string sourceBuildingId;
            if (!SourceBuildingByCenter.TryGetValue(centerKey, out sourceBuildingId))
                throw new InvalidOperationException("Production AddTo position has no matching source building: " +
                    centerKey);
            ContextPlacements.Add(new ContextModelPlacement(sourceBuildingId, key, position,
                availableFootprint, yaw, maxHeight, footprintIsLocal));
            return maxHeight;
        }
        internal static string CenterKey(float x, float z)
        {
            return x.ToString("R", CultureInfo.InvariantCulture) + "|" +
                z.ToString("R", CultureInfo.InvariantCulture);
        }
    }
    internal sealed class ContextModelPlacement
    {
        internal readonly string sourceBuildingId, key;
        internal readonly Vector3 worldPosition, size;
        internal readonly float yaw, height;
        internal readonly bool footprintIsLocal;
        internal ContextModelPlacement(string id, string key, Vector3 position, Vector3 size,
            float yaw, float height, bool footprintIsLocal)
        {
            sourceBuildingId = id; this.key = key; worldPosition = position;
            this.size = size; this.yaw = yaw; this.height = height;
            this.footprintIsLocal = footprintIsLocal;
        }
    }
}
