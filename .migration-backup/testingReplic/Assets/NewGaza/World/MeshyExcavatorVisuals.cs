using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>Optional Meshy surface replacement over the retained native joint rig.</summary>
    internal static class MeshyExcavatorVisuals
    {
        internal const string ResourcePath = "Equipment/MeshyExcavator";
        [Serializable] private sealed class Preference { public string model = "original"; }
        private sealed class Piece
        {
            internal string name;
            internal int joint;
            internal int material;
            internal Mesh mesh;
            internal Vector3[] positions;
            internal Vector3[] normals;
            internal Vector2[] uv;
            internal int[] triangles;
        }
        private sealed class Asset { internal Piece[] pieces; }
        // Shared within a city's geometry owner, never across destroyed city meshes.
        private static readonly ConditionalWeakTable<CityGeometry, Asset> cache =
            new ConditionalWeakTable<CityGeometry, Asset>();

        internal static bool IsSelected()
        {
            TextAsset preference = Resources.Load<TextAsset>("Equipment/ExcavatorAppearance");
            if (preference == null) return false; // Existing installs without the new setting.
            Preference selected = JsonUtility.FromJson<Preference>(preference.text);
            if (selected == null || (selected.model != "meshy" && selected.model != "original"))
                throw new InvalidOperationException("ExcavatorAppearance.model must be meshy or original.");
            return selected.model == "meshy";
        }

        internal static void Attach(CityGeometry geometry, Transform[] joints, Transform payload)
        {
            if (joints == null || joints.Length != 5) throw new ArgumentException("Five excavator joints required.");
            Asset asset = cache.GetValue(geometry, Load);
            // Validate/load everything before hiding the preserved original surfaces.
            var materials = new[] {
                geometry.Material("Meshy yellow enamel", new Color(.82f,.49f,.025f), .4f),
                geometry.Material("Meshy track rubber", new Color(.025f,.031f,.038f), .15f),
                geometry.Material("Meshy dark steel", new Color(.11f,.14f,.17f), .4f),
                geometry.Material("Meshy opaque cab", new Color(.045f,.11f,.15f), .72f)
            };
            materials[0].SetFloat("_Metallic", .15f);
            materials[2].SetFloat("_Metallic", .65f);
            foreach (MeshRenderer renderer in joints[0].gameObject.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool loadFragment = false;
                for (Transform ancestor = renderer.transform; ancestor != null; ancestor = ancestor.parent)
                    if (ancestor == payload) { loadFragment = true; break; }
                if (!loadFragment) renderer.enabled = false;
            }
            foreach (Piece piece in asset.pieces)
            {
                var obj = new GameObject("Meshy • " + piece.name + " • " + piece.material);
                obj.transform.SetParent(joints[piece.joint], false);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
                obj.transform.localScale = Vector3.one;
                obj.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
                MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[piece.material];
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Asset Load(CityGeometry geometry)
        {
            TextAsset source = Resources.Load<TextAsset>(ResourcePath);
            if (source == null)
                throw new InvalidOperationException("Meshy excavator is selected but Resources/" +
                    ResourcePath + ".bytes is missing. Install the Meshy art package, or select " +
                    "New Gaza / Equipment / Use original excavator.");
            using (var stream = new MemoryStream(source.bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "NGEXC001")
                    throw new InvalidDataException("Unsupported Meshy excavator asset format.");
                int count = Count(reader, 1, 32);
                var pieces = new Piece[count];
                int totalIndices = 0;
                int totalVertices = 0;
                for (int i = 0; i < count; i++)
                {
                    int length = Count(reader, 1, 128);
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(length));
                    int joint = Count(reader, 0, 4);
                    int material = Count(reader, 0, 3);
                    int vertices = Count(reader, 3, 65535);
                    int indices = Count(reader, 3, 120000);
                    if (indices % 3 != 0) throw new InvalidDataException("Invalid triangle count.");
                    totalIndices += indices;
                    totalVertices += vertices;
                    if (totalVertices > 60000) throw new InvalidDataException("Meshy vertex budget exceeded.");
                    if (totalIndices > 60000) throw new InvalidDataException("Meshy triangle budget exceeded.");
                    var positions = new Vector3[vertices];
                    var normals = new Vector3[vertices];
                    var uv = new Vector2[vertices];
                    for (int v = 0; v < vertices; v++)
                    {
                        positions[v] = new Vector3(Number(reader), Number(reader), Number(reader));
                        normals[v] = new Vector3(Number(reader), Number(reader), Number(reader));
                        uv[v] = new Vector2(Number(reader), Number(reader));
                    }
                    var triangles = new int[indices];
                    for (int t = 0; t < indices; t++) triangles[t] = Count(reader, 0, vertices - 1);
                    pieces[i] = new Piece { name = name, joint = joint, material = material,
                        positions = positions, normals = normals, uv = uv, triangles = triangles };
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected Meshy asset suffix.");
                // Ownership only transfers after complete validation.
                foreach (Piece piece in pieces)
                {
                    piece.mesh = geometry.Own(new Mesh { name = "Meshy excavator • " + piece.name,
                        vertices = piece.positions, normals = piece.normals, uv = piece.uv,
                        triangles = piece.triangles });
                    piece.mesh.RecalculateBounds();
                    piece.positions = piece.normals = null;
                    piece.uv = null;
                    piece.triangles = null;
                }
                return new Asset { pieces = pieces };
            }
        }

        private static int Count(BinaryReader reader, int min, int max)
        {
            int value = reader.ReadInt32();
            if (value < min || value > max) throw new InvalidDataException("Invalid Meshy asset count/index.");
            return value;
        }
        private static float Number(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000f)
                throw new InvalidDataException("Invalid Meshy geometry coordinate.");
            return value;
        }
    }
}
