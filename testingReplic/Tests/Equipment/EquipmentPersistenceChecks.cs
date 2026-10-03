using System;
using System.Reflection;
using NewGaza;
using NewGaza.Core;
using UnityEngine;

internal static class EquipmentPersistenceChecks
{
    private static int checks;

    internal static int Run(CityGeometry geometry, Material material, CityRoadNetwork roads,
        Vector3 job, Vector3 depot)
    {
        checks = 0;
        var state = new GameState {
            excavators = 1, trucks = 1, bulldozers = 1,
            jobDistrict = 0, jobStage = JobStage.Clearing,
            districts = new[] { new DistrictState() },
            development = new CityDevelopmentState { activeRubbleId = "fixture-site", dispatchDepotId = "central" }
        };
        var original = Create(geometry, material, roads, state, job, depot);
        for (int i = 0; i < 100; i++) Update(original, .1f);
        Check(!original.WorkCrewReady, "Short dispatch must still be traveling");
        original.CaptureSave(state);
        var saved = state.fleet;
        Check(saved.rubbleId == "fixture-site" && saved.depotId == "central", "Snapshot bound to contract");
        Check(Vector3.Distance(Root(original, "excavator").localPosition,
            new Vector3(saved.excavator.x, saved.excavator.y, saved.excavator.z)) < .00001f,
            "Capture actual excavator position");
        Check(Vector3.Distance(Root(original, "truck").localPosition,
            new Vector3(saved.truck.x, saved.truck.y, saved.truck.z)) < .00001f,
            "Capture actual truck position");
        var resumed = Create(geometry, material, roads, state, job, depot);
        resumed.RestoreSave(state);
        foreach (string name in new[] { "excavator", "truck", "bulldozer" })
        {
            Check(Vector3.Distance(Root(original, name).localPosition, Root(resumed, name).localPosition) < .00001f,
                name + " restores without restarting at depot");
        }
        Update(resumed, .05f);
        foreach (string name in new[] { "excavator", "truck", "bulldozer" })
        {
            Check(Vector3.Distance(Root(original, name).localPosition, Root(resumed, name).localPosition) < .03f,
                name + " resumes continuously without teleport");
        }
        Check(!resumed.WorkCrewReady, "Loading never fabricates physical arrival");
        Vector3 truckBefore = Root(resumed, "truck").localPosition;
        for (int i = 0; i < 20; i++) Update(resumed, .1f);
        Check(Vector3.Distance(truckBefore, Root(resumed, "truck").localPosition) > .01f,
            "Resumed truck continues along replanned route");
        state.jobStage = JobStage.Idle;
        resumed.Refresh(state, job, depot);
        Vector3 afterOfflineTransition = Root(resumed, "truck").localPosition;
        resumed.RestoreSave(state);
        Check(Vector3.Distance(afterOfflineTransition, Root(resumed, "truck").localPosition) < .00001f,
            "Offline transition rejects stale clearing snapshot");
        state.jobStage = JobStage.Clearing;
        state.development.activeRubbleId = "different-site";
        resumed.Refresh(state, job, depot);
        Vector3 afterNewContract = Root(resumed, "truck").localPosition;
        resumed.RestoreSave(state);
        Check(Vector3.Distance(afterNewContract, Root(resumed, "truck").localPosition) < .00001f,
            "Different contract rejects old poses");
        return checks;
    }

    private static CityFleet Create(CityGeometry geometry, Material material, CityRoadNetwork roads,
        GameState state, Vector3 job, Vector3 depot)
    {
        var fleet = new GameObject("Persistence fixture fleet").AddComponent<CityFleet>();
        fleet.Initialize(geometry, material, material, material, material, material, material);
        fleet.ConfigureRoads(roads, _ => 1f);
        fleet.Refresh(state, job, depot);
        return fleet;
    }

    private static Transform Root(CityFleet fleet, string name) => (Transform)typeof(CityFleet)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fleet);
    private static void Update(CityFleet fleet, float delta)
    {
        Time.deltaTime = delta;
        typeof(CityFleet).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fleet, null);
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Equipment persistence: " + message);
    }
}