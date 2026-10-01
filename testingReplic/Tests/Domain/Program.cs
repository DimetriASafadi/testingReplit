using System;
using System.Collections.Generic;
using System.Text.Json;
using NewGaza.Core;

internal static class Program
{
    private const long Epoch = 1700000000;
    private static int assertions;

    private static void Main()
    {
        var tests = new Action[]
        {
            Catalog, AffordableStart, InvalidActions, SalvagePhases, OfflineAndPersistence,
            ProjectValidation, AgricultureAndIndustry, CommerceAndRemainders,
            DailyAndRollback, Upgrades, OverflowAndCorruption, RecyclingWaitsForStorage, TransactionAtomicity, AllDistrictProgression,
            EarnedProgressionWithoutGrants
        };
        foreach (var test in tests)
        {
            test();
            Console.WriteLine("PASS " + test.Method.Name);
        }
        Console.WriteLine("PASS " + tests.Length + " regression groups / " + assertions + " assertions");
    }

    private static EconomyService New() { return new EconomyService(GameCatalog.CreateNew(Epoch)); }
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    private static void Ok(ActionResult result) { Check(result.success, result.message); }
    private static void Fail(ActionResult result)
    {
        Check(!result.success && !string.IsNullOrWhiteSpace(result.message), "Expected explicit Arabic failure");
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual);
    }
    private static void Throws(Action action)
    {
        bool thrown = false;
        try { action(); } catch (InvalidOperationException) { thrown = true; }
        Check(thrown, "Expected explicit corrupt-state exception");
    }
    private static void Fleet(EconomyService economy)
    {
        Ok(economy.BuyEquipment("factory", economy.State.lastSeenUtc));
        Ok(economy.BuyEquipment("excavator", economy.State.lastSeenUtc));
        Ok(economy.BuyEquipment("bulldozer", economy.State.lastSeenUtc));
        Ok(economy.BuyEquipment("truck", economy.State.lastSeenUtc));
        Ok(economy.BuyEquipment("truck", economy.State.lastSeenUtc));
    }
    private static void FinishJob(EconomyService economy)
    {
        for (int i = 0; i < 3 && economy.State.jobStage != JobStage.Idle; i++)
            economy.Tick(economy.State.jobFinishUtc);
        Equal(JobStage.Idle, economy.State.jobStage, "Salvage completed");
    }
    private static void Fund(EconomyService economy)
    {
        // Fixture funds for focused defensive tests only; never part of production.
        economy.State.coins = 100000000;
        economy.State.stock.concrete = 100000;
        economy.State.stock.iron = 100000;
    }
    private static void CompleteProject(EconomyService economy, int district, string id)
    {
        Ok(economy.StartProject(district, id, economy.State.lastSeenUtc));
        economy.Tick(economy.FindProject(district, id).finishUtc);
        Check(economy.FindProject(district, id).completed, "Project finished");
    }

    private static void Catalog()
    {
        Equal(11, GameCatalog.Districts.Length, "Ten neighborhoods and final Rashid");
        Equal("الشجاعية", GameCatalog.Districts[0].name, "First district");
        Equal("الزيتون", GameCatalog.Districts[1].name, "Second district");
        Equal("شارع الرشيد", GameCatalog.Districts[10].name, "Final district");
        string[] rarityTiers = { "عادي", "نادر", "ملحمي", "أسطوري" };
        var observedTiers = new HashSet<string>();
        int previousTier = -1;
        for (int d = 0; d < 11; d++)
        {
            var definition = GameCatalog.Districts[d];
            if (d < 10)
            {
                int tier = Array.IndexOf(rarityTiers, definition.rarity);
                Check(tier >= 0, "Normal districts use only the four source rarity classes");
                Check(tier >= previousTier, "Rarity groups advance in sensible district order");
                Equal(d < 3 ? rarityTiers[0] : d < 6 ? rarityTiers[1] : d < 8 ? rarityTiers[2] : rarityTiers[3],
                    definition.rarity, "Expected rarity group");
                previousTier = tier;
                observedTiers.Add(definition.rarity);
            }
            else Equal("ختامي", definition.rarity, "Rashid uses separate final district classification");
            Check(definition.projects.Length >= 7, "Enough projects");
            var kinds = new HashSet<ProjectKind>();
            var ids = new HashSet<string>();
            foreach (var project in definition.projects)
            {
                Check(ids.Add(project.id), "Unique project ID within district");
                Check(project.cost >= 0 && project.durationSeconds > 0, "Authentic positive project cost/time");
                kinds.Add(project.kind);
            }
            foreach (ProjectKind kind in new[] { ProjectKind.Housing, ProjectKind.Road, ProjectKind.Water,
                         ProjectKind.Power, ProjectKind.Park, ProjectKind.Services, ProjectKind.Investment })
                Check(kinds.Contains(kind), "Required project kind");
        }
        Equal(4, observedTiers.Count, "All four source rarity classes are represented");
        var first = GameCatalog.Districts[0].projects;
        Equal(25000L, Array.Find(first, p => p.id == "housing").cost, "Small house cost");
        Equal(7200, Array.Find(first, p => p.id == "housing").durationSeconds, "Small house timer");
        Equal(150000L, Array.Find(first, p => p.id == "road").cost, "Road cost");
        Equal(64800, Array.Find(first, p => p.id == "road").durationSeconds, "Road timer");
    }

    private static void AffordableStart()
    {
        var e = New();
        Equal(50000L, e.State.coins, "Initial balance");
        Fleet(e);
        Equal(10000L, e.State.coins, "Factory excavator bulldozer and two trucks affordable");
        Ok(e.StartProject(0, "farm", Epoch));
        Ok(e.StartSalvage(0, Epoch));
        Check(e.State.coins >= 0, "Seed purchase leaves positive balance");
        FinishJob(e);
        Check(e.State.stock.concrete > 0 && e.State.stock.iron > 0, "Stored resources");
        Ok(e.SellResources("wood", e.State.lastSeenUtc));
        Ok(e.SellResources("other", e.State.lastSeenUtc));
        Ok(e.SellResources("concrete", e.State.lastSeenUtc));
        Ok(e.SellResources("iron", e.State.lastSeenUtc));
        Fail(e.SellResources("all", e.State.lastSeenUtc));
    }

    private static void InvalidActions()
    {
        var e = New();
        Fail(e.SelectDistrict(-1)); Fail(e.SelectDistrict(11)); Fail(e.SelectDistrict(1));
        Fail(e.BuyEquipment("ad", Epoch)); Fail(e.BuyEquipment(null, Epoch));
        Fail(e.UpgradeFactory(Epoch)); Fail(e.UpgradeEquipment(Epoch));
        Fail(e.StartSalvage(0, Epoch)); Fail(e.StartSalvage(1, Epoch));
        Fail(e.StartProject(0, null, Epoch)); Fail(e.StartProject(-1, "farm", Epoch));
        Fail(e.StartProject(1, "farm", Epoch)); Fail(e.CollectIncome(0, "missing", Epoch));
        Fail(e.CollectIncome(0, "farm", Epoch)); Fail(e.ClaimDistrictReward(0, Epoch));
        Fail(e.ClaimDistrictReward(10, Epoch)); Fail(e.SellResources("unknown", Epoch));
        Fail(e.BuyEquipment("truck", -1)); Fail(e.ClaimDailyGift(-1));
        Equal(50000L, e.State.coins, "Invalid actions never award or debit money");
        Equal(0f, e.Progress(-1), "Invalid district progress");
        Check(e.FindProject(0, "bogus") == null, "Unknown lookup");
    }

    private static void SalvagePhases()
    {
        var e = New(); Fleet(e);
        Ok(e.StartSalvage(0, Epoch));
        Fail(e.StartSalvage(0, Epoch));
        Equal(Epoch + 180, e.State.jobFinishUtc, "Clearing duration");
        e.Tick(Epoch + 179); Equal(JobStage.Clearing, e.State.jobStage, "No premature clearing");
        e.Tick(Epoch + 180); Equal(JobStage.Hauling, e.State.jobStage, "Hauling starts");
        Equal(Epoch + 240, e.State.jobFinishUtc, "Two trucks haul faster");
        e.Tick(Epoch + 240); Equal(JobStage.Recycling, e.State.jobStage, "Recycling starts");
        Equal(0, e.State.districts[0].clearedLoads, "No completion until resources delivered");
        e.Tick(Epoch + 480);
        Equal(1, e.State.districts[0].clearedLoads, "One local load");
        Equal(40, e.State.stock.concrete, "Concrete output");
        Equal(15, e.State.stock.iron, "Iron output");
        Equal(12, e.State.stock.wood, "Wood output");
        Equal(8, e.State.stock.other, "Other output");
        e.Tick(Epoch + 10000);
        Equal(40, e.State.stock.concrete, "No repeated job reward");
        for (int i = 1; i < GameCatalog.Districts[0].rubbleLoads; i++)
        {
            Ok(e.StartSalvage(0, e.State.lastSeenUtc)); FinishJob(e);
        }
        float progress = e.Progress(0);
        var imported = e.StartSalvage(0, e.State.lastSeenUtc);
        Ok(imported);
        Check(imported.message.Contains("مستورد"), "Imported contracts explicitly labeled");
        FinishJob(e);
        Equal(progress, e.Progress(0), "Imported salvage cannot advance completion");
        Equal(GameCatalog.Districts[0].rubbleLoads, e.State.districts[0].clearedLoads, "Local loads capped");
    }

    private static void OfflineAndPersistence()
    {
        var e = New(); Fleet(e);
        Ok(e.StartSalvage(0, Epoch));
        Ok(e.StartProject(0, "farm", Epoch));
        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(e.State, options);
        var loaded = new EconomyService(JsonSerializer.Deserialize<GameState>(json, options));
        loaded.Tick(Epoch + 86400 * 7);
        Equal(JobStage.Idle, loaded.State.jobStage, "All phases catch up offline");
        Equal(1, loaded.State.districts[0].clearedLoads, "Only active job completed offline");
        Equal(1500L, loaded.PendingIncome(0, "farm", loaded.State.lastSeenUtc), "Farm matures offline");
        string before = JsonSerializer.Serialize(loaded.State, options);
        loaded.Tick(loaded.State.lastSeenUtc);
        Equal(before, JsonSerializer.Serialize(loaded.State, options), "Tick is idempotent");
        Ok(loaded.CollectIncome(0, "farm", loaded.State.lastSeenUtc));
        long coins = loaded.State.coins;
        Fail(loaded.CollectIncome(0, "farm", loaded.State.lastSeenUtc));
        Equal(coins, loaded.State.coins, "No double harvest");
    }

    private static void ProjectValidation()
    {
        var e = New();
        Fail(e.StartProject(0, "housing", Epoch)); // Requires water.
        Fail(e.StartProject(0, "water", Epoch)); // Requires stored resources.
        var definition = GameCatalog.Districts[0].projects[6];
        long originalCost = definition.cost;
        int originalConcrete = definition.concreteCost;
        int originalIron = definition.ironCost;
        int originalDuration = definition.durationSeconds;
        long originalIncome = definition.income;
        try
        {
            definition.cost = -500; Fail(e.StartProject(0, "farm", Epoch));
            definition.cost = originalCost; definition.concreteCost = -1;
            Fail(e.StartProject(0, "farm", Epoch));
            definition.concreteCost = originalConcrete; definition.ironCost = -1;
            Fail(e.StartProject(0, "farm", Epoch));
            definition.ironCost = originalIron; definition.durationSeconds = -1;
            Fail(e.StartProject(0, "farm", Epoch));
            definition.durationSeconds = originalDuration; definition.income = -1;
            Fail(e.StartProject(0, "farm", Epoch));
            Equal(50000L, e.State.coins, "Negative catalog costs cannot create coins");
        }
        finally
        {
            definition.cost = originalCost; definition.concreteCost = originalConcrete;
            definition.ironCost = originalIron; definition.durationSeconds = originalDuration;
            definition.income = originalIncome;
        }
        Fund(e);
        Ok(e.StartProject(0, "water", Epoch));
        Fail(e.StartProject(0, "water", Epoch));
        e.Tick(Epoch + 3599);
        Check(!e.FindProject(0, "water").completed, "Real water timer");
        e.Tick(Epoch + 3600);
        Fail(e.StartProject(0, "water", e.State.lastSeenUtc));
        CompleteProject(e, 0, "housing");
        Fail(e.CollectIncome(0, "housing", e.State.lastSeenUtc));
    }

    private static void AgricultureAndIndustry()
    {
        var e = New();
        Ok(e.StartProject(0, "farm", Epoch));
        Fail(e.StartProject(0, "farm", Epoch));
        Fail(e.CollectIncome(0, "farm", Epoch + 299));
        Check(!e.FindProject(0, "farm").completed, "Wait to harvest");
        Ok(e.CollectIncome(0, "farm", Epoch + 300));
        Equal(51000L, e.State.coins, "500 seed input, 1500 harvest output");
        float progress = e.Progress(0);
        Fail(e.CollectIncome(0, "farm", Epoch + 300));
        Ok(e.StartProject(0, "farm", Epoch + 300));
        Equal(progress, e.Progress(0), "Replanting does not undo completed investment");
        Fail(e.CollectIncome(0, "farm", Epoch + 599));
        Ok(e.CollectIncome(0, "farm", Epoch + 600));
        Equal(52000L, e.State.coins, "Repeat farm profit");
        Fail(e.StartProject(0, "industry", e.State.lastSeenUtc));
        Fund(e); CompleteProject(e, 0, "power");
        long coins = e.State.coins; int concrete = e.State.stock.concrete; int iron = e.State.stock.iron;
        CompleteProject(e, 0, "industry");
        Fail(e.StartProject(0, "industry", e.State.lastSeenUtc));
        Ok(e.CollectIncome(0, "industry", e.State.lastSeenUtc));
        Equal(coins + 1500, e.State.coins, "Industry net cash");
        Equal(concrete + 70, e.State.stock.concrete, "Industry input/output concrete");
        Equal(iron + 25, e.State.stock.iron, "Industry input/output iron");
        Equal(20, e.State.stock.wood, "Industry wood output");
        Equal(10, e.State.stock.other, "Industry other output");
        Fail(e.CollectIncome(0, "industry", e.State.lastSeenUtc));
        CompleteProject(e, 0, "industry"); Ok(e.CollectIncome(0, "industry", e.State.lastSeenUtc));
    }

    private static void CommerceAndRemainders()
    {
        var e = New(); Fund(e);
        CompleteProject(e, 0, "power"); CompleteProject(e, 0, "services");
        CompleteProject(e, 0, "commerce");
        long finish = e.State.lastSeenUtc;
        Equal(0L, e.PendingIncome(0, "commerce", finish), "No income at build completion");
        Equal(0L, e.PendingIncome(0, "commerce", finish + 3599), "Whole period only");
        Equal(3000L, e.PendingIncome(0, "commerce", finish + 3600), "One period");
        Equal(9000L, e.PendingIncome(0, "commerce", finish + 3 * 3600 + 500), "Offline accrual");
        Ok(e.CollectIncome(0, "commerce", finish + 3 * 3600 + 500));
        Equal(finish + 3 * 3600, e.FindProject(0, "commerce").lastIncomeUtc, "Remainder retained");
        Fail(e.CollectIncome(0, "commerce", finish + 3 * 3600 + 500));
        Ok(e.CollectIncome(0, "commerce", finish + 4 * 3600));
        long coins = e.State.coins;
        Fail(e.CollectIncome(0, "commerce", finish));
        Equal(coins, e.State.coins, "Rollback creates no extra income");
        e.Tick(finish + 100 * 3600);
        Equal(96 * 3000L, e.PendingIncome(0, "commerce", e.State.lastSeenUtc), "Multiple offline periods");
        Fail(e.StartProject(0, "commerce", e.State.lastSeenUtc));
    }

    private static void DailyAndRollback()
    {
        var e = New();
        Check(e.CanClaimDailyGift(Epoch), "First gift available");
        Ok(e.ClaimDailyGift(Epoch));
        Equal(53000L, e.State.coins, "Guaranteed daily coins");
        Equal(20, e.State.stock.concrete, "Guaranteed daily materials");
        Fail(e.ClaimDailyGift(Epoch)); Fail(e.ClaimDailyGift(Epoch - 1));
        Fail(e.ClaimDailyGift(Epoch + 86399));
        Check(!e.CanClaimDailyGift(Epoch + 86399), "Full 24 hours required");
        Ok(e.ClaimDailyGift(Epoch + 86400));
        Fail(e.ClaimDailyGift(Epoch + 86400));
        e.Tick(Epoch + 86400 * 10);
        Check(!e.CanClaimDailyGift(Epoch + 86400 * 2), "Rollback below high-water denied");
        Fail(e.ClaimDailyGift(Epoch + 86400 * 2));
        Ok(e.ClaimDailyGift(Epoch + 86400 * 10));
        Equal(59000L, e.State.coins, "No multiple accumulated gifts");
        e.Tick(Epoch); Equal(Epoch + 86400 * 10, e.State.lastSeenUtc, "High water retained");
        long highwater = e.State.lastSeenUtc;
        Ok(e.StartProject(0, "farm", Epoch));
        Equal(highwater + 300, e.FindProject(0, "farm").finishUtc, "Rollback cannot shorten new timer");
        var zero = new EconomyService(GameCatalog.CreateNew(0));
        Fail(zero.ClaimDailyGift(0));
        Ok(zero.ClaimDailyGift(1)); Fail(zero.ClaimDailyGift(1));
    }

    private static void Upgrades()
    {
        var e = New(); Fleet(e); Fund(e);
        Fail(e.BuyEquipment("factory", Epoch));
        Ok(e.StartSalvage(0, Epoch));
        Fail(e.UpgradeFactory(Epoch)); Fail(e.UpgradeEquipment(Epoch));
        FinishJob(e);
        for (int level = 2; level <= 5; level++)
        {
            long coins = e.State.coins;
            Ok(e.UpgradeFactory(e.State.lastSeenUtc));
            Equal(coins - (level - 1) * 20000L, e.State.coins, "Factory upgrade cost");
            Ok(e.UpgradeEquipment(e.State.lastSeenUtc));
            Equal(level, e.State.factoryLevel, "Factory level"); Equal(level, e.State.equipmentLevel, "Equipment level");
        }
        Fail(e.UpgradeFactory(e.State.lastSeenUtc)); Fail(e.UpgradeEquipment(e.State.lastSeenUtc));
        int stock = e.State.stock.concrete;
        Ok(e.StartSalvage(0, e.State.lastSeenUtc));
        Equal(36L, e.State.jobFinishUtc - e.State.lastSeenUtc, "Level five clearing speed");
        FinishJob(e);
        Equal(stock + 200, e.State.stock.concrete, "Level five factory yield");
        e.State.coins = 0;
        Fail(e.BuyEquipment("truck", e.State.lastSeenUtc));
        Ok(e.StartSalvage(0, e.State.lastSeenUtc));
        FinishJob(e); Ok(e.SellResources("all", e.State.lastSeenUtc));
        Check(e.State.coins > 0, "Zero balance is not an economic softlock");
    }

    private static void OverflowAndCorruption()
    {
        Throws(() => new EconomyService(new GameState()));
        var state = GameCatalog.CreateNew(Epoch); state.coins = -1;
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.stock.iron = -1;
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.districts[1].unlocked = true;
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.districts[0].projects[0].id = "forged";
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.districts[0].rewardClaimed = true;
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.districts[0].projects[0].completed = true;
        Throws(() => new EconomyService(state));
        state = GameCatalog.CreateNew(Epoch); state.districts[0].projects[0].startedUtc = Epoch + 1;
        Throws(() => new EconomyService(state));
        var e = New(); e.State.coins = long.MaxValue; e.State.stock.wood = 1;
        Fail(e.SellResources("all", Epoch)); Equal(1, e.State.stock.wood, "Overflow sale preserves goods");
        Fail(e.ClaimDailyGift(Epoch)); Equal(0L, e.State.lastGiftUtc, "Overflow gift remains unclaimed");
        e.State.coins = 50000; Fleet(e); e.State.stock.concrete = int.MaxValue;
        Fail(e.StartSalvage(0, Epoch)); Equal(JobStage.Idle, e.State.jobStage, "Overflow production not started");
        e.State.stock.concrete = 0;
        Ok(e.StartProject(0, "farm", Epoch)); e.Tick(Epoch + 300);
        e.State.coins = long.MaxValue;
        Fail(e.CollectIncome(0, "farm", e.State.lastSeenUtc));
        Equal(1500L, e.PendingIncome(0, "farm", e.State.lastSeenUtc), "Overflow harvest remains available");
        var end = new EconomyService(GameCatalog.CreateNew(long.MaxValue));
        Fail(end.StartProject(0, "farm", long.MaxValue));
        var nearEnd = new EconomyService(GameCatalog.CreateNew(long.MaxValue - 200));
        Fleet(nearEnd);
        Fail(nearEnd.StartSalvage(0, nearEnd.State.lastSeenUtc));
        Equal(JobStage.Idle, nearEnd.State.jobStage, "Every salvage phase must fit real timer range");
    }

    private static void RecyclingWaitsForStorage()
    {
        var e = New(); Fleet(e);
        e.State.stock.concrete = int.MaxValue - 40;
        Ok(e.StartProject(0, "farm", Epoch));
        Ok(e.StartSalvage(0, Epoch));
        Ok(e.ClaimDailyGift(Epoch)); // Initially fits, but takes 20 of the batch's 40 free slots.
        long deadline = Epoch + 480;
        int concrete = e.State.stock.concrete;
        int iron = e.State.stock.iron;
        int wood = e.State.stock.wood;
        int other = e.State.stock.other;
        e.Tick(deadline);
        Equal(JobStage.Recycling, e.State.jobStage, "Ready recycling waits for capacity without throwing");
        Equal(deadline, e.State.jobFinishUtc, "Waiting batch retains its original deadline");
        Equal(deadline, e.State.lastSeenUtc, "Storage wait still advances high-water clock");
        Equal(0, e.State.districts[0].clearedLoads, "Waiting batch does not credit district completion");
        Check(e.FindProject(0, "farm").completed, "Storage wait does not block other project timers");
        Equal(concrete, e.State.stock.concrete, "No partial recycling concrete delivery");
        Equal(iron, e.State.stock.iron, "No partial recycling iron delivery");
        Equal(wood, e.State.stock.wood, "No partial recycling wood delivery");
        Equal(other, e.State.stock.other, "No partial recycling other delivery");
        var options = new JsonSerializerOptions { IncludeFields = true };
        string waiting = JsonSerializer.Serialize(e.State, options);
        e.Tick(deadline);
        Equal(waiting, JsonSerializer.Serialize(e.State, options), "Repeated blocked tick is idempotent");
        var resumed = new EconomyService(JsonSerializer.Deserialize<GameState>(waiting, options));
        resumed.Tick(deadline + 86400);
        Equal(JobStage.Recycling, resumed.State.jobStage, "Full saved batch survives reload and offline tick");
        var blocked = resumed.StartSalvage(0, resumed.State.lastSeenUtc);
        Fail(blocked);
        Check(blocked.message.Contains("المخزن"), "Waiting batch explains how to release capacity");
        Ok(resumed.SellResources("concrete", resumed.State.lastSeenUtc));
        Equal(0, resumed.State.stock.concrete, "Sale is allowed while finished batch waits");
        Equal(JobStage.Recycling, resumed.State.jobStage, "Sale preserves batch for the next tick");
        resumed.Tick(resumed.State.lastSeenUtc);
        Equal(JobStage.Idle, resumed.State.jobStage, "Free capacity releases the waiting batch");
        Equal(0L, resumed.State.jobFinishUtc, "Delivered batch clears deadline");
        Equal(1, resumed.State.districts[0].clearedLoads, "Waiting local load credited exactly once");
        Equal(40, resumed.State.stock.concrete, "Sold stock replaced by exact waiting concrete output");
        Equal(iron + 15, resumed.State.stock.iron, "Exact waiting iron output");
        Equal(wood + 12, resumed.State.stock.wood, "Exact waiting wood output");
        Equal(other + 8, resumed.State.stock.other, "Exact waiting other output");
        string delivered = JsonSerializer.Serialize(resumed.State, options);
        resumed.Tick(resumed.State.lastSeenUtc);
        Equal(delivered, JsonSerializer.Serialize(resumed.State, options), "No duplicate batch on repeated delivery tick");

        var industrial = New(); Fleet(industrial); Fund(industrial);
        CompleteProject(industrial, 0, "power");
        CompleteProject(industrial, 0, "industry");
        industrial.State.stock.concrete = int.MaxValue - 80;
        long started = industrial.State.lastSeenUtc;
        Ok(industrial.StartSalvage(0, started));
        Ok(industrial.CollectIncome(0, "industry", started)); // Production fills the batch's free space.
        Equal(int.MaxValue, industrial.State.stock.concrete, "Industry can fill remaining storage while salvage is pending");
        int industryIron = industrial.State.stock.iron;
        industrial.Tick(started + 480);
        Equal(JobStage.Recycling, industrial.State.jobStage, "Industry-induced storage wait is recoverable");
        Equal(0, industrial.State.districts[0].clearedLoads, "Industrial storage wait credits no premature load");
        Equal(industryIron, industrial.State.stock.iron, "Industrial storage wait has no partial salvage output");
        Ok(industrial.SellResources("all", industrial.State.lastSeenUtc));
        industrial.Tick(industrial.State.lastSeenUtc);
        Equal(40, industrial.State.stock.concrete, "Industry-blocked salvage delivered after sale");
        Equal(15, industrial.State.stock.iron, "Industry-blocked salvage iron delivered once");
        Equal(12, industrial.State.stock.wood, "Industry-blocked salvage wood delivered once");
        Equal(8, industrial.State.stock.other, "Industry-blocked salvage other delivered once");
        Equal(1, industrial.State.districts[0].clearedLoads, "Industry-blocked load credited once");
        delivered = JsonSerializer.Serialize(industrial.State, options);
        industrial.Tick(industrial.State.lastSeenUtc);
        Equal(delivered, JsonSerializer.Serialize(industrial.State, options), "No industry-blocked batch duplication");
    }

    private static void TransactionAtomicity()
    {
        var e = New();
        e.State.coins = 499;
        Fail(e.StartProject(0, "farm", Epoch));
        Equal(499L, e.State.coins, "Unaffordable seeds not charged");
        Equal(0L, e.FindProject(0, "farm").finishUtc, "Unaffordable seeds do not create a timer");
        Fund(e);
        var water = Array.Find(GameCatalog.Districts[0].projects, p => p.id == "water");
        e.State.stock.iron = water.ironCost - 1;
        long coins = e.State.coins; int concrete = e.State.stock.concrete;
        Fail(e.StartProject(0, "water", Epoch));
        Equal(coins, e.State.coins, "Missing iron does not charge money");
        Equal(concrete, e.State.stock.concrete, "Missing iron does not consume concrete");
        e.State.stock.iron++;
        CompleteProject(e, 0, "water");
        Equal(coins - water.cost, e.State.coins, "Exact project money consumption");
        Equal(concrete - water.concreteCost, e.State.stock.concrete, "Exact project concrete consumption");
        Equal(0, e.State.stock.iron, "Exact project iron consumption");
        Fund(e); CompleteProject(e, 0, "power"); CompleteProject(e, 0, "industry");
        e.State.stock.wood = int.MaxValue;
        coins = e.State.coins; concrete = e.State.stock.concrete;
        Fail(e.CollectIncome(0, "industry", e.State.lastSeenUtc));
        Equal(coins, e.State.coins, "Full industry output storage does not credit coins");
        Equal(concrete, e.State.stock.concrete, "Full industry output storage does not partially deliver materials");
        Equal(4500L, e.PendingIncome(0, "industry", e.State.lastSeenUtc), "Uncollected industry remains ready");
        Ok(e.SellResources("wood", e.State.lastSeenUtc));
        Ok(e.CollectIncome(0, "industry", e.State.lastSeenUtc));
    }

    private static void AllDistrictProgression()
    {
        var e = New(); Fleet(e); Fund(e);
        for (int district = 0; district < 11; district++)
        {
            Check(e.State.districts[district].unlocked, "District unlocked in sequence");
            Ok(e.SelectDistrict(district));
            for (int i = 0; i < GameCatalog.Districts[district].rubbleLoads; i++)
            {
                Ok(e.StartSalvage(district, e.State.lastSeenUtc)); FinishJob(e);
            }
            foreach (var definition in GameCatalog.Districts[district].projects)
                CompleteProject(e, district, definition.id);
            Equal(1f, e.Progress(district), "Exactly 100 percent");
            if (district < 10) Check(!e.State.districts[district + 1].unlocked, "Completion alone does not unlock");
            if (district < 9) Check(!e.State.districts[10].unlocked, "Rashid requires all ten claimed");
            if (district == 0)
            {
                long beforeGift = e.State.coins;
                Ok(e.ClaimDailyGift(e.State.lastSeenUtc));
                Equal(beforeGift + 4000, e.State.coins, "Daily gift scales with 100 percent completion before reward claim");
            }
            long before = e.State.coins;
            Ok(e.ClaimDistrictReward(district, e.State.lastSeenUtc));
            Equal(before + GameCatalog.Districts[district].completionReward, e.State.coins, "Exact one-time reward");
            before = e.State.coins;
            Fail(e.ClaimDistrictReward(district, e.State.lastSeenUtc));
            Equal(before, e.State.coins, "Cannot double claim district");
            Fail(e.StartProject(district, "farm", e.State.lastSeenUtc + 300));
            Ok(e.CollectIncome(district, "farm", e.State.lastSeenUtc));
            Ok(e.StartProject(district, "farm", e.State.lastSeenUtc));
            Equal(1f, e.Progress(district), "Repeat crops do not undo reward eligibility");
            if (district > 0) Ok(e.SelectDistrict(district - 1));
        }
        Check(e.State.cityCompletedUtc > 0, "Final completion timestamp");
        Equal(1f, e.Progress(10), "Final corniche and hospitality complete");
        long coins = e.State.coins;
        Ok(e.ClaimDailyGift(e.State.lastSeenUtc));
        Equal(coins + 14000, e.State.coins, "Gift scales with eleven claimed districts");
        Ok(e.SelectDistrict(0)); Ok(e.StartSalvage(0, e.State.lastSeenUtc)); FinishJob(e);
        Equal(1f, e.Progress(0), "Return to previous district for imported recycling");
    }

    private static void EarnedProgressionWithoutGrants()
    {
        // End-to-end solvability uses only public actions and elapsed Unix time, no fixture money.
        var e = New(); Fleet(e);
        for (int district = 0; district < 11; district++)
        {
            foreach (var definition in GameCatalog.Districts[district].projects)
            {
                int attempts = 0;
                while (e.State.coins < definition.cost || e.State.stock.concrete < definition.concreteCost
                    || e.State.stock.iron < definition.ironCost)
                {
                    Check(++attempts < 1000, "Project must be economically reachable");
                    Ok(e.StartSalvage(district, e.State.lastSeenUtc)); FinishJob(e);
                    // Keep materials reserved for this project, sell spare fractions by kind.
                    if (e.State.stock.concrete >= definition.concreteCost + 40
                        && e.State.stock.iron >= definition.ironCost + 15
                        && definition.concreteCost == 0 && definition.ironCost == 0)
                        Ok(e.SellResources("all", e.State.lastSeenUtc));
                    else
                    {
                        if (e.State.stock.wood > 0) Ok(e.SellResources("wood", e.State.lastSeenUtc));
                        if (e.State.stock.other > 0) Ok(e.SellResources("other", e.State.lastSeenUtc));
                        if (e.State.coins < definition.cost && e.State.stock.concrete > definition.concreteCost + 200)
                            Ok(e.SellResources("concrete", e.State.lastSeenUtc));
                        if (e.State.coins < definition.cost && e.State.stock.iron > definition.ironCost + 100)
                            Ok(e.SellResources("iron", e.State.lastSeenUtc));
                    }
                }
                CompleteProject(e, district, definition.id);
                if (definition.income > 0 && definition.incomeSeconds == 0)
                    Ok(e.CollectIncome(district, definition.id, e.State.lastSeenUtc));
            }
            while (e.State.districts[district].clearedLoads < GameCatalog.Districts[district].rubbleLoads)
            {
                Ok(e.StartSalvage(district, e.State.lastSeenUtc)); FinishJob(e);
            }
            Ok(e.ClaimDistrictReward(district, e.State.lastSeenUtc));
            Check(e.State.coins >= 0 && e.State.stock.concrete >= 0 && e.State.stock.iron >= 0, "No negative economic state");
        }
        Check(e.State.cityCompletedUtc > 0, "All eleven districts reachable without grants");
    }
}