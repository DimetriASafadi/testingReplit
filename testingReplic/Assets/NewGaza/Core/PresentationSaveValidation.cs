using System;

namespace NewGaza.Core
{
    /// <summary>Optional view/movement snapshots must never introduce invalid transforms.</summary>
    public static class PresentationSaveValidation
    {
        public static void Validate(GameState state)
        {
            var camera = state.camera;
            if (camera != null && (!Coordinate(camera.x) || !Coordinate(camera.z) ||
                !Finite(camera.zoom) || camera.zoom < .6f || camera.zoom > 10000 ||
                !Finite(camera.yaw) || Math.Abs(camera.yaw) > 360))
                throw new InvalidOperationException("Invalid saved camera.");
            var fleet = state.fleet;
            if (fleet == null) return;
            if (fleet.district < 0 || fleet.district >= GameCatalog.Districts.Length ||
                fleet.stage < JobStage.Idle || fleet.stage > JobStage.Recycling)
                throw new InvalidOperationException("Invalid saved fleet.");
            Vehicle(fleet.excavator);
            Vehicle(fleet.truck);
            Vehicle(fleet.bulldozer);
        }

        private static void Vehicle(VehicleSaveState pose)
        {
            if (pose == null || !Coordinate(pose.x) || !Coordinate(pose.y) ||
                !Coordinate(pose.z) || !Finite(pose.yaw) || Math.Abs(pose.yaw) > 360)
                throw new InvalidOperationException("Invalid saved vehicle position.");
        }

        private static bool Coordinate(float value) => Finite(value) && Math.Abs(value) <= 1000000;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}