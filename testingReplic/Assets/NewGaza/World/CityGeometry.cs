using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    internal enum SurfaceKind
    {
        None,
        Plaster,
        Concrete,
        Stone,
        Asphalt
    }

    // Small reusable source meshes. Static details are merged by material into district-sized
    // chunks, rather than producing a Renderer (or a collider) for every window and leaf.
    internal sealed class CityGeometry : IDisposable
    {
        internal readonly Mesh Box;
        internal readonly Mesh Cylinder;
        internal readonly Mesh Cone;
        internal readonly Mesh Roof;
        internal readonly Mesh Leaf;
        internal readonly Mesh BrokenConcrete;
        private readonly List<Mesh> owned = new List<Mesh>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Dictionary<SurfaceKind, Texture2D> surfaceTextures =
            new Dictionary<SurfaceKind, Texture2D>();
        private readonly Shader shader;
        private readonly Material materialTemplate;

        internal CityGeometry()
        {
            // A Resources material keeps the Lit shader and its variants in player builds,
            // where a Shader.Find-only runtime material can otherwise be stripped.
            materialTemplate = Resources.Load<Material>("NewGazaLit");
            shader = materialTemplate != null ? materialTemplate.shader : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("New Gaza requires Resources/NewGazaLit (URP Lit) or the Universal Render Pipeline/Lit shader.");
            Box = CreateBox();
            Cylinder = CreateRound(10, 1f);
            Cone = CreateRound(8, 0.04f);
            Roof = CreateRoof();
            Leaf = CreateLeaf();
            BrokenConcrete = CreateBrokenConcrete();
        }

        internal Material Material(string key, Color color, float smoothness = 0.15f,
            float emission = 0f, SurfaceKind surface = SurfaceKind.None)
        {
            if (materials.TryGetValue(key, out Material existing)) return existing;
            var material = materialTemplate != null ? new Material(materialTemplate) : new Material(shader);
            material.name = "New Gaza • " + key;
            material.enableInstancing = true;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (surface != SurfaceKind.None)
                material.SetTexture("_BaseMap", SurfaceTexture(surface));
            if (emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }
            materials.Add(key, material);
            return material;
        }

        internal Mesh Own(Mesh mesh)
        {
            owned.Add(mesh);
            return mesh;
        }

        internal void Release(Mesh mesh)
        {
            // Shared primitive meshes belong to the geometry owner, not a single rig.
            if (mesh == Box || mesh == Cylinder || mesh == Cone || mesh == Roof || mesh == Leaf || mesh == BrokenConcrete) return;
            if (mesh != null && owned.Remove(mesh))
                UnityEngine.Object.Destroy(mesh);
        }

        private Mesh Make(string name, Vector3[] vertices, int[] triangles, Vector2[] uv = null)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            if (uv != null) mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Own(mesh);
        }

        private Mesh CreateBox()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            Vector2[] faceUv = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            Vector3[] corners =
            {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
            };
            int[] faces = { 0,3,2,1, 5,6,7,4, 4,7,3,0, 1,2,6,5, 3,7,6,2, 4,0,1,5 };
            for (int i = 0; i < faces.Length; i += 4)
            {
                int start = vertices.Count;
                for (int j = 0; j < 4; j++)
                {
                    vertices.Add(corners[faces[i + j]]);
                    uv.Add(faceUv[j]);
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            return Make("Shared hard-edged cube", vertices.ToArray(), triangles.ToArray(), uv.ToArray());
        }

        private Texture2D SurfaceTexture(SurfaceKind kind)
        {
            if (surfaceTextures.TryGetValue(kind, out Texture2D existing)) return existing;
            const int resolution = 64;
            float offset = (int)kind * 17.31f;
            float broadStrength, grainStrength, streakStrength;
            switch (kind)
            {
                case SurfaceKind.Plaster:
                    broadStrength = .12f;
                    grainStrength = .018f;
                    streakStrength = .045f;
                    break;
                case SurfaceKind.Concrete:
                    broadStrength = .13f;
                    grainStrength = .022f;
                    streakStrength = .025f;
                    break;
                case SurfaceKind.Stone:
                    broadStrength = .09f;
                    grainStrength = .014f;
                    streakStrength = .018f;
                    break;
                case SurfaceKind.Asphalt:
                    broadStrength = .075f;
                    grainStrength = .012f;
                    streakStrength = .012f;
                    break;
                default:
                    return null;
            }

            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)resolution;
                    float v = y / (float)resolution;
                    float broad = TilePerlin(u,v,3.1f,offset + .7f,offset + 2.3f);
                    float grain = TilePerlin(u,v,11f,offset + 4.1f,offset + 6.7f);
                    float streaks = TilePerlin(u,v,7.5f,.8f,offset + 9.2f,offset + 11.4f);
                    float value = 1f + (broad - .5f) * broadStrength +
                        (grain - .5f) * grainStrength + (streaks - .5f) * streakStrength;
                    pixels[y * resolution + x] = new Color(value,value,value,1f);
                }

            var texture = new Texture2D(resolution,resolution,TextureFormat.RGBA32,true,false)
            {
                name = "New Gaza • procedural " + kind.ToString().ToLowerInvariant() + " albedo",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1
            };
            texture.SetPixels(pixels);
            texture.Apply(true,false);
            surfaceTextures.Add(kind,texture);
            return texture;
        }

        private static float TilePerlin(float u, float v, float frequency, float offsetX,
            float offsetY)
        {
            return TilePerlin(u,v,frequency,frequency,offsetX,offsetY);
        }

        private static float TilePerlin(float u, float v, float frequencyX, float frequencyY,
            float offsetX, float offsetY)
        {
            float blendU = u * u * (3f - 2f * u);
            float blendV = v * v * (3f - 2f * v);
            float leftBottom = Mathf.PerlinNoise(offsetX + u * frequencyX,
                offsetY + v * frequencyY);
            float rightBottom = Mathf.PerlinNoise(offsetX + (u - 1f) * frequencyX,
                offsetY + v * frequencyY);
            float leftTop = Mathf.PerlinNoise(offsetX + u * frequencyX,
                offsetY + (v - 1f) * frequencyY);
            float rightTop = Mathf.PerlinNoise(offsetX + (u - 1f) * frequencyX,
                offsetY + (v - 1f) * frequencyY);
            return Mathf.Lerp(Mathf.Lerp(leftBottom,rightBottom,blendU),
                Mathf.Lerp(leftTop,rightTop,blendU),blendV);
        }

        private Mesh CreateRound(int segments, float topRadius)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p = new Vector3(Mathf.Cos(a) * .5f, -.5f, Mathf.Sin(a) * .5f);
                Vector3 q = new Vector3(Mathf.Cos(b) * .5f, -.5f, Mathf.Sin(b) * .5f);
                Vector3 r = new Vector3(q.x * topRadius, .5f, q.z * topRadius);
                Vector3 s = new Vector3(p.x * topRadius, .5f, p.z * topRadius);
                int n = vertices.Count;
                vertices.AddRange(new[] { p, s, r, q, new Vector3(0f,-.5f,0f), q, p,
                    new Vector3(0f,.5f,0f), s, r });
                // Side p/s/r and p/r/q faces point radially out. Caps use center/p/q
                // (negative Y) and center/r/s (positive Y), not the inverted order.
                triangles.AddRange(new[] { n,n+1,n+2,n,n+2,n+3,n+4,n+6,n+5,n+7,n+9,n+8 });
            }
            return Make("Shared low-poly round", vertices.ToArray(), triangles.ToArray());
        }

        private Mesh CreateRoof()
        {
            Vector3 a = new Vector3(-.5f, -.5f, -.5f);
            Vector3 b = new Vector3(.5f, -.5f, -.5f);
            Vector3 c = new Vector3(0f, .5f, -.5f);
            Vector3 d = new Vector3(-.5f, -.5f, .5f);
            Vector3 e = new Vector3(.5f, -.5f, .5f);
            Vector3 f = new Vector3(0f, .5f, .5f);
            return Make("Shared pitched roof",
                new[] { a,c,b, d,e,f, a,d,f,c, b,c,f,e, a,b,e,d },
                new[] { 0,1,2,3,4,5,6,7,8,6,8,9,10,11,12,10,12,13,14,15,16,14,16,17 });
        }

        private Mesh CreateLeaf()
        {
            return Make("Shared palm frond",
                new[] { Vector3.zero, new Vector3(-.26f,.1f,.45f),
                    new Vector3(0f,-.16f,1f), new Vector3(.26f,.1f,.45f),
                    Vector3.zero, new Vector3(-.26f,.1f,.45f),
                    new Vector3(0f,-.16f,1f), new Vector3(.26f,.1f,.45f) },
                new[] { 0,1,2,0,2,3,6,5,4,7,6,4 });
        }

        private Mesh CreateBrokenConcrete()
        {
            // A shared, angular fragment mesh for collapsed slabs and rubble. Duplicated
            // triangle vertices preserve the hard, chipped facets after mesh batching.
            Vector3[] lower =
            {
                new Vector3(-.46f,-.46f,-.32f), new Vector3(-.31f,-.5f,-.48f),
                new Vector3(.29f,-.47f,-.42f), new Vector3(.48f,-.5f,.08f),
                new Vector3(.22f,-.46f,.45f), new Vector3(-.37f,-.5f,.34f)
            };
            Vector3[] upper =
            {
                new Vector3(-.34f,.18f,-.23f), new Vector3(-.22f,.34f,-.35f),
                new Vector3(.22f,.11f,-.3f), new Vector3(.36f,.31f,.06f),
                new Vector3(.16f,.23f,.31f), new Vector3(-.28f,.08f,.24f)
            };
            var vertices = new List<Vector3>(72);
            var uv = new List<Vector2>(72);
            var triangles = new List<int>(72);
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                AddFragmentTriangle(vertices, uv, triangles, upper[0], upper[next], upper[i]);
                AddFragmentTriangle(vertices, uv, triangles, lower[0], lower[i], lower[next]);
                AddFragmentTriangle(vertices, uv, triangles, lower[i], upper[next], lower[next]);
                AddFragmentTriangle(vertices, uv, triangles, lower[i], upper[i], upper[next]);
            }
            var mesh = new Mesh
            {
                name = "Shared irregular broken concrete shard",
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                uv = uv.ToArray()
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Own(mesh);
        }

        private static void AddFragmentTriangle(List<Vector3> vertices, List<Vector2> uv,
            List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            uv.Add(new Vector2(a.x + .5f, a.z + .5f));
            uv.Add(new Vector2(b.x + .5f, b.z + .5f));
            uv.Add(new Vector2(c.x + .5f, c.z + .5f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public void Dispose()
        {
            foreach (Mesh mesh in owned) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            owned.Clear();
            foreach (Material material in materials.Values) UnityEngine.Object.Destroy(material);
            materials.Clear();
            foreach (Texture2D texture in surfaceTextures.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            surfaceTextures.Clear();
        }
    }

    internal sealed class CityMeshBatch
    {
        private readonly CityGeometry geometry;
        private readonly Dictionary<Material, List<CombineInstance>> batches =
            new Dictionary<Material, List<CombineInstance>>();

        internal CityMeshBatch(CityGeometry geometry) { this.geometry = geometry; }

        internal void Add(Mesh mesh, Material material, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            Add(mesh, material, Matrix4x4.TRS(position, rotation, scale));
        }

        internal void Add(Mesh mesh, Material material, Matrix4x4 transform)
        {
            if (!batches.TryGetValue(material, out List<CombineInstance> instances))
            {
                instances = new List<CombineInstance>();
                batches.Add(material, instances);
            }
            // A Unity importer may split one OBJ into submeshes. Keep every face
            // while applying the library's single baked albedo material.
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                instances.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = submesh,
                    transform = transform
                });
        }

        internal void Box(Material material, Vector3 position, Vector3 scale, float yaw = 0f)
        {
            Add(geometry.Box, material, position, scale, Quaternion.Euler(0f, yaw, 0f));
        }

        internal void Round(Material material, Vector3 position, Vector3 scale)
        {
            Add(geometry.Cylinder, material, position, scale, Quaternion.identity);
        }

        internal void Beam(Material material, Vector3 start, Vector3 end, float width)
        {
            Vector3 direction = end - start;
            if (direction.sqrMagnitude < .0001f) return;
            Add(geometry.Box, material, (start + end) * .5f,
                new Vector3(width, direction.magnitude, width),
                Quaternion.FromToRotation(Vector3.up, direction));
        }

        internal GameObject Build(string name, Transform parent, Vector3 position, bool castShadows = false)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            foreach (var batch in batches)
            {
                var child = new GameObject(batch.Key.name);
                child.transform.SetParent(root.transform, false);
                var mesh = geometry.Own(new Mesh { name = name + " merged", indexFormat = IndexFormat.UInt32 });
                mesh.CombineMeshes(batch.Value.ToArray(), true, true);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = batch.Key;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
            return root;
        }
    }
}