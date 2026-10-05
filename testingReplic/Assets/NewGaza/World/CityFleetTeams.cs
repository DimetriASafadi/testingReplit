using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>One independently animated trio per reservation/ownership slot.</summary>
    public sealed class CityFleetTeams : MonoBehaviour
    {
        private sealed class Team
        {
            internal CityFleet fleet;
            internal GameState view;
            internal RubbleDispatchState job;
            internal string depotId;
        }
        private readonly List<Team> teams = new List<Team>();
        private CityGeometry geometry;
        private Material yellow, glass, dark, iron, teal, rubble;
        private CityRoadNetwork roads;
        private Func<string, float> roadSpeed;
        private Func<Vector3, bool> paved;
        private Func<string, Vector3> workPoint, depotPoint;
        internal void Initialize(CityFleet first, CityGeometry geom, Material y, Material g,
            Material d, Material i, Material t, Material r, CityRoadNetwork network,
            Func<string, float> speed, Func<Vector3, bool> surface,
            Func<string, Vector3> work, Func<string, Vector3> depot)
        {
            geometry = geom; yellow = y; glass = g; dark = d; iron = i; teal = t; rubble = r;
            roads = network; roadSpeed = speed; paved = surface; workPoint = work; depotPoint = depot;
            teams.Add(new Team { fleet = first });
        }
        public CityFleet ForJob(RubbleDispatchState job) =>
            job != null && job.slot < teams.Count ? teams[job.slot].fleet : null;
        public CityFleet ForSite(string id)
        {
            foreach (var team in teams) if (team.job?.siteId == id) return team.fleet;
            return teams[0].fleet;
        }
        public int Count => teams.Count;
        public CityFleet At(int index) => teams[index].fleet;
        public void Refresh(GameState state, Vector3 defaultWork, Vector3 defaultDepot)
        {
            int count = Math.Max(1, Math.Max(state.excavators, Math.Max(state.trucks, state.bulldozers)));
            while (teams.Count < count)
            {
                var fleet = new GameObject("Independent crew • " + (teams.Count + 1)).AddComponent<CityFleet>();
                fleet.transform.SetParent(transform.parent, false);
                fleet.Initialize(geometry, yellow, glass, dark, iron, teal, rubble);
                fleet.ConfigureRoads(roads, roadSpeed);
                fleet.ConfigureTravelSurface(paved);
                teams.Add(new Team { fleet = fleet });
            }
            var jobs = state.development?.dispatches ?? Array.Empty<RubbleDispatchState>();
            for (int index = 0; index < teams.Count; index++)
            {
                Team team = teams[index];
                var job = Array.Find(jobs, j => j.slot == index && j.stage != JobStage.Recycling);
                bool legacy = index == 0 && state.jobStage != JobStage.Idle;
                Vector3 work = job != null ? workPoint(job.siteId) : defaultWork;
                if (job != null) team.depotId = job.depotId;
                Vector3 depot = job != null || team.depotId != null ? depotPoint(team.depotId) : defaultDepot;
                depot += new Vector3(index * .55f, 0, 0);
                // This is a presentation view, never the mutable authoritative economy.
                team.view = new GameState {
                    excavators = index < state.excavators ? 1 : 0,
                    trucks = index < state.trucks ? 1 : 0,
                    bulldozers = index < state.bulldozers ? 1 : 0,
                    districts = state.districts,
                    jobDistrict = job != null ? Array.Find(state.development.rubble, s => s.id == job.siteId).district :
                        legacy ? state.jobDistrict : -1,
                    jobStage = job?.stage ?? (legacy ? state.jobStage : JobStage.Idle),
                    development = new CityDevelopmentState {
                        activeRubbleId = job?.siteId ?? (legacy ? state.development?.activeRubbleId : null),
                        dispatchDepotId = job?.depotId ?? (legacy ? state.development?.dispatchDepotId : null)
                    }
                };
                // A genuinely free trio may start at the chosen depot. Never reset
                // an existing/outbound/returning reservation's moving transforms.
                if (job != null && team.job?.id != job.id) team.fleet.BeginDepotDispatch();
                team.job = job;
                team.fleet.Refresh(team.view, work, depot);
            }
        }
        public void Capture(GameState state)
        {
            if (state.jobStage != JobStage.Idle) teams[0].fleet.CaptureSave(state);
            foreach (var team in teams)
                if (team.job != null)
                {
                    team.fleet.CaptureSave(team.view);
                    team.job.fleet = team.view.fleet;
                }
        }
        public void Restore(GameState state)
        {
            if (state.jobStage != JobStage.Idle) teams[0].fleet.RestoreSave(state);
            foreach (var team in teams)
            {
                var saved = team.job?.fleet;
                if (saved == null)
                {
                    if (team.job?.stage == JobStage.Hauling) team.fleet.RestoreReturnWithoutSnapshot();
                    continue;
                }
                // Offline work completion changes the phase, not the saved position.
                team.view.fleet = new FleetSaveState {
                    district = team.view.jobDistrict, stage = team.view.jobStage,
                    rubbleId = team.job.siteId, depotId = team.job.depotId,
                    excavator = saved.excavator, truck = saved.truck, bulldozer = saved.bulldozer
                };
                team.fleet.RestoreSave(team.view);
            }
        }
    }
}
