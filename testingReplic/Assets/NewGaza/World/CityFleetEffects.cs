using System;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityFleet
    {
        private Func<Vector3, bool> travelSurfaceIsPaved;
        private EquipmentDustRig[] dustRigs;
        private Transform[] truckSteeringPivots;
        private Vector3 previousTruckSteeringPosition;
        private Quaternion previousTruckSteeringHeading;
        private bool hasTruckSteeringSample;
        private float truckSteeringAngle;

        internal float TruckSteeringAngleDegrees { get { return truckSteeringAngle; } }
        internal Transform[] TruckSteeringPivots { get { return truckSteeringPivots; } }

        /// <summary>Supplies the actual surface classifier used to suppress paved-road dust.</summary>
        public void ConfigureTravelSurface(Func<Vector3, bool> isPaved)
        {
            if (isPaved == null) throw new ArgumentNullException(nameof(isPaved));
            travelSurfaceIsPaved = isPaved;
        }

        /// <summary>Current number of living pooled dust clouds across all fleet machines.</summary>
        public int ActiveDustParticleCount
        {
            get
            {
                if (dustRigs == null) return 0;
                int count = 0;
                for (int i = 0; i < dustRigs.Length; i++)
                    if (dustRigs[i] != null) count += dustRigs[i].ActiveCount;
                return count;
            }
        }

        /// <summary>Number of living pooled clouds for excavator, truck, or dozer (0–2).</summary>
        public int ActiveDustParticleCountForMachine(int machineIndex)
        {
            if (machineIndex < 0 || machineIndex > 2)
                throw new ArgumentOutOfRangeException(nameof(machineIndex));
            return dustRigs == null || dustRigs[machineIndex] == null
                ? 0 : dustRigs[machineIndex].ActiveCount;
        }

        private void UpdateCityFleetEffects(float dt)
        {
            if (dustRigs == null)
            {
                dustRigs = new[]
                {
                    new EquipmentDustRig(geometry, excavator, .07f,
                        excavatorTracks.LowestShoeVertexYModel, "Excavator"),
                    new EquipmentDustRig(geometry, truck, .07f, truckWheelBottomYModel, "Tipper truck"),
                    new EquipmentDustRig(geometry, bulldozer, .07f,
                        bulldozerTracks.LowestShoeVertexYModel, "Bulldozer")
                };
                for (int i = 0; i < dustRigs.Length; i++)
                {
                    ownedMaterials.Add(dustRigs[i].DustMaterial);
                    ownedTextures.Add(dustRigs[i].DustTexture);
                }
            }

            AdvanceDustRig(0, excavator, excavatorOwned, dt);
            AdvanceDustRig(1, truck, truckOwned, dt);
            AdvanceDustRig(2, bulldozer, bulldozerOwned, dt);
        }

        private void AdvanceDustRig(int index, Transform vehicle, bool owned, float dt)
        {
            bool visible = owned && vehicle != null && vehicle.gameObject.activeInHierarchy;
            bool paved = visible && travelSurfaceIsPaved != null &&
                travelSurfaceIsPaved(vehicle.position);
            dustRigs[index].Advance(dt, paved || !visible);
        }

        private void UpdateTruckSteering(float dt)
        {
            if (truckSteeringPivots == null)
                CreateTruckSteeringPivots();
            if (truckSteeringPivots == null) return;

            Vector3 position = truck.localPosition;
            Quaternion heading = truck.localRotation;
            if (!hasTruckSteeringSample)
            {
                previousTruckSteeringPosition = position;
                previousTruckSteeringHeading = heading;
                hasTruckSteeringSample = true;
                return;
            }

            Vector3 displacement = position - previousTruckSteeringPosition;
            Vector3 previousForward = previousTruckSteeringHeading * Vector3.forward;
            Vector3 currentForward = heading * Vector3.forward;
            float yawRadians = Mathf.Atan2(
                Vector3.Dot(Vector3.Cross(previousForward, currentForward), Vector3.up),
                Vector3.Dot(previousForward, currentForward));
            float planarDistanceModel = new Vector2(displacement.x, displacement.z).magnitude /
                Mathf.Max(.001f, VehicleScale);
            float targetAngle = 0f;
            if (truckOwned && truck.gameObject.activeInHierarchy && truckTravelAdvancedThisFrame &&
                dt > .0001f && planarDistanceModel > .0001f)
            {
                const float wheelbaseModel = 2.215f;
                targetAngle = Mathf.Atan2(wheelbaseModel * yawRadians,
                    planarDistanceModel) * Mathf.Rad2Deg;
                targetAngle = Mathf.Clamp(targetAngle, -28f, 28f);
            }

            if (dt > .0001f)
                truckSteeringAngle = Mathf.Lerp(truckSteeringAngle, targetAngle,
                    Mathf.Clamp01(dt * 8f));
            for (int wheel = 0; wheel < truckSteeringPivots.Length; wheel++)
                truckSteeringPivots[wheel].localRotation =
                    Quaternion.Euler(0f, truckSteeringAngle, 0f);

            previousTruckSteeringPosition = position;
            previousTruckSteeringHeading = heading;
        }

        private void CreateTruckSteeringPivots()
        {
            if (truck == null || wheels == null || wheels.Length < 2) return;
            truckSteeringPivots = new Transform[2];
            for (int side = 0; side < truckSteeringPivots.Length; side++)
            {
                Transform wheel = wheels[side];
                Vector3 position = wheel.localPosition;
                Quaternion rotation = wheel.localRotation;
                Vector3 scale = wheel.localScale;
                Transform pivot = new GameObject(side == 0
                    ? "Left front steering knuckle" : "Right front steering knuckle").transform;
                pivot.SetParent(truck, false);
                pivot.localPosition = position;
                pivot.localRotation = Quaternion.identity;
                pivot.localScale = Vector3.one;
                wheel.SetParent(pivot, false);
                wheel.localPosition = Vector3.zero;
                wheel.localRotation = rotation;
                wheel.localScale = scale;
                truckSteeringPivots[side] = pivot;
            }
        }
    }
}