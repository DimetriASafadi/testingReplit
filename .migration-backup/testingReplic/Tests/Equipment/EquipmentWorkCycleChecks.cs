using System;
using System.Reflection;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

internal static class EquipmentWorkCycleChecks
{
    private static int assertions;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        assertions = 0;
        Resources.RootDirectory = AppContext.BaseDirectory;
        var roads = new CityRoadNetwork(CityBasemap.LoadFromResources());
        using (var geometry = new CityGeometry())
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            CheckCycle(geometry, material, roads, false);
            CheckCycle(geometry, material, roads, true);
            assertions += EquipmentTeamsAndCornersChecks.Run(geometry, material, roads);
            UnityEngine.Object.Destroy(material);
        }
        return assertions;
    }

    private static void CheckCycle(CityGeometry geometry, Material material,
        CityRoadNetwork roads, bool missingRoad)
    {
        var root = new GameObject("Production building work-cycle regression");
        var fleet = root.AddComponent<CityFleet>();
        fleet.Initialize(geometry, material, material, material, material, material, material);
        fleet.ConfigureRoads(roads, id => 1f);
        var depot = new Vector3(0f, -.09f, 0f);
        var job = new Vector3(.7f, -.09f, .9f);
        var state = new GameState {
            excavators = 1, bulldozers = 1, trucks = 1,
            jobStage = JobStage.Idle, jobDistrict = -1,
            districts = new[] { new DistrictState() },
            development = new CityDevelopmentState { initialized = true }
        };
        fleet.Refresh(state, job, depot);
        state.jobStage = JobStage.Clearing;
        state.jobDistrict = 0;
        state.development.activeRubbleId = "building-under-test";
        fleet.Refresh(state, job, depot);
        var excavator = (Transform)Get(fleet, "excavator");
        var bulldozer = (Transform)Get(fleet, "bulldozer");
        var truck = (Transform)Get(fleet, "truck");
        var initialTruck = truck.localPosition;
        if (missingRoad)
        {
            // Exercise the real BeginInbound fallback, not a fabricated arrival.
            Set(fleet, "roadTripRoute", null);
            var route = (Vector3[])Get(fleet, "tripRoute");
            Set(fleet, "tripRouteLength", Vector3.Distance(route[0], route[1]));
            Call(fleet, "BeginInbound");
            Check(Get(fleet, "truckTripState").ToString() == "Inbound",
                "Absent road must not leave the truck permanently parked.");
        }
        else
        {
            // Reproduce a queued retarget while a previous plan is still active.
            job = new Vector3(.9f, -.09f, 1.1f);
            fleet.Refresh(state, job, depot);
            Check((bool)Get(fleet, "pendingRouteRebuild"), "Fixture queues a real destination change.");
        }
        int frames = 0;
        while (!fleet.WorkCrewReady && frames++ < 8000)
        {
            Step(fleet);
            if (!fleet.WorkCrewReady)
                Check(Math.Abs((float)Get(fleet, "phase")) < .001f,
                    "Digging clock must stay stopped until the full crew docks.");
        }
        Check(fleet.WorkCrewReady, "All three vehicles must arrive; no repeating dock-turn/replan loop.");
        Check(Vector3.Distance(initialTruck, truck.localPosition) > .1f,
            "Truck must physically travel rather than report an artificial arrival.");
        Check(!(bool)Get(fleet, "pendingRouteRebuild"), "Arrival consumes the queued destination change.");
        Check(Vector3.Distance(excavator.localPosition, (Vector3)Get(fleet, "excavatorRoadTarget")) < .001f &&
            Vector3.Distance(bulldozer.localPosition, (Vector3)Get(fleet, "bulldozerRoadTarget")) < .03f,
            "Tracked vehicles arrive at the selected building's actual work targets.");
        var turret = (Transform)Get(fleet, "turret");
        Vector3 initialTurret = turret.localRotation * Vector3.forward;
        bool articulated = false;
        for (int frame = 0; frame < 1200; frame++)
        {
            Step(fleet);
            articulated |= Vector3.Distance(turret.localRotation * Vector3.forward, initialTurret) > .01f;
            Check(Get(fleet, "truckTripState").ToString() == "ParkedAtWork",
                "Building task keeps the receiving truck docked during its complete working minute.");
            Check(fleet.WorkCrewReady, "Working crew remains at the building throughout the animation.");
        }
        Check(articulated, "The production excavator joints animate while working.");
        Check((float)Get(fleet, "phase") >= 59.9f,
            "Docked animation progresses through a full 60-second work interval.");
        state.jobStage = JobStage.Hauling;
        state.development.crewArrived = true;
        fleet.Refresh(state, job, depot);
        frames = 0;
        while (!fleet.DepotCrewReady && frames++ < 8000) Step(fleet);
        Check(fleet.DepotCrewReady, "Full crew physically returns to the factory and finishes unloading.");
        Check(Get(fleet, "truckTripState").ToString() == "ParkedAtDepot",
            "Truck stays at the factory after the building task instead of repeating the delivery.");
        Check(Vector3.Distance(excavator.localPosition, bulldozer.localPosition) > .4f &&
            Vector3.Distance(excavator.localPosition, truck.localPosition) > .2f &&
            Vector3.Distance(bulldozer.localPosition, truck.localPosition) > .2f,
            "Factory parking slots keep all three vehicle roots separate.");
        for (int frame = 0; frame < 100; frame++) Step(fleet);
        Check(fleet.DepotCrewReady, "Returned crew remains parked rather than restarting its route.");
        UnityEngine.Object.Destroy(root);
    }

    private static void Step(CityFleet fleet)
    {
        Time.deltaTime = .05f;
        Call(fleet, "Update");
    }
    private static object Get(object target, string name) =>
        target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Call(object target, string name) =>
        target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
