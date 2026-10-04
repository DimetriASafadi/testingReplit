using System;
using System.Text.Json;
using NewGaza.Core;

internal static class SalvageScenarioChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long start = 1800000000;
        var economy = new EconomyService(GameCatalog.CreateNew(start));
        var rules = new CityDevelopmentService(economy);
        var state = economy.State;
        string siteId = CityDevelopmentService.SiteId(0, 0);
        check(rules.Clear(siteId, null, start).message.Contains("مصنع"), "missing factory is reported first");
        check(rules.Build("recycling", 0, 10, 10, 0, start, (a,b,c,d,e) => null).success, "place recycling factory");
        economy.Tick(rules.Data.buildings[0].finishUtc);
        string depot = rules.Data.buildings[0].id;
        check(rules.Clear(siteId, depot, state.lastSeenUtc).message.Contains("المعدات"), "missing machines prompt after factory");
        foreach (string kind in new[] { "excavator", "bulldozer", "truck" })
            check(economy.BuyEquipment(kind, state.lastSeenUtc).success, "purchase required " + kind);
        bool arrived = false, returned = false;
        economy.ClearingCrewReady = () => arrived;
        economy.HaulingCrewReady = () => returned;
        long coins = state.coins;
        check(rules.Clear(siteId, depot, state.lastSeenUtc).success, "dispatch from recycling factory");
        economy.Tick(state.lastSeenUtc + 1000);
        check(!rules.Data.crewArrived && state.jobStage == JobStage.Clearing && state.stock.concrete == 0, "travel does not count as site work");
        arrived = true;
        economy.Tick(state.lastSeenUtc);
        long finish = state.jobFinishUtc;
        check(finish - state.lastSeenUtc == 60, "starter crew works a full minute after arrival");
        economy.Tick(finish - 1);
        check(state.jobStage == JobStage.Clearing, "no early departure");
        economy.Tick(finish);
        check(state.jobStage == JobStage.Hauling, "all machines enter return phase");
        economy.Tick(finish + 1000);
        check(state.jobStage == JobStage.Hauling && state.stock.concrete == 0 && state.coins == coins, "return gate blocks resources and automatic cash");
        check(!rules.Clear(CityDevelopmentService.SiteId(0, 1), depot, state.lastSeenUtc).success, "cannot redispatch while returning");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var restored = new EconomyService(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, options), options));
        check(restored.State.development.dispatchDepotId == depot &&
            restored.State.development.activeRubbleId == siteId, "return target and source survive save reload");
        returned = true;
        economy.Tick(state.lastSeenUtc + 2);
        check(rules.Site(siteId).cleared && state.jobStage == JobStage.Idle, "return completes exactly the selected site");
        long sale = state.stock.concrete * 20L + state.stock.iron * 60L + state.stock.wood * 35L + state.stock.other * 10L;
        check(sale > 0 && state.coins == coins, "resources credited without automatic money");
        check(economy.SellResources("all", state.lastSeenUtc).success && state.coins == coins + sale, "sale credits exact money");
        check(!economy.SellResources("all", state.lastSeenUtc).success, "sale cannot be repeated");
        check(!rules.Clear(siteId, depot, state.lastSeenUtc).success, "cleared site cannot be rewarded twice");
        check(rules.Build("small_house", 0, 40, 40, 0, state.lastSeenUtc, (a,b,c,d,e) => null).success,
            "house builds with zero resources after selling all");
        check(state.stock.concrete == 0 && state.stock.iron == 0 && state.stock.wood == 0 && state.stock.other == 0,
            "building does not grant or consume salvage resources");
        check(economy.ClaimDailyGift(state.lastSeenUtc).success && state.stock.concrete == 0 && state.stock.iron == 0,
            "daily gift is money only");
    }
}