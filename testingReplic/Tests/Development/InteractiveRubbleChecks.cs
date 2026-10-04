using System;
using System.Text.Json;
using NewGaza.Core;

internal static class InteractiveRubbleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 1800000000;
        var economy = new EconomyService(GameCatalog.CreateNew(now));
        var rules = new CityDevelopmentService(economy);
        var site = new RubbleSiteState { id = "background:source-42", background = true, sourceBuildingId = "source-42",
            district = 0, projectId = "housing", x = 50, z = 60, width = 2, depth = 1, height = .5f,
            yaw = 45, buildingPrice = 7500 };
        int primary = rules.Data.rubble.Length;
        rules.RegisterBackgroundSites(new[] { site });
        rules.RegisterBackgroundSites(new[] { site });
        check(rules.Data.rubble.Length == primary + 1, "background registration is additive and idempotent");
        check(RubblePicking.Hit(site, 50, 10, 60, 0, -1, 0, out var distance) &&
            distance > 9 && distance < 10, "map ray picks actual background rubble without colliders");
        check(!RubblePicking.Hit(site, 100, 10, 100, 0, -1, 0, out _), "rubble hit does not claim distant roads or ground");
        check(!RubblePicking.Hit(site, 50, 10, 60, 0, 1, 0, out _), "ray behind the camera cannot select rubble");
        check(rules.Clear(site.id, null, now).message.Contains("مصنع"), "background site gives missing-factory response");
        check(rules.BuildRotated("recycling", 0, 10, 10, 37.5f, now, (a,b,c,d,e) => null).success,
            "factory accepts non-cardinal, fractional rotation");
        economy.Tick(rules.Data.buildings[0].finishUtc);
        foreach (var kind in new[] { "excavator", "bulldozer", "truck" })
            check(economy.BuyEquipment(kind, economy.State.lastSeenUtc).success, "equip background removal");
        economy.ClearingCrewReady = () => true;
        economy.HaulingCrewReady = () => true;
        long coins = economy.State.coins;
        check(rules.Clear(site.id, rules.Data.buildings[0].id, economy.State.lastSeenUtc).success, "background rubble dispatches normal crew");
        economy.Tick(economy.State.lastSeenUtc);
        economy.Tick(economy.State.lastSeenUtc + RubbleEconomy.TotalSeconds(economy.State, site) + 5);
        check(site.cleared && economy.State.jobStage == JobStage.Idle && economy.State.stock.concrete > 0 &&
            economy.State.coins == coins, "background removal gives stock once, never automatic coins");
        check(!rules.Data.rubble[0].cleared, "background removal does not silently clear canonical project parcels");
        check(!rules.Clear(site.id, rules.Data.buildings[0].id, economy.State.lastSeenUtc).success, "background cannot be farmed twice");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var reloaded = new EconomyService(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(economy.State, options), options));
        var loadedRules = new CityDevelopmentService(reloaded);
        check(loadedRules.Site(site.id).cleared && loadedRules.Site(site.id).sourceBuildingId == "source-42",
            "stable background removal identity survives save reload");
        check(CityDevelopmentService.BuildingYaw(loadedRules.Data.buildings[0]) == 37.5f,
            "exact held-rotation angle survives save reload");
        check(CityDevelopmentService.BuildingYaw(new PlacedBuildingState { quarterTurn = 1 }) == 90,
            "old quarter-turn saves keep their original orientation");
        var definition = CityBuildingCatalog.Find("recycling");
        CityDevelopmentService.Footprint(definition, 45, out float width, out float depth);
        check(Math.Abs(width - depth) < .0001 && width > definition.widthMeters / 20f,
            "diagonal rotated footprint uses a safe projected width and depth");
        check(!rules.BuildRotated("small_house", 0, 80, 80, float.NaN, economy.State.lastSeenUtc, (a,b,c,d,e) => null).success,
            "invalid rotation rejected before charging money");
        check(loadedRules.BuildRotated("small_house", 0, site.x, site.z, 123.25f, reloaded.State.lastSeenUtc,
            (a,b,c,d,e) => null).success, "cleared background supports a freely rotated replacement building");
        check(RubblePicking.Hit(site, 50, 10, 60, 0, -1, 0, out distance) && distance > 10,
            "cleared-site selection collapses to ground, not the old floating building volume");
        float progress = CityDevelopmentService.Progress(reloaded.State, 0);
        loadedRules.RegisterBackgroundSites(new[] { new RubbleSiteState { id = "background:extra", background = true,
            sourceBuildingId = "extra", district = 0, projectId = "housing", x = 90, z = 90, width = 1,
            depth = 1, height = .2f, buildingPrice = 2500 } });
        check(CityDevelopmentService.Progress(reloaded.State, 0) == progress,
            "new optional background sites do not reduce earned neighborhood progress");
    }
}