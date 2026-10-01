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
    public class GameState
    {
        public int version = 2;
        public string playerName = "بنّاء المدينة";
        public long coins = 50000;
        public ResourceStock stock = new ResourceStock();
        public int factoryLevel;
        public int excavators;
        public int trucks;
        public int bulldozers;
        public int equipmentLevel = 1;
        public int selectedDistrict;
        public DistrictState[] districts;
        public JobStage jobStage;
        public int jobDistrict;
        public long jobFinishUtc;
        public long lastSeenUtc;
        public long lastGiftUtc;
        public long cityCompletedUtc;
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