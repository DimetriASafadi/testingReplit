using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static void Destroy(Object value)
        {
            // Model hierarchy removal rather than retaining stale native road overlays.
            // Mesh destruction remains a no-op; the geometry owner tracks its releases.
            if (value is GameObject target)
            {
                target.SetActive(false);
                target.transform.SetParent(null, false);
            }
        }
        public static void DestroyImmediate(Object value) { Destroy(value); }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform { get { return gameObject == null ? null : gameObject.transform; } }
        public T GetComponent<T>() where T : Component
        {
            return gameObject == null ? null : gameObject.GetComponent<T>();
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            return gameObject == null ? Array.Empty<T>() : gameObject.GetComponentsInChildren<T>(includeInactive);
        }
    }

    public class MonoBehaviour : Component { }

    public sealed class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        private bool active = true;
        public readonly Transform transform;
        public GameObject(string name = "GameObject")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            components.Add(transform);
        }
        public void SetActive(bool value) { active = value; }
        public bool activeSelf { get { return active; } }
        public bool activeInHierarchy
        {
            get { return active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); }
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < components.Count; i++)
                if (components[i] is T typed) return typed;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            var found = new List<T>();
            Collect(transform, found, includeInactive);
            return found.ToArray();
        }
        private static void Collect<T>(Transform root, List<T> found, bool includeInactive) where T : Component
        {
            if (includeInactive || root.gameObject.activeInHierarchy)
            {
                T component = root.gameObject.GetComponent<T>();
                if (component != null) found.Add(component);
            }
            for (int i = 0; i < root.childCount; i++) Collect(root.GetChild(i), found, includeInactive);
        }
    }

    public sealed class Transform : Component
    {
        private readonly List<Transform> children = new List<Transform>();
        private Transform parentValue;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public Transform parent { get { return parentValue; } }
        public int childCount { get { return children.Count; } }
        public Transform GetChild(int index) { return children[index]; }
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            Vector3 worldPosition = position;
            Quaternion worldRotation = rotation;
            if (parentValue != null) parentValue.children.Remove(this);
            parentValue = parent;
            if (parentValue != null) parentValue.children.Add(this);
            if (worldPositionStays)
            {
                position = worldPosition;
                rotation = worldRotation;
            }
        }
        public Vector3 position
        {
            get { return localToWorldMatrix.MultiplyPoint3x4(Vector3.zero); }
            set { localPosition = parentValue == null ? value : parentValue.InverseTransformPoint(value); }
        }
        public Quaternion rotation
        {
            get { return parentValue == null ? localRotation : parentValue.rotation * localRotation; }
            set { localRotation = parentValue == null ? value : Quaternion.Inverse(parentValue.rotation) * value; }
        }
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                Matrix4x4 local = Matrix4x4.TRS(localPosition, localRotation, localScale);
                return parentValue == null ? local : local * parentValue.localToWorldMatrix;
            }
        }
        public Vector3 TransformPoint(Vector3 point) { return localToWorldMatrix.MultiplyPoint3x4(point); }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            if (!Matrix4x4.Invert(localToWorldMatrix, out Matrix4x4 inverse))
                throw new InvalidOperationException("Singular transform in production fleet fixture.");
            return inverse.MultiplyPoint3x4(point);
        }
    }

    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class MeshRenderer : Component
    {
        public Material sharedMaterial;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public bool enabled = true;
    }

    public sealed class Mesh : Object
    {
        private Vector3[] vertexData = Array.Empty<Vector3>();
        private int[] triangleData = Array.Empty<int>();
        private Vector2[] uvData = Array.Empty<Vector2>();
        private Vector3[] normalData = Array.Empty<Vector3>();
        public Rendering.IndexFormat indexFormat;
        public Vector3[] vertices { get { return vertexData; } set { vertexData = value ?? Array.Empty<Vector3>(); } }
        public int[] triangles { get { return triangleData; } set { triangleData = value ?? Array.Empty<int>(); } }
        public Vector2[] uv { get { return uvData; } set { uvData = value ?? Array.Empty<Vector2>(); } }
        public Vector3[] normals { get { return normalData; } set { normalData = value ?? Array.Empty<Vector3>(); } }
        public int subMeshCount { get { return triangleData.Length == 0 ? 0 : 1; } }
        public void SetVertices(List<Vector3> values) { vertexData = values.ToArray(); }
        public void SetTriangles(List<int> values, int submesh)
        {
            if (submesh != 0) throw new ArgumentOutOfRangeException(nameof(submesh));
            triangleData = values.ToArray();
        }
        public void SetTriangles(int[] values, int submesh)
        {
            if (submesh != 0) throw new ArgumentOutOfRangeException(nameof(submesh));
            triangleData = (int[])values.Clone();
        }
        public int[] GetTriangles(int submesh) { return (int[])triangleData.Clone(); }
        public void RecalculateBounds() { }
        public void RecalculateNormals()
        {
            var result = new Vector3[vertexData.Length];
            for (int i = 0; i + 2 < triangleData.Length; i += 3)
            {
                int a = triangleData[i], b = triangleData[i + 1], c = triangleData[i + 2];
                Vector3 normal = Vector3.Cross(vertexData[b] - vertexData[a], vertexData[c] - vertexData[a]).normalized;
                result[a] += normal;
                result[b] += normal;
                result[c] += normal;
            }
            for (int i = 0; i < result.Length; i++) result[i] = result[i].normalized;
            normalData = result;
        }
        public void CombineMeshes(CombineInstance[] instances, bool mergeSubMeshes, bool useMatrices)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < instances.Length; i++)
            {
                Mesh source = instances[i].mesh;
                int start = vertices.Count;
                Matrix4x4 transform = useMatrices ? instances[i].transform : Matrix4x4.identity;
                Matrix4x4 inverse = Matrix4x4.identity;
                Matrix4x4.Invert(transform, out inverse);
                Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
                for (int v = 0; v < source.vertices.Length; v++)
                {
                    vertices.Add(transform.MultiplyPoint3x4(source.vertices[v]));
                    uv.Add(source.uv.Length == source.vertices.Length ? source.uv[v] : Vector2.zero);
                    Vector3 normal = source.normals.Length == source.vertices.Length
                        ? normalTransform.MultiplyVector(source.normals[v]).normalized : Vector3.up;
                    normals.Add(normal);
                }
                int[] sourceTriangles = instances[i].subMeshIndex == 0 ? source.triangles : Array.Empty<int>();
                for (int t = 0; t < sourceTriangles.Length; t++)
                    triangles.Add(start + sourceTriangles[t]);
            }
            vertexData = vertices.ToArray();
            uvData = uv.ToArray();
            normalData = normals.ToArray();
            triangleData = triangles.ToArray();
        }
    }

    public struct CombineInstance
    {
        public Mesh mesh;
        public int subMeshIndex;
        public Matrix4x4 transform;
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) { return new Shader { name = name }; }
    }

    public sealed class Material : Object
    {
        private readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        private readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Vector2> textureScales = new Dictionary<string, Vector2>();
        public Shader shader;
        public bool enableInstancing;
        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source)
        {
            shader = source.shader;
            foreach (var pair in source.colors) colors.Add(pair.Key, pair.Value);
            foreach (var pair in source.floats) floats.Add(pair.Key, pair.Value);
            foreach (var pair in source.textures) textures.Add(pair.Key, pair.Value);
            foreach (var pair in source.textureScales) textureScales.Add(pair.Key, pair.Value);
            enableInstancing = source.enableInstancing;
        }
        public void SetColor(string property, Color value) { colors[property] = value; }
        public Color GetColor(string property) { return colors.TryGetValue(property, out Color value) ? value : Color.white; }
        public void SetFloat(string property, float value) { floats[property] = value; }
        public float GetFloat(string property) { return floats.TryGetValue(property, out float value) ? value : 0f; }
        public void SetTexture(string property, Texture2D value) { textures[property] = value; }
        public Texture2D GetTexture(string property) { return textures.TryGetValue(property, out Texture2D value) ? value : null; }
        public void SetTextureScale(string property, Vector2 value) { textureScales[property] = value; }
        public Vector2 GetTextureScale(string property) { return textureScales.TryGetValue(property, out Vector2 value) ? value : Vector2.one; }
        public void EnableKeyword(string keyword) { }
    }

    public sealed class Texture2D : Object
    {
        private Color[] pixels = Array.Empty<Color>();
        public readonly int width, height;
        public readonly TextureFormat format;
        public readonly bool mipmap, linear;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public int anisoLevel;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap, bool linear)
        {
            this.width = width;
            this.height = height;
            this.format = format;
            this.mipmap = mipmap;
            this.linear = linear;
        }
        public void SetPixels(Color[] values) { pixels = (Color[])values.Clone(); }
        public Color[] GetPixels() { return (Color[])pixels.Clone(); }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public static class Resources
    {
        private static readonly Shader LitShader = new Shader { name = "Universal Render Pipeline/Lit" };
        public static string RootDirectory;
        public static T Load<T>(string path) where T : class
        {
            if (typeof(T) == typeof(Material) && path == "NewGazaLit")
                return new Material(LitShader) as T;
            if (typeof(T) == typeof(TextAsset) && !string.IsNullOrEmpty(RootDirectory))
            {
                string file = Path.Combine(RootDirectory,
                    path.Replace('/', Path.DirectorySeparatorChar) + ".json");
                if (File.Exists(file)) return new TextAsset(File.ReadAllText(file)) as T;
            }
            return null;
        }
    }

    public sealed class TextAsset : Object
    {
        public readonly string text;
        public TextAsset(string value) { text = value; }
    }

    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };
        public static T FromJson<T>(string json) { return JsonSerializer.Deserialize<T>(json, Options); }
    }

    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1f, 1f, 1f, 1f); } }
        public static Color operator *(Color color, float value)
        {
            return new Color(color.r * value, color.g * value, color.b * value, color.a * value);
        }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0f, 0f); } }
        public static Vector2 one { get { return new Vector2(1f, 1f); } }
        public static Vector2 up { get { return new Vector2(0f, 1f); } }
        public static Vector2 down { get { return new Vector2(0f, -1f); } }
        public static Vector2 right { get { return new Vector2(1f, 0f); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector2 normalized { get { return this / Math.Max(magnitude, 1e-20f); } }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { return a + (b - a) * t; }
        public static float Dot(Vector2 a, Vector2 b) { return a.x * b.x + a.y * b.y; }
        public static float Distance(Vector2 a, Vector2 b) { return (a - b).magnitude; }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.x * b, a.y * b); }
        public static Vector2 operator /(Vector2 a, float b) { return new Vector2(a.x / b, a.y / b); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return this / Math.Max(magnitude, 1e-20f); } }
        public static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            Vector3 direction = Vector3.Lerp(a.normalized, b.normalized, t).normalized;
            return direction * Mathf.Lerp(a.magnitude, b.magnitude, t);
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator *(float b, Vector3 a) { return a * b; }
        public static Vector3 operator /(Vector3 a, float b) { return new Vector3(a.x / b, a.y / b, a.z / b); }
    }

    public struct Quaternion
    {
        private readonly System.Numerics.Quaternion value;
        private Quaternion(System.Numerics.Quaternion value) { this.value = System.Numerics.Quaternion.Normalize(value); }
        public static Quaternion identity { get { return new Quaternion(System.Numerics.Quaternion.Identity); } }
        public static Quaternion Euler(float x, float y, float z)
        {
            const float toRadians = (float)(Math.PI / 180.0);
            return new Quaternion(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * toRadians,
                x * toRadians, z * toRadians));
        }
        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            const float toRadians = (float)(Math.PI / 180.0);
            var normal = new System.Numerics.Vector3(axis.x, axis.y, axis.z);
            return new Quaternion(System.Numerics.Quaternion.CreateFromAxisAngle(
                System.Numerics.Vector3.Normalize(normal), angle * toRadians));
        }
        public static Quaternion Inverse(Quaternion q) { return new Quaternion(System.Numerics.Quaternion.Inverse(q.value)); }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            return new Quaternion(System.Numerics.Quaternion.Slerp(a.value, b.value, Mathf.Clamp01(t)));
        }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 a = from.normalized, b = to.normalized;
            float dot = Vector3.Dot(a, b);
            if (dot > .999999f) return identity;
            if (dot < -.999999f) return Euler(180f, 0f, 0f);
            Vector3 cross = Vector3.Cross(a, b);
            return new Quaternion(new System.Numerics.Quaternion(cross.x, cross.y, cross.z, 1f + dot));
        }
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            Vector3 f = forward.normalized;
            Vector3 r = Vector3.Cross(upwards, f).normalized;
            Vector3 u = Vector3.Cross(f, r);
            var matrix = new System.Numerics.Matrix4x4(
                r.x, r.y, r.z, 0f, u.x, u.y, u.z, 0f, f.x, f.y, f.z, 0f,
                0f, 0f, 0f, 1f);
            return new Quaternion(System.Numerics.Quaternion.CreateFromRotationMatrix(matrix));
        }
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Abs(System.Numerics.Quaternion.Dot(a.value, b.value));
            return (float)(Math.Acos(Math.Min(1f, dot)) * 2.0 * 180.0 / Math.PI);
        }
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            var input = new System.Numerics.Vector3(point.x, point.y, point.z);
            System.Numerics.Vector3 output = System.Numerics.Vector3.Transform(input, rotation.value);
            return new Vector3(output.X, output.Y, output.Z);
        }
        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion(System.Numerics.Quaternion.Multiply(a.value, b.value));
        }
        internal System.Numerics.Quaternion NumericsValue { get { return value; } }
    }

    public struct Matrix4x4
    {
        private readonly System.Numerics.Matrix4x4 value;
        private Matrix4x4(System.Numerics.Matrix4x4 value) { this.value = value; }
        public static Matrix4x4 identity { get { return new Matrix4x4(System.Numerics.Matrix4x4.Identity); } }
        public static Matrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var s = System.Numerics.Matrix4x4.CreateScale(scale.x, scale.y, scale.z);
            var r = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation.NumericsValue);
            var t = System.Numerics.Matrix4x4.CreateTranslation(position.x, position.y, position.z);
            return new Matrix4x4(s * r * t);
        }
        public Vector3 MultiplyPoint3x4(Vector3 point)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(point.x, point.y, point.z), value);
            return new Vector3(result.X, result.Y, result.Z);
        }
        public Vector3 MultiplyVector(Vector3 vector)
        {
            var result = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(vector.x, vector.y, vector.z), value);
            return new Vector3(result.X, result.Y, result.Z);
        }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) { return new Matrix4x4(a.value * b.value); }
        public static bool Invert(Matrix4x4 input, out Matrix4x4 result)
        {
            bool ok = System.Numerics.Matrix4x4.Invert(input.value, out System.Numerics.Matrix4x4 inverted);
            result = new Matrix4x4(inverted);
            return ok;
        }
        public static Matrix4x4 Transpose(Matrix4x4 input)
        {
            return new Matrix4x4(System.Numerics.Matrix4x4.Transpose(input.value));
        }
        public float[] ToArray()
        {
            return new[]
            {
                value.M11,value.M12,value.M13,value.M14, value.M21,value.M22,value.M23,value.M24,
                value.M31,value.M32,value.M33,value.M34, value.M41,value.M42,value.M43,value.M44
            };
        }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public static float Abs(float value) { return Math.Abs(value); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Max(params float[] values)
        {
            float result = float.NegativeInfinity;
            for (int i = 0; i < values.Length; i++) result = Math.Max(result, values[i]);
            return result;
        }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        public static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
        public static float Clamp01(float value) { return Clamp(value, 0f, 1f); }
        public static float Sqrt(float value) { return (float)Math.Sqrt(value); }
        public static float Sin(float value) { return (float)Math.Sin(value); }
        public static float Cos(float value) { return (float)Math.Cos(value); }
        public static float Asin(float value) { return (float)Math.Asin(value); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Pow(float value, float power) { return (float)Math.Pow(value, power); }
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
        public static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }
        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = t * t * (3f - 2f * t);
            return LerpUnclamped(from, to, t);
        }
        public static float Repeat(float value, float length)
        {
            return Clamp(value - Floor(value / length) * length, 0f, length);
        }
        public static float Floor(float value) { return (float)Math.Floor(value); }
        public static float InverseLerp(float a, float b, float value)
        {
            if (a == b) return 0f;
            return Clamp01((value - a) / (b - a));
        }
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = Repeat(b - a + 180f, 360f) - 180f;
            return a + delta * Clamp01(t);
        }
        public static float DeltaAngle(float current, float target)
        {
            return Repeat(target - current + 180f, 360f) - 180f;
        }
        public static float PerlinNoise(float x, float y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float xf = x - x0, yf = y - y0;
            float u = SmoothStep(0f, 1f, xf), v = SmoothStep(0f, 1f, yf);
            float a = Gradient(x0, y0, xf, yf);
            float b = Gradient(x0 + 1, y0, xf - 1f, yf);
            float c = Gradient(x0, y0 + 1, xf, yf - 1f);
            float d = Gradient(x0 + 1, y0 + 1, xf - 1f, yf - 1f);
            return Clamp01((Lerp(Lerp(a, b, u), Lerp(c, d, u), v) + 1f) * .5f);
        }
        private static float Gradient(int x, int y, float dx, float dy)
        {
            uint hash = unchecked((uint)(x * 374761393 + y * 668265263));
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            float angle = (hash & 0xffff) * (2f * PI / 65535f);
            return (float)Math.Cos(angle) * dx + (float)Math.Sin(angle) * dy;
        }
    }

    public static class Time { public static float deltaTime; }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On }
}