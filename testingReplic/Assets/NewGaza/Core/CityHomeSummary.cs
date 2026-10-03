using System;
using System.Text;

namespace NewGaza.Core
{
    /// <summary>Read-only main-menu projection. Never creates rewards or save changes.</summary>
    public sealed class CityHomeSummary
    {
        public int District { get; private set; }
        public string DistrictName { get; private set; }
        public string PlayerName { get; private set; }
        public long Coins { get; private set; }
        public int Concrete { get; private set; }
        public int Iron { get; private set; }
        public int Wood { get; private set; }
        public int Other { get; private set; }
        public float Progress { get; private set; }
        public int ClearedSites { get; private set; }
        public int TotalSites { get; private set; }
        public int CompletedBuildings { get; private set; }
        public int BuildingWorks { get; private set; }
        public int RemainingBuildings => BuildingWorks;
        public bool GiftReady { get; private set; }
        public long GiftSeconds { get; private set; }
        public string NeedsText { get; private set; }
        public int FactoryLevel { get; private set; }
        public int Excavators { get; private set; }
        public int Trucks { get; private set; }
        public int Bulldozers { get; private set; }

        public static int ResumeDistrict(GameState state)
        {
            if (state.jobStage != JobStage.Idle && Accessible(state, state.jobDistrict)) return state.jobDistrict;
            long latest = -1; int pending = -1;
            if (state.development != null && state.development.initialized)
                foreach (var building in state.development.buildings)
                    if (!building.completed && Accessible(state, building.district) && building.startedUtc > latest)
                    { latest = building.startedUtc; pending = building.district; }
            for (int d = 0; d < state.districts.Length; d++)
                if (Accessible(state, d))
                    foreach (var project in state.districts[d].projects)
                        if (!project.completed && project.finishUtc > 0 && project.startedUtc > latest)
                        { latest = project.startedUtc; pending = d; }
            if (pending >= 0) return pending;
            if (Accessible(state, state.selectedDistrict) && !state.districts[state.selectedDistrict].rewardClaimed)
                return state.selectedDistrict;
            for (int d = 0; d < state.districts.Length; d++)
                if (Accessible(state, d) && !state.districts[d].rewardClaimed) return d;
            return Accessible(state, state.selectedDistrict) ? state.selectedDistrict : 0;
        }

        private static bool Accessible(GameState state, int district) =>
            district >= 0 && district < state.districts.Length && state.districts[district].unlocked;

        public static CityHomeSummary Create(EconomyService economy)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            var state = economy.State;
            int d = ResumeDistrict(state);
            var summary = new CityHomeSummary {
                District = d, DistrictName = GameCatalog.Districts[d].name, PlayerName = state.playerName,
                Coins = state.coins, Concrete = state.stock.concrete, Iron = state.stock.iron,
                Wood = state.stock.wood, Other = state.stock.other, Progress = economy.Progress(d),
                FactoryLevel = state.factoryLevel, Excavators = state.excavators, Trucks = state.trucks,
                Bulldozers = state.bulldozers, GiftReady = economy.CanClaimDailyGift(state.lastSeenUtc)
            };
            summary.GiftSeconds = summary.GiftReady ? 0 : Math.Max(0, 86400 - (state.lastSeenUtc - state.lastGiftUtc));
            foreach (var project in state.districts[d].projects)
            {
                if (project.completed) summary.CompletedBuildings++;
                else if (project.finishUtc > 0) summary.BuildingWorks++;
            }
            if (state.development != null && state.development.initialized)
            {
                foreach (var site in state.development.rubble)
                    if (site.district == d) { summary.TotalSites++; if (site.cleared) summary.ClearedSites++; }
                foreach (var building in state.development.buildings)
                    if (building.district == d)
                    { if (building.completed) summary.CompletedBuildings++; else summary.BuildingWorks++; }
                string[] labels = { "سكن", "زراعة", "صناعة", "تجارة", "رفاهية", "عمران" };
                int[] needs = CityDevelopmentService.Needs(d), supplied = CityDevelopmentService.Supplied(state, d);
                var text = new StringBuilder();
                for (int n = 0; n < needs.Length; n++)
                {
                    if (n > 0) text.Append(n % 2 == 0 ? "\n" : "  ·  ");
                    text.Append(labels[n]).Append(" ").Append(Math.Min(needs[n], supplied[n])).Append("/").Append(needs[n]);
                }
                summary.NeedsText = text.ToString();
            }
            else
            {
                summary.TotalSites = GameCatalog.Districts[d].rubbleLoads;
                summary.ClearedSites = Math.Min(summary.TotalSites, state.districts[d].clearedLoads);
                summary.NeedsText = "المشاريع المتبقية: " +
                    (state.districts[d].projects.Length - summary.CompletedBuildings);
            }
            return summary;
        }
    }
}