using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    public enum CityActivityKind { Rubble, Construction, Income, ProjectConstruction, ProjectIncome, DistrictReward }
    public sealed class CityActivityItem
    {
        public string key, title, status, buildingId, projectId, siteId;
        public CityActivityKind kind;
        public int district;
        public long finishUtc;
        public bool hasCoordinates;
        public float x, z, width = .4f, depth = .4f;
    }

    // Read-only projection of current gameplay, not a second source of progress or invented events.
    public static class CityActivityCatalog
    {
        public static List<CityActivityItem> Collect(GameState state, long now)
        {
            var items = new List<CityActivityItem>();
            if (state == null) return items;
            foreach (var job in RubbleDispatches.Jobs(state))
            {
                var site = Array.Find(state.development.rubble, s => s.id == job.siteId);
                items.Add(new CityActivityItem {
                    key = "rubble:" + job.id, kind = CityActivityKind.Rubble, siteId = job.siteId,
                    district = site.district, title = "فريق إزالة الدمار " + (job.slot + 1),
                    status = RubbleDispatches.Status(job), finishUtc = job.crewArrived ? job.finishUtc : 0
                });
            }
            if (state.jobStage != JobStage.Idle)
                items.Add(new CityActivityItem { key = "active-rubble", kind = CityActivityKind.Rubble,
                    district = state.jobDistrict, title = "مهمة إزالة الدمار",
                    status = ActiveWorkPresentation.Status(state, now), finishUtc = state.jobFinishUtc });
            if (state.development != null)
                foreach (var building in state.development.buildings)
                {
                    if (building == null || !state.districts[building.district].unlocked) continue;
                    var definition = CityBuildingCatalog.Find(building.definitionId);
                    if (!building.completed)
                        items.Add(BuildingItem(building, definition, CityActivityKind.Construction,
                            "البناء جارٍ", building.finishUtc));
                    else
                    {
                        long income = CityDevelopmentService.PendingIncome(building, now);
                        if (income > 0) items.Add(BuildingItem(building, definition, CityActivityKind.Income,
                            "دخل جاهز للجمع: " + income + " عملة", 0));
                    }
                }
            for (int d = 0; d < state.districts.Length; d++)
            {
                var district = state.districts[d];
                if (!district.unlocked) continue;
                for (int p = 0; p < district.projects.Length; p++)
                {
                    var project = district.projects[p];
                    var definition = GameCatalog.Districts[d].projects[p];
                    CityActivityKind kind; string status; long finish = 0;
                    if (!project.completed && project.startedUtc > 0)
                    { kind = CityActivityKind.ProjectConstruction; status = "المشروع قيد التنفيذ"; finish = project.finishUtc; }
                    else if (project.completed && project.finishUtc <= now && definition.income > 0 &&
                        definition.incomeSeconds > 0 && project.lastIncomeUtc > 0 &&
                        now - project.lastIncomeUtc >= definition.incomeSeconds)
                    { kind = CityActivityKind.ProjectIncome; status = "دخل المشروع جاهز للجمع"; }
                    else continue;
                    items.Add(new CityActivityItem { key = "project:" + district.id + ":" + project.id,
                        kind = kind, title = definition.name, status = status, district = d,
                        projectId = project.id, finishUtc = finish });
                }
                if (!district.rewardClaimed && state.development != null && state.development.initialized &&
                    CityDevelopmentService.Complete(state, d))
                    items.Add(new CityActivityItem { key = "reward:" + district.id,
                        kind = CityActivityKind.DistrictReward, district = d,
                        title = "مكافأة حي " + GameCatalog.Districts[d].name, status = "الحي مكتمل — المكافأة جاهزة للاستلام" });
            }
            return items;
        }
        private static CityActivityItem BuildingItem(PlacedBuildingState building, CityBuildingDefinition definition,
            CityActivityKind kind, string status, long finish)
        {
            CityDevelopmentService.Footprint(definition, CityDevelopmentService.BuildingYaw(building), out float width, out float depth);
            return new CityActivityItem { key = "building:" + building.id, kind = kind,
                buildingId = building.id, district = building.district, title = definition.name,
                status = status, finishUtc = finish, hasCoordinates = true,
                x = building.x, z = building.z, width = width, depth = depth };
        }
    }
}