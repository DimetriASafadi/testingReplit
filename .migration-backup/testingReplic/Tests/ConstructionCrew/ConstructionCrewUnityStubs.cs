using System;
using System.Collections.Generic;
using System.Numerics;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool destroyed;
        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.destroyed = true;
            if (value is GameObject gameObject) gameObject.SetActive(false);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform { get { return gameObject == null ? null : gameObject.transform; } }
        public T GetComponent<T>() where T : Component
        {
            return gameObject == null ? null : gameObject.GetComponent<T>();
        }
    }

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
        public bool activeInHierarchy { get { return active && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < components.Count; i++)
                if (components[i] is T typed) return typed;
            return null;
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
            Vector3 position = this.position;
            if (parentValue != null) parentValue.children.Remove(this);
            parentValue = parent;
            if (parentValue != null) parentValue.children.Add(this);
            if (worldPositionStays) this.position = position;
        }
        public Vector3 position
        {
            get { return parentValue == null ? localPosition : parentValue.position + parentValue.rotation * localPosition; }
            set { localPosition = parentValue == null ? value : Quaternion.Inverse(parentValue.rotation) * (value - parentValue.position); }
        }
        public Quaternion rotation { get { return parentValue == null ? localRotation : parentValue.rotation * localRotation; } }
    }

    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public sealed class MeshRenderer : Component
    {
        public Material sharedMaterial;
        public Material[] sharedMaterials;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public bool enabled = true;
    }

    public sealed class Mesh : Object
    {
        private Vector3[] vertexData = Array.Empty<Vector3>();
        private readonly List<int[]> submeshTriangles = new List<int[]>();
        public Rendering.IndexFormat indexFormat;
        public int subMeshCount
        {
            get { return submeshTriangles.Count; }
            set
            {
                while (submeshTriangles.Count < value) submeshTriangles.Add(Array.Empty<int>());
                while (submeshTriangles.Count > value) submeshTriangles.RemoveAt(submeshTriangles.Count - 1);
            }
        }
        public Vector3[] vertices { get { return vertexData; } set { vertexData = value ?? Array.Empty<Vector3>(); } }
        public int[] triangles
        {
            get
            {
                int length = 0;
                for (int i = 0; i < submeshTriangles.Count; i++) length += submeshTriangles[i].Length;
                var result = new int[length];
                int offset = 0;
                for (int i = 0; i < submeshTriangles.Count; i++)
                {
                    Array.Copy(submeshTriangles[i], 0, result, offset, submeshTriangles[i].Length);
                    offset += submeshTriangles[i].Length;
                }
                return result;
            }
            set
            {
                submeshTriangles.Clear();
                submeshTriangles.Add(value ?? Array.Empty<int>());
            }
        }
        public Vector3[] normals { get; private set; } = Array.Empty<Vector3>();
        public void SetTriangles(int[] values, int submesh, bool calculateBounds = true)
        {
            if (submesh < 0 || submesh >= submeshTriangles.Count) throw new ArgumentOutOfRangeException(nameof(submesh));
            submeshTriangles[submesh] = (int[])values.Clone();
        }
        public int[] GetTriangles(int submesh) { return (int[])submeshTriangles[submesh].Clone(); }
        public void RecalculateBounds() { }
        public void RecalculateNormals()
        {
            var result = new Vector3[vertexData.Length];
            for (int submesh = 0; submesh < submeshTriangles.Count; submesh++)
            {
                int[] indices = submeshTriangles[submesh];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 normal = Vector3.Cross(vertexData[indices[i + 1]] - vertexData[indices[i]],
                        vertexData[indices[i + 2]] - vertexData[indices[i]]).normalized;
                    result[indices[i]] += normal;
                    result[indices[i + 1]] += normal;
                    result[indices[i + 2]] += normal;
                }
            }
            for (int i = 0; i < result.Length; i++) result[i] = result[i].normalized;
            normals = result;
        }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) { return new Shader { name = name }; }
    }
    public sealed class Material : Object
    {
        public readonly Shader shader;
        private Color baseColor = Color.white;
        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source) { shader = source.shader; baseColor = source.baseColor; }
        public void SetFloat(string property, float value) { }
        public void SetColor(string property, Color color) { if (property == "_BaseColor") baseColor = color; }
        public Color GetColor(string property) { return property == "_BaseColor" ? baseColor : Color.white; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r,float g,float b,float a = 1f) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white { get { return new Color(1f, 1f, 1f, 1f); } }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
        public static Vector3 left { get { return new Vector3(-1f, 0f, 0f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public static Vector3 back { get { return new Vector3(0f, 0f, -1f); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return this / Math.Max(magnitude, 1e-20f); } }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Math.Max(0, Math.Min(1, t)); return a + (b - a) * t; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x*b.x + a.y*b.y + a.z*b.z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static Vector3 operator *(float b, Vector3 a) { return a*b; }
        public static Vector3 operator /(Vector3 a, float b) { return new Vector3(a.x/b,a.y/b,a.z/b); }
    }

    public struct Quaternion
    {
        private readonly System.Numerics.Quaternion value;
        private Quaternion(System.Numerics.Quaternion value) { this.value = System.Numerics.Quaternion.Normalize(value); }
        public static Quaternion identity { get { return new Quaternion(System.Numerics.Quaternion.Identity); } }
        public static Quaternion Euler(float x, float y, float z)
        {
            const float radians = (float)(Math.PI / 180d);
            return new Quaternion(System.Numerics.Quaternion.CreateFromYawPitchRoll(y*radians,x*radians,z*radians));
        }
        public static Quaternion Inverse(Quaternion q) { return new Quaternion(System.Numerics.Quaternion.Inverse(q.value)); }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 a = from.normalized, b = to.normalized;
            float dot = Vector3.Dot(a,b);
            if (dot > .999999f) return identity;
            if (dot < -.999999f) return Euler(180f,0f,0f);
            Vector3 cross = Vector3.Cross(a,b);
            return new Quaternion(new System.Numerics.Quaternion(cross.x,cross.y,cross.z,1f+dot));
        }
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            System.Numerics.Vector3 result = System.Numerics.Vector3.Transform(
                new System.Numerics.Vector3(point.x,point.y,point.z), rotation.value);
            return new Vector3(result.X,result.Y,result.Z);
        }
        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion(System.Numerics.Quaternion.Multiply(a.value,b.value));
        }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 180f / PI;
        public static float Abs(float x) { return Math.Abs(x); }
        public static float Sin(float x) { return (float)Math.Sin(x); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Math.Max(0, Math.Min(1, t)); }
        public static float Cos(float x) { return (float)Math.Cos(x); }
        public static float Atan2(float y,float x) { return (float)Math.Atan2(y,x); }
        public static float Max(float a,float b) { return Math.Max(a,b); }
        public static float Min(float a,float b) { return Math.Min(a,b); }
        public static float Repeat(float t,float length) { return Math.Max(0f,Math.Min(length,t-(float)Math.Floor(t/length)*length)); }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On }
}