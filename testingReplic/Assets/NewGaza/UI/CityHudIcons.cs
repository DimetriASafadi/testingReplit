using System;
using UnityEngine;

namespace NewGaza.UI
{
    /// <summary>Small deterministic outline sprites owned by one CityHud instance.</summary>
    public sealed class CityHudIcons : IDisposable
    {
        public enum Icon { None, Map, Projects, Fleet, Investment, Resources, Gift, Settings, Close }

        private const int Size = 48;
        private readonly Sprite[] sprites = new Sprite[9];
        private readonly Texture2D[] textures = new Texture2D[9];
        private bool disposed;

        public Sprite Get(Icon icon)
        {
            if (icon == Icon.None) return null;
            if (disposed) throw new ObjectDisposedException("CityHudIcons");
            int index = (int)icon;
            if (index <= 0 || index >= sprites.Length) throw new ArgumentOutOfRangeException("icon");
            if (sprites[index] == null) Create(icon, index);
            return sprites[index];
        }

        private void Create(Icon icon, int index)
        {
            var pixels = new Color32[Size * Size];
            switch (icon)
            {
                case Icon.Map:
                    Line(pixels, 24, 7, 17, 16, 3);
                    Line(pixels, 17, 16, 15, 21, 3);
                    Line(pixels, 15, 21, 24, 37, 3);
                    Line(pixels, 24, 37, 33, 21, 3);
                    Line(pixels, 33, 21, 31, 16, 3);
                    Line(pixels, 31, 16, 24, 7, 3);
                    Circle(pixels, 24, 19, 4.5f, 2.5f);
                    break;
                case Icon.Projects:
                    Polyline(pixels, new[] { 14, 10, 29, 10, 35, 16, 35, 38, 14, 38, 14, 10 }, 3);
                    Line(pixels, 29, 10, 29, 16, 3);
                    Line(pixels, 29, 16, 35, 16, 3);
                    Line(pixels, 19, 23, 30, 23, 2.5f);
                    Line(pixels, 19, 29, 30, 29, 2.5f);
                    Line(pixels, 19, 34, 27, 34, 2.5f);
                    break;
                case Icon.Fleet:
                    Polyline(pixels, new[] { 8, 17, 26, 17, 26, 31, 8, 31, 8, 17 }, 3);
                    Polyline(pixels, new[] { 26, 20, 32, 20, 38, 26, 38, 31, 26, 31 }, 3);
                    Line(pixels, 31, 21, 37, 26, 3);
                    Circle(pixels, 16, 13, 4, 2.5f);
                    Circle(pixels, 33, 13, 4, 2.5f);
                    break;
                case Icon.Investment:
                    Line(pixels, 10, 10, 10, 37, 3);
                    Line(pixels, 10, 10, 39, 10, 3);
                    Polyline(pixels, new[] { 14, 16, 21, 23, 27, 20, 37, 33 }, 3.5f);
                    Circle(pixels, 21, 23, 2, 2.5f);
                    Circle(pixels, 27, 20, 2, 2.5f);
                    Circle(pixels, 37, 33, 2, 2.5f);
                    break;
                case Icon.Resources:
                    Polyline(pixels, new[] { 10, 30, 10, 18, 24, 10, 38, 18, 38, 30, 24, 38, 10, 30 }, 3);
                    Line(pixels, 10, 18, 24, 26, 3);
                    Line(pixels, 38, 18, 24, 26, 3);
                    Line(pixels, 24, 26, 24, 38, 3);
                    Line(pixels, 17, 14, 31, 22, 2);
                    break;
                case Icon.Gift:
                    Polyline(pixels, new[] { 12, 17, 36, 17, 36, 33, 12, 33, 12, 17 }, 3);
                    Polyline(pixels, new[] { 10, 33, 38, 33, 38, 38, 10, 38, 10, 33 }, 3);
                    Line(pixels, 24, 17, 24, 38, 3);
                    Ellipse(pixels, 19, 41, 5, 4, 3);
                    Ellipse(pixels, 29, 41, 5, 4, 3);
                    Line(pixels, 24, 37, 19, 41, 3);
                    Line(pixels, 24, 37, 29, 41, 3);
                    break;
                case Icon.Settings:
                {
                    var gear = new int[34];
                    for (int point = 0; point < 16; point++)
                    {
                        float angle = point * Mathf.PI / 8f - Mathf.PI * 0.5f;
                        float radius = point % 2 == 0 ? 17 : 13;
                        gear[point * 2] = Mathf.RoundToInt(24 + Mathf.Cos(angle) * radius);
                        gear[point * 2 + 1] = Mathf.RoundToInt(24 + Mathf.Sin(angle) * radius);
                    }
                    gear[32] = gear[0];
                    gear[33] = gear[1];
                    Polyline(pixels, gear, 3);
                    Circle(pixels, 24, 24, 6, 3);
                    break;
                }
                case Icon.Close:
                    Line(pixels, 14, 14, 34, 34, 3.5f);
                    Line(pixels, 34, 14, 14, 34, 3.5f);
                    break;
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.name = "City HUD " + icon + " outline";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100);
            sprite.name = "City HUD " + icon + " outline";
            textures[index] = texture;
            sprites[index] = sprite;
        }

        private static void Polyline(Color32[] pixels, int[] points, float width)
        {
            for (int i = 0; i + 3 < points.Length; i += 2)
                Line(pixels, points[i], points[i + 1], points[i + 2], points[i + 3], width);
        }

        private static void Line(Color32[] pixels, float x1, float y1, float x2, float y2, float width)
        {
            float minX = Mathf.Max(0, Mathf.Floor(Mathf.Min(x1, x2) - width));
            float maxX = Mathf.Min(Size - 1, Mathf.Ceil(Mathf.Max(x1, x2) + width));
            float minY = Mathf.Max(0, Mathf.Floor(Mathf.Min(y1, y2) - width));
            float maxY = Mathf.Min(Size - 1, Mathf.Ceil(Mathf.Max(y1, y2) + width));
            float dx = x2 - x1;
            float dy = y2 - y1;
            float lengthSquared = dx * dx + dy * dy;
            for (int y = (int)minY; y <= (int)maxY; y++)
                for (int x = (int)minX; x <= (int)maxX; x++)
                {
                    float t = lengthSquared <= 0 ? 0 :
                        Mathf.Clamp01(((x + 0.5f - x1) * dx + (y + 0.5f - y1) * dy) / lengthSquared);
                    float px = x1 + t * dx;
                    float py = y1 + t * dy;
                    float distance = Mathf.Sqrt((x + 0.5f - px) * (x + 0.5f - px) +
                        (y + 0.5f - py) * (y + 0.5f - py));
                    Set(pixels, x, y, Mathf.Clamp01(width * 0.5f + 0.65f - distance));
                }
        }

        private static void Circle(Color32[] pixels, float cx, float cy, float radius, float width)
        {
            float outer = radius + width;
            for (int y = Mathf.Max(0, Mathf.FloorToInt(cy - outer)); y <= Mathf.Min(Size - 1, Mathf.CeilToInt(cy + outer)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(cx - outer)); x <= Mathf.Min(Size - 1, Mathf.CeilToInt(cx + outer)); x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    Set(pixels, x, y, Mathf.Clamp01(width * 0.5f + 0.65f - Mathf.Abs(distance - radius)));
                }
        }

        private static void Ellipse(Color32[] pixels, float cx, float cy, float rx, float ry, float width)
        {
            const int steps = 28;
            float previousX = cx + rx;
            float previousY = cy;
            for (int i = 1; i <= steps; i++)
            {
                float angle = i * Mathf.PI * 2 / steps;
                float nextX = cx + Mathf.Cos(angle) * rx;
                float nextY = cy + Mathf.Sin(angle) * ry;
                Line(pixels, previousX, previousY, nextX, nextY, width);
                previousX = nextX;
                previousY = nextY;
            }
        }

        private static void Set(Color32[] pixels, int x, int y, float alpha)
        {
            byte value = (byte)Mathf.RoundToInt(alpha * 255);
            int index = y * Size + x;
            if (value > pixels[index].a) pixels[index] = new Color32(255, 255, 255, value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) UnityEngine.Object.Destroy(sprites[i]);
                if (textures[i] != null) UnityEngine.Object.Destroy(textures[i]);
                sprites[i] = null;
                textures[i] = null;
            }
        }
    }
}