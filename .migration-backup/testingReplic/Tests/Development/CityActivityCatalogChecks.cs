using System;
using System.Linq;
using NewGaza.Core;

internal static class CityActivityCatalogChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 200000;
        var state = GameCatalog.CreateNew(now);
        var rules = new CityDevelopmentService(new EconomyService(state));
        check(CityActivityCatalog.Collect(state, now).Count == 0, "fresh city does not invent work or ready rewards");
        state.jobStage = JobStage.Clearing; state.jobDistrict = 0;
        state.development.activeRubbleId = "work"; state.development.crewArrived = false;
        var events = CityActivityCatalog.Collect(state, now);
        check(events.Count == 1 && events[0].kind == CityActivityKind.Rubble &&
            events[0].status.Contains("الطريق"), "active traveling crew appears with honest status");
        state.development.crewArrived = true;
        check(CityActivityCatalog.Collect(state, now)[0].status.Contains("إزالة"), "arrival updates the work event");
        state.jobStage = JobStage.Idle; state.development.activeRubbleId = null;
        var definition = CityBuildingCatalog.All.First(item => item.hourlyIncome > 0);
        var building = new PlacedBuildingState { id = "test-event", definitionId = definition.id, district = 0,
            x = 40, z = 50, startedUtc = now - 10, finishUtc = now + 100, lastIncomeUtc = now };
        rules.Data.buildings = new[] { building };
        events = CityActivityCatalog.Collect(state, now);
        check(events.Count == 1 && events[0].kind == CityActivityKind.Construction &&
            events[0].hasCoordinates && events[0].x == 40 && events[0].z == 50 &&
            events[0].width > 0 && events[0].finishUtc == now + 100, "construction has true position footprint and deadline");
        building.completed = true; building.lastIncomeUtc = now;
        check(CityActivityCatalog.Collect(state, now).Count == 0, "completed building is not permanently shown as under construction");
        building.lastIncomeUtc = now - 7200;
        events = CityActivityCatalog.Collect(state, now);
        check(events.Count == 1 && events[0].kind == CityActivityKind.Income &&
            events[0].key == "building:test-event", "ready income replaces construction at the same real site");
        long coins = state.coins, incomeTime = building.lastIncomeUtc;
        CityActivityCatalog.Collect(state, now);
        check(state.coins == coins && building.lastIncomeUtc == incomeTime &&
            state.jobStage == JobStage.Idle, "event discovery is read-only and never collects money or launches work");
        building.lastIncomeUtc = now;
        var project = state.districts[0].projects[0];
        project.startedUtc = now - 10; project.finishUtc = now + 300;
        events = CityActivityCatalog.Collect(state, now);
        check(events.Count == 1 && events[0].kind == CityActivityKind.ProjectConstruction &&
            events[0].projectId == project.id && !events[0].hasCoordinates, "legacy construction routes through native plot anchors, not made-up coordinates");
        project.startedUtc = project.finishUtc = 0;
        var withIncome = GameCatalog.Districts[0].projects.Select((item, index) => new { item, index })
            .FirstOrDefault(entry => entry.item.income > 0 && entry.item.incomeSeconds > 0);
        if (withIncome != null)
        {
            var incomeProject = state.districts[0].projects[withIncome.index];
            incomeProject.completed = true;
            incomeProject.lastIncomeUtc = now - withIncome.item.incomeSeconds;
            check(CityActivityCatalog.Collect(state, now).Any(item => item.kind == CityActivityKind.ProjectIncome),
                "real matured project income appears as ready");
        }
        rules.Data.buildings = Array.Empty<PlacedBuildingState>();
        foreach (var item in state.districts[0].projects) { item.completed = false; item.startedUtc = item.finishUtc = 0; }
        check(CityActivityCatalog.Collect(state, now).Count == 0, "removed work sites leave no stale event records");
        check(CityActivityCatalog.Collect(null, now).Count == 0, "pre-ready UI has no events");
    }
}