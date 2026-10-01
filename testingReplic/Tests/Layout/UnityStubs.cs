using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => this / Math.Max(magnitude, 1e-20f);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator *(float b, Vector3 a) => a * b;
        public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.x / b, a.y / b, a.z / b);

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Quaternion
    {
        private float yaw;

        private Quaternion(float yaw) { this.yaw = yaw; }
        public static Quaternion identity => new Quaternion(0f);
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(y);

        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            return new Quaternion((float)(Math.Atan2(forward.x, forward.z) * 180.0 / Math.PI));
        }

        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float angle = rotation.yaw * (float)Math.PI / 180f;
            float sine = (float)Math.Sin(angle);
            float cosine = (float)Math.Cos(angle);
            return new Vector3(point.x * cosine + point.z * sine, point.y,
                -point.x * sine + point.z * cosine);
        }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Max(params float[] values)
        {
            float max = float.NegativeInfinity;
            foreach (float value in values) if (value > max) max = value;
            return max;
        }
        public static float Pow(float value, float power) => (float)Math.Pow(value, power);
    }
}