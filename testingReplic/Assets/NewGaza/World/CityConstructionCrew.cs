using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>Three articulated tradespeople moving around the outside of a construction plot.</summary>
    internal sealed class CityConstructionCrew : IDisposable
    {
        private const int MaterialCount = 5;
        private const int WorkersCount = 3;
        private const int BoxVertexCount = 24;
        private const int SkinBoxesPerWorker = 4;
        private const int VestBoxesPerWorker = 3;
        private const int ClothBoxesPerWorker = 11;
        private const int HelmetBoxesPerWorker = 1;
        private const int ToolBoxesPerWorker = 5;
        private static readonly int[] BoxesPerMaterial = { SkinBoxesPerWorker, VestBoxesPerWorker,
            ClothBoxesPerWorker, HelmetBoxesPerWorker, ToolBoxesPerWorker };

        private readonly Transform root;
        private readonly Mesh mesh;
        private readonly Vector3[] cubeVertices;
        private readonly Vector3[] vertices;
        private readonly Vector3[][] materialVertices;
        private readonly CityConstructionWorkerRig[] workers;
        private readonly float routeWidth;
        private readonly float routeDepth;
        private CityConstructionPhase phase = CityConstructionPhase.Inactive;
        private float elapsed;
        private bool disposed;

        internal CityConstructionCrew(Transform parent, Vector3 footprint, CityGeometry geometry,
            Material skin, Material vest, Material cloth, Material helmet, Material tool)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (skin == null) throw new ArgumentNullException(nameof(skin));
            if (vest == null) throw new ArgumentNullException(nameof(vest));
            if (cloth == null) throw new ArgumentNullException(nameof(cloth));
            if (helmet == null) throw new ArgumentNullException(nameof(helmet));
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            if (!IsFinite(footprint.x) || !IsFinite(footprint.z) ||
                footprint.x <= 0f || footprint.z <= 0f)
                throw new ArgumentOutOfRangeException(nameof(footprint),
                    "Construction crew footprint dimensions must be finite and positive.");

            routeWidth = Mathf.Max(.8f, footprint.x + .98f);
            routeDepth = Mathf.Max(.8f, footprint.z + .98f);
            cubeVertices = geometry.Box.vertices;
            if (cubeVertices == null || cubeVertices.Length != BoxVertexCount)
                throw new InvalidOperationException("Construction crew requires CityGeometry's 24-vertex hard-edged box.");
            int[] cubeTriangles = geometry.Box.triangles;
            if (cubeTriangles == null || cubeTriangles.Length != 36)
                throw new InvalidOperationException("Construction crew requires CityGeometry's 36-triangle hard-edged box.");

            root = new GameObject("City construction crew").transform;
            root.SetParent(parent, false);
            root.localPosition = Vector3.zero;
            root.gameObject.SetActive(false);

            int totalVertices = 0;
            for (int material = 0; material < MaterialCount; material++)
                totalVertices += BoxesPerMaterial[material] * WorkersCount * BoxVertexCount;
            vertices = new Vector3[totalVertices];
            materialVertices = new Vector3[MaterialCount][];
            for (int material = 0; material < MaterialCount; material++)
            {
                int count = BoxesPerMaterial[material] * WorkersCount * BoxVertexCount;
                materialVertices[material] = new Vector3[count];
            }

            mesh = new Mesh
            {
                name = "Three articulated construction workers",
                indexFormat = IndexFormat.UInt16,
                subMeshCount = MaterialCount
            };
            mesh.vertices = vertices;
            int materialVertexOffset = 0;
            for (int material = 0; material < MaterialCount; material++)
            {
                int boxes = BoxesPerMaterial[material] * WorkersCount;
                var triangles = new int[boxes * cubeTriangles.Length];
                int triangleOffset = 0;
                for (int box = 0; box < boxes; box++)
                    for (int index = 0; index < cubeTriangles.Length; index++)
                        triangles[triangleOffset++] = materialVertexOffset +
                            box * BoxVertexCount + cubeTriangles[index];
                mesh.SetTriangles(triangles, material, false);
                materialVertexOffset += boxes * BoxVertexCount;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject renderObject = new GameObject("Articulated crew mesh");
            renderObject.transform.SetParent(root, false);
            renderObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = renderObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { skin, vest, cloth, helmet, tool };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            workers = new CityConstructionWorkerRig[WorkersCount];
            for (int worker = 0; worker < WorkersCount; worker++)
                workers[worker] = new CityConstructionWorkerRig(worker);
        }

        internal void SetPhase(CityConstructionPhase nextPhase, bool visible)
        {
            if (disposed) return;
            if ((int)nextPhase < (int)CityConstructionPhase.Inactive ||
                (int)nextPhase > (int)CityConstructionPhase.Complete)
                throw new ArgumentOutOfRangeException(nameof(nextPhase));
            bool phaseChanged = phase != nextPhase;
            if (phaseChanged)
            {
                phase = nextPhase;
                elapsed = 0f;
            }

            bool active = visible && (int)nextPhase >= (int)CityConstructionPhase.Foundation &&
                (int)nextPhase <= (int)CityConstructionPhase.Finishing;
            if (root.gameObject.activeSelf != active)
            {
                root.gameObject.SetActive(active);
            }
            if (active) WritePose();
        }

        internal void Update(float deltaTime)
        {
            if (disposed || !root.gameObject.activeInHierarchy ||
                deltaTime <= 0f || !IsFinite(deltaTime))
                return;
            elapsed = (float)(((double)elapsed + deltaTime) % 1000000d);
            WritePose();
        }

        private void WritePose()
        {
            float perimeter = 2f * (routeWidth + routeDepth);
            for (int worker = 0; worker < WorkersCount; worker++)
            {
                float distance = Mathf.Repeat(elapsed * .31f + perimeter * worker / WorkersCount,
                    perimeter);
                Vector3 position;
                Vector3 direction;
                if (distance < routeWidth)
                {
                    position = new Vector3(-routeWidth * .5f + distance, 0f, -routeDepth * .5f);
                    direction = Vector3.right;
                }
                else if ((distance -= routeWidth) < routeDepth)
                {
                    position = new Vector3(routeWidth * .5f, 0f, -routeDepth * .5f + distance);
                    direction = Vector3.forward;
                }
                else if ((distance -= routeDepth) < routeWidth)
                {
                    position = new Vector3(routeWidth * .5f - distance, 0f, routeDepth * .5f);
                    direction = Vector3.left;
                }
                else
                {
                    distance -= routeWidth;
                    position = new Vector3(-routeWidth * .5f, 0f, routeDepth * .5f - distance);
                    direction = Vector3.back;
                }
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                workers[worker].Write(materialVertices, cubeVertices, worker,
                    position, yaw, elapsed, phase);
            }

            int vertexOffset = 0;
            for (int material = 0; material < MaterialCount; material++)
            {
                Array.Copy(materialVertices[material], 0, vertices, vertexOffset,
                    materialVertices[material].Length);
                vertexOffset += materialVertices[material].Length;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }
    }
}