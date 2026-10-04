using System;
using System.Collections.Generic;
using System.Text.Json;
using NewGaza.Core;

internal static class Program
{
    private static int assertions;
    private const long Now = 200000;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
    private static void Invalid(Action operation, string label)
    {
        bool failed = false;
        try { operation(); } catch (InvalidOperationException) { failed = true; }
        Check(failed, label);
    }
    private static CityDevelopmentService Setup(out EconomyService economy)
    {
        var state = GameCatalog.CreateNew(Now);
        state.development.requiresPlacedFactory = false;
        state.coins = 10000000; state.stock.concrete = 10000; state.stock.iron = 10000;
        economy = new EconomyService(state);
        return new CityDevelopmentService(economy);
    }
    private static string Land(CityBuildingDefinition definition, int district, float x, float z, int turn) => null;
    private static void Main()
    {
        Catalog();
        ScreenInputChecks.Run(Check);
        InterfaceViewportChecks.Run(Check);
        PlacementAndTimers();
        ClearingAndTravel();
        NeedsRewardsAndPersistence();
        MigrationAndCorruption();
        HomeMenuProjection();
        FeedbackEvents();
        EconomyChecks.Run(Check);
        SalvageScenarioChecks.Run(Check);
        Console.WriteLine("PASS free-build development / " + assertions + " assertions");
        Console.WriteLine("Domain only: no Unity editor, physics, UI, shader, or device rendering was run.");
    }

    private static void FeedbackEvents()
    {
        var rules = Setup(out var economy); var state = economy.State;
        var tracker = new CityFeedbackTracker();
        var options = new JsonSerializerOptions { IncludeFields = true };
        string before = JsonSerializer.Serialize(state, options);
        Check(!tracker.Capture(state).Any, "cold save never plays previous achievements");
        Check(JsonSerializer.Serialize(state, options) == before, "feedback never mutates game state");
        Check(!tracker.Capture(state).Any, "idle ticks create no effects");
        long balance = state.coins;
        state.coins -= 10; Check(!tracker.Capture(state).Any, "spending is not a receipt");
        state.coins += 25; var frame = tracker.Capture(state);
        Check(frame.coins == 25 && !frame.reward, "actual positive receipt with exact delta");
        Check(!tracker.Capture(state).Any, "receipt plays once");
        state.lastGiftUtc = Now; state.coins += 100;
        frame = tracker.Capture(state);
        Check(frame.reward && frame.coins == 100, "actual daily gift fade and coins");
        state.districts[0].projects[0].completed = true;
        frame = tracker.Capture(state);
        Check(frame.completed == 1 && !frame.reward, "one true project completion checkmark");
        Check(!tracker.Capture(state).Any, "completion does not repeat");
        state.development.buildings = new[] { new PlacedBuildingState { id = "feedback-building", completed = true } };
        Check(tracker.Capture(state).completed == 1, "completed free building detected");
        state.development.rubble[0].cleared = true;
        Check(tracker.Capture(state).completed == 1, "site clearance detected");
        state.roadSegments = new[] { new RoadSegmentState { id = "feedback-road", level = 0 } };
        Check(!tracker.Capture(state).Any, "unimproved discovered road not an achievement");
        state.roadSegments[0].level = 1;
        Check(tracker.Capture(state).completed == 1, "real road improvement detected");
        int d = Array.FindIndex(state.districts, district => !district.unlocked);
        state.districts[d].unlocked = true; state.districts[0].rewardClaimed = true;
        frame = tracker.Capture(state);
        Check(frame.reward && frame.unlocked.Length == 1 && frame.unlocked[0] == d, "earned district transition and reward");
        Check(!tracker.Capture(state).Any, "earned district appears once");
        Check(!new CityFeedbackTracker().Capture(state).Any, "reopening save does not replay events");
        state.development.buildings = Array.Empty<PlacedBuildingState>();
        Check(!tracker.Capture(state).Any, "removal is not task completion");
        Check(state.coins == balance + 115, "feedback does not grant currency");
    }

