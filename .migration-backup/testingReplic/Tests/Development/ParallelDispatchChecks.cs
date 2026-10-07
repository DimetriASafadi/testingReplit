using System;
using System.Linq;
using System.Text.Json;
using NewGaza.Core;

internal static class ParallelDispatchChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const long now = 1800000000;
        var state = GameCatalog.CreateNew(now);
        state.development.requiresPlacedFactory = false;
        state.factoryLevel = 1;
        state.excavators = state.trucks = state.bulldozers = 2;
        state.equipmentUnits = null;
        var economy = new EconomyService(state);
        var rules = new CityDevelopmentService(economy);
        string[] sites = Enumerable.Range(0, 4).Select(i => CityDevelopmentService.SiteId(0, i)).ToArray();
        bool firstArrived = false, secondArrived = false, firstReturned = false, secondReturned = false;
        economy.SiteCrewReady = j => j.slot == 0 ? firstArrived : secondArrived;
        economy.SiteDepotReady = j => j.slot == 0 ? firstReturned : secondReturned;
        check(rules.Clear(sites[0], "central", now).success && rules.Clear(sites[1], "central", now).success,
            "two complete spare trios can work at two distinct buildings concurrently");
        var a = rules.Data.dispatches[0]; var b = rules.Data.dispatches[1];
        check(a.excavatorId != b.excavatorId && a.truckId != b.truckId && a.bulldozerId != b.bulldozerId &&
            RubbleDispatches.AvailableTeams(state) == 0, "each job reserves exactly one distinct machine of each kind");
        check(!rules.Clear(sites[0], "central", now).success && !rules.Clear(sites[2], "central", now).success,
            "cannot duplicate a site job or allocate the same machines to a third site");
        economy.Tick(now + 100);
        check(!a.crewArrived && !b.crewArrived && !rules.Site(sites[0]).cleared, "travel never consumes working time");
        firstArrived = true; economy.Tick(now + 100);
        check(a.finishUtc == now + 160 && !b.crewArrived, "each team starts its own minute only at actual arrival");
        secondArrived = true; economy.Tick(now + 130);
        check(b.finishUtc == now + 190, "different arrival times produce independent deadlines");
        economy.Tick(now + 159);
        check(!rules.Site(sites[0]).cleared, "rubble cannot disappear before the full minute");
        economy.Tick(now + 160);
        check(rules.Site(sites[0]).cleared && !rules.Site(sites[1]).cleared && a.stage == JobStage.Hauling,
            "finished site immediately becomes clean while its crew starts returning");
        check(state.districts[0].clearedLoads == 1 && state.stock.concrete == 0 &&
            RubbleDispatches.AvailableTeams(state) == 0, "clean-land progress is distinct from cargo delivery and machine availability");
        check(rules.Build("small_house", 0, 40, 40, 0, state.lastSeenUtc,
            (d, region, x, z, turn) => rules.Site(sites[0]).cleared ? null : "unclean").success,
            "a clean site is eligible for construction before its machines reach the factory");
        check(!rules.Clear(sites[2], "central", state.lastSeenUtc).success,
            "a returning trio cannot be reused for another site");
        a.fleet = new FleetSaveState { district = 0, stage = JobStage.Hauling, rubbleId = a.siteId, depotId = a.depotId,
            excavator = new VehicleSaveState { x = 1 }, truck = new VehicleSaveState { x = 2 }, bulldozer = new VehicleSaveState { x = 3 } };
        var snapshot = GameStateSnapshot.Capture(state);
        a.fleet.truck.x = 4;
        check(snapshot.development.dispatches[0].fleet.truck.x == 2,
            "background-save snapshots detach each job and all its moving transforms");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var loaded = new EconomyService(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state, options), options));
        loaded.Tick(now + 10000);
        check(loaded.State.development.rubble[0].cleared && loaded.State.development.rubble[1].cleared &&
            RubbleDispatches.AvailableTeams(loaded.State) == 0 && loaded.State.development.dispatches.Length == 2,
            "reload/offline time preserves cleared sites and reservations; it cannot fabricate a safe factory arrival");
        firstReturned = true;
        economy.Tick(now + 190);
        check(rules.Site(sites[1]).cleared && RubbleDispatches.AvailableTeams(state) == 1 &&
            rules.Data.dispatches.Length == 1, "only the physically returned team becomes available");
        int delivered = state.stock.concrete;
        economy.Tick(state.lastSeenUtc);
        check(delivered > 0 && state.stock.concrete == delivered, "cargo is credited exactly once at return");
        firstArrived = false; firstReturned = false;
        check(rules.Clear(sites[2], "central", state.lastSeenUtc).success &&
            RubbleDispatches.ForSite(state, sites[2]).excavatorId == a.excavatorId,
            "a returned trio can immediately start a new site while the other trio is still returning");
        var events = CityActivityCatalog.Collect(state, state.lastSeenUtc);
        check(events.Exists(e => e.siteId == sites[1]) && events.Exists(e => e.siteId == sites[2]),
            "activities list independently identifies both concurrent crews and their map targets");
        state.stock.concrete = int.MaxValue; secondReturned = true;
        economy.Tick(state.lastSeenUtc);
        check(b.stage == JobStage.Recycling && RubbleDispatches.AvailableTeams(state) == 1,
            "full storage holds cargo but does not keep safely returned machines locked");
        check(rules.Clear(sites[3], "central", state.lastSeenUtc).success,
            "released machines may take another site while an old cargo batch awaits storage");
        new EconomyService(state); // Includes released IDs reused by a newer reservation.
        var corrupt = GameStateSnapshot.Capture(state);
        var active = Array.FindAll(corrupt.development.dispatches, j => j.stage != JobStage.Recycling);
        active[1].slot = active[0].slot;
        active[1].excavatorId = active[0].excavatorId; active[1].truckId = active[0].truckId; active[1].bulldozerId = active[0].bulldozerId;
        bool rejected = false;
        try { new EconomyService(corrupt); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "invalid overlapping reservations are rejected without resetting the campaign");
    }
}
