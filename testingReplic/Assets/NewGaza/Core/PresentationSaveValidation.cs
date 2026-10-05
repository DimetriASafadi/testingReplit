using System;

namespace NewGaza.Core
{
    /// <summary>Optional view/movement snapshots must never introduce invalid transforms.</summary>
    public static class PresentationSaveValidation
    {
        public static void Validate(GameState state)
        {
            var camera = state.camera;
            if (camera != null && !CameraValid(camera))
                throw new InvalidOperationException("Invalid saved camera.");
            ValidateFleet(state.fleet);
        }
        public static void ValidateFleet(FleetSaveState fleet)
        {
            if (fleet == null) return;
            if (fleet.district < 0 || fleet.district >= GameCatalog.Districts.Length ||
                fleet.stage < JobStage.Idle || fleet.stage > JobStage.Recycling)
                throw new InvalidOperationException("Invalid saved fleet.");
            Vehicle(fleet.excavator);
            Vehicle(fleet.truck);
            Vehicle(fleet.bulldozer);
        }

        /// <summary>
        /// A checksummed legacy payload may materialize missing optional objects as
        /// zero-valued objects in Unity. Recover only presentation, before the normal
        /// gameplay validation; never use this to make invalid game progress valid.
        /// </summary>
        public static string RecoverForLoad(GameState state)
        {
            if (state == null) return null; // Migration reports the missing game.
            string warning = null;
            if (state.camera != null && !CameraValid(state.camera))
            {
                state.camera = null;
                warning = "تم تحميل تقدمك؛ أُعيد ضبط عرض الكاميرا لأن بيانات العرض القديمة غير صالحة.";
            }
            if (state.fleet != null)
            {
                var fleet = state.fleet;
                if (fleet.district < 0 || fleet.district >= GameCatalog.Districts.Length ||
                    fleet.stage < JobStage.Idle || fleet.stage > JobStage.Recycling ||
                    !VehicleValid(fleet.excavator) || !VehicleValid(fleet.truck) ||
                    !VehicleValid(fleet.bulldozer))
                {
                    state.fleet = null;
                    warning = (warning == null ? "" : warning + "\n") +
                        "أُعيد ضبط مواضع عرض المعدات؛ ملكيتها وترقياتها وعقد العمل محفوظة.";
                }
            }
            return warning;
        }

        private static bool CameraValid(CameraSaveState camera) =>
            Coordinate(camera.x) && Coordinate(camera.z) &&
            Finite(camera.zoom) && camera.zoom >= .6f && camera.zoom <= 10000 &&
            Finite(camera.yaw) && Math.Abs(camera.yaw) <= 360;

        private static bool VehicleValid(VehicleSaveState pose) =>
            pose != null && Coordinate(pose.x) && Coordinate(pose.y) &&
            Coordinate(pose.z) && Finite(pose.yaw) && Math.Abs(pose.yaw) <= 360;

        private static void Vehicle(VehicleSaveState pose)
        {
            if (!VehicleValid(pose))
                throw new InvalidOperationException("Invalid saved vehicle position.");
        }

        private static bool Coordinate(float value) => Finite(value) && Math.Abs(value) <= 1000000;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}