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
            Catalog, Geography, AffordableStart, InvalidActions, SalvagePhases, OfflineAndPersistence,
            ProjectValidation, AgricultureAndIndustry, CommerceAndRemainders,
            DailyAndRollback, Upgrades, OverflowAndCorruption, RecyclingWaitsForStorage, TransactionAtomicity, AllDistrictProgression,
            EarnedProgressionWithoutGrants, LegacyMigrationBoundaries, LegacyTimersAndJobs,
            ExpandedProgressionAndFinale, LegacyCorruption, RoadImprovements
        };
        foreach (var test in tests)
        {
            test();
            Console.WriteLine("PASS " + test.Method.Name);
        }
        Console.WriteLine("PASS " + tests.Length + " regression groups / " + assertions + " assertions");
    }

    // Legacy contract regression suite. Fresh placed-factory onboarding is covered in Development.
    private static EconomyService New()
    {
        var state = GameCatalog.CreateNew(Epoch); state.development.requiresPlacedFactory = false;
        return new EconomyService(state);
    }
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
    private static void ThrowsRegistration(Action action)
    {
        bool thrown = false;
        try { action(); }
        catch (ArgumentException) { thrown = true; }
        catch (InvalidOperationException) { thrown = true; }
        Check(thrown, "Expected invalid road registration exception");
    }
    private static void Fleet(EconomyService economy)
    {
        if (economy.State.development != null) economy.State.development.requiresPlacedFactory = false;
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
        Equal(13, GameCatalog.Districts.Length, "Twelve neighborhoods and final Rashid");
        Equal(12, GameCatalog.NeighborhoodCount, "Neighborhood API");
        Equal("الشجاعية", GameCatalog.Districts[0].name, "First district");
        Equal("التفاح", GameCatalog.Districts[1].name, "Second district follows east-to-west order");
        Equal("شارع الرشيد", GameCatalog.Districts[GameCatalog.FinalDistrictIndex].name, "Final district");
        string[] rarityTiers = { "عادي", "نادر", "ملحمي", "أسطوري" };
        var observedTiers = new HashSet<string>();
        int previousTier = -1;
        for (int d = 0; d < GameCatalog.Districts.Length; d++)
        {
            var definition = GameCatalog.Districts[d];
            if (d < GameCatalog.NeighborhoodCount)
            {
                int tier = Array.IndexOf(rarityTiers, definition.rarity);
                Check(tier >= 0, "Normal districts use only the four source rarity classes");
                Check(tier >= previousTier, "Rarity groups advance in sensible district order");
                Equal(d < 3 ? rarityTiers[0] : d < 8 ? rarityTiers[1] : d < 10 ? rarityTiers[2] : rarityTiers[3],
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

    private static void Geography()
    {
        string[] expected =
        {
            "الشجاعية", "التفاح", "الشيخ رضوان", "الدرج", "الكرامة", "البلدة القديمة",
            "النصر", "الصبرة", "الزيتون", "الرمال", "تل الهوا", "الشيخ عجلين", "شارع الرشيد"
        };
        string[] projectIds = { "water", "power", "housing", "road", "park", "services", "farm", "commerce", "industry" };
        Equal(expected.Length, GameGeography.Districts.Length, "Exactly twelve approved neighborhoods and final route");
        var ids = new HashSet<string>();
        var names = new HashSet<string>();
        for (int d = 0; d < expected.Length; d++)
        {
            var location = GameGeography.Districts[d];
            Equal(expected[d], location.name, "Verified geographic progression order");
            Equal(location.name, GameCatalog.Districts[d].name, "Map and economy agree");
            Check(ids.Add(location.id) && names.Add(location.name), "Unique geographic identity");
            Check(location.latitude > 31.48 && location.latitude < 31.57 &&
                location.longitude > 34.39 && location.longitude < 34.49, "Gaza reference point, not a similarly named locality");
            Check(location.sourceUrl.StartsWith("https://"), "Every point has a source");
            var point = GameGeography.DistrictPoint(d);
            Check(point.x > GameGeography.MapMinX && point.x < GameGeography.MapMaxX &&
                point.z > GameGeography.MapMinZ && point.z < GameGeography.MapMaxZ, "Camera bounds contain all districts");
            if (d > 0 && d < GameCatalog.NeighborhoodCount)
                Check(location.longitude < GameGeography.Districts[d - 1].longitude, "East-to-west normal district progression");
            Equal(projectIds.Length, GameCatalog.Districts[d].projects.Length, "Legacy save project count unchanged");
            for (int p = 0; p < projectIds.Length; p++)
                Equal(projectIds[p], GameCatalog.Districts[d].projects[p].id, "Legacy milestone project IDs unchanged");
        }
        var origin = GameGeography.Project(GameGeography.OriginLatitude, GameGeography.OriginLongitude);
        Check(Math.Abs(origin.x) < .001 && Math.Abs(origin.z) < .001, "Projection origin");
        var east = GameGeography.Project(GameGeography.OriginLatitude, GameGeography.OriginLongitude + .01);
        var north = GameGeography.Project(GameGeography.OriginLatitude + .01, GameGeography.OriginLongitude);
        Check(east.x > 47 && east.x < 48 && Math.Abs(east.z) < .001, "Longitude projects east with latitude correction");
        Check(north.z > 55 && north.z < 56 && Math.Abs(north.x) < .001, "Latitude projects north at fixed scale");
        Equal(GameGeography.Coastline.Length, GameGeography.RashidRoute.Length, "Matched shoreline sampling");
        double routeLength = 0;
        for (int i = 0; i < GameGeography.Coastline.Length; i++)
        {
            var coast = GameGeography.Coastline[i];
            var road = GameGeography.RashidRoute[i];
            Check(road.x > coast.x, "Rashid is on land, not in the sea");
            Check(Math.Abs(road.z - coast.z) < .001, "Same latitude for coast and road samples");
            if (i > 0)
            {
                var previous = GameGeography.RashidRoute[i - 1];
                Check(coast.z > GameGeography.Coastline[i - 1].z, "Coast ordered southwest to northeast");
                Check(road.z > previous.z, "Route ordered southwest to northeast");
                routeLength += Math.Sqrt(Math.Pow(road.x - previous.x, 2) + Math.Pow(road.z - previous.z, 2));
            }
        }
        Check(routeLength / GameGeography.UnitsPerKilometre > 8 &&
            routeLength / GameGeography.UnitsPerKilometre < 12, "Final route covers the city waterfront, not a tiny parcel");
        Check(GameGeography.DistrictPoint(4).z > GameGeography.DistrictPoint(2).z,
            "Karama stays north of Sheikh Radwan, never moved into a game grid");
        var oldCheckpoint = GameCatalog.CreateNew(Epoch);
        oldCheckpoint.coins = 43210;
        oldCheckpoint.stock.iron = 7;
        string json = JsonSerializer.Serialize(oldCheckpoint, new JsonSerializerOptions { IncludeFields = true });
        var restored = new EconomyService(JsonSerializer.Deserialize<GameState>(json,
            new JsonSerializerOptions { IncludeFields = true }));
        Equal(43210L, restored.State.coins, "Existing schema retains coins");
        Equal(7, restored.State.stock.iron, "Existing schema retains resources");
        Equal(2, restored.State.version, "Stable district identity uses v2 schema");
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
        Fail(e.SelectDistrict(-1)); Fail(e.SelectDistrict(GameCatalog.Districts.Length)); Fail(e.SelectDistrict(1));
        Fail(e.BuyEquipment("ad", Epoch)); Fail(e.BuyEquipment(null, Epoch));
        Fail(e.UpgradeFactory(Epoch)); Fail(e.UpgradeEquipment(Epoch));
        Fail(e.StartSalvage(0, Epoch)); Fail(e.StartSalvage(1, Epoch));
        Fail(e.StartProject(0, null, Epoch)); Fail(e.StartProject(-1, "farm", Epoch));
        Fail(e.StartProject(1, "farm", Epoch)); Fail(e.CollectIncome(0, "missing", Epoch));
        Fail(e.CollectIncome(0, "farm", Epoch)); Fail(e.ClaimDistrictReward(0, Epoch));
        Fail(e.ClaimDistrictReward(GameCatalog.FinalDistrictIndex, Epoch)); Fail(e.SellResources("unknown", Epoch));
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
            if (level <= 4)
                foreach (var unit in e.State.equipmentUnits) Ok(e.UpgradeEquipment(unit.id, e.State.lastSeenUtc));
            Equal(level, e.State.factoryLevel, "Factory level");
        }
        Fail(e.UpgradeFactory(e.State.lastSeenUtc)); Fail(e.UpgradeEquipment(e.State.lastSeenUtc));
        int stock = e.State.stock.concrete;
        Ok(e.StartSalvage(0, e.State.lastSeenUtc));
        Equal(103L, e.State.jobFinishUtc - e.State.lastSeenUtc, "Independent fully upgraded clearing speed");
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
        for (int district = 0; district < GameCatalog.Districts.Length; district++)
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
            if (district < GameCatalog.FinalDistrictIndex) Check(!e.State.districts[district + 1].unlocked, "Completion alone does not unlock");
            if (district < GameCatalog.NeighborhoodCount) Check(!e.State.districts[GameCatalog.FinalDistrictIndex].unlocked, "Rashid requires all twelve claimed");
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
        Check(e.CityComplete, "All current rewards claimed");
        Equal(1f, e.Progress(GameCatalog.FinalDistrictIndex), "Final corniche and hospitality complete");
        long coins = e.State.coins;
        Ok(e.ClaimDailyGift(e.State.lastSeenUtc));
        Equal(coins + 3000 + GameCatalog.Districts.Length * 1000L, e.State.coins, "Gift scales with all claimed districts");
        Ok(e.SelectDistrict(0)); Ok(e.StartSalvage(0, e.State.lastSeenUtc)); FinishJob(e);
        Equal(1f, e.Progress(0), "Return to previous district for imported recycling");
    }

    private static void EarnedProgressionWithoutGrants()
    {
        // End-to-end solvability uses only public actions and elapsed Unix time, no fixture money.
        var e = New(); Fleet(e);
        for (int district = 0; district < GameCatalog.Districts.Length; district++)
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
        Check(e.State.cityCompletedUtc > 0 && e.CityComplete, "All current districts reachable without grants");
    }

    private static readonly string[] OldIds =
    {
        "shujaiya", "tuffah", "sheikh-radwan", "karama", "old-city", "sabra",
        "zeitoun", "rimal", "tel-al-hawa", "sheikh-ijlin", "rashid"
    };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };
    private static string Json<T>(T value) { return JsonSerializer.Serialize(value, JsonOptions); }
    private static int DistrictIndex(string id) { return Array.FindIndex(GameCatalog.Districts, d => d.id == id); }

    private static GameState LegacyFixture(int claimedCount)
    {
        // Actual v1 wire shape: eleven positional districts, no district ID/access fields.
        // Deliberately independent of CreateNew, geography, current balances and current project definitions.
        string[] ids = { "water", "power", "housing", "road", "park", "services", "farm", "commerce", "industry" };
        int[] durations = { 3600, 5400, 7200, 64800, 3600, 10800, 300, 3600, 900 };
        var districts = new object[11];
        for (int d = 0; d < districts.Length; d++)
        {
            bool complete = d < claimedCount;
            var projects = new object[9];
            long next = Epoch + d * 150000;
            for (int p = 0; p < projects.Length; p++)
            {
                long finish = next + (d == 10 && p == 7 ? 14400 : durations[p]);
                projects[p] = new
                {
                    id = ids[p], startedUtc = complete ? next : 0, finishUtc = complete ? finish : 0,
                    completed = complete, lastIncomeUtc = complete && p != 6 && p != 8 ? finish : 0
                };
                next = finish;
            }
            districts[d] = new
            {
                unlocked = d <= claimedCount, rewardClaimed = complete,
                clearedLoads = complete ? 4 + d : 0, projects
            };
        }
        string payload = Json(new
        {
            version = 1, playerName = "لاعب قديم", coins = 7654321L,
            stock = new { concrete = 4321, iron = 3210, wood = 219, other = 108 },
            factoryLevel = 3, excavators = 2, trucks = 4, bulldozers = 3, equipmentLevel = 2,
            selectedDistrict = Math.Min(claimedCount, 10), districts,
            jobStage = 0, jobDistrict = 0, jobFinishUtc = 0L,
            lastSeenUtc = Epoch + 2000000, lastGiftUtc = Epoch + 100,
            cityCompletedUtc = claimedCount == 11 ? Epoch + 1900000 : 0
        });
        Check(!payload.Contains("legacyAccess") && !payload.Contains("shujaiya"), "True v1 fixture has no v2 district identity");
        return JsonSerializer.Deserialize<GameState>(payload, JsonOptions);
    }

    private static void LegacyMigrationBoundaries()
    {
        for (int claims = 0; claims <= 11; claims++)
        {
            var old = LegacyFixture(claims);
            string original = Json(old);
            var migrated = GameStateMigration.Upgrade(old);
            Equal(original, Json(old), "Migration never mutates input");
            Equal(2, migrated.version, "Upgraded schema");
            Equal(13, migrated.districts.Length, "Expansion length");
            Equal(old.playerName, migrated.playerName, "Name retained");
            Equal(old.coins, migrated.coins, "Migration creates no coins");
            Equal(Json(old.stock), Json(migrated.stock), "All stock retained");
            Equal(old.factoryLevel, migrated.factoryLevel, "Factory retained");
            Equal(old.equipmentLevel, migrated.equipmentLevel, "Equipment level retained");
            Equal(old.excavators, migrated.excavators, "Excavators retained");
            Equal(old.trucks, migrated.trucks, "Trucks retained");
            Equal(old.bulldozers, migrated.bulldozers, "Bulldozers retained");
            Equal(old.lastSeenUtc, migrated.lastSeenUtc, "High-water retained");
            Equal(old.lastGiftUtc, migrated.lastGiftUtc, "Gift deadline retained");
            Equal(old.cityCompletedUtc, migrated.cityCompletedUtc, "Historical completion retained");
            Equal(OldIds[old.selectedDistrict], migrated.districts[migrated.selectedDistrict].id, "Selected identity retained");
            var economy = new EconomyService(migrated);
            Check(!economy.CityComplete, "Even previously completed city needs expansion rewards");
            for (int d = 0; d < OldIds.Length; d++)
            {
                int index = DistrictIndex(OldIds[d]);
                var current = migrated.districts[index];
                Equal(old.districts[d].unlocked, current.unlocked, "Previously earned access retained");
                Equal(old.districts[d].unlocked, current.legacyAccess, "Persist explicit migration access only for unlocked old IDs");
                Equal(old.districts[d].rewardClaimed, current.rewardClaimed, "Old rewards retained");
                Equal(old.districts[d].clearedLoads, current.clearedLoads, "Old rubble retained");
                Equal(Json(old.districts[d].projects), Json(current.projects), "Every old project and timestamp retained");
                Equal(4 + d, GameCatalog.Districts[index].rubbleLoads, "Old rubble requirement does not rise with index");
                Equal(d == 10 ? 1000000L : 100000L + d * 25000, GameCatalog.Districts[index].completionReward, "Old reward unchanged");
                if (current.unlocked) Ok(economy.SelectDistrict(index));
            }
            var daraj = migrated.districts[DistrictIndex("daraj")];
            var nasr = migrated.districts[DistrictIndex("nasr")];
            Equal(claims >= 3, daraj.unlocked, "Eligible inserted Daraj opens on migration");
            Check(!nasr.unlocked, "Nasr waits for full prefix including Daraj");
            foreach (var fresh in new[] { daraj, nasr })
            {
                Check(!fresh.rewardClaimed && !fresh.legacyAccess && fresh.clearedLoads == 0, "New neighborhoods receive no work or rewards");
                Equal(9, fresh.projects.Length, "Nine independent new projects");
                foreach (var project in fresh.projects)
                    Check(!project.completed && project.startedUtc == 0 && project.finishUtc == 0 && project.lastIncomeUtc == 0,
                        "New projects have no free completion or inherited deadlines");
            }
            Check(!ReferenceEquals(daraj.projects, nasr.projects) && !ReferenceEquals(daraj.projects[0], nasr.projects[0]),
                "Inserted districts have independent state");
            string checkpoint = Json(migrated);
            Equal(checkpoint, Json(GameStateMigration.Upgrade(migrated)), "Second migration is a no-op");
            var reloaded = GameStateMigration.Upgrade(JsonSerializer.Deserialize<GameState>(checkpoint, JsonOptions));
            Equal(checkpoint, Json(reloaded), "Reload does not duplicate, reset or unlock ahead");
            new EconomyService(reloaded);
        }
    }

    private static void LegacyTimersAndJobs()
    {
        foreach (int frontier in new[] { 0, 2, 3, 4, 5, 9, 10 })
        foreach (JobStage stage in new[] { JobStage.Clearing, JobStage.Hauling, JobStage.Recycling })
        {
            var old = LegacyFixture(frontier);
            old.jobDistrict = frontier;
            old.jobStage = stage;
            old.jobFinishUtc = old.lastSeenUtc + 60;
            // Ongoing farm, water and power; later project deadlines must remain untouched at load.
            var projects = old.districts[frontier].projects;
            foreach (int p in new[] { 0, 1, 6 })
            {
                projects[p].startedUtc = old.lastSeenUtc;
                projects[p].finishUtc = old.lastSeenUtc + (p == 0 ? 3600 : p == 1 ? 5400 : 300);
            }
            var e = new EconomyService(GameStateMigration.Upgrade(old));
            int target = DistrictIndex(OldIds[frontier]);
            Equal(target, e.State.jobDistrict, "Active job remapped by old identity");
            Equal(target, e.State.selectedDistrict, "Active selection remapped by old identity");
            Equal(stage, e.State.jobStage, "Job stage untouched");
            Equal(old.jobFinishUtc, e.State.jobFinishUtc, "Job deadline untouched");
            Equal(Json(projects), Json(e.State.districts[target].projects), "All concurrent deadlines untouched");
            int stock = e.State.stock.concrete;
            FinishJob(e);
            Equal(1, e.State.districts[target].clearedLoads, "Job credits original neighborhood only");
            Equal(stock + 120, e.State.stock.concrete, "Exactly one old-level job yield");
            Equal(0, e.State.districts[DistrictIndex("daraj")].clearedLoads, "Job does not credit inserted district");
            e.Tick(old.lastSeenUtc + 5400);
            Check(e.FindProject(target, "water").completed && e.FindProject(target, "power").completed, "Build timers catch up after migration");
            Ok(e.CollectIncome(target, "farm", e.State.lastSeenUtc));
            Fail(e.CollectIncome(target, "farm", e.State.lastSeenUtc));
        }

        var completed = LegacyFixture(11);
        int oldRimal = 7;
        var commerce = completed.districts[oldRimal].projects[7];
        commerce.lastIncomeUtc = completed.lastSeenUtc - 7200 - 123;
        var farm = completed.districts[oldRimal].projects[6];
        farm.startedUtc = completed.lastSeenUtc - 30;
        farm.finishUtc = completed.lastSeenUtc + 270; // Completed milestone, new cycle in flight.
        var industry = completed.districts[oldRimal].projects[8];
        industry.startedUtc = 0;
        industry.finishUtc = 0;
        industry.lastIncomeUtc = completed.lastSeenUtc - 77; // Previously harvested batch.
        completed.selectedDistrict = oldRimal;
        var resumed = new EconomyService(GameStateMigration.Upgrade(completed));
        int rimal = DistrictIndex("rimal");
        Equal(Json(completed.districts[oldRimal].projects), Json(resumed.State.districts[rimal].projects), "Commerce remainder/replant/harvest state retained");
        Equal(6000L, resumed.PendingIncome(rimal, "commerce", resumed.State.lastSeenUtc), "Accrued commerce unchanged");
        Ok(resumed.CollectIncome(rimal, "commerce", resumed.State.lastSeenUtc));
        Equal(completed.lastSeenUtc - 123, resumed.FindProject(rimal, "commerce").lastIncomeUtc, "Old commerce remainder survives");
        Equal(0L, resumed.PendingIncome(rimal, "farm", resumed.State.lastSeenUtc), "Ongoing replant not granted early");
        Equal(0L, resumed.PendingIncome(rimal, "industry", resumed.State.lastSeenUtc), "Harvested industry not regranted");
        Ok(resumed.CollectIncome(rimal, "farm", farm.finishUtc));
        Fail(resumed.CollectIncome(rimal, "farm", farm.finishUtc));

        foreach (int frontier in new[] { 5, 10 })
        {
            var legacy = LegacyFixture(frontier);
            var projects = legacy.districts[frontier].projects;
            long baseline = legacy.lastSeenUtc - 30000;
            projects[1].completed = true;
            projects[1].startedUtc = baseline;
            projects[1].finishUtc = baseline + 5400;
            projects[1].lastIncomeUtc = projects[1].finishUtc;
            projects[5].completed = true;
            projects[5].startedUtc = projects[1].finishUtc;
            projects[5].finishUtc = projects[5].startedUtc + 10800;
            projects[5].lastIncomeUtc = projects[5].finishUtc;
            projects[7].startedUtc = legacy.lastSeenUtc;
            projects[7].finishUtc = legacy.lastSeenUtc + (frontier == 10 ? 14400 : 3600);
            var building = new EconomyService(GameStateMigration.Upgrade(legacy));
            int target = DistrictIndex(OldIds[frontier]);
            Equal(Json(projects), Json(building.State.districts[target].projects), "In-flight ordinary/final commerce preserves exact v1 construction deadline");
            Equal(0L, building.PendingIncome(target, "commerce", legacy.lastSeenUtc), "No income granted before commercial construction");
            building.Tick(projects[7].finishUtc + 3599);
            Equal(0L, building.PendingIncome(target, "commerce", building.State.lastSeenUtc), "Commercial clock remains anchored to old deadline");
            Ok(building.CollectIncome(target, "commerce", projects[7].finishUtc + 3600));
            Fail(building.CollectIncome(target, "commerce", projects[7].finishUtc + 3600));
        }
    }

    private static void FinishDistrict(EconomyService economy, int district)
    {
        // Public actions only: no free inserted projects, rubble or completion.
        for (int i = economy.State.districts[district].clearedLoads; i < GameCatalog.Districts[district].rubbleLoads; i++)
        {
            Ok(economy.StartSalvage(district, economy.State.lastSeenUtc));
            FinishJob(economy);
        }
        foreach (var project in GameCatalog.Districts[district].projects)
            if (!economy.FindProject(district, project.id).completed) CompleteProject(economy, district, project.id);
    }

    private static void ExpandedProgressionAndFinale()
    {
        int daraj = DistrictIndex("daraj"), nasr = DistrictIndex("nasr"), rashid = GameCatalog.FinalDistrictIndex;
        foreach (int claims in new[] { 5, 9, 10, 11 })
        {
            var old = LegacyFixture(claims);
            var e = new EconomyService(GameStateMigration.Upgrade(old));
            long historical = e.State.cityCompletedUtc;
            if (claims >= 10)
            {
                Ok(e.SelectDistrict(rashid)); // Legacy final access survives expansion.
                if (claims == 10) FinishDistrict(e, rashid);
                long balance = e.State.coins;
                Fail(e.ClaimDistrictReward(rashid, e.State.lastSeenUtc));
                Equal(balance, e.State.coins, "No final reward before all twelve (or twice)");
            }
            FinishDistrict(e, daraj);
            Check(!e.State.districts[nasr].unlocked, "Building alone never opens Nasr");
            long before = e.State.coins;
            Ok(e.ClaimDistrictReward(daraj, e.State.lastSeenUtc));
            Equal(before + GameCatalog.Districts[daraj].completionReward, e.State.coins, "Only earned inserted reward paid");
            Check(e.State.districts[nasr].unlocked, "Claim traverses old claimed Karama/Old City to open Nasr");
            Check(!e.CityComplete, "One inserted reward is insufficient");
            var reload = GameStateMigration.Upgrade(JsonSerializer.Deserialize<GameState>(Json(e.State), JsonOptions));
            e = new EconomyService(reload);
            FinishDistrict(e, nasr);
            Ok(e.ClaimDistrictReward(nasr, e.State.lastSeenUtc));
            if (claims == 9)
            {
                Check(!e.State.districts[rashid].unlocked, "Non-grandfathered Rashid still waits for last old neighborhood");
                int last = DistrictIndex("sheikh-ijlin");
                FinishDistrict(e, last);
                Ok(e.ClaimDistrictReward(last, e.State.lastSeenUtc));
                Check(e.State.districts[rashid].unlocked, "All twelve now open new Rashid access");
                FinishDistrict(e, rashid);
            }
            if (claims == 9 || claims == 10)
            {
                before = e.State.coins;
                Ok(e.ClaimDistrictReward(rashid, e.State.lastSeenUtc));
                Equal(before + 1000000, e.State.coins, "Unclaimed old final paid once only after all twelve");
            }
            if (claims >= 9)
            {
                Check(e.CityComplete, "Expanded city complete only when every current reward is claimed");
                Check(e.State.cityCompletedUtc > 0, "Completion history recorded");
                if (claims == 11) Equal(historical, e.State.cityCompletedUtc, "Original city completion history never overwritten");
                before = e.State.coins;
                Fail(e.ClaimDistrictReward(rashid, e.State.lastSeenUtc));
                Equal(before, e.State.coins, "Expansion never reawards already claimed million");
            }
            else Check(!e.CityComplete, "Remaining old neighborhoods still require real work");
        }
    }

    private static void LegacyCorruption()
    {
        var corruptions = new Action<GameState>[]
        {
            s => s.version = 0,
            s => s.version = 3,
            s => s.version = 2, // Cannot relabel an eleven-district file as current.
            s => s.coins = -1,
            s => s.stock = null,
            s => s.stock.other = -1,
            s => s.factoryLevel = 6,
            s => s.trucks = 1001,
            s => s.equipmentLevel = 0,
            s => s.excavators = 0, // Upgraded equipment cannot exist without the purchased fleet.
            s => s.lastGiftUtc = s.lastSeenUtc + 1,
            s => s.cityCompletedUtc = s.lastSeenUtc - 1, // No final reward.
            s => s.selectedDistrict = 11,
            s => s.selectedDistrict = 10, // Locked selection.
            s => s.districts = new DistrictState[13],
            s => s.districts[2] = null,
            s => s.districts[0].projects[0] = null,
            s => s.districts[0].projects[0].id = "forged",
            s => s.districts[0].projects[0].finishUtc++,
            s => s.districts[0].projects[0].lastIncomeUtc = s.lastSeenUtc + 1,
            s => s.districts[0].projects[0].lastIncomeUtc++, // Water cannot earn hourly income.
            s => s.districts[0].projects[0].completed = false,
            s => s.districts[0].clearedLoads = 3,
            s => s.districts[4].unlocked = true, // Would appear close to insertion; still invalid v1.
            s => { s.districts[3].unlocked = false; s.selectedDistrict = 0; }, // Earned frontier must already be open.
            s => s.districts[10].clearedLoads = 1,
            s => s.districts[10].projects[6].completed = true,
            s => s.districts[3].id = "karama",
            s => s.districts[3].legacyAccess = true,
            s => s.jobDistrict = 11,
            s => s.jobStage = (JobStage)99,
            s => s.jobFinishUtc = 123, // Idle deadline impossible.
            s => { s.jobStage = JobStage.Clearing; s.jobFinishUtc = s.lastSeenUtc + 20; s.jobDistrict = 10; },
            s => { s.jobStage = JobStage.Clearing; s.jobFinishUtc = s.lastSeenUtc + 20; s.factoryLevel = 0; }
        };
        Throws(() => GameStateMigration.Upgrade(null));
        foreach (var corrupt in corruptions)
        {
            var legacy = LegacyFixture(3);
            corrupt(legacy);
            string before = Json(legacy);
            Throws(() => GameStateMigration.Upgrade(legacy));
            Equal(before, Json(legacy), "Rejected legacy data is never rewritten");
        }
        // Frozen final commerce duration differs from ordinary shops.
        var final = LegacyFixture(11);
        final.districts[10].projects[7].finishUtc = final.districts[10].projects[7].startedUtc + 3600;
        Throws(() => GameStateMigration.Upgrade(final));
        var current = GameStateMigration.Upgrade(LegacyFixture(11));
        current.districts[DistrictIndex("daraj")].legacyAccess = true;
        Throws(() => GameStateMigration.Upgrade(current));
        current = GameStateMigration.Upgrade(LegacyFixture(3));
        current.districts[DistrictIndex("sabra")].legacyAccess = true;
        current.districts[DistrictIndex("sabra")].unlocked = true;
        Throws(() => GameStateMigration.Upgrade(current));
        current = GameCatalog.CreateNew(Epoch);
        current.districts[0].id = "tuffah";
        Throws(() => GameStateMigration.Upgrade(current));
    }

    private static void RoadImprovements()
    {
        var state = GameCatalog.CreateNew(Epoch);
        state.roadSegments = null;
        var economy = new EconomyService(state);
        Check(economy.State.roadSegments != null && economy.State.roadSegments.Length == 0,
            "Missing additive road state initializes without changing old progress");
        var definitions = new[]
        {
            new RoadSegmentDefinition("12345:0", "Road A", 250f),
            new RoadSegmentDefinition("12345:1", "Road B", 1000.01f)
        };
        economy.RegisterRoadSegments(definitions);
        Equal(750L, RoadEconomy.GetCost(definitions[0], 1).coins, "Fractional kilometre repair cost");
        var upgrade = RoadEconomy.GetCost(definitions[0], 2);
        Equal(2000L, upgrade.coins, "Fractional kilometre upgrade coins");
        Equal(3, upgrade.concrete, "Fractional kilometre concrete rounds up");
        Equal(1, upgrade.iron, "Fractional kilometre iron rounds up");
        Equal(0.5f, RoadEconomy.SpeedMultiplier(0), "Road level zero speed");
        Equal(1f, RoadEconomy.SpeedMultiplier(1), "Road level one speed");
        Equal(2f, RoadEconomy.SpeedMultiplier(2), "Road level two speed");

        economy.State.coins = 0;
        long coins = economy.State.coins;
        var stockBefore = Json(economy.State.stock);
        Fail(economy.ImproveRoad("999:0", 1));
        Fail(economy.ImproveRoad("12345:0", 2));
        Fail(economy.ImproveRoad("12345:0", 1)); // Cannot afford.
        Equal(coins, economy.State.coins, "Unknown, skipped, and unaffordable improvements are atomic");
        Equal(stockBefore, Json(economy.State.stock), "Unaffordable improvement consumes no materials");
        economy.State.coins = long.MaxValue;
        economy.State.stock.concrete = 10;
        economy.State.stock.iron = 10;
        coins = economy.State.coins;
        Ok(economy.ImproveRoad("12345:0", 1));
        Equal(coins - 750, economy.State.coins, "Repair debited exactly once");
        Equal(1, RoadEconomy.GetLevel(economy.State, "12345:0"), "Repair level persisted in state");
        Fail(economy.ImproveRoad("12345:0", 1));
        Fail(economy.ImproveRoad("12345:0", 0));
        coins = economy.State.coins;
        economy.State.stock.concrete = 0;
        economy.State.stock.iron = 0;
        Fail(economy.ImproveRoad("12345:0", 2));
        Equal(coins, economy.State.coins, "Missing upgrade materials do not charge currency");
        Equal(0, economy.State.stock.concrete, "Missing materials remain untouched");
        economy.State.stock.concrete = 10;
        economy.State.stock.iron = 10;
        Ok(economy.ImproveRoad("12345:0", 2));
        Equal(2, RoadEconomy.GetLevel(economy.State, "12345:0"), "Upgrade follows repair");
        Equal(7, economy.State.stock.concrete, "Upgrade concrete debited exactly");
        Equal(9, economy.State.stock.iron, "Upgrade iron debited exactly");
        string serialized = Json(economy.State);
        var reloaded = new EconomyService(System.Text.Json.JsonSerializer.Deserialize<GameState>(serialized, JsonOptions));
        Equal(2, RoadEconomy.GetLevel(reloaded.State, "12345:0"), "Road level survives serialization");
        Equal(serialized, Json(GameStateMigration.Upgrade(reloaded.State)), "Version two migration preserves road progress");

        var legacy = LegacyFixture(3);
        legacy.roadSegments = null;
        var migrated = GameStateMigration.Upgrade(legacy);
        Check(migrated.roadSegments != null && migrated.roadSegments.Length == 0,
            "Legacy migration adds empty road state without resetting earned district progress");
        Equal(legacy.coins, migrated.coins, "Additive road migration preserves old currency");
        Equal(Json(legacy.stock), Json(migrated.stock), "Additive road migration preserves old materials");
        var roads = new[]
        {
            new RoadSegmentDefinition("12345:0", "A", 100),
            new RoadSegmentDefinition("12345:0", "Duplicate", 200)
        };
        ThrowsRegistration(() => new EconomyService(GameCatalog.CreateNew(Epoch)).RegisterRoadSegments(roads));
        foreach (string id in new[] { "0:0", "01:0", "123:01", "123:-1", "123:0:1", " 123:0" })
            ThrowsRegistration(() => new EconomyService(GameCatalog.CreateNew(Epoch))
                .RegisterRoadSegments(new[] { new RoadSegmentDefinition(id, "Invalid", 100) }));
        foreach (float length in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 1000.02f })
            ThrowsRegistration(() => new EconomyService(GameCatalog.CreateNew(Epoch))
                .RegisterRoadSegments(new[] { new RoadSegmentDefinition("123:0", "Invalid", length) }));
        foreach (Action<GameState> corrupt in new Action<GameState>[]
        {
            s => s.roadSegments = new[] { new RoadSegmentState { id = "bad", level = 1 } },
            s => s.roadSegments = new[] { new RoadSegmentState { id = "123:0", level = -1 } },
            s => s.roadSegments = new[] { new RoadSegmentState { id = "123:0", level = 3 } },
            s => s.roadSegments = new[] { new RoadSegmentState { id = "123:0", level = 1 }, new RoadSegmentState { id = "123:0", level = 2 } },
            s => s.roadSegments = new RoadSegmentState[] { null }
        })
        {
            var malformed = GameCatalog.CreateNew(Epoch);
            corrupt(malformed);
            Throws(() => new EconomyService(malformed));
        }
        var once = new EconomyService(GameCatalog.CreateNew(Epoch));
        once.RegisterRoadSegments(new RoadSegmentDefinition[0]);
        ThrowsRegistration(() => once.RegisterRoadSegments(new RoadSegmentDefinition[0]));
    }
}