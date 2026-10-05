using System;
using System.Collections.Generic;
using System.Reflection;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

internal static class EquipmentTeamsAndCornersChecks
{
    internal static int Run(CityGeometry geometry, Material material, CityRoadNetwork roads)
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException(message);
        }
        var route = new CityRoadRoute(new List<Vector3> {
            Vector3.zero, new Vector3(0, 0, 1), new Vector3(1, 0, 1)
        }, new List<string> { "incoming", "outgoing" });
        Check(Vector3.Distance(route.PositionAtDistance(0), Vector3.zero) < .00001f &&
            Vector3.Distance(route.PositionAtDistance(2), new Vector3(1, 0, 1)) < .00001f,
            "Rounded routes retain exact endpoints.");
        Vector3 previous = route.DirectionAtDistance(.879f);
        for (float d = .88f; d <= 1.121f; d += .002f)
        {
            Vector3 direction = route.DirectionAtDistance(d);
            Check(Vector3.Distance(previous, direction) < .025f, "Junction heading changes continuously, not by 90 degrees.");
            Vector3 cornerPoint = route.PositionAtDistance(d);
            Check(cornerPoint.x >= 0 && cornerPoint.x <= .121f && cornerPoint.z <= 1 && cornerPoint.z >= .879f,
                "Corner stays inside a small bounded junction region.");
            previous = direction;
        }
        Check(route.RoadIdAtDistance(.99f) == "incoming" && route.RoadIdAtDistance(1.01f) == "outgoing",
            "Road source IDs and upgrade lookup survive rounding.");
        Check(route.TurnSpeedLimit(1f, 45f) < .1f && route.TurnSpeedLimit(.5f, 45f) == float.MaxValue,
            "Tight junctions slow the vehicle while straight roads retain upgraded speed.");
        var root = new GameObject("Concurrent production fleets regression");
        var first = new GameObject("First trio").AddComponent<CityFleet>();
        first.transform.SetParent(root.transform, false);
        first.Initialize(geometry, material, material, material, material, material, material);
        first.ConfigureRoads(roads, id => 1f);
        var manager = root.AddComponent<CityFleetTeams>();
        Vector3 Work(string id) => id == "a" ? new Vector3(.7f, -.09f, 1f) : new Vector3(-.7f, -.09f, .8f);
        var depot = new Vector3(0, -.09f, 0);
        manager.Initialize(first, geometry, material, material, material, material, material, material,
            roads, id => 1f, point => false, Work, id => depot);
        var a = new RubbleDispatchState { id = "dispatch-a", siteId = "a", depotId = "central", slot = 0 };
        var b = new RubbleDispatchState { id = "dispatch-b", siteId = "b", depotId = "central", slot = 1 };
        var state = new GameState {
            excavators = 2, trucks = 2, bulldozers = 2, jobStage = JobStage.Idle, jobDistrict = -1,
            districts = new[] { new DistrictState() },
            development = new CityDevelopmentState {
                rubble = new[] { new RubbleSiteState { id = "a" }, new RubbleSiteState { id = "b" } },
                dispatches = new[] { a, b }
            }
        };
        manager.Refresh(state, Work("a"), depot);
        Check(manager.Count == 2 && manager.ForJob(a) != manager.ForJob(b), "Two dispatches use two real independent model trios.");
        void Step()
        {
            Time.deltaTime = .05f;
            for (int i = 0; i < manager.Count; i++)
                typeof(CityFleet).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager.At(i), null);
        }
        int frames = 0;
        while ((!manager.ForJob(a).WorkCrewReady || !manager.ForJob(b).WorkCrewReady) && frames++ < 8000)
        {
            var roots = new[] { Root(manager.At(0), "excavator"), Root(manager.At(1), "bulldozer") };
            var forward = new[] { roots[0].localRotation * Vector3.forward, roots[1].localRotation * Vector3.forward };
            Step();
            for (int i = 0; i < roots.Length; i++)
                Check(Vector3.Distance(forward[i], roots[i].localRotation * Vector3.forward) <= .053f,
                    "Tracked vehicle yaw is bounded to 60 degrees/second, including work docking.");
        }
        Check(manager.ForJob(a).WorkCrewReady && manager.ForJob(b).WorkCrewReady, "Both independent crews really reach their own sites.");
        a.stage = JobStage.Hauling;
        manager.Refresh(state, Work("a"), depot);
        for (int i = 0; i < 100; i++) Step();
        Check(manager.ForJob(b).WorkCrewReady && !manager.ForJob(a).WorkCrewReady,
            "One site can return while the other crew continues its working animation.");
        manager.Capture(state);
        var old = a.fleet.excavator;
        var point = Root(manager.ForJob(a), "excavator").localPosition;
        Check(Vector3.Distance(point, new Vector3(old.x, old.y, old.z)) < .00001f &&
            a.fleet != b.fleet, "Each crew stores its own actual moving roots.");
        // Offline work completion must not discard the saved position and pretend
        // the crew already arrived home. Restore replans from that saved position.
        b.stage = JobStage.Hauling;
        manager.Refresh(state, Work("a"), depot);
        var savedPoint = new Vector3(b.fleet.excavator.x, b.fleet.excavator.y, b.fleet.excavator.z);
        manager.Restore(state);
        Check(Vector3.Distance(Root(manager.ForJob(b), "excavator").localPosition, savedPoint) < .00001f &&
            !manager.ForJob(b).DepotCrewReady, "Phase changes on reload preserve positions and the physical return gate.");
        frames = 0;
        while ((!manager.ForJob(a).DepotCrewReady || !manager.ForJob(b).DepotCrewReady) && frames++ < 8000) Step();
        Check(manager.ForJob(a).DepotCrewReady && manager.ForJob(b).DepotCrewReady, "Both saved crews finish a real independent return.");
        UnityEngine.Object.Destroy(root);
        return checks;
    }
    private static Transform Root(CityFleet fleet, string field) =>
        (Transform)typeof(CityFleet).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fleet);
}