    private static void HomeMenuProjection()
    {
        var rules = Setup(out var economy); var state = economy.State;
        int initial = state.selectedDistrict;
        var options = new JsonSerializerOptions { IncludeFields = true };
        var before = JsonSerializer.Serialize(state, options);
        var home = CityHomeSummary.Create(economy);
        Check(JsonSerializer.Serialize(state, options) == before, "home projection must not mutate save, coins, gifts, or access");
        Check(home.District == initial && home.DistrictName == GameCatalog.Districts[initial].name, "first home district");
        Check(home.Coins == state.coins && home.Concrete == state.stock.concrete &&
            home.Iron == state.stock.iron && home.Wood == state.stock.wood, "actual resource strip values");
        Check(home.Progress == economy.Progress(initial) && home.FactoryLevel == state.factoryLevel, "authoritative progress/factory");
        Check(home.TotalSites > 0 && home.TotalSites >= home.ClearedSites, "actual district rubble counts");
        Check(home.GiftReady && home.GiftSeconds == 0, "new player gift really ready");
        Check(home.NeedsText.Split('\n').Length == 3, "compact three-row regional requirements");
        state.districts[1].unlocked = true;
        state.selectedDistrict = 1; state.jobDistrict = initial; state.jobStage = JobStage.Clearing;
        Check(CityHomeSummary.ResumeDistrict(state) == initial, "resume active rubble work rather than camera district");
        state.jobStage = JobStage.Idle;
        state.development.buildings = new[] { new PlacedBuildingState { id = "menu-test", district = initial, startedUtc = Now,
            finishUtc = Now + 30, definitionId = CityBuildingCatalog.All[0].id } };
        Check(CityHomeSummary.ResumeDistrict(state) == initial, "resume unfinished free building");
        home = CityHomeSummary.Create(economy);
        Check(home.BuildingWorks == 1 && home.CompletedBuildings == 0, "live unfinished building count");
        state.development.buildings[0].completed = true;
        Check(CityHomeSummary.ResumeDistrict(state) == 1, "completed building does not redirect away from chosen district");
        state.development.buildings[0].completed = false; state.districts[initial].unlocked = false;
        Check(CityHomeSummary.ResumeDistrict(state) == 1, "never resume inaccessible building");
        state.districts[initial].unlocked = true; state.development.buildings = Array.Empty<PlacedBuildingState>();
        state.selectedDistrict = 1; state.districts[1].rewardClaimed = true;
        Check(CityHomeSummary.ResumeDistrict(state) == initial, "resume first open unrewarded area if selected is completed");
        state.lastGiftUtc = Now;
        home = CityHomeSummary.Create(economy);
        Check(!home.GiftReady && home.GiftSeconds == 86400, "real gift cooldown, no fake readiness");
        state.lastSeenUtc = Now + 86399;
        Check(CityHomeSummary.Create(economy).GiftSeconds == 1, "gift countdown before boundary");
        state.lastSeenUtc++;
        Check(CityHomeSummary.Create(economy).GiftReady, "gift opens at 24 hours");
    }
    private static void Catalog()
    {
        Check(CityBuildingCatalog.All.Length == 42, "all 41 requested types plus warehouse");
        var ids = new HashSet<string>();
        var counts = new int[7];
        foreach (var definition in CityBuildingCatalog.All)
        {
            Check(ids.Add(definition.id), "unique definition");
            Check(definition.widthMeters > 0 && definition.depthMeters > 0 && definition.floors > 0, "dimensions");
            Check(definition.cost > 0 && definition.duration > 0 && definition.hourlyIncome >= 0, "balance values");
            counts[(int)definition.category]++;
        }
        Check(counts[0] == 2 && counts[1] == 1 && counts[2] == 6 && counts[3] == 10 &&
            counts[4] == 10 && counts[5] == 6 && counts[6] == 7, "category coverage");
        Check(CityBuildingCatalog.Find("housing_4").floors == 4 && CityBuildingCatalog.Find("housing_6").floors == 6, "exact floor variants");
        int east = 0, west = 0;
        for (int d = 0; d < GameCatalog.NeighborhoodCount; d++)
        {
            if (GameGeography.DistrictPoint(d).x > GameGeography.DistrictPoint(east).x) east = d;
            if (GameGeography.DistrictPoint(d).x < GameGeography.DistrictPoint(west).x) west = d;
        }
        Check(CityDevelopmentService.Needs(east)[1] > CityDevelopmentService.Needs(west)[1], "east agriculture");
        Check(CityDevelopmentService.Needs(west)[3] > CityDevelopmentService.Needs(east)[3], "west commerce");
        Check(CityDevelopmentService.Needs(west)[5] > CityDevelopmentService.Needs(east)[5], "west urban housing");
    }
    private static void PlacementAndTimers()
    {
        var service = Setup(out var economy); var state = economy.State;
        long coins = state.coins; int concrete = state.stock.concrete;
        Check(!service.Build("bad", 0, 1, 1, 0, Now, Land).success, "unknown type");
        Check(!service.Build("small_house", 1, 1, 1, 0, Now, Land).success, "locked region");
        Check(!service.Build("small_house", 0, float.NaN, 1, 0, Now, Land).success, "nonfinite");
        Check(!service.Build("small_house", 0, 1, 1, 3, Now, Land).success, "unsupported rotation");
        Check(!service.Build("small_house", 0, 1, 1, 0, Now, (a, b, c, d, e) => "rubble").success, "rubble veto");
        Check(!service.Build("small_house", 0, 1, 1, 0, Now, null).success, "missing terrain fails explicitly");
        Check(state.coins == coins && state.stock.concrete == concrete, "failure atomicity");
        Check(service.Build("small_house", 0, 1, 1, 0, Now, Land).success, "clean land placement");
        Check(state.coins == coins - 2500 && state.stock.concrete == concrete, "cash-only deduction preserves resources");
        Check(!service.Build("small_house", 0, 1, 1, 0, Now, Land).success, "construction occupancy");
        Check(service.Build("equipment_store", 0, 3, 4, 1, Now, Land).success, "rotated second building");
        var building = service.Data.buildings[0];
        economy.Tick(building.finishUtc - 1); Check(!building.completed, "not early");
        economy.Tick(building.finishUtc); Check(building.completed, "completion");
        long ready = building.finishUtc + 7200;
        economy.Tick(ready);
        Check(CityDevelopmentService.PendingIncome(building, ready) == CityBuildingCatalog.Find("small_house").hourlyIncome * 2, "hourly income");
        Check(service.Collect(building.id, ready).success, "income collect");
        Check(!service.Collect(building.id, ready).success, "no double collection");
        Check(!service.Build("wheat", 0, 8, 8, 0, Now, Land).success, "rollback prevents spend");
        Check(!service.Collect(building.id, Now).success, "rollback prevents income");
        CityDevelopmentService.Validate(state);
        CityDevelopmentService.ValidateSpatial(state);
        float secondX = service.Data.buildings[1].x, secondZ = service.Data.buildings[1].z;
        service.Data.buildings[1].x = building.x; service.Data.buildings[1].z = building.z;
        Invalid(() => CityDevelopmentService.ValidateSpatial(state), "overlapping save rejected before load");
        service.Data.buildings[1].x = secondX; service.Data.buildings[1].z = secondZ;
    }
    private static void ClearingAndTravel()
    {
        var service = Setup(out var economy); var state = economy.State;
        Check(service.Data.rubble.Length == GameCatalog.Districts.Length * 9, "distributed starter sites");
        string site = CityDevelopmentService.SiteId(0, 0);
        Check(!service.Clear(site, "central", Now).success, "factory required");
        state.factoryLevel = 1; state.excavators = state.trucks = state.bulldozers = 1;
        state.equipmentUnits = null;
        economy.ClearingCrewReady = () => false;
        Check(service.Clear(site, "central", Now).success, "selected ruin dispatch");
        Check(!service.Clear(CityDevelopmentService.SiteId(0, 1), "central", Now).success, "one pooled fleet contract");
        economy.Tick(Now + 10000);
        Check(!service.Site(site).cleared && state.jobStage == JobStage.Clearing && !service.Data.crewArrived, "travel cannot clear remotely");
        Check(state.stock.concrete == 10000, "no resources before arrival");
        economy.ClearingCrewReady = () => true;
        economy.Tick(Now + 10001);
        Check(service.Data.crewArrived && state.jobStage == JobStage.Clearing, "arrival starts full clearing duration");
        long end = state.jobFinishUtc;
        economy.Tick(end - 1); Check(state.jobStage == JobStage.Clearing, "arrival clearing not early");
        economy.Tick(end); Check(state.jobStage == JobStage.Hauling, "clearing then hauling");
        economy.Tick(Now + 20000);
        Check(service.Site(site).cleared && state.jobStage == JobStage.Idle && service.Data.activeRubbleId == null, "chosen site only cleared");
        Check(!service.Site(CityDevelopmentService.SiteId(0, 1)).cleared, "neighbour remains damaged");
        var output = RubbleEconomy.Yield(state, service.Site(site));
        Check(state.stock.concrete == 10000 + output.concrete && state.stock.iron == 10000 + output.iron, "bounded recycled stock once");
        Check(!service.Clear(site, "central", state.lastSeenUtc).success, "no repeated ruin reward");
        Check(!service.Clear(CityDevelopmentService.SiteId(1, 0), "central", state.lastSeenUtc).success, "locked clearance");
        CityDevelopmentService.Validate(state);
        Check(service.Build("recycling", 0, 9, 9, 0, state.lastSeenUtc, Land).success, "additional factory");
        economy.Tick(state.lastSeenUtc + 500);
        var depot = service.Data.buildings[0];
        Check(service.Clear(CityDevelopmentService.SiteId(0, 1), depot.id, state.lastSeenUtc).success, "dynamic depot eligible");
        Check(service.Data.dispatchDepotId == depot.id, "chosen depot persisted");
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { IncludeFields = true });
        var loaded = JsonSerializer.Deserialize<GameState>(json, new JsonSerializerOptions { IncludeFields = true });
        var resumed = new EconomyService(loaded); var resumedService = new CityDevelopmentService(resumed);
        resumed.ClearingCrewReady = () => true; resumed.Tick(loaded.lastSeenUtc + 1);
        long afterArrival = loaded.lastSeenUtc;
        json = JsonSerializer.Serialize(loaded, new JsonSerializerOptions { IncludeFields = true });
        loaded = JsonSerializer.Deserialize<GameState>(json, new JsonSerializerOptions { IncludeFields = true });
        resumed = new EconomyService(loaded); resumedService = new CityDevelopmentService(resumed);
        resumed.ClearingCrewReady = () => false; resumed.Tick(afterArrival + 10000);
        Check(resumedService.Site(CityDevelopmentService.SiteId(0, 1)).cleared, "persisted arrival permits offline completion");
    }
    private static void NeedsRewardsAndPersistence()
    {
        var service = Setup(out var economy); var state = economy.State;
        foreach (var site in service.Data.rubble) if (site.district == 0) site.cleared = true;
        Check(!economy.ClaimDistrictReward(0, Now).success, "needs gate, not rubble alone");
        string[] definitions = { "small_house", "medium_house", "wheat", "olive", "glass", "cement", "food_shop",
            "modern_mall", "clothes_shop", "housing_tower", "modern_housing", "cafe", "zoo" };
        for (int i = 0; i < definitions.Length; i++)
            Check(service.Build(definitions[i], 0, 10 + i * 10, 20, 0, Now, Land).success, "diverse needs construction");
        Check(!CityDevelopmentService.Complete(state, 0), "unfinished buildings don't meet needs");
        economy.Tick(Now + 1000);
        Check(CityDevelopmentService.Complete(state, 0) && economy.Progress(0) == 1, "needs completed");
        long before = state.coins;
        Check(economy.ClaimDistrictReward(0, state.lastSeenUtc).success, "free buildings unlock next region");
        Check(state.districts[1].unlocked && !state.districts[2].unlocked, "sequential unlock only");
        Check(state.coins == before + GameCatalog.Districts[0].completionReward, "one reward");
        Check(!economy.ClaimDistrictReward(0, state.lastSeenUtc).success, "no duplicate district reward");
        Check(!state.districts[0].projects[0].completed, "no fabricated legacy completion timestamps");
        new EconomyService(state);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var loaded = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, options), options);
        new EconomyService(loaded);
        Check(loaded.development.buildings.Length == definitions.Length && loaded.development.buildings[0].x == 10, "placements persist");
        Check(loaded.districts[0].rewardClaimed && loaded.districts[1].unlocked, "reward persists");
    }
    private static void MigrationAndCorruption()
    {
        var state = GameCatalog.CreateNew(Now); state.development = null;
        state.districts[0].clearedLoads = 2; state.coins = 12345; state.lastGiftUtc = 17;
        var economy = new EconomyService(state); float progress = economy.Progress(0);
        var service = new CityDevelopmentService(economy);
        Check(service.Data.legacyProgress && state.coins == 12345 && state.lastGiftUtc == 17, "legacy economy preserved");
        Check(economy.Progress(0) >= progress && state.districts[0].clearedLoads == 2, "legacy progress not reduced");
        Check(service.Data.rubble[0].cleared && !service.Data.rubble[8].cleared, "deterministic legacy clearance");
        int schema = service.Data.schema; service.Data.schema = 99;
        Invalid(() => CityDevelopmentService.Validate(state), "unsupported module rejected"); service.Data.schema = schema;
        var site = service.Data.rubble[9]; site.cleared = true;
        Invalid(() => CityDevelopmentService.Validate(state), "locked site damage rejected"); site.cleared = false;
        string id = service.Data.rubble[1].id; service.Data.rubble[1].id = service.Data.rubble[0].id;
        Invalid(() => CityDevelopmentService.Validate(state), "duplicate sites rejected"); service.Data.rubble[1].id = id;
        CityDevelopmentService.Validate(state);
    }
}