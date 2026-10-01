using System;

namespace NewGaza.Core
{
    /// <summary>Versioned, lossless expansion. No ticking, spending, rewards or timer resets on load.</summary>
    public static class GameStateMigration
    {
        // Frozen shipped v1 positional identity. Never derive this from the current geography/order.
        private static readonly string[] LegacyIds =
        {
            "shujaiya", "tuffah", "sheikh-radwan", "karama", "old-city", "sabra",
            "zeitoun", "rimal", "tel-al-hawa", "sheikh-ijlin", "rashid"
        };
        private static readonly DistrictDefinition[] LegacyCatalog = BuildLegacyCatalog();

        internal static int LegacyIndex(string id) { return Array.IndexOf(LegacyIds, id); }

        public static GameState Upgrade(GameState saved)
        {
            if (saved == null) throw new InvalidOperationException("بيانات الحفظ مفقودة");
            if (saved.version == 2)
            {
                EconomyService.ValidateState(saved, GameCatalog.Districts, 2);
                return saved;
            }
            if (saved.version != 1) throw new InvalidOperationException("إصدار الحفظ غير مدعوم؛ لم يتم تغيير تقدمك");
            EconomyService.ValidateState(saved, LegacyCatalog, 1);

            var upgraded = GameCatalog.CreateNew(saved.lastSeenUtc);
            upgraded.playerName = saved.playerName;
            upgraded.coins = saved.coins;
            upgraded.stock = new ResourceStock
            {
                concrete = saved.stock.concrete, iron = saved.stock.iron,
                wood = saved.stock.wood, other = saved.stock.other
            };
            upgraded.factoryLevel = saved.factoryLevel;
            upgraded.excavators = saved.excavators;
            upgraded.trucks = saved.trucks;
            upgraded.bulldozers = saved.bulldozers;
            upgraded.equipmentLevel = saved.equipmentLevel;
            upgraded.lastGiftUtc = saved.lastGiftUtc;
            upgraded.cityCompletedUtc = saved.cityCompletedUtc;
            upgraded.jobStage = saved.jobStage;
            upgraded.jobFinishUtc = saved.jobFinishUtc;
            for (int oldIndex = 0; oldIndex < LegacyIds.Length; oldIndex++)
            {
                int index = Array.FindIndex(GameCatalog.Districts, district => district.id == LegacyIds[oldIndex]);
                if (index < 0) throw new InvalidOperationException("Missing legacy district in current catalog.");
                var old = saved.districts[oldIndex];
                var district = upgraded.districts[index];
                district.unlocked = old.unlocked;
                district.legacyAccess = old.unlocked;
                district.rewardClaimed = old.rewardClaimed;
                district.clearedLoads = old.clearedLoads;
                for (int p = 0; p < old.projects.Length; p++)
                {
                    var project = old.projects[p];
                    district.projects[p] = new ProjectState
                    {
                        id = project.id, startedUtc = project.startedUtc, finishUtc = project.finishUtc,
                        completed = project.completed, lastIncomeUtc = project.lastIncomeUtc
                    };
                }
                if (oldIndex == saved.selectedDistrict) upgraded.selectedDistrict = index;
                if (oldIndex == saved.jobDistrict) upgraded.jobDistrict = index;
            }
            ReconcileUnlocks(upgraded);
            EconomyService.ValidateState(upgraded, GameCatalog.Districts, 2);
            return upgraded;
        }

        internal static void ReconcileUnlocks(GameState state)
        {
            // Walk through already-claimed legacy districts, not just the immediate next index.
            bool claimedPrefix = true;
            foreach (var district in state.districts)
            {
                if (claimedPrefix) district.unlocked = true;
                claimedPrefix &= district.rewardClaimed;
            }
        }

        private static DistrictDefinition[] BuildLegacyCatalog()
        {
            // Only immutable v1 validation facts; no references to mutable/current project definitions.
            string[] ids = { "water", "power", "housing", "road", "park", "services", "farm", "commerce", "industry" };
            int[] durations = { 3600, 5400, 7200, 64800, 3600, 10800, 300, 3600, 900 };
            string[] prerequisites = { null, null, "water", "water", "road", "power", null, "services", "power" };
            var result = new DistrictDefinition[LegacyIds.Length];
            for (int d = 0; d < result.Length; d++)
            {
                var projects = new ProjectDefinition[ids.Length];
                for (int p = 0; p < projects.Length; p++)
                    projects[p] = new ProjectDefinition
                    {
                        id = ids[p], durationSeconds = d == 10 && ids[p] == "commerce" ? 14400 : durations[p],
                        prerequisite = prerequisites[p],
                        kind = p >= 6 ? ProjectKind.Investment : ProjectKind.Housing
                    };
                result[d] = new DistrictDefinition { id = LegacyIds[d], rubbleLoads = 4 + d, projects = projects };
            }
            return result;
        }
    }
}