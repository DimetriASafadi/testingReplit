using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Batches imported Resources models while leaving their source meshes owned by Unity.
    /// </summary>
    internal sealed class CityModelLibrary : IDisposable
    {
        private sealed class ImportedModel
        {
            internal readonly Mesh[] meshes;
            internal readonly Matrix4x4[] childTransforms;
            internal readonly Bounds bounds;
            internal readonly Material material;

            internal ImportedModel(Mesh[] meshes, Matrix4x4[] childTransforms, Bounds bounds,
                Material material)
            {
                this.meshes = meshes;
                this.childTransforms = childTransforms;
                this.bounds = bounds;
                this.material = material;
            }
        }

        private readonly Dictionary<string, ImportedModel> models =
            new Dictionary<string, ImportedModel>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private static readonly HashSet<string> housingStageKeys =
            new HashSet<string>(CityHousingProfiles.ModelKeys, StringComparer.Ordinal);

        internal int ImportedModelCount { get { return models.Count; } }

        internal CityModelLibrary()
        {
            // Fail during world initialization rather than silently substituting procedural
            // geometry when a converted model or its albedo has not been installed.
            try
            {
                Load("apartment");
                Load("ruined_building");
                Load("rubble_heap");
                Load("apartment_context");
                Load("ruined_building_context");
                foreach (string key in CityHousingProfiles.ModelKeys)
                    Load(key);
                foreach (string key in CityRuinProfiles.ModelKeys)
                    Load(key);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Adds all imported mesh filters as one normalized model placement and returns its
        /// resulting geometry height. Position is the center of its footprint at ground level.
        /// maxHeight is an absolute world-space cap, not a fraction or multiplier of the parcel.
        /// </summary>
        internal float AddTo(CityMeshBatch batch, string key, Vector3 position,
            Vector3 availableFootprint, float yaw, float maxHeight, bool footprintIsLocal = false)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (!models.TryGetValue(key, out ImportedModel model))
                throw new ArgumentException("Unknown New Gaza imported model: " + key, nameof(key));
            if (availableFootprint.x <= 0f || availableFootprint.z <= 0f || maxHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(availableFootprint),
                    "Imported model footprint and maximum height must be positive.");

            Bounds bounds = model.bounds;
            float radians = yaw * Mathf.Deg2Rad;
            float cosine = Mathf.Abs(Mathf.Cos(radians));
            float sine = Mathf.Abs(Mathf.Sin(radians));
            // OSM oriented parcels specify local dimensions, unlike the axis-aligned
            // scatter envelope. Their yaw must not shrink the model a second time.
            float rotatedWidth = footprintIsLocal ? bounds.size.x :
                cosine * bounds.size.x + sine * bounds.size.z;
            float rotatedDepth = footprintIsLocal ? bounds.size.z :
                sine * bounds.size.x + cosine * bounds.size.z;
            float scale = Mathf.Min(availableFootprint.x / rotatedWidth,
                availableFootprint.z / rotatedDepth, maxHeight / bounds.size.y);
            float height = bounds.size.y * scale;

            Matrix4x4 placement = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f),
                Vector3.one) *
                Matrix4x4.Scale(Vector3.one * scale) *
                Matrix4x4.Translate(new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z));
            for (int i = 0; i < model.meshes.Length; i++)
                batch.Add(model.meshes[i], model.material, placement * model.childTransforms[i]);
            return height;
        }

        private ImportedModel Load(string key)
        {
            if (models.TryGetValue(key, out ImportedModel cached)) return cached;

            string resourcePath = "Models/" + key;
            GameObject source = Resources.Load<GameObject>(resourcePath);
            if (source == null)
                throw new InvalidOperationException("Missing imported New Gaza model resource: Resources/" +
                    resourcePath + ". Add the converted OBJ and its Unity-imported model asset.");
            string textureResourcePath = housingStageKeys.Contains(key)
                ? "Models/house_small_redtile_final"
                : key == "apartment_context" ? "Models/apartment" :
                    key == "ruined_building_context" ? "Models/ruined_building" : resourcePath;
            Texture2D albedo = Resources.Load<Texture2D>(textureResourcePath + "_albedo");
            if (albedo == null)
                throw new InvalidOperationException("Missing imported New Gaza albedo resource: Resources/" +
                    textureResourcePath + "_albedo.png.");
            Material template = Resources.Load<Material>("NewGazaLit");
            if (template == null || template.shader == null)
                throw new InvalidOperationException("New Gaza imported models require Resources/NewGazaLit " +
                    "to retain the URP Lit shader in player builds.");

            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
            var meshes = new List<Mesh>(filters.Length);
            var childTransforms = new List<Matrix4x4>(filters.Length);
            Matrix4x4 rootInverse = source.transform.worldToLocalMatrix;
            bool hasBounds = false;
            Bounds bounds = default(Bounds);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                    throw new InvalidOperationException("Imported New Gaza mesh is not readable: Resources/" +
                        resourcePath + ". In Unity run New Gaza / Prepare imported city models, then build again.");
                Matrix4x4 childTransform = rootInverse * filters[i].transform.localToWorldMatrix;
                meshes.Add(mesh);
                childTransforms.Add(childTransform);
                EncapsulateTransformedBounds(ref bounds, ref hasBounds, mesh.bounds, childTransform);
            }
            if (meshes.Count == 0 || !hasBounds)
                throw new InvalidOperationException("Imported New Gaza model has no mesh geometry: Resources/" +
                    resourcePath + ".");
            if (bounds.size.x <= Mathf.Epsilon || bounds.size.y <= Mathf.Epsilon ||
                bounds.size.z <= Mathf.Epsilon)
                throw new InvalidOperationException("Imported New Gaza model has invalid bounds: Resources/" +
                    resourcePath + ".");

            var material = new Material(template)
            {
                name = "New Gaza • imported " + key + " albedo",
                enableInstancing = true
            };
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", albedo);
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Metallic", 0f);
            ownedMaterials.Add(material);

            var imported = new ImportedModel(meshes.ToArray(), childTransforms.ToArray(), bounds, material);
            models.Add(key, imported);
            return imported;
        }

        private static void EncapsulateTransformedBounds(ref Bounds aggregate, ref bool initialized,
            Bounds source, Matrix4x4 transform)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z);
                point = transform.MultiplyPoint3x4(point);
                if (initialized) aggregate.Encapsulate(point);
                else
                {
                    aggregate = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
            }
        }

        public void Dispose()
        {
            foreach (Material material in ownedMaterials)
                if (material != null) UnityEngine.Object.Destroy(material);
            ownedMaterials.Clear();
            models.Clear();
        }
    }
}