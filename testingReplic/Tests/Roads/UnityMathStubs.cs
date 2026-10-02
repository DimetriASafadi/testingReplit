using System;
using System.IO;
using System.Text.Json;

namespace UnityEngine
{
    public class Object { }
    public sealed class TextAsset : Object
    {
        public readonly string text;
        public TextAsset(string value) { text = value; }
    }
    public static class Resources
    {
        public static string RootDirectory;
        public static T Load<T>(string path) where T : class
        {
            if (typeof(T) != typeof(TextAsset) || string.IsNullOrEmpty(RootDirectory)) return null;
            string file = Path.Combine(RootDirectory, path.Replace('/', Path.DirectorySeparatorChar) + ".json");
            return File.Exists(file) ? new TextAsset(File.ReadAllText(file)) as T : null;
        }
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
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { float m = magnitude; return m < 1e-20f ? zero : this / m; } }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 operator +(Vector3 a, Vector3 b)
        { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b)
        { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b)
        { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b)
        { return new Vector3(a.x / b, a.y / b, a.z / b); }
    }
    public static class Mathf
    {
        public static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
        public static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
}