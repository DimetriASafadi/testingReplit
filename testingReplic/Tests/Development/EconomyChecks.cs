using System;
using System.Text.Json;
using NewGaza.Core;

internal static class EconomyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 1800000000;
        var state = GameCatalog.CreateNew(now);
        var economy = new EconomyService(state); var rules = new CityDevelopmentService(economy);
        check(state.factoryLevel == 0 && state.equipmentUnits.Length == 0, "fresh opening has no automatic factory or equipment");
        check(!economy.BuyEquipment("factory", now).success, "central instant factory not sold to fresh players");
        check(!economy.BuyEquipment("excavator", now).success, "opening first requires placed recycler");
        check(state.coins == 1000000, "rejected opening actions leave million starting balance intact");
        check(!rules.Build("recycling", 0, 10, 10, 0, now, (d, region, x, z, turn) => "ركام").success, "factory cannot bypass land validator");
        check(rules.Build("recycling", 0, 10, 10, 0, now, (d, region, x, z, turn) => null).success, "factory may be placed on clear land");
        check(state.coins == 985000 && state.factoryLevel == 0, "construction not instant completion");
        foreach (string kind in new[] { "excavator", "truck", "bulldozer" })
            check(economy.BuyEquipment(kind, now).success, "buy required equipment after placing factory: " + kind);
        check(state.coins == 966000 && state.equipmentUnits.Length == 3, "million opening less actual purchases, three owned machines");
        string siteId = CityDevelopmentService.SiteId(0, 0);
        check(!rules.Clear(siteId, rules.Data.buildings[0].id, now).success, "cannot process before factory finishes");
        economy.Tick(rules.Data.buildings[0].finishUtc);
        check(state.factoryLevel == 1 && rules.Data.dynamicFactoryProvided, "completed placed recycler enables real operation");
        check(!economy.StartSalvage(0, state.lastSeenUtc).success, "fresh game cannot mint income from nonexistent imported rubble");
        var site = rules.Site(siteId);
        long expected = RubbleEconomy.BuildingPrice(site) / 3;
        var yield = RubbleEconomy.Yield(state, site);
        check(yield.concrete * 20L + yield.iron * 60L + yield.wood * 35L + yield.other * 10L <= expected,
            "extra reusable materials have capped sale value");
        long balance = state.coins;
        economy.ClearingCrewReady = () => false;
        check(rules.Clear(siteId, rules.Data.buildings[0].id, state.lastSeenUtc).success, "real depot can dispatch to actual rubble");
        check(!economy.UpgradeEquipment(state.equipmentUnits[0].id, state.lastSeenUtc).success, "busy machine upgrade rejected");
        economy.Tick(state.lastSeenUtc + 600);
        check(state.coins == balance && !site.cleared, "travel/wait time does not award money before completion");
        economy.ClearingCrewReady = () => true;
        economy.HaulingCrewReady = () => true;
        economy.Tick(state.lastSeenUtc);
        economy.Tick(state.lastSeenUtc + RubbleEconomy.TotalSeconds(state, site) + 1);
        check(site.cleared && state.jobStage == JobStage.Idle && state.coins == balance, "completion gives resources, not automatic money");
        economy.Tick(state.lastSeenUtc + 600);
        check(state.coins == balance, "no automatic payout while idle");
        check(economy.SellResources("all", state.lastSeenUtc).success && state.coins > balance, "manual resource sale grants money");
        check(!rules.Clear(siteId, rules.Data.buildings[0].id, state.lastSeenUtc).success, "cleaned rubble cannot be farmed repeatedly");

        state.coins = 100000;
        var first = state.equipmentUnits[0]; var truck = state.equipmentUnits[1];
        double before = EquipmentEconomy.Capacity(state, "excavator");
        long price = first.purchasePrice / 2;
        check(!economy.UpgradeEquipment(state.lastSeenUtc).success, "no fleet-wide upgrade path");
        for (int i = 0; i < 3; i++)
        {
            balance = state.coins;
            check(economy.UpgradeEquipment(first.id, state.lastSeenUtc).success, "individual upgrade stage " + (i + 1));
            check(state.coins == balance - price, "each stage costs original half price");
            check(truck.level == 1, "other machines never inherit upgrades");
        }
        check(!economy.UpgradeEquipment(first.id, state.lastSeenUtc).success, "fourth upgrade rejected");
        check(EquipmentEconomy.Capacity(state, "excavator") > before, "individual upgrade actually improves clearing capacity");
        check(economy.BuyEquipment("excavator", state.lastSeenUtc).success && state.equipmentUnits[3].level == 1,
            "new machine does not inherit upgraded peer level");
        long time = RubbleEconomy.ClearingSeconds(state, site);
        check(time >= 30 && time <= 60,
            "work lasts one starter minute, with earned equipment improvements");
        foreach (var unit in state.equipmentUnits)
        {
            check(EquipmentEconomy.UpgradeCost(unit) == EquipmentEconomy.Price(unit.kind) / 2, "type-specific original half price");
            if (unit.kind != "excavator")
                for (int i = 0; i < 3; i++) check(economy.UpgradeEquipment(unit.id, state.lastSeenUtc).success, "all machine kinds have three upgrades");
        }
        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(state, options);
        var loaded = new EconomyService(JsonSerializer.Deserialize<GameState>(json, options));
        check(loaded.State.equipmentUnits[0].id == first.id && loaded.State.equipmentUnits[0].level == 4 &&
            loaded.State.equipmentUnits[0].purchasePrice == first.purchasePrice, "machine identity, upgrades and original price survive save");
        check(loaded.State.development.rubble[0].cleared && loaded.State.coins == state.coins, "clearance and exact reward survive reload");
        double effectivePower = EquipmentEconomy.Capacity(loaded.State, "excavator");
        loaded.State.equipmentUnits = null; loaded.State.equipmentLevel = 5;
        var migrated = new EconomyService(loaded.State);
        check(migrated.State.equipmentUnits.Length == 4 && migrated.State.equipmentUnits[0].level == 4 &&
            EquipmentEconomy.Capacity(migrated.State, "excavator") >= effectivePower, "legacy fleet migration preserves owned machines and earned performance");
        check(!migrated.UpgradeEquipment(migrated.State.equipmentUnits[0].id, state.lastSeenUtc).success,
            "fully upgraded legacy machine never charged again");
        string snapshot = JsonSerializer.Serialize(migrated.State, options);
        new EconomyService(migrated.State);
        check(snapshot == JsonSerializer.Serialize(migrated.State, options), "equipment migration is idempotent");
        var legacy = GameCatalog.CreateNew(now);
        legacy.excavators = legacy.trucks = legacy.bulldozers = 1;
        legacy.equipmentLevel = 2; legacy.equipmentUnits = null;
        legacy.development.requiresPlacedFactory = false;
        var legacyEconomy = new EconomyService(legacy);
        double prior = EquipmentEconomy.Capacity(legacy, "excavator");
        check(legacyEconomy.UpgradeEquipment(legacy.equipmentUnits[0].id, now).success &&
            EquipmentEconomy.Capacity(legacy, "excavator") > prior, "remaining legacy upgrades improve preserved power, not just the level label");
        var overflow = new EconomyService(JsonSerializer.Deserialize<GameState>(json, options));
        var pending = new CityDevelopmentService(overflow);
        overflow.ClearingCrewReady = () => true;
        overflow.HaulingCrewReady = () => true;
        var next = pending.Site(CityDevelopmentService.SiteId(0, 1));
        check(pending.Clear(next.id, pending.Data.buildings[0].id, overflow.State.lastSeenUtc).success, "second authentic contract");
        overflow.Tick(overflow.State.lastSeenUtc);
        long finish = pending.Data.dispatches[0].finishUtc;
        overflow.HaulingCrewReady = () => false;
        overflow.Tick(finish);
        int concreteBefore = overflow.State.stock.concrete;
        overflow.State.coins = long.MaxValue;
        overflow.HaulingCrewReady = () => true;
        overflow.Tick(finish);
        check(next.cleared && overflow.State.jobStage == JobStage.Idle &&
            overflow.State.stock.concrete > concreteBefore && overflow.State.coins == long.MaxValue,
            "full wallet does not block resource delivery or overflow currency");
        overflow.State.coins = 0;
        overflow.Tick(overflow.State.lastSeenUtc);
        check(next.cleared && overflow.State.coins == 0, "completed delivery never grants automatic cash");
        for (int d = 0; d < GameCatalog.Districts.Length; d++)
            foreach (var definition in GameCatalog.Districts[d].projects)
                for (int level = 1; level <= 5; level++)
                {
                    var sample = new RubbleSiteState { district = d, projectId = definition.id };
                    state.factoryLevel = level;
                    var stock = RubbleEconomy.Yield(state, sample);
                    check(stock.concrete * 20L + stock.iron * 60L + stock.wood * 35L + stock.other * 10L <= RubbleEconomy.Reward(sample),
                        "all map units and factory tiers preserve bounded salvage material value");
                }
    }
}