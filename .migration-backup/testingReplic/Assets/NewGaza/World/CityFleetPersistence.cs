using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityFleet
    {
        internal void CaptureSave(GameState state)
        {
            if (excavator == null || truck == null || bulldozer == null) return;
            state.fleet = new FleetSaveState {
                district = state.jobDistrict, stage = state.jobStage,
                rubbleId = state.development?.activeRubbleId,
                depotId = state.development?.dispatchDepotId,
                excavator = Capture(excavator), truck = Capture(truck),
                bulldozer = Capture(bulldozer)
            };
        }

        internal void RestoreSave(GameState state)
        {
            var saved = state.fleet;
            // Offline completion/new dispatch invalidates the old movement snapshot.
            if (saved == null || saved.district != state.jobDistrict ||
                saved.stage != state.jobStage ||
                saved.rubbleId != state.development?.activeRubbleId ||
                saved.depotId != state.development?.dispatchDepotId) return;
            Restore(excavator, saved.excavator);
            Restore(truck, saved.truck);
            Restore(bulldozer, saved.bulldozer);
            trackedDestinationInitialized = true;
            trackedDestinationClearing = false;
            // Force the next movement update to plan even when the saved state is Idle.
            lastExcavatorRoadTarget = excavator.localPosition + Vector3.up * 10;
            lastBulldozerRoadTarget = bulldozer.localPosition + Vector3.up * 10;
            excavatorOnRoad = bulldozerOnRoad = false;
            excavatorDockedAtWork = bulldozerDockedAtWork = false;
            // Re-plan against the actual current street graph FROM the restored roots.
            // Never pretend arrival or advance a travel animation by offline wall time.
            if (truckOwned) BeginTruckReassignment(stage == JobStage.Clearing);
            transitionClock = 2f;
            haveMotionSample = false;
            hasTruckWorldSpeedSample = false;
            SnapshotMotion();
        }
        internal void RestoreReturnWithoutSnapshot()
        {
            // A missing optional pose is not evidence that a reserved crew is
            // safely home. Conservatively return from its last known work site.
            excavator.localPosition = jobCenter + VehicleOffset(
                new Vector3(0, EquipmentMotion.WorkRootHeightModel, 0));
            bulldozer.localPosition = excavator.localPosition +
                VehicleOffset(new Vector3(1.55f, dozerWorkRootOffsetModel, -.48f));
            truck.localPosition = tripRoute[0];
            lastExcavatorRoadTarget = excavator.localPosition + Vector3.up * 10;
            lastBulldozerRoadTarget = bulldozer.localPosition + Vector3.up * 10;
            excavatorOnRoad = bulldozerOnRoad = false;
            excavatorDockedAtWork = bulldozerDockedAtWork = false;
            BeginTruckReassignment(false);
        }

        private static VehicleSaveState Capture(Transform root)
        {
            var point = root.localPosition;
            var forward = root.localRotation * Vector3.forward;
            return new VehicleSaveState {
                x = point.x, y = point.y, z = point.z,
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
            };
        }

        private static void Restore(Transform root, VehicleSaveState pose)
        {
            root.localPosition = new Vector3(pose.x, pose.y, pose.z);
            root.localRotation = Quaternion.Euler(0, pose.yaw, 0);
        }
    }
}