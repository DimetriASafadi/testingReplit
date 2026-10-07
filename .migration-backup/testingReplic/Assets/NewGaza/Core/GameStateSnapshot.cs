using System;

namespace NewGaza.Core
{
    // Copy on the gameplay thread; the writer never observes later live-state mutations.
    public static class GameStateSnapshot
    {
        public static GameState Capture(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var copy = state.CopyRecord();
            copy.stock = state.stock?.CopyRecord();
            copy.camera = state.camera?.CopyRecord();
            copy.roadSegments = ArrayCopy(state.roadSegments, item => item?.CopyRecord());
            copy.equipmentUnits = ArrayCopy(state.equipmentUnits, item => item?.CopyRecord());
            copy.districts = ArrayCopy(state.districts, item =>
            {
                if (item == null) return null;
                var district = item.CopyRecord();
                district.projects = ArrayCopy(item.projects, project => project?.CopyRecord());
                return district;
            });
            if (state.development != null)
            {
                copy.development = state.development.CopyRecord();
                copy.development.rubble = ArrayCopy(state.development.rubble, item => item?.CopyRecord());
                copy.development.buildings = ArrayCopy(state.development.buildings, item => item?.CopyRecord());
                copy.development.dispatches = ArrayCopy(state.development.dispatches, item =>
                {
                    if (item == null) return null;
                    var job = item.CopyRecord();
                    job.fleet = FleetCopy(item.fleet);
                    return job;
                });
            }
            if (state.fleet != null)
            {
                copy.fleet = state.fleet.CopyRecord();
                copy.fleet.excavator = state.fleet.excavator?.CopyRecord();
                copy.fleet.truck = state.fleet.truck?.CopyRecord();
                copy.fleet.bulldozer = state.fleet.bulldozer?.CopyRecord();
            }
            return copy;
        }

        private static T[] ArrayCopy<T>(T[] source, Func<T, T> copy)
        {
            if (source == null) return null;
            var result = new T[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = copy(source[i]);
            return result;
        }
        private static FleetSaveState FleetCopy(FleetSaveState fleet)
        {
            if (fleet == null) return null;
            var copy = fleet.CopyRecord();
            copy.excavator = fleet.excavator?.CopyRecord();
            copy.truck = fleet.truck?.CopyRecord();
            copy.bulldozer = fleet.bulldozer?.CopyRecord();
            return copy;
        }
    }
    public partial class RubbleDispatchState { internal RubbleDispatchState CopyRecord() => (RubbleDispatchState)MemberwiseClone(); }
    public partial class GameState { internal GameState CopyRecord() => (GameState)MemberwiseClone(); }
    public partial class ResourceStock { internal ResourceStock CopyRecord() => (ResourceStock)MemberwiseClone(); }
    public partial class DistrictState { internal DistrictState CopyRecord() => (DistrictState)MemberwiseClone(); }
    public partial class ProjectState { internal ProjectState CopyRecord() => (ProjectState)MemberwiseClone(); }
    public partial class RoadSegmentState { internal RoadSegmentState CopyRecord() => (RoadSegmentState)MemberwiseClone(); }
    public partial class EquipmentUnitState { internal EquipmentUnitState CopyRecord() => (EquipmentUnitState)MemberwiseClone(); }
    public partial class CityDevelopmentState { internal CityDevelopmentState CopyRecord() => (CityDevelopmentState)MemberwiseClone(); }
    public partial class RubbleSiteState { internal RubbleSiteState CopyRecord() => (RubbleSiteState)MemberwiseClone(); }
    public partial class PlacedBuildingState { internal PlacedBuildingState CopyRecord() => (PlacedBuildingState)MemberwiseClone(); }
    public partial class CameraSaveState { internal CameraSaveState CopyRecord() => (CameraSaveState)MemberwiseClone(); }
    public partial class FleetSaveState { internal FleetSaveState CopyRecord() => (FleetSaveState)MemberwiseClone(); }
    public partial class VehicleSaveState { internal VehicleSaveState CopyRecord() => (VehicleSaveState)MemberwiseClone(); }
}