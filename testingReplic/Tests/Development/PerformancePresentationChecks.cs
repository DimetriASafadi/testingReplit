using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NewGaza.Core;

internal static class PerformancePresentationChecks
{
    private sealed class Request { internal int number; }
    internal static void Run(Action<bool, string> check)
    {
        var live = GameCatalog.CreateNew(200000);
        var rules = new CityDevelopmentService(new EconomyService(live));
        var sites = new RubbleSiteState[8000];
        for (int i = 0; i < sites.Length; i++)
            sites[i] = new RubbleSiteState { id = "background:dense-" + i, background = true,
                sourceBuildingId = "dense-" + i, district = 0, projectId = "housing",
                x = 20 + i % 100, z = 20 + i / 100, width = .5f, depth = .5f, height = .3f,
                buildingPrice = 5000 };
        rules.RegisterBackgroundSites(sites);
        live.camera = new CameraSaveState { x = 20, z = 20, zoom = 2, yaw = 30 };
        live.fleet = new FleetSaveState { excavator = new VehicleSaveState { x = 21 } };
        live.equipmentUnits = new[] { new EquipmentUnitState { id = "snapshot", kind = "truck", level = 1 } };
        live.roadSegments = new[] { new RoadSegmentState { id = "road", level = 0 } };
        live.development.buildings = new[] { new PlacedBuildingState { id = "building", x = 30 } };
        var watch = Stopwatch.StartNew();
        var snapshot = GameStateSnapshot.Capture(live);
        watch.Stop();
        Console.WriteLine("DENSE SNAPSHOT fixture: " + snapshot.development.rubble.Length +
            " sites copied in " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms (.NET fixture, not device timing).");
        long oldCoins = snapshot.coins;
        live.coins--; live.stock.wood++; live.camera.zoom = 100; live.fleet.excavator.x = 90;
        live.development.rubble[0].cleared = true; live.development.rubble[live.development.rubble.Length - 1].x = 777;
        live.development.buildings[0].x = 70; live.districts[0].projects[0].completed = true;
        live.equipmentUnits[0].level = 3; live.roadSegments[0].level = 2;
        check(snapshot.coins == oldCoins && snapshot.stock.wood != live.stock.wood &&
            snapshot.camera.zoom == 2 && snapshot.fleet.excavator.x == 21, "snapshot isolates currency, stock and presentation");
        check(!snapshot.development.rubble[0].cleared && snapshot.development.rubble[snapshot.development.rubble.Length - 1].x != 777 &&
            snapshot.development.buildings[0].x == 30 && !snapshot.districts[0].projects[0].completed &&
            snapshot.equipmentUnits[0].level == 1 && snapshot.roadSegments[0].level == 0,
            "snapshot deeply isolates every saved array, including 8000 background sites");
        var writes = new List<int>(); int active = 0, maximumActive = 0;
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var queue = new BackgroundSaveQueue<Request>(request =>
            {
                int count = Interlocked.Increment(ref active); maximumActive = Math.Max(maximumActive, count);
                if (request.number == 0) { entered.Set(); if (!release.Wait(5000)) throw new Exception("test writer release timeout"); }
                writes.Add(request.number); Interlocked.Decrement(ref active);
            });
            queue.Enqueue(new Request { number = 0 });
            check(entered.Wait(5000), "writer starts asynchronously without waiting for caller");
            for (int i = 1; i <= 200; i++) queue.Enqueue(new Request { number = i });
            release.Set(); queue.Flush();
            check(writes.Count == 2 && writes[0] == 0 && writes[1] == 200 && maximumActive == 1,
                "rapid zoom saves coalesce to latest snapshot, exactly one writer and no queue growth");
        }
        var recovering = new BackgroundSaveQueue<Request>(request => { if (request.number == 0) throw new InvalidOperationException("disk failure"); });
        recovering.Enqueue(new Request());
        bool rejected = false;
        try { recovering.Flush(); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && recovering.TakeFailure() != null && recovering.TakeFailure() == null,
            "background failure surfaces to lifecycle flush and main-thread notification, never silent");
        recovering.Enqueue(new Request { number = 1 }); recovering.Flush();
        check(recovering.TakeFailure() == null, "writer can recover and persist a later valid request");
        float previous = 0;
        for (int i = 0; i < 600; i++)
        {
            float opacity = FleetMarkerPresentation.Opacity(i / 100f);
            check(opacity >= previous && opacity >= 0 && opacity <= .951f, "overhead fade is monotonic and bounded");
            previous = opacity;
        }
        check(FleetMarkerPresentation.Opacity(.6f) == 0 && FleetMarkerPresentation.Opacity(1.25f) == 0 &&
            FleetMarkerPresentation.Opacity(3f) > 0 && FleetMarkerPresentation.Opacity(5f) > .94f &&
            FleetMarkerPresentation.Opacity(float.NaN) == 0, "close machinery is unobstructed; distant machinery has visible markers");
    }
}