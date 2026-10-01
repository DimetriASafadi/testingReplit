using System.Collections.Generic;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Low-poly, mechanically readable surfaces shared by the presentation fleet.</summary>
    internal static class EquipmentGeometry
    {
        internal const int TrackShoeCountPerSide = 20;
        private const float TrackShoeThickness = .075f;
        private const float TrackShoeWidthMargin = .02f;

        internal static Mesh TrackBelt(CityGeometry owner, string name, float length, float height, float width)
        {
            float radius = height * .5f;
            float straight = Mathf.Max(.05f, length * .5f - radius);
            var outline = new List<Vector2>();
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(0f, Mathf.PI, i / 8f);
                outline.Add(new Vector2(radius * Mathf.Cos(a), straight + radius * Mathf.Sin(a)));
            }
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Lerp(Mathf.PI, Mathf.PI * 2f, i / 8f);
                outline.Add(new Vector2(radius * Mathf.Cos(a), -straight + radius * Mathf.Sin(a)));
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int[] outlineStart = new int[2];
            int count = outline.Count;
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * width * .5f;
                outlineStart[side] = vertices.Count;
                for (int i = 0; i < count; i++)
                    vertices.Add(new Vector3(x, radius + outline[i].x, outline[i].y));
                int center = vertices.Count;
                vertices.Add(new Vector3(x, radius, 0f));
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    if (side == 0)
                        triangles.AddRange(new[] { center, outlineStart[side] + next, outlineStart[side] + i });
                    else
                        triangles.AddRange(new[] { center, outlineStart[side] + i, outlineStart[side] + next });
                }
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int a = outlineStart[0] + i, b = outlineStart[0] + next;
                int c = outlineStart[1] + next, d = outlineStart[1] + i;
                triangles.AddRange(new[] { a, b, c, a, c, d });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        internal static Mesh TrackShoeLoop(CityGeometry owner, string name, float length,
            float height, float width, List<Vector3> vertices)
        {
            var mesh = new Mesh { name = name };
            int[] triangles = new int[TrackShoeCountPerSide * 36];
            int[] boxTriangles =
            {
                0,2,1, 0,3,2, 4,5,6, 4,6,7,
                1,2,6, 1,6,5, 0,4,7, 0,7,3,
                3,7,6, 3,6,2, 0,1,5, 0,5,4
            };
            for (int shoe = 0; shoe < TrackShoeCountPerSide; shoe++)
                for (int triangle = 0; triangle < boxTriangles.Length; triangle++)
                    triangles[shoe * boxTriangles.Length + triangle] =
                        shoe * 8 + boxTriangles[triangle];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            WriteTrackShoeVertices(mesh, vertices, length, height, width, 0f);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        internal static void UpdateTrackShoeLoop(Mesh mesh, List<Vector3> vertices,
            float length, float height, float width, float travelDistance)
        {
            WriteTrackShoeVertices(mesh, vertices, length, height, width, travelDistance);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void WriteTrackShoeVertices(Mesh mesh, List<Vector3> vertices,
            float length, float height, float width, float travelDistance)
        {
            vertices.Clear();
            float radius = height * .5f;
            float straight = length * .5f - radius;
            float straightLength = straight * 2f;
            float arcLength = Mathf.PI * radius;
            float perimeter = straightLength * 2f + arcLength * 2f;
            float spacing = perimeter / TrackShoeCountPerSide;
            float shoeLength = spacing * .86f;
            float halfWidth = (width + TrackShoeWidthMargin) * .5f;
            float halfThickness = TrackShoeThickness * .5f;

            for (int shoe = 0; shoe < TrackShoeCountPerSide; shoe++)
            {
                float distance = Mathf.Repeat(shoe * spacing - travelDistance, perimeter);
                Vector2 center;
                Vector2 tangent;
                Vector2 normal;
                if (distance < straightLength)
                {
                    center = new Vector2(height, straight - distance);
                    tangent = new Vector2(0f, -1f);
                    normal = new Vector2(1f, 0f);
                }
                else if ((distance -= straightLength) < arcLength)
                {
                    float angle = Mathf.PI * 2f - distance / radius;
                    center = new Vector2(radius + radius * Mathf.Cos(angle),
                        -straight + radius * Mathf.Sin(angle));
                    tangent = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
                    normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                else if ((distance -= arcLength) < straightLength)
                {
                    center = new Vector2(0f, -straight + distance);
                    tangent = new Vector2(0f, 1f);
                    normal = new Vector2(-1f, 0f);
                }
                else
                {
                    distance -= straightLength;
                    float angle = Mathf.PI - distance / radius;
                    center = new Vector2(radius + radius * Mathf.Cos(angle),
                        straight + radius * Mathf.Sin(angle));
                    tangent = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
                    normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }

                Vector3 center3 = new Vector3(0f, center.x, center.y);
                Vector3 normal3 = new Vector3(0f, normal.x, normal.y);
                Vector3 tangent3 = new Vector3(0f, tangent.x, tangent.y);
                Vector3 halfX = Vector3.right * halfWidth;
                Vector3 halfN = normal3 * halfThickness;
                Vector3 halfT = tangent3 * (shoeLength * .5f);
                vertices.Add(center3 - halfX - halfN - halfT);
                vertices.Add(center3 + halfX - halfN - halfT);
                vertices.Add(center3 + halfX + halfN - halfT);
                vertices.Add(center3 - halfX + halfN - halfT);
                vertices.Add(center3 - halfX - halfN + halfT);
                vertices.Add(center3 + halfX - halfN + halfT);
                vertices.Add(center3 + halfX + halfN + halfT);
                vertices.Add(center3 - halfX + halfN + halfT);
            }
            mesh.SetVertices(vertices);
        }

        internal static Mesh TireTread(CityGeometry owner, string name)
        {
            const int around = 24;
            const int across = 8;
            const float majorRadius = .36f;
            const float sectionRadius = .15f;
            var vertices = new Vector3[(around + 1) * (across + 1)];
            var triangles = new int[around * across * 6];
            for (int i = 0; i <= around; i++)
            {
                float a = i * Mathf.PI * 2f / around;
                for (int j = 0; j <= across; j++)
                {
                    float b = j * Mathf.PI * 2f / across;
                    float r = majorRadius + sectionRadius * Mathf.Cos(b);
                    vertices[i * (across + 1) + j] =
                        new Vector3(Mathf.Sin(a) * r, Mathf.Sin(b) * sectionRadius, Mathf.Cos(a) * r);
                }
            }
            int index = 0;
            for (int i = 0; i < around; i++)
                for (int j = 0; j < across; j++)
                {
                    int a = i * (across + 1) + j;
                    int b = a + across + 1;
                    triangles[index++] = a;
                    triangles[index++] = b;
                    triangles[index++] = a + 1;
                    triangles[index++] = a + 1;
                    triangles[index++] = b;
                    triangles[index++] = b + 1;
                }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        internal static Mesh BucketShell(CityGeometry owner, string name)
        {
            // Open, rolled bowl: the profile curves up from the cutting edge into the heel.
            Vector2[] profile =
            {
                new Vector2(-.17f,.42f), new Vector2(-.15f,.29f), new Vector2(-.11f,.11f),
                new Vector2(-.04f,-.08f), new Vector2(.08f,-.23f), new Vector2(.25f,-.29f)
            };
            const int across = 6;
            const float halfWidth = .32f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int layer = 0; layer < 2; layer++)
                for (int i = 0; i < profile.Length; i++)
                    for (int j = 0; j <= across; j++)
                    {
                        float x = Mathf.Lerp(-halfWidth, halfWidth, j / (float)across);
                        float thickness = layer == 0 ? 0f : .025f;
                        vertices.Add(new Vector3(x, profile[i].x - thickness, profile[i].y));
                    }
            int stride = across + 1;
            int sheetSize = profile.Length * stride;
            for (int layer = 0; layer < 2; layer++)
            {
                int offset = layer * sheetSize;
                for (int i = 0; i < profile.Length - 1; i++)
                    for (int j = 0; j < across; j++)
                    {
                        int a = offset + i * stride + j;
                        int b = a + 1;
                        int c = a + stride + 1;
                        int d = a + stride;
                        if (layer == 0) triangles.AddRange(new[] { a, b, c, a, c, d });
                        else triangles.AddRange(new[] { a, c, b, a, d, c });
                    }
            }
            // Join the rolled side edges to make the shell a real, closed plate.
            for (int i = 0; i < profile.Length - 1; i++)
                for (int edge = 0; edge < 2; edge++)
                {
                    int j = edge == 0 ? 0 : across;
                    int a = i * stride + j;
                    int b = (i + 1) * stride + j;
                    int c = sheetSize + (i + 1) * stride + j;
                    int d = sheetSize + i * stride + j;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
            int first = 0, last = (profile.Length - 1) * stride;
            for (int j = 0; j < across; j++)
            {
                triangles.AddRange(new[] { first + j, sheetSize + first + j,
                    sheetSize + first + j + 1, first + j, sheetSize + first + j + 1, first + j + 1 });
                triangles.AddRange(new[] { last + j, last + j + 1,
                    sheetSize + last + j + 1, last + j, sheetSize + last + j + 1, sheetSize + last + j });
            }

            // Thick, pressed side cheeks carry the bucket pivot load.
            AddCheek(vertices, triangles, -halfWidth, halfWidth);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        private static void AddCheek(List<Vector3> vertices, List<int> triangles, float left, float right)
        {
            Vector2[] shape =
            {
                new Vector2(-.17f,.39f), new Vector2(.22f,.21f), new Vector2(.28f,-.23f),
                new Vector2(.05f,-.22f), new Vector2(-.15f,.04f)
            };
            for (int side = 0; side < 2; side++)
            {
                int start = vertices.Count;
                float x = side == 0 ? left : right;
                foreach (Vector2 p in shape) vertices.Add(new Vector3(x, p.x, p.y));
                int center = vertices.Count;
                vertices.Add(new Vector3(x, -.01f, .02f));
                for (int i = 0; i < shape.Length; i++)
                {
                    int next = (i + 1) % shape.Length;
                    if (side == 0) triangles.AddRange(new[] { center, start + next, start + i });
                    else triangles.AddRange(new[] { center, start + i, start + next });
                }
            }
        }

        internal static Mesh CurvedBlade(CityGeometry owner, string name)
        {
            const int across = 16;
            const int rows = 5;
            const float halfWidth = 1.02f;
            const float halfHeight = .34f;
            const float thickness = .055f;
            var vertices = new Vector3[(across + 1) * (rows + 1) * 2];
            var triangles = new List<int>();
            for (int face = 0; face < 2; face++)
                for (int i = 0; i <= across; i++)
                    for (int j = 0; j <= rows; j++)
                    {
                        float u = i / (float)across;
                        float v = j / (float)rows;
                        float x = Mathf.Lerp(-halfWidth, halfWidth, u);
                        float y = Mathf.Lerp(-halfHeight, halfHeight, v);
                        float sweep = .14f * (1f - x * x / (halfWidth * halfWidth));
                        float curl = .14f * v * v;
                        float z = sweep - curl + (face == 0 ? 0f : -thickness);
                        int at = face * (across + 1) * (rows + 1) + i * (rows + 1) + j;
                        vertices[at] = new Vector3(x, y, z);
                    }
            int faceSize = (across + 1) * (rows + 1);
            for (int face = 0; face < 2; face++)
            {
                int offset = face * faceSize;
                for (int i = 0; i < across; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        int a = offset + i * (rows + 1) + j;
                        int b = a + rows + 1;
                        int c = b + 1;
                        int d = a + 1;
                        if (face == 0) triangles.AddRange(new[] { a, b, c, a, c, d });
                        else triangles.AddRange(new[] { a, c, b, a, d, c });
                    }
            }
            for (int i = 0; i < across; i++)
            {
                AddBladeRim(triangles, i * (rows + 1), (i + 1) * (rows + 1), faceSize);
                int a = i * (rows + 1) + rows;
                AddBladeRim(triangles, a, a + rows + 1, faceSize);
            }
            AddBladeRim(triangles, 0, rows, faceSize);
            AddBladeRim(triangles, across * (rows + 1), across * (rows + 1) + rows, faceSize);
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        private static void AddBladeRim(List<int> triangles, int a, int b, int faceSize)
        {
            triangles.AddRange(new[] { a, b, faceSize + b, a, faceSize + b, faceSize + a });
        }

        internal static Mesh TaperedBeam(CityGeometry owner, string name)
        {
            Vector3[] vertices =
            {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f),
                new Vector3(-.34f,.5f,-.36f), new Vector3(.34f,.5f,-.36f),
                new Vector3(.34f,.5f,.36f), new Vector3(-.34f,.5f,.36f)
            };
            int[] triangles =
            {
                0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4,
                1,2,6,1,6,5, 2,3,7,2,7,6, 3,0,4,3,4,7
            };
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        internal static Mesh WedgeTooth(CityGeometry owner, string name)
        {
            Vector3[] vertices =
            {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.25f,-.5f), new Vector3(-.5f,.25f,-.5f),
                new Vector3(-.27f,-.5f,.5f), new Vector3(.27f,-.5f,.5f),
                new Vector3(.27f,.05f,.5f), new Vector3(-.27f,.05f,.5f),
                new Vector3(0f,-.28f,.5f)
            };
            int[] triangles =
            {
                0,2,1,0,3,2, 0,1,5,0,5,4, 1,2,6,1,6,5,
                2,3,8,2,8,6, 3,0,4,3,4,8, 4,5,6,4,6,7, 6,8,7
            };
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }

        internal static Mesh Quad(CityGeometry owner, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = new[] { a, b, c, d },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 }
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return owner.Own(mesh);
        }
    }
}