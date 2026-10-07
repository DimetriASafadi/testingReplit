using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>Bounded, world-space soft dust trail driven by measured vehicle travel.</summary>
    internal sealed class EquipmentDustRig
    {
        internal const int ParticleCapacity = 48;
        internal const float ParticleLifetimeSeconds = 4.2f;

        private readonly Transform vehicle;
        private readonly float vehicleScale;
        private readonly float groundVertexYModel;
        private readonly Vector3[] vertices = new Vector3[ParticleCapacity * 4];
        private readonly Color[] colors = new Color[ParticleCapacity * 4];
        private readonly Vector2[] uvs = new Vector2[ParticleCapacity * 4];
        private readonly int[] triangles = new int[ParticleCapacity * 6];
        private readonly Vector3[] centers = new Vector3[ParticleCapacity];
        private readonly Vector3[] velocities = new Vector3[ParticleCapacity];
        private readonly float[] ages = new float[ParticleCapacity];
        private readonly float[] lifetimes = new float[ParticleCapacity];
        private readonly float[] sizes = new float[ParticleCapacity];
        private readonly float[] opacity = new float[ParticleCapacity];
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Transform cloudSpace;
        internal Material DustMaterial { get; private set; }
        internal Texture2D DustTexture { get; private set; }
        private Vector3 previousPosition;
        private Vector3 lastTravelDirection;
        private float emissionDistance;
        private int nextParticle;
        private int seed = 173;
        private bool hasSample;
        private int activeCount;

        internal int ActiveCount { get { return activeCount; } }

        internal EquipmentDustRig(CityGeometry geometry, Transform vehicle, float vehicleScale,
            float groundVertexYModel, string name)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            this.vehicle = vehicle;
            this.vehicleScale = vehicleScale;
            this.groundVertexYModel = groundVertexYModel;

            var dustObject = new GameObject(name + " world-space dust trail");
            dustObject.transform.SetParent(vehicle.parent, false);
            cloudSpace = dustObject.transform;
            mesh = new Mesh { name = name + " pooled 48-cloud billboard mesh" };
            mesh.indexFormat = IndexFormat.UInt16;
            geometry.Own(mesh);
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            for (int particle = 0; particle < ParticleCapacity; particle++)
            {
                int vertex = particle * 4;
                int triangle = particle * 6;
                uvs[vertex] = new Vector2(0f, 0f);
                uvs[vertex + 1] = new Vector2(1f, 0f);
                uvs[vertex + 2] = new Vector2(1f, 1f);
                uvs[vertex + 3] = new Vector2(0f, 1f);
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            Shader shader = Shader.Find("NewGaza/Soft Dust");
            if (shader == null)
                throw new InvalidOperationException(
                    "Required shader 'NewGaza/Soft Dust' was not found; equipment dust cannot render.");
            var material = new Material(shader) { name = name + " dust material" };
            material.SetColor("_Tint", new Color(.72f, .64f, .52f, .42f));
            Texture2D texture = BuildRadialTexture();
            material.SetTexture("_DustTex", texture);
            DustMaterial = material;
            DustTexture = texture;
            renderer = dustObject.AddComponent<MeshRenderer>();
            dustObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        internal void Advance(float dt, bool isPaved)
        {
            Vector3 position = vehicle.position;
            bool movingVisible = vehicle.gameObject.activeInHierarchy && dt > .0001f;
            if (!hasSample)
            {
                previousPosition = position;
                hasSample = true;
            }
            else
            {
                Vector3 displacement = position - previousPosition;
                float distance = displacement.magnitude;
                float speed = dt > .0001f ? distance / dt : 0f;
                if (!movingVisible || isPaved) emissionDistance = 0f;
                if (movingVisible && !isPaved && speed > .012f && distance > .0001f)
                {
                    lastTravelDirection = displacement / distance;
                    emissionDistance += distance;
                    const float spacing = .055f;
                    int emitted = 0;
                    while (emissionDistance >= spacing && emitted < 4)
                    {
                        emissionDistance -= spacing;
                        Emit(position, speed, lastTravelDirection);
                        emitted++;
                    }
                }
                previousPosition = position;
            }

            if (dt <= 0f) return;
            UpdateParticles(dt);
        }

        private void Emit(Vector3 vehiclePosition, float speed, Vector3 travelDirection)
        {
            int slot = nextParticle;
            nextParticle = (nextParticle + 1) % ParticleCapacity;
            bool wasAlive = ages[slot] < lifetimes[slot];
            if (!wasAlive) activeCount++;
            ages[slot] = 0f;
            lifetimes[slot] = ParticleLifetimeSeconds * Range(.80f, 1.18f);
            sizes[slot] = Range(.060f, .115f);
            opacity[slot] = Range(.19f, .34f);
            Vector3 rear = vehiclePosition - travelDirection * Range(.035f, .085f);
            float side = Range(-.050f, .050f);
            Vector3 across = new Vector3(-travelDirection.z, 0f, travelDirection.x);
            float ground = vehiclePosition.y + groundVertexYModel * vehicleScale;
            centers[slot] = new Vector3(rear.x, ground + .012f, rear.z) + across * side;
            velocities[slot] = across * Range(-.012f, .012f) +
                Vector3.up * Range(.002f, .010f) -
                travelDirection * Mathf.Clamp(speed * .012f, .008f, .065f);
        }

        private void UpdateParticles(float dt)
        {
            activeCount = 0;
            for (int particle = 0; particle < ParticleCapacity; particle++)
            {
                if (ages[particle] < lifetimes[particle])
                {
                    ages[particle] += dt;
                    if (ages[particle] >= lifetimes[particle])
                        ages[particle] = lifetimes[particle];
                }
                float life = ages[particle] < lifetimes[particle]
                    ? 1f - ages[particle] / lifetimes[particle] : 0f;
                float alpha = life * life * opacity[particle];
                if (alpha > .001f) activeCount++;
                float spread = 1f + (1f - life) * .9f;
                Vector3 center = centers[particle] + velocities[particle] * ages[particle];
                float size = sizes[particle] * spread;
                int vertex = particle * 4;
                vertices[vertex] = cloudSpace.InverseTransformPoint(
                    new Vector3(center.x - size, center.y, center.z - size));
                vertices[vertex + 1] = cloudSpace.InverseTransformPoint(
                    new Vector3(center.x + size, center.y, center.z - size));
                vertices[vertex + 2] = cloudSpace.InverseTransformPoint(
                    new Vector3(center.x + size, center.y, center.z + size));
                vertices[vertex + 3] = cloudSpace.InverseTransformPoint(
                    new Vector3(center.x - size, center.y, center.z + size));
                Color color = new Color(1f, 1f, 1f, alpha);
                colors[vertex] = colors[vertex + 1] =
                    colors[vertex + 2] = colors[vertex + 3] = color;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            renderer.enabled = activeCount > 0;
        }

        private float Range(float minimum, float maximum)
        {
            unchecked
            {
                seed = seed * 1103515245 + 12345;
                uint bits = (uint)(seed >> 8);
                return minimum + (maximum - minimum) * ((bits & 0x00ffffffu) / 16777215f);
            }
        }

        private static Texture2D BuildRadialTexture()
        {
            const int resolution = 32;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Procedural soft radial dust alpha",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1
            };
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = (x + .5f) / resolution * 2f - 1f;
                    float dy = (y + .5f) / resolution * 2f - 1f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01(1f - radius);
                    float alpha = edge * edge * (3f - 2f * edge);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}