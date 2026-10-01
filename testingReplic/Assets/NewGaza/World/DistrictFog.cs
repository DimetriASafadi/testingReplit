using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>
    /// One collider-free, soft radial cloud mesh for a district. District-local placement means
    /// inland and Rashid fog follow their respective project plots, not a rectangular district slab.
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

        internal void Initialize(Material sharedMaterial, int districtIndex, Vector3[] plotCenters,
            Quaternion[] plotRotations, Vector3[] plotSizes,
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
            BuildPlotPatches(districtIndex, plotCenters, plotRotations, plotSizes,
                vertices, uvs, colors, triangles);

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

        private static void BuildPlotPatches(int districtIndex, Vector3[] centers,
            Quaternion[] rotations, Vector3[] sizes, List<Vector3> vertices,
            List<Vector2> uvs, List<Color> colors, List<int> triangles)
        {
            if (centers == null || rotations == null || sizes == null) return;
            int count = Mathf.Min(centers.Length, Mathf.Min(rotations.Length, sizes.Length));
            for (int i = 0; i < count; i++)
            {
                Vector3 center = centers[i];
                Quaternion rotation = rotations[i];
                float width = Mathf.Clamp(sizes[i].x * 1.15f, 5.2f, 9.2f);
                float depth = Mathf.Clamp(sizes[i].z * 1.15f, 5.2f, 10.5f);
                float variation = ((districtIndex + i) % 3 - 1) * .3f;
                AddCard(center + Vector3.up * 7.4f, width, depth, .31f, rotation.eulerAngles.y,
                    vertices, uvs, colors, triangles);
                AddCard(center + rotation * new Vector3(variation - .35f, 9.2f, .25f),
                    width * .82f, depth * .82f, .23f, rotation.eulerAngles.y,
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