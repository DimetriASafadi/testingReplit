using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>
    /// One collider-free, soft radial cloud mesh for a district. District-local placement means
    /// inland fog follows the existing tile scale and Rashid fog stays in separated plot patches.
    /// </summary>
    public sealed class DistrictFog : MonoBehaviour
    {
        private const float FadeDuration = .55f;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock properties;
        private float opacity;
        private float targetOpacity;

        internal void Initialize(Material sharedMaterial, int districtIndex, bool coastal,
            Vector3[] plotCenters, Quaternion[] plotRotations, Vector3[] plotSizes,
            bool initiallyShown)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            if (coastal)
                BuildCoastalPatches(districtIndex, plotCenters, plotRotations, plotSizes,
                    vertices, uvs, colors, triangles);
            else
                BuildInlandCloud(districtIndex, vertices, uvs, colors, triangles);

            mesh = new Mesh { name = "Localized soft dust / fog • district " + (districtIndex + 1) };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;

            properties = new MaterialPropertyBlock();
            opacity = targetOpacity = initiallyShown ? 1f : 0f;
            meshRenderer.SetPropertyBlock(properties);
            SetOpacity(opacity);
            meshRenderer.enabled = initiallyShown;
        }

        /// <summary>Sets the authoritative visibility target; fading uses unscaled time.</summary>
        public void Show(bool show)
        {
            targetOpacity = show ? 1f : 0f;
            if (show && meshRenderer != null) meshRenderer.enabled = true;
            else if (!show && opacity <= 0f && meshRenderer != null) meshRenderer.enabled = false;
        }

        private void Update()
        {
            if (Mathf.Abs(opacity - targetOpacity) < .001f)
            {
                opacity = targetOpacity;
                if (opacity == 0f && meshRenderer != null) meshRenderer.enabled = false;
                return;
            }
            opacity = Mathf.MoveTowards(opacity, targetOpacity,
                Time.unscaledDeltaTime / FadeDuration);
            SetOpacity(opacity);
            if (opacity == 0f && meshRenderer != null) meshRenderer.enabled = false;
        }

        private void SetOpacity(float value)
        {
            if (meshRenderer == null) return;
            meshRenderer.GetPropertyBlock(properties);
            properties.SetFloat("_FogOpacity", value);
            meshRenderer.SetPropertyBlock(properties);
        }

        private static void BuildInlandCloud(int districtIndex, List<Vector3> vertices,
            List<Vector2> uvs, List<Color> colors, List<int> triangles)
        {
            float variation = (districtIndex % 3 - 1) * .35f;
            AddCard(new Vector3(-3f + variation, 7.4f, -1f), 15.5f, 8.7f, .34f, 0f,
                vertices, uvs, colors, triangles);
            AddCard(new Vector3(3.6f + variation, 8.2f, -1.2f), 14.5f, 8.4f, .30f, 0f,
                vertices, uvs, colors, triangles);
            AddCard(new Vector3(-5f + variation, 9.5f, 2.4f), 10.5f, 6.2f, .25f, 0f,
                vertices, uvs, colors, triangles);
            AddCard(new Vector3(1.4f + variation, 10.3f, 2.5f), 14.2f, 7.1f, .28f, 0f,
                vertices, uvs, colors, triangles);
            AddCard(new Vector3(7f + variation, 11.2f, 1.8f), 8.2f, 5.6f, .22f, 0f,
                vertices, uvs, colors, triangles);
        }

        private static void BuildCoastalPatches(int districtIndex, Vector3[] centers,
            Quaternion[] rotations, Vector3[] sizes, List<Vector3> vertices,
            List<Vector2> uvs, List<Color> colors, List<int> triangles)
        {
            if (centers == null || rotations == null || sizes == null) return;
            int count = Mathf.Min(centers.Length, Mathf.Min(rotations.Length, sizes.Length));
            for (int i = 0; i < count; i++)
            {
                Vector3 center = centers[i];
                Quaternion rotation = rotations[i];
                float width = Mathf.Clamp(sizes[i].x * 2.2f, 13f, 19f);
                float depth = Mathf.Clamp(sizes[i].z * 1.65f, 13f, 19f);
                float variation = ((districtIndex + i) % 3 - 1) * .45f;
                AddCard(center + Vector3.up * 7.6f, width, depth, .32f, rotation.eulerAngles.y,
                    vertices, uvs, colors, triangles);
                AddCard(center + rotation * new Vector3(variation - 2.1f, 9.2f, 1.1f),
                    width * .77f, depth * .74f, .26f, rotation.eulerAngles.y,
                    vertices, uvs, colors, triangles);
                AddCard(center + rotation * new Vector3(2.4f - variation, 10.9f, -1.2f),
                    width * .65f, depth * .68f, .22f, rotation.eulerAngles.y,
                    vertices, uvs, colors, triangles);
            }
        }

        private static void AddCard(Vector3 center, float width, float depth, float density,
            float yaw, List<Vector3> vertices, List<Vector2> uvs, List<Color> colors,
            List<int> triangles)
        {
            int first = vertices.Count;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            vertices.Add(center + rotation * new Vector3(-width * .5f, 0f, -depth * .5f));
            vertices.Add(center + rotation * new Vector3(-width * .5f, 0f, depth * .5f));
            vertices.Add(center + rotation * new Vector3(width * .5f, 0f, depth * .5f));
            vertices.Add(center + rotation * new Vector3(width * .5f, 0f, -depth * .5f));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            Color color = new Color(1f, 1f, 1f, density);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        internal void Dispose()
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (meshFilter != null) meshFilter.sharedMesh = null;
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
            meshFilter = null;
            meshRenderer = null;
            properties = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}