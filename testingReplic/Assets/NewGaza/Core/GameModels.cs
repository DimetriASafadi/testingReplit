using System;

namespace NewGaza.Core
{
    public enum ProjectKind { Housing, Road, Power, Water, Park, Services, Investment, Landmark }
    public enum JobStage { Idle, Clearing, Hauling, Recycling }

    [Serializable]
    public class ProjectDefinition
    {
        public string id;
        public string name;
        public ProjectKind kind;
        public long cost;
        public int durationSeconds;
        public long income;
        public int incomeSeconds;
        public int concreteCost;
        public int ironCost;
        public string prerequisite;
        public string description;
    }

    [Serializable]
    public class DistrictDefinition
    {
        public string id;
        public string name;
        public string rarity;
        public string description;
        public int rubbleLoads;
        public long completionReward;
        public ProjectDefinition[] projects;
    }

    [Serializable]
    public class ProjectState
    {
        public string id;
        public long startedUtc;
        public long finishUtc;
        public bool completed;
        public long lastIncomeUtc;
    }

    [Serializable]
    public class DistrictState
    {
        public string id;
        // Only v1 migration may preserve access across newly inserted, unclaimed districts.
        public bool legacyAccess;
        public bool unlocked;
        public bool rewardClaimed;
        public int clearedLoads;
        public ProjectState[] projects;
    }

    [Serializable]
    public class ResourceStock
    {
        public int concrete;
        public int iron;
        public int wood;
        public int other;
    }

    [Serializable]
    public class RoadSegmentState
    {
        public string id;
        public int level;
    }

    [Serializable]
    public class RoadSegmentDefinition
    {
        public string id;
        public string name;
        public float lengthMeters;

        public RoadSegmentDefinition(string id, string name, float lengthMeters)
        {
            this.id = id;
            this.name = name;
            this.lengthMeters = lengthMeters;
        }
    }

    [Serializable]
    public class RoadImprovementCost
    {
        public long coins;
        public int concrete;
        public int iron;
    }

    [Serializable]
    public class GameState
    {
        public int version = 2;
        public string playerName = "بنّاء المدينة";
        public long coins = 50000;
        public bool millionOpeningBalanceApplied;
        public ResourceStock stock = new ResourceStock();
        public int factoryLevel;
        public int excavators;
        public int trucks;
        public int bulldozers;
        public int equipmentLevel = 1;
        public EquipmentUnitState[] equipmentUnits;
        public int selectedDistrict;
        public DistrictState[] districts;
        public JobStage jobStage;
        public int jobDistrict;
        public long jobFinishUtc;
        public long lastSeenUtc;
        public long lastGiftUtc;
        public long cityCompletedUtc;
        public RoadSegmentState[] roadSegments = new RoadSegmentState[0];
        public CityDevelopmentState development;
        // Additive: saves made before these snapshots remain valid.
        public CameraSaveState camera;
        public FleetSaveState fleet;
    }

    // Optional additive module: older v1/v2 saves keep their IDs, dates and rewards.
    [Serializable]
    public class CityDevelopmentState
    {
        public int schema = 1;
        public bool initialized;
        public bool legacyProgress;
        public bool dynamicFactoryProvided;
        public bool requiresPlacedFactory;
        public RubbleSiteState[] rubble = new RubbleSiteState[0];
        public PlacedBuildingState[] buildings = new PlacedBuildingState[0];
        public string activeRubbleId;
        public string dispatchDepotId;
        public bool crewArrived;
    }

    [Serializable]
    public class EquipmentUnitState
    {
        public string id, kind;
        public long purchasePrice;
        public int level = 1;
        public int legacyPower;
    }

    [Serializable]
    public class RubbleSiteState
    {
        public string id;
        public int district;
        public string projectId;
        public bool cleared;
    }

    [Serializable]
    public class PlacedBuildingState
    {
        public string id;
        public string definitionId;
        public int district;
        public float x, z;
        public int quarterTurn;
        public long startedUtc, finishUtc, lastIncomeUtc;
        public bool completed;
    }

    [Serializable]
    public class CameraSaveState
    {
        public float x, z, zoom, yaw;
    }

    [Serializable]
    public class VehicleSaveState
    {
        public float x, y, z, yaw;
    }

    [Serializable]
    public class FleetSaveState
    {
        public int district;
        public JobStage stage;
        public string rubbleId, depotId;
        public VehicleSaveState excavator, truck, bulldozer;
    }

    public struct ActionResult
    {
        public bool success;
        public string message;
        public ActionResult(bool success, string message)
        {
            this.success = success;
            this.message = message;
        }
        public static ActionResult Ok(string text) { return new ActionResult(true, text); }
        public static ActionResult Fail(string text) { return new ActionResult(false, text); }
    }
}