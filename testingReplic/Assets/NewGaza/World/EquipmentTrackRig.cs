using System.Collections.Generic;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Closed track shoes and road rollers driven only by measured vehicle travel.</summary>
    internal sealed class EquipmentTrackRig
    {
        private sealed class TrackSide
        {
            internal Mesh shoeMesh;
            internal List<Vector3> shoeVertices;
            internal Transform[] rollers;
            internal float[] rollerRadii;
            internal float[] rollerAngles;
            internal float trackCenterX;
            internal float traveledModelDistance;
        }

        private readonly Transform vehicle;
        private readonly float vehicleScale;
        private readonly float length;
        private readonly float height;
        private readonly float width;
        private readonly TrackSide[] sides = new TrackSide[2];
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private bool hasSample;
        internal float LowestShoeVertexYModel { get; private set; }
        internal float LeftTrackDistanceModel { get { return sides[0].traveledModelDistance; } }
        internal float RightTrackDistanceModel { get { return sides[1].traveledModelDistance; } }
        internal float LeftRollerAngleDegrees { get { return sides[0].rollerAngles[0]; } }
        internal float RightRollerAngleDegrees { get { return sides[1].rollerAngles[0]; } }

        internal EquipmentTrackRig(CityGeometry geometry, Transform vehicle, Mesh beltMesh,
            Material rubber, Material steel, Material dark, float vehicleScale,
            float lateralOffset, float length, float height, float width)
        {
            this.vehicle = vehicle;
            this.vehicleScale = vehicleScale;
            this.length = length;
            this.height = height;
            this.width = width;

            for (int sideIndex = 0; sideIndex < sides.Length; sideIndex++)
            {
                int side = sideIndex == 0 ? -1 : 1;
                Transform assembly = new GameObject(
                    side < 0 ? "Left continuous track assembly" : "Right continuous track assembly").transform;
                assembly.SetParent(vehicle, false);
                assembly.localPosition = new Vector3(side * lateralOffset, 0f, 0f);

                AddMesh(assembly, "Rubber continuous track carcass", beltMesh, rubber);
                var shoeVertices = new List<Vector3>(EquipmentGeometry.TrackShoeCountPerSide * 8);
                Mesh shoeMesh = EquipmentGeometry.TrackShoeLoop(geometry,
                    side < 0 ? "Left circulating steel shoes" : "Right circulating steel shoes",
                    length, height, width, shoeVertices);
                if (sideIndex == 0)
                {
                    LowestShoeVertexYModel = shoeVertices[0].y;
                    for (int vertex = 1; vertex < shoeVertices.Count; vertex++)
                        LowestShoeVertexYModel = Mathf.Min(LowestShoeVertexYModel,
                            shoeVertices[vertex].y);
                }
                AddMesh(assembly, "Circulating steel track shoes", shoeMesh, steel);

                var rollers = new Transform[5];
                var rollerRadii = new float[5];
                var rollerAngles = new float[5];
                float straight = length * .5f - height * .5f;
                for (int rollerIndex = 0; rollerIndex < rollers.Length; rollerIndex++)
                {
                    float z = Mathf.Lerp(-straight, straight,
                        rollerIndex / (float)(rollers.Length - 1));
                    float rollerDiameter = rollerIndex == 0 ||
                        rollerIndex == rollers.Length - 1 ? height : .28f;
                    Transform roller = new GameObject("Rotating road roller " + (rollerIndex + 1)).transform;
                    roller.SetParent(assembly, false);
                    roller.localPosition = new Vector3(side * .025f, .235f, z);
                    rollers[rollerIndex] = roller;
                    rollerRadii[rollerIndex] = rollerDiameter * .5f;

                    AddCylinder(geometry, roller, "Road roller barrel",
                        dark, new Vector3(rollerDiameter, .10f, rollerDiameter), Vector3.zero);
                    AddCylinder(geometry, roller, "Road roller steel hub",
                        steel, new Vector3(rollerDiameter * .48f, .035f,
                            rollerDiameter * .48f),
                        new Vector3(side * .059f, 0f, 0f));
                }

                sides[sideIndex] = new TrackSide
                {
                    shoeMesh = shoeMesh,
                    shoeVertices = shoeVertices,
                    rollers = rollers,
                    rollerRadii = rollerRadii,
                    rollerAngles = rollerAngles,
                    trackCenterX = side * lateralOffset
                };
            }
        }

        internal void Update()
        {
            Vector3 position = vehicle.localPosition;
            Quaternion rotation = vehicle.localRotation;
            if (!hasSample)
            {
                previousPosition = position;
                previousRotation = rotation;
                hasSample = true;
                return;
            }

            Vector3 displacement = position - previousPosition;
            Quaternion averageHeading = Quaternion.Slerp(previousRotation, rotation, .5f);
            float centerTravel = Vector3.Dot(displacement,
                averageHeading * Vector3.forward) / Mathf.Max(.001f, vehicleScale);
            Vector3 previousForward = previousRotation * Vector3.forward;
            Vector3 currentForward = rotation * Vector3.forward;
            float yawRadians = Mathf.Atan2(
                Vector3.Dot(Vector3.Cross(previousForward, currentForward), Vector3.up),
                Vector3.Dot(previousForward, currentForward));
            if (Mathf.Abs(centerTravel) > .00001f || Mathf.Abs(yawRadians) > .00001f)
            {
                for (int side = 0; side < sides.Length; side++)
                {
                    TrackSide track = sides[side];
                    float signedSideTravel = centerTravel +
                        yawRadians * track.trackCenterX;
                    if (Mathf.Abs(signedSideTravel) <= .00001f) continue;
                    track.traveledModelDistance += signedSideTravel;
                    EquipmentGeometry.UpdateTrackShoeLoop(track.shoeMesh, track.shoeVertices,
                        length, height, width, track.traveledModelDistance);
                    for (int roller = 0; roller < track.rollers.Length; roller++)
                    {
                        track.rollerAngles[roller] += signedSideTravel /
                            track.rollerRadii[roller] * Mathf.Rad2Deg;
                        track.rollers[roller].localRotation =
                            Quaternion.AngleAxis(track.rollerAngles[roller], Vector3.right);
                    }
                }
            }

            previousPosition = position;
            previousRotation = rotation;
        }

        private static void AddMesh(Transform parent, string name, Mesh mesh, Material material)
        {
            Transform part = new GameObject(name).transform;
            part.SetParent(parent, false);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Transform AddCylinder(CityGeometry geometry, Transform parent, string name,
            Material material, Vector3 scale, Vector3 position)
        {
            Transform part = new GameObject(name).transform;
            part.SetParent(parent, false);
            part.localPosition = position;
            part.localRotation = Quaternion.Euler(0f, 0f, 90f);
            part.localScale = scale;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = geometry.Cylinder;
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return part;
        }
    }
}