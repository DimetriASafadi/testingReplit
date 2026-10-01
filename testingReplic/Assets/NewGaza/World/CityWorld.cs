using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>
    /// Deterministic, authored coastal diorama, not a geographic reconstruction.
    /// The session is the only owner of progress; this class is a disposable view.
    /// </summary>
    public sealed class CityWorld : MonoBehaviour
    {
        private sealed class PlotView
        {
            internal Transform anchor;
            internal GameObject visual;
            internal int stage = -1;
            internal Vector3 size;
            internal ProjectDefinition definition;
        }

        private sealed class DistrictView
        {
            internal Transform root;
            internal Vector3 center;
            internal PlotView[] plots;
            internal GameObject rubble;
            internal GameObject crane;
            internal GameObject badge;
            internal GameObject damagedPlots;
            internal GameObject finishedPlots;
            internal CitySelectable salvageHit;
            internal int rubbleRemaining = -1;
            internal bool unlocked;
            internal bool rewarded;
        }

        private GameSession session;
        private CityGeometry geometry;
        private Transform cityRoot;
        private DistrictView[] districts;
        private GameObject selection;
        private GameObject districtSelection;
        private GameObject factory;
        private GameObject factorySite;
        private CitySelectable factoryHit;
        private CityFleet fleet;
        private int selectedDistrict = -1;
        private int selectedPlot = -1;
        private int factoryLevel = -1;
        private bool finale;
        private Material sand, limestone, cream, terracotta, teal, glass, asphalt, sidewalk;
        private Material dark, iron, rubble, leaf, grass, yellow, white, water, sea, foam;
        private Material[] districtColors;

        /// <summary>World bounds approximately x[-63,42], z[-49,49], buildings up to y=9.</summary>
        public void Initialize(GameSession gameSession)
        {
            if (gameSession == null) throw new ArgumentNullException(nameof(gameSession));
            if (session != null) session.Changed -= Refresh;
            if (session != null) session.PlotSelected -= SetSelectedPlot;
            if (cityRoot != null)
            {
                cityRoot.gameObject.SetActive(false);
                Destroy(cityRoot.gameObject);
            }
            geometry?.Dispose();
            session = gameSession;
            geometry = new CityGeometry();
            MakePalette();
            cityRoot = new GameObject("New Gaza • coastal diorama").transform;
            cityRoot.SetParent(transform, false);
            BuildLandscape();
            BuildDistricts();
            BuildFactorySite();
            BuildSelection();
            fleet = new GameObject("Salvage fleet • articulated machines").AddComponent<CityFleet>();
            fleet.transform.SetParent(cityRoot, false);
            fleet.Initialize(geometry, yellow, glass, dark, iron, teal, rubble);
            session.Changed += Refresh;
            session.PlotSelected += SetSelectedPlot;
            selectedDistrict = -1;
            selectedPlot = -1;
            factoryLevel = -1;
            finale = false;
            Refresh();
        }

        private void MakePalette()
        {
            sand = geometry.Material("warm sand", Hex(0xDEC69B));
            limestone = geometry.Material("limestone", Hex(0xD6B893));
            cream = geometry.Material("ivory plaster", Hex(0xF4E7CE));
            terracotta = geometry.Material("terracotta", Hex(0xBC6950));
            teal = geometry.Material("petrol teal", Hex(0x277E80));
            glass = geometry.Material("deep blue glazing", Hex(0x214F62), .5f);
            asphalt = geometry.Material("blue slate streets", Hex(0x52676C));
            sidewalk = geometry.Material("pale sandstone paving", Hex(0xEDDFC4));
            dark = geometry.Material("charcoal rubber", Hex(0x293A40));
            iron = geometry.Material("steel", Hex(0x91A5A6), .35f);
            rubble = geometry.Material("dusty broken concrete", Hex(0xA69983));
            leaf = geometry.Material("palm green", Hex(0x3F8063));
            grass = geometry.Material("garden sage", Hex(0x8BA477));
            yellow = geometry.Material("construction saffron", Hex(0xEFB745));
            white = geometry.Material("road chalk", Hex(0xF9F1D9));
            water = geometry.Material("shallow turquoise", Hex(0x56C6BB), .5f);
            sea = geometry.Material("Mediterranean", Hex(0x268D9A), .6f);
            foam = geometry.Material("sea foam", Hex(0xBEEADF), .2f);
            districtColors = new[]
            {
                geometry.Material("district ochre", Hex(0xD69B64)),
                geometry.Material("district coral", Hex(0xD08068)),
                geometry.Material("district sage", Hex(0x91A47C)),
                geometry.Material("district blue", Hex(0x689EAA)),
                geometry.Material("district rose", Hex(0xBB8476)),
                geometry.Material("district mint", Hex(0x81B4A4)),
                geometry.Material("district gold", Hex(0xC7AA69)),
                geometry.Material("district slate", Hex(0x7B99A4)),
                geometry.Material("district olive", Hex(0xA1A66D)),
                geometry.Material("district clay", Hex(0xB7745A)),
                geometry.Material("Rashid turquoise", Hex(0x4FAAA3))
            };
        }

        private static Color Hex(int hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
        }

        private void BuildLandscape()
        {
            var land = new CityMeshBatch(geometry);
            land.Box(limestone, new Vector3(3f, -1.1f, 0f), new Vector3(78f, 2f, 98f));
            land.Box(sand, new Vector3(3f, -.06f, 0f), new Vector3(78f, .2f, 97.8f));
            land.Box(terracotta, new Vector3(3f, -1.95f, 0f), new Vector3(78f, .2f, 98f));
            land.Box(sea, new Vector3(-51f, -.45f, 0f), new Vector3(24f, .35f, 98f));
            land.Box(water, new Vector3(-40.2f, -.26f, 0f), new Vector3(5f, .12f, 98f));
            land.Box(sand, new Vector3(-37.8f, -.08f, 0f), new Vector3(4f, .22f, 98f));
            // The gently stepped shoreline is deliberately stylized, not a map.
            for (int i = 0; i < 16; i++)
            {
                float z = -45f + i * 6f;
                land.Box(foam, new Vector3(-39.1f - .23f * Mathf.Sin(i * 1.8f), -.17f, z),
                    new Vector3(.18f, .025f, 4.6f), 4f * Mathf.Sin(i));
                land.Box(water, new Vector3(-47f - i % 3 * 2.7f, -.245f, z + 1.4f),
                    new Vector3(3.2f, .02f, .08f), -12f);
            }
            land.Build("Layered sand plinth / sea / shoreline", cityRoot, Vector3.zero);
            var streets = new CityMeshBatch(geometry);
            Road(streets, new Vector3(-30.1f,.1f,0f), 3.7f, 95f, true);
            Road(streets, new Vector3(0f,.1f,0f), 3.8f, 95f, true);
            Road(streets, new Vector3(30.1f,.1f,0f), 3.7f, 95f, true);
            for (int row = 0; row <= 5; row++)
                Road(streets, new Vector3(0f,.1f,-47.5f + row * 19f), 3.7f, 62f, false);
            // Long corniche pedestrian ribbon, stone seawall and regularly spaced lamps.
            streets.Box(sidewalk, new Vector3(-33.6f,.13f,0f), new Vector3(2.2f,.24f,96f));
            streets.Box(limestone, new Vector3(-34.6f,.48f,0f), new Vector3(.25f,.65f,96f));
            for (int i = 0; i < 24; i++)
            {
                float z = -45.5f + i * 4f;
                Palm(streets, new Vector3(-32.9f,.22f,z), 2.5f + i % 3 * .25f, i * 41f);
                if (i % 2 == 0) Bench(streets, new Vector3(-34f,.26f,z + 1.3f), 90f);
                Lamp(streets, new Vector3(-28f,.25f,z + .9f), -90f);
                if (i % 3 == 0)
                {
                    Lamp(streets, new Vector3(2.3f,.25f,z), 90f);
                    Lamp(streets, new Vector3(32.4f,.25f,z), 90f);
                }
            }
            streets.Build("Connected roads / sidewalks / corniche", cityRoot, Vector3.zero);
            var harbor = new CityMeshBatch(geometry);
            harbor.Box(limestone, new Vector3(-43.8f,.05f,34f), new Vector3(10f,.6f,1.6f));
            for (int i = 0; i < 7; i++)
                harbor.Round(terracotta, new Vector3(-39.6f - i * 1.3f,-.75f,34f), new Vector3(.3f,1.4f,.3f));
            Boat(harbor, new Vector3(-48f,-.1f,29f), 20f, 1.2f);
            Boat(harbor, new Vector3(-53f,-.1f,-19f), -25f, .85f);
            // Seafront lookout, retained as scenery rather than an economic project.
            harbor.Round(sidewalk, new Vector3(-42.5f,.2f,43f), new Vector3(2.5f,.4f,2.5f));
            harbor.Round(cream, new Vector3(-42.5f,1.7f,43f), new Vector3(1f,2.8f,1f));
            harbor.Round(teal, new Vector3(-42.5f,3.35f,43f), new Vector3(1.3f,.55f,1.3f));
            harbor.Add(geometry.Cone, terracotta, new Vector3(-42.5f,4f,43f),
                new Vector3(1.6f,.8f,1.6f), Quaternion.identity);
            harbor.Build("Fishing harbor / lookout", cityRoot, Vector3.zero);
        }

        private void Road(CityMeshBatch batch, Vector3 point, float width, float length, bool vertical)
        {
            Vector3 size = vertical ? new Vector3(width,.12f,length) : new Vector3(length,.12f,width);
            Vector3 pavement = vertical ? new Vector3(width + 1f,.16f,length) : new Vector3(length,.16f,width + 1f);
            batch.Box(sidewalk, point, pavement);
            batch.Box(asphalt, point + Vector3.up * .045f, size);
            int marks = Mathf.FloorToInt(length / 3f);
            for (int i = 0; i < marks; i++)
            {
                float offset = -length * .5f + 1.6f + i * 3f;
                Vector3 pos = point + new Vector3(vertical ? 0f : offset, .115f, vertical ? offset : 0f);
                batch.Box(white, pos, vertical ? new Vector3(.09f,.015f,1.05f) : new Vector3(1.05f,.015f,.09f));
            }
        }

        private void BuildDistricts()
        {
            int count = GameCatalog.Districts.Length;
            districts = new DistrictView[count];
            for (int i = 0; i < count; i++)
            {
                bool coast = i == 10;
                var definition = GameCatalog.Districts[i];
                var district = new DistrictView();
                district.center = coast ? new Vector3(-36.6f,0f,0f) :
                    new Vector3(i % 2 == 0 ? -15f : 15f, 0f, -38f + i / 2 * 19f);
                district.root = new GameObject("District " + (i + 1) + " • " + definition.name).transform;
                district.root.SetParent(cityRoot, false);
                district.root.localPosition = district.center;
                districts[i] = district;
                var baseBatch = new CityMeshBatch(geometry);
                Vector3 tileSize = coast ? new Vector3(3.7f,.15f,82f) : new Vector3(25.5f,.15f,14.8f);
                baseBatch.Box(coast ? sand : limestone, new Vector3(0f,.075f,0f), tileSize);
                if (!coast)
                {
                    baseBatch.Box(districtColors[i], new Vector3(0f,.17f,-7.15f), new Vector3(25.4f,.12f,.28f));
                    baseBatch.Box(sidewalk, new Vector3(0f,.2f,0f), new Vector3(25f,.12f,1.25f));
                    baseBatch.Box(sidewalk, new Vector3(0f,.2f,0f), new Vector3(.8f,.12f,14f));
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 p = new Vector3((corner % 2 == 0 ? -1f : 1f) * 11.9f,.22f,
                            (corner < 2 ? -1f : 1f) * 6.3f);
                        baseBatch.Box(districtColors[i], p, new Vector3(1.1f,.3f,1.1f));
                        Palm(baseBatch, p + Vector3.up * .15f, 2f + i % 3 * .3f, i * 24f + corner * 57f);
                    }
                    // Different edge monuments give every district a readable silhouette:
                    // columns, water urns, orchard rows, market awnings, or stepped gateways.
                    DistrictSignature(baseBatch, i);
                }
                GameObject baseObject = baseBatch.Build("District paving and identity", district.root, Vector3.zero);
                AddHit(baseObject, i, -1, new Vector3(0f,.12f,0f), tileSize + new Vector3(0f,.1f,0f));
                district.plots = new PlotView[definition.projects.Length];
                int columns = definition.projects.Length <= 6 ? 3 : 4;
                int rows = Mathf.Max(2, Mathf.CeilToInt(definition.projects.Length / (float)columns));
                for (int p = 0; p < definition.projects.Length; p++)
                {
                    Vector3 size = coast ? new Vector3(3.2f,0f,Mathf.Min(9f,76f / Mathf.Max(1,definition.projects.Length))) :
                        new Vector3(22.4f / columns,0f,12f / rows);
                    Vector3 position = coast ?
                        new Vector3(0f,.18f,-36f + (p + .5f) * 72f / Mathf.Max(1,definition.projects.Length)) :
                        new Vector3(-11.2f + (p % columns + .5f) * size.x, .18f,
                            -6f + (p / columns + .5f) * size.z);
                    Transform anchor = new GameObject("Plot " + p + " • " + definition.projects[p].name).transform;
                    anchor.SetParent(district.root, false);
                    anchor.localPosition = position;
                    district.plots[p] = new PlotView { anchor = anchor, size = size, definition = definition.projects[p] };
                    AddHit(anchor.gameObject, i, p, new Vector3(0f,1.1f,0f),
                        new Vector3(size.x * .85f,2.2f,size.z * .83f));
                }
                Vector3 salvagePos = coast ? new Vector3(0f,.2f,-42f) : new Vector3(-11f,.2f,-6.1f);
                var salvage = new CityMeshBatch(geometry);
                RubblePile(salvage, Vector3.zero, 1.1f, i + 31);
                district.rubble = salvage.Build("Local rubble • clearing progress", district.root, salvagePos);
                district.salvageHit = AddHit(district.rubble, i, -2, new Vector3(0f,.65f,0f), new Vector3(2f,1.3f,1.8f));
                var sign = new CityMeshBatch(geometry);
                sign.Round(teal, new Vector3(0f,.15f,0f), new Vector3(1.3f,.16f,1.3f));
                sign.Box(white, new Vector3(0f,.25f,0f), new Vector3(.65f,.04f,.15f), 45f);
                sign.Box(white, new Vector3(0f,.25f,0f), new Vector3(.65f,.04f,.15f), -45f);
                sign.Build("Rubble interaction marker", district.root, salvagePos);
                var badge = new CityMeshBatch(geometry);
                badge.Round(yellow, Vector3.zero, new Vector3(1f,.15f,1f));
                badge.Box(white, new Vector3(-.13f,.1f,0f), new Vector3(.35f,.06f,.11f), -45f);
                badge.Box(white, new Vector3(.15f,.1f,.09f), new Vector3(.6f,.06f,.11f), 45f);
                district.badge = badge.Build("Claimed district medallion", district.root,
                    coast ? new Vector3(0f,.3f,42f) : new Vector3(10.8f,.32f,-6.2f));
                district.badge.SetActive(false);
                district.crane = BuildCrane(district.root,
                    coast ? new Vector3(.7f,.25f,0f) : new Vector3(10.8f,.25f,5.5f));
                district.crane.SetActive(false);
            }
        }

        private void DistrictSignature(CityMeshBatch batch, int district)
        {
            Material accent = districtColors[district];
            float z = 6.5f;
            switch (district % 5)
            {
                case 0:
                    for (int k = 0; k < 3; k++)
                    {
                        batch.Round(cream, new Vector3(-2f + k * 2f,.7f,z), new Vector3(.45f,1.1f,.45f));
                        batch.Round(accent, new Vector3(-2f + k * 2f,1.3f,z), new Vector3(.65f,.2f,.65f));
                    }
                    break;
                case 1:
                    batch.Box(accent, new Vector3(0f,.65f,z), new Vector3(3.5f,1f,.5f));
                    batch.Box(cream, new Vector3(0f,1.3f,z), new Vector3(4f,.25f,.8f));
                    break;
                case 2:
                    for (int k = 0; k < 4; k++) Palm(batch, new Vector3(-3f + k * 2f,.22f,z), 1.8f, k * 72f);
                    break;
                case 3:
                    batch.Round(accent, new Vector3(0f,.6f,z), new Vector3(1.3f,.9f,1.3f));
                    batch.Round(water, new Vector3(0f,1.08f,z), new Vector3(1.15f,.08f,1.15f));
                    break;
                default:
                    batch.Box(accent, new Vector3(-1.1f,.9f,z), new Vector3(.4f,1.5f,.4f));
                    batch.Box(accent, new Vector3(1.1f,.9f,z), new Vector3(.4f,1.5f,.4f));
                    batch.Box(cream, new Vector3(0f,1.65f,z), new Vector3(2.6f,.3f,.5f));
                    break;
            }
        }

        private static CitySelectable AddHit(GameObject target, int district, int plot, Vector3 center, Vector3 size)
        {
            var hit = target.AddComponent<CitySelectable>();
            hit.districtIndex = district;
            hit.plotIndex = plot;
            var collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            return hit;
        }

        public void Refresh()
        {
            if (session == null || session.State == null || districts == null) return;
            GameState state = session.State;
            for (int d = 0; d < districts.Length && d < state.districts.Length; d++)
            {
                DistrictView view = districts[d];
                DistrictState district = state.districts[d];
                view.unlocked = district.unlocked;
                view.rewarded = district.rewardClaimed;
                view.badge.SetActive(district.rewardClaimed);
                int remaining = Mathf.Max(0, GameCatalog.Districts[d].rubbleLoads - district.clearedLoads);
                if (view.rubbleRemaining != remaining)
                {
                    view.rubbleRemaining = remaining;
                    // Keep the hit target available after clearing: imported salvage contracts
                    // do not restore local ruins and are still dispatched by the session.
                    float scale = remaining == 0 ? .16f :
                        Mathf.Lerp(.3f,1f,remaining / (float)Mathf.Max(1,GameCatalog.Districts[d].rubbleLoads));
                    view.rubble.transform.localScale = new Vector3(1f,scale,1f);
                }
                bool constructing = false;
                bool plotChanged = false;
                for (int p = 0; p < view.plots.Length; p++)
                {
                    ProjectState project = FindProject(district, view.plots[p].definition.id);
                    int stage = project != null && project.completed ? 3 :
                        project != null && project.startedUtc > 0 ? 2 : remaining == 0 ? 1 : 0;
                    constructing |= stage == 2;
                    if (stage == view.plots[p].stage) continue;
                    ReplacePlot(view.plots[p], d, p, stage);
                    plotChanged = true;
                }
                if (plotChanged) MergeDistrictPlots(view);
                view.crane.SetActive(constructing);
            }
            if (state.factoryLevel != factoryLevel)
            {
                factoryLevel = state.factoryLevel;
                ReplaceFactory();
            }
            int current = Mathf.Clamp(state.selectedDistrict,0,districts.Length - 1);
            factoryHit.districtIndex = current;
            if (current != selectedDistrict) FocusDistrict(current);
            fleet.Refresh(state, DistrictPosition(Mathf.Clamp(state.jobDistrict,0,districts.Length - 1)),
                new Vector3(35f,.2f,-35f));
        }

        private static ProjectState FindProject(DistrictState district, string id)
        {
            if (district.projects == null) return null;
            foreach (ProjectState project in district.projects)
                if (project != null && project.id == id) return project;
            return null;
        }

        private void MergeDistrictPlots(DistrictView district)
        {
            ReleaseVisual(district.damagedPlots);
            ReleaseVisual(district.finishedPlots);
            var damaged = new CityMeshBatch(geometry);
            var finished = new CityMeshBatch(geometry);
            foreach (PlotView plot in district.plots)
            {
                CityMeshBatch target = plot.stage == 3 ? finished : damaged;
                foreach (MeshFilter filter in plot.visual.GetComponentsInChildren<MeshFilter>())
                {
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    renderer.enabled = false;
                    target.Add(filter.sharedMesh,renderer.sharedMaterial,plot.anchor.localPosition,
                        Vector3.one,Quaternion.identity);
                }
            }
            // Source meshes remain cached for incremental stage changes. Only this district's
            // presentation batch is replaced; normal one-second session ticks do no mesh work.
            district.damagedPlots = damaged.Build("District plot batch • ruins / foundations / construction",
                district.root,Vector3.zero);
            district.finishedPlots = finished.Build("District plot batch • completed architecture",
                district.root,Vector3.zero,true);
        }

        private void ReplacePlot(PlotView plot, int district, int index, int stage)
        {
            ReleaseVisual(plot.visual);
            plot.stage = stage;
            var batch = new CityMeshBatch(geometry);
            Vector3 footprint = new Vector3(plot.size.x * .82f,.12f,plot.size.z * .8f);
            batch.Box(stage == 3 ? sidewalk : sand, new Vector3(0f,.04f,0f), footprint);
            if (stage == 0) Ruin(batch, district, index, footprint);
            else if (stage == 1) ClearedPlot(batch, footprint);
            else if (stage == 2) Construction(batch, district, footprint);
            else FinishedProject(batch, plot.definition, district, index, footprint);
            plot.visual = batch.Build(stage == 0 ? "Damaged structure" : stage == 1 ? "Cleared foundation" :
                stage == 2 ? "Under construction / scaffold" : "Completed • " + plot.definition.name,
                plot.anchor, Vector3.zero, stage == 3);
            // The selectable volume follows the architecture, so tapping an upper-storey
            // roof hits its own plot rather than the ground behind it in an angled view.
            float hitHeight = stage == 1 ? .65f : stage == 2 ? 4.5f : 2.2f;
            if (stage == 3)
            {
                switch (plot.definition.kind)
                {
                    case ProjectKind.Housing: hitHeight = district >= 5 ? 5.5f : 3f; break;
                    case ProjectKind.Landmark: hitHeight = 5.6f; break;
                    case ProjectKind.Water: hitHeight = 3f; break;
                    case ProjectKind.Power: hitHeight = 1.3f; break;
                    case ProjectKind.Road: hitHeight = 3.1f; break;
                    case ProjectKind.Park: hitHeight = 3.2f; break;
                    case ProjectKind.Services: hitHeight = 2.8f; break;
                    case ProjectKind.Investment:
                        hitHeight = district == 10 && plot.definition.id == "commerce" ? 5.6f :
                            plot.definition.id == "farm" ? 1.8f : 3.2f;
                        break;
                }
            }
            BoxCollider hit = plot.anchor.GetComponent<BoxCollider>();
            hit.center = new Vector3(0f,hitHeight * .5f,0f);
            hit.size = new Vector3(plot.size.x * .85f,hitHeight,plot.size.z * .83f);
        }

        private void ReleaseVisual(GameObject visual)
        {
            if (visual == null) return;
            visual.SetActive(false);
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) geometry.Release(filter.sharedMesh);
            Destroy(visual);
        }

        private void Ruin(CityMeshBatch batch, int district, int plot, Vector3 footprint)
        {
            float w = footprint.x * .7f;
            float depth = footprint.z * .68f;
            float h = .9f + (district + plot) % 3 * .35f;
            batch.Box(rubble, new Vector3(-w * .5f,h * .5f,-depth * .18f), new Vector3(.24f,h,depth * .65f));
            batch.Box(limestone, new Vector3(-w * .2f,h * .45f,depth * .45f), new Vector3(w * .65f,h * .9f,.2f));
            batch.Box(rubble, new Vector3(w * .4f,h * .22f,depth * .28f), new Vector3(.26f,h * .44f,depth * .4f));
            batch.Box(terracotta, new Vector3(-w * .17f,h + .1f,depth * .45f), new Vector3(w * .37f,.19f,.38f), 7f);
            batch.Beam(iron, new Vector3(-w * .5f,h,0f), new Vector3(-w * .43f,h + .55f,.1f), .045f);
            batch.Beam(iron, new Vector3(-w * .2f,h,depth * .45f),
                new Vector3(-w * .1f,h + .6f,depth * .37f), .045f);
            RubblePile(batch, new Vector3(w * .15f,.1f,-depth * .15f),
                Mathf.Min(footprint.x,footprint.z) * .34f, district * 17 + plot * 31);
        }

        private void RubblePile(CityMeshBatch batch, Vector3 pos, float size, int seed)
        {
            var random = new System.Random(seed);
            for (int n = 0; n < 12; n++)
            {
                float x = ((float)random.NextDouble() - .5f) * size * 1.4f;
                float z = ((float)random.NextDouble() - .5f) * size * 1.25f;
                float s = size * (.14f + (float)random.NextDouble() * .22f);
                float y = .07f + Mathf.Max(0f, .5f - Mathf.Abs(x / size) - Mathf.Abs(z / size)) * size * .45f;
                batch.Add(geometry.Box, n % 4 == 0 ? terracotta : rubble, pos + new Vector3(x,y,z),
                    new Vector3(s,s * .65f,s * .9f),
                    Quaternion.Euler(n * 17f % 35f,n * 73f,n * 7f % 25f));
            }
        }

        private void ClearedPlot(CityMeshBatch batch, Vector3 footprint)
        {
            float x = footprint.x * .42f, z = footprint.z * .42f;
            batch.Box(limestone, new Vector3(0f,.12f,0f), new Vector3(x * 1.7f,.12f,z * 1.7f));
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -x : x,.25f,i < 2 ? -z : z);
                batch.Box(teal, p, new Vector3(.12f,.35f,.12f));
            }
            batch.Box(white, new Vector3(0f,.2f,-z), new Vector3(x * 2f,.025f,.06f));
            batch.Box(white, new Vector3(0f,.2f,z), new Vector3(x * 2f,.025f,.06f));
            batch.Box(white, new Vector3(-x,.2f,0f), new Vector3(.06f,.025f,z * 2f));
            batch.Box(white, new Vector3(x,.2f,0f), new Vector3(.06f,.025f,z * 2f));
            batch.Box(terracotta, new Vector3(x * .6f,.28f,z * .6f), new Vector3(.45f,.3f,.45f));
        }

        private void Construction(CityMeshBatch batch, int district, Vector3 footprint)
        {
            float x = footprint.x * .34f, z = footprint.z * .32f;
            float height = district == 10 ? 3.8f : 2.4f + district % 3 * .55f;
            batch.Box(limestone, new Vector3(0f,.18f,0f), new Vector3(x * 2.1f,.22f,z * 2.1f));
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = new Vector3(i % 2 == 0 ? -x : x,height * .5f,i < 2 ? -z : z);
                batch.Box(cream, p, new Vector3(.2f,height,.2f));
                batch.Box(iron, p + new Vector3(i % 2 == 0 ? -.18f : .18f,.2f,0f),
                    new Vector3(.055f,height + .4f,.055f));
            }
            for (float y = 1f; y < height; y += 1.1f)
            {
                batch.Box(limestone, new Vector3(0f,y,0f), new Vector3(x * 2f,.14f,z * 2f));
                batch.Box(terracotta, new Vector3(0f,y + .12f,-z - .22f), new Vector3(x * 2.3f,.1f,.5f));
                batch.Beam(iron, new Vector3(-x - .15f,y - .75f,-z - .24f),
                    new Vector3(x + .15f,y + .2f,-z - .24f), .05f);
                batch.Box(yellow, new Vector3(0f,y + .4f,-z - .38f), new Vector3(x * 2.3f,.06f,.06f));
            }
            batch.Box(teal, new Vector3(0f,.55f,z + .18f), new Vector3(x * 1.4f,.65f,.08f));
            batch.Box(yellow, new Vector3(x,.5f,-z - .25f), new Vector3(.55f,.8f,.08f));
            batch.Box(dark, new Vector3(x,.5f,-z - .3f), new Vector3(.15f,.65f,.025f), 24f);
        }

        private void FinishedProject(CityMeshBatch batch, ProjectDefinition project, int district, int index, Vector3 footprint)
        {
            string id = (project.id ?? string.Empty).ToLowerInvariant();
            Material accent = districtColors[Mathf.Min(district,districtColors.Length - 1)];
            float scale = Mathf.Min(footprint.x / 4.6f,footprint.z / 4.3f);
            switch (project.kind)
            {
                case ProjectKind.Road:
                    Road(batch, new Vector3(0f,.19f,0f), footprint.x * .55f, footprint.z * .95f, true);
                    for (int n = 0; n < 5; n++)
                        batch.Box(white,new Vector3(-footprint.x * .2f + n * footprint.x * .1f,.3f,0f),
                            new Vector3(footprint.x * .045f,.02f,footprint.z * .23f));
                    Lamp(batch,new Vector3(footprint.x * .36f,.2f,footprint.z * .22f),-90f);
                    Palm(batch,new Vector3(-footprint.x * .35f,.2f,-footprint.z * .25f),2f * scale,index * 32f);
                    break;
                case ProjectKind.Power:
                    Utility(batch, footprint, false);
                    break;
                case ProjectKind.Water:
                    Utility(batch, footprint, true);
                    break;
                case ProjectKind.Park:
                    Park(batch, footprint, district);
                    break;
                case ProjectKind.Investment:
                    if (id.Contains("farm") || id.Contains("agri"))
                        Farm(batch, footprint);
                    else if (id.Contains("factory") || id.Contains("workshop") || id.Contains("industry"))
                        Workshop(batch, footprint);
                    else if (district == 10 || id.Contains("hotel")) Hotel(batch, footprint);
                    else Commerce(batch, footprint, accent);
                    break;
                case ProjectKind.Landmark:
                    if (district == 10 || id.Contains("hotel")) Hotel(batch, footprint);
                    else Landmark(batch, footprint, accent, district);
                    break;
                case ProjectKind.Services:
                    if (id.Contains("market") || id.Contains("shop")) Commerce(batch, footprint, accent);
                    else ServiceBuilding(batch, footprint, accent, id.Contains("clinic") ||
                        id.Contains("hospital") || id == "services");
                    break;
                default:
                    if (id.Contains("hotel")) Hotel(batch, footprint);
                    else Housing(batch, footprint, accent, district, index,
                        id.Contains("apartment") || id.Contains("tower") || id.Contains("multi") || district >= 5);
                    break;
            }
        }

        private void Housing(CityMeshBatch batch, Vector3 footprint, Material accent, int district, int index, bool tall)
        {
            float w = footprint.x * .61f, d = footprint.z * .6f;
            int floors = tall ? 3 + district % 2 : 1 + (district + index) % 2;
            float h = floors * 1.05f;
            Building(batch, Vector3.zero, w,d,h,cream,accent,floors);
            if (floors == 1)
                batch.Add(geometry.Roof, terracotta, new Vector3(0f,h + .5f,0f),
                    new Vector3(w + .25f,.65f,d + .3f),Quaternion.identity);
            else
            {
                batch.Box(accent,new Vector3(w * .22f,h + .45f,d * .14f),new Vector3(w * .3f,.55f,d * .38f));
                SolarPanel(batch,new Vector3(-w * .2f,h + .32f,-d * .1f),Mathf.Min(.75f,w * .3f));
                batch.Round(cream,new Vector3(w * .25f,h + .9f,d * .2f),new Vector3(.35f,.45f,.35f));
            }
            batch.Box(terracotta,new Vector3(-w * .3f,.3f,-d * .67f),new Vector3(.65f,.4f,.4f));
            Palm(batch,new Vector3(w * .65f,.15f,d * .25f),1.8f,index * 70f);
        }

        private void Building(CityMeshBatch batch, Vector3 pos, float w, float d, float h,
            Material body, Material accent, int floors)
        {
            batch.Box(body,pos + new Vector3(0f,h * .5f + .16f,0f),new Vector3(w,h,d));
            batch.Box(accent,pos + new Vector3(0f,h + .19f,0f),new Vector3(w + .18f,.22f,d + .18f));
            batch.Box(limestone,pos + new Vector3(0f,.24f,0f),new Vector3(w + .13f,.26f,d + .13f));
            batch.Box(teal,pos + new Vector3(0f,.55f,-d * .5f - .02f),new Vector3(.48f,.78f,.065f));
            for (int floor = 0; floor < floors; floor++)
            {
                float y = .85f + floor * (h / floors);
                for (int col = 0; col < 3; col++)
                {
                    float x = (col - 1) * w * .29f;
                    if (floor == 0 && col == 1) continue;
                    batch.Box(glass,pos + new Vector3(x,y,-d * .5f - .028f),new Vector3(w * .17f,.42f,.06f));
                    batch.Box(cream,pos + new Vector3(x,y - .27f,-d * .5f - .12f),new Vector3(w * .21f,.08f,.25f));
                    batch.Box(glass,pos + new Vector3(x,y,d * .5f + .028f),new Vector3(w * .17f,.42f,.06f));
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    batch.Box(glass,pos + new Vector3(side * (w * .5f + .025f),y,-d * .22f),
                        new Vector3(.06f,.43f,d * .23f));
                    batch.Box(glass,pos + new Vector3(side * (w * .5f + .025f),y,d * .22f),
                        new Vector3(.06f,.43f,d * .23f));
                }
                if (floor > 0)
                    batch.Box(accent,pos + new Vector3(0f,y - .43f,-d * .5f - .16f),
                        new Vector3(w * .94f,.1f,.3f));
            }
        }

        private void Commerce(CityMeshBatch batch, Vector3 footprint, Material accent)
        {
            float w = footprint.x * .7f, d = footprint.z * .48f;
            Building(batch,new Vector3(0f,0f,.35f),w,d,1.55f,cream,accent,1);
            batch.Box(glass,new Vector3(0f,.85f,.35f - d * .5f - .05f),new Vector3(w * .82f,1f,.08f));
            for (int i = 0; i < 6; i++)
                batch.Box(i % 2 == 0 ? accent : cream,new Vector3(-w * .5f + (i + .5f) * w / 6f,1.35f,-d * .5f),
                    new Vector3(w / 6f,.16f,.95f));
            for (int i = 0; i < 3; i++)
            {
                batch.Box(terracotta,new Vector3(-w * .3f + i * w * .3f,.38f,-d * .5f - .6f),
                    new Vector3(w * .21f,.45f,.5f));
                batch.Round(i % 2 == 0 ? grass : yellow,new Vector3(-w * .3f + i * w * .3f,.65f,-d * .5f - .6f),
                    new Vector3(w * .19f,.16f,.42f));
            }
            SolarPanel(batch,new Vector3(0f,1.85f,.35f),Mathf.Min(w * .33f,.9f));
        }

        private void ServiceBuilding(CityMeshBatch batch, Vector3 footprint, Material accent, bool clinic)
        {
            float w = footprint.x * .73f, d = footprint.z * .64f;
            Building(batch,Vector3.zero,w,d,2.25f,cream,accent,2);
            batch.Box(accent,new Vector3(0f,1.15f,-d * .5f - .22f),new Vector3(.95f,1.95f,.4f));
            batch.Box(glass,new Vector3(0f,.6f,-d * .5f - .45f),new Vector3(.52f,.8f,.04f));
            batch.Box(white,new Vector3(0f,1.75f,-d * .5f - .45f),new Vector3(.48f,.13f,.04f));
            if (clinic) batch.Box(white,new Vector3(0f,1.75f,-d * .5f - .46f),new Vector3(.13f,.48f,.04f));
            else
            {
                batch.Box(yellow,new Vector3(0f,1.78f,-d * .5f - .47f),new Vector3(.25f,.22f,.04f));
                batch.Beam(iron,new Vector3(w * .65f,.2f,-d * .4f),new Vector3(w * .65f,2.6f,-d * .4f),.04f);
                batch.Box(teal,new Vector3(w * .65f + .22f,2.35f,-d * .4f),new Vector3(.45f,.3f,.035f));
            }
            Palm(batch,new Vector3(-w * .65f,.16f,-d * .3f),1.75f,25f);
        }

        private void Park(CityMeshBatch batch, Vector3 footprint, int district)
        {
            batch.Box(grass,new Vector3(0f,.16f,0f),new Vector3(footprint.x * .91f,.17f,footprint.z * .9f));
            batch.Box(sidewalk,new Vector3(0f,.27f,0f),new Vector3(footprint.x * .88f,.07f,.5f),22f);
            batch.Round(cream,new Vector3(0f,.38f,0f),new Vector3(1.2f,.22f,1.2f));
            batch.Round(water,new Vector3(0f,.51f,0f),new Vector3(.99f,.04f,.99f));
            batch.Round(cream,new Vector3(0f,.73f,0f),new Vector3(.18f,.45f,.18f));
            for (int i = 0; i < 4; i++)
                Palm(batch,new Vector3((i % 2 == 0 ? -1f : 1f) * footprint.x * .32f,.28f,
                    (i < 2 ? -1f : 1f) * footprint.z * .3f),1.9f + i % 2 * .45f,district * 24f + i * 80f);
            Bench(batch,new Vector3(-footprint.x * .19f,.25f,-footprint.z * .32f),0f);
            Bench(batch,new Vector3(footprint.x * .19f,.25f,footprint.z * .32f),180f);
        }

        private void Farm(CityMeshBatch batch, Vector3 footprint)
        {
            batch.Box(grass,new Vector3(0f,.15f,0f),new Vector3(footprint.x * .9f,.16f,footprint.z * .88f));
            for (int row = 0; row < 4; row++)
            {
                float z = -footprint.z * .32f + row * footprint.z * .18f;
                batch.Box(terracotta,new Vector3(-footprint.x * .1f,.26f,z),new Vector3(footprint.x * .59f,.11f,.22f));
                for (int col = 0; col < 5; col++)
                {
                    float x = -footprint.x * .33f + col * footprint.x * .115f;
                    batch.Add(geometry.Cone,leaf,new Vector3(x,.47f,z),new Vector3(.25f,.43f,.25f),Quaternion.identity);
                    if (row % 2 == 0) batch.Round(yellow,new Vector3(x,.47f,z - .12f),new Vector3(.1f,.13f,.1f));
                }
            }
            float w = footprint.x * .23f;
            batch.Box(cream,new Vector3(footprint.x * .3f,.7f,footprint.z * .12f),
                new Vector3(w,1.1f,footprint.z * .5f));
            batch.Add(geometry.Roof,teal,new Vector3(footprint.x * .3f,1.4f,footprint.z * .12f),
                new Vector3(w + .2f,.45f,footprint.z * .52f),Quaternion.identity);
            batch.Box(glass,new Vector3(footprint.x * .3f,.78f,-footprint.z * .14f),new Vector3(w * .5f,.6f,.05f));
            batch.Round(water,new Vector3(footprint.x * .3f,.5f,-footprint.z * .32f),new Vector3(.45f,.6f,.45f));
        }

        private void Workshop(CityMeshBatch batch, Vector3 footprint)
        {
            float w = footprint.x * .67f, d = footprint.z * .62f;
            batch.Box(cream,new Vector3(0f,.85f,0f),new Vector3(w,1.5f,d));
            batch.Add(geometry.Roof,teal,new Vector3(0f,1.85f,0f),new Vector3(w + .25f,.6f,d + .2f),Quaternion.identity);
            batch.Box(glass,new Vector3(0f,.75f,-d * .5f - .03f),new Vector3(w * .5f,1f,.06f));
            for (int n = 0; n < 4; n++)
                batch.Box(iron,new Vector3(0f,.42f + n * .23f,-d * .5f - .07f),new Vector3(w * .5f,.035f,.04f));
            batch.Round(terracotta,new Vector3(w * .45f,1.6f,d * .35f),new Vector3(.3f,2.8f,.3f));
            batch.Box(yellow,new Vector3(-w * .25f,.4f,-d * .7f),new Vector3(.5f,.5f,.5f));
        }

        private void Utility(CityMeshBatch batch, Vector3 footprint, bool isWater)
        {
            float x = footprint.x * .22f;
            if (isWater)
            {
                for (int i = 0; i < 4; i++)
                    batch.Box(iron,new Vector3((i % 2 == 0 ? -1f : 1f) * .6f,1f,
                        (i < 2 ? -1f : 1f) * .6f),new Vector3(.13f,1.8f,.13f));
                batch.Round(cream,new Vector3(0f,2.15f,0f),new Vector3(1.8f,1f,1.8f));
                batch.Round(teal,new Vector3(0f,2.72f,0f),new Vector3(1.9f,.15f,1.9f));
                batch.Beam(teal,new Vector3(.68f,.25f,0f),new Vector3(.68f,2.1f,0f),.12f);
                batch.Beam(teal,new Vector3(.68f,.25f,0f),new Vector3(x * 1.8f,.25f,0f),.12f);
                batch.Box(cream,new Vector3(-x,.5f,-footprint.z * .3f),new Vector3(.75f,.65f,.6f));
            }
            else
            {
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                        SolarPanel(batch,new Vector3((col == 0 ? -1f : 1f) * x,.75f,
                            (row == 0 ? -1f : 1f) * footprint.z * .23f),Mathf.Min(1f,footprint.x * .2f));
                batch.Box(iron,new Vector3(0f,.5f,0f),new Vector3(.5f,.7f,.5f));
                batch.Box(yellow,new Vector3(0f,.66f,-.27f),new Vector3(.24f,.32f,.03f));
            }
        }

        private void SolarPanel(CityMeshBatch batch, Vector3 pos, float width)
        {
            batch.Box(iron,pos + Vector3.down * .25f,new Vector3(width * 1.2f,.5f,.08f));
            Quaternion tilt = Quaternion.Euler(-17f,0f,0f);
            batch.Add(geometry.Box,iron,pos,new Vector3(width * 1.65f,.08f,width),tilt);
            batch.Add(geometry.Box,glass,pos + Vector3.up * .055f,new Vector3(width * 1.54f,.035f,width * .9f),tilt);
            for (int i = 0; i < 3; i++)
                batch.Add(geometry.Box,teal,pos + new Vector3((i - 1) * width * .48f,.08f,0f),
                    new Vector3(.025f,.018f,width * .9f),tilt);
        }

        private void Landmark(CityMeshBatch batch, Vector3 footprint, Material accent, int district)
        {
            float w = footprint.x * .62f, d = footprint.z * .57f;
            Building(batch,Vector3.zero,w,d,1.8f,cream,accent,1);
            batch.Add(geometry.Cone,accent,new Vector3(0f,2.55f,0f),new Vector3(w * .8f,1.4f,w * .8f),Quaternion.identity);
            batch.Round(cream,new Vector3(w * .6f,1.7f,d * .25f),new Vector3(.5f,3.1f,.5f));
            batch.Round(accent,new Vector3(w * .6f,3.25f,d * .25f),new Vector3(.75f,.2f,.75f));
            batch.Add(geometry.Cone,terracotta,new Vector3(w * .6f,3.7f,d * .25f),
                new Vector3(.65f,.75f,.65f),Quaternion.identity);
            Palm(batch,new Vector3(-w * .65f,.16f,d * .15f),2f,district * 35f);
        }

        private void Hotel(CityMeshBatch batch, Vector3 footprint)
        {
            float w = footprint.x * .7f, d = footprint.z * .55f;
            Building(batch,new Vector3(0f,0f,footprint.z * .1f),w,d,4.8f,cream,teal,4);
            batch.Box(teal,new Vector3(0f,5.2f,footprint.z * .1f),new Vector3(w * .62f,.5f,d * .55f));
            batch.Box(sidewalk,new Vector3(0f,.25f,-footprint.z * .32f),new Vector3(w * .9f,.2f,footprint.z * .24f));
            batch.Box(water,new Vector3(0f,.37f,-footprint.z * .32f),new Vector3(w * .68f,.04f,footprint.z * .18f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = new Vector3(side * footprint.x * .35f,.15f,-footprint.z * .2f);
                Palm(batch,p,2.5f,side * 30f);
            }
        }

        private GameObject BuildCrane(Transform parent, Vector3 pos)
        {
            var batch = new CityMeshBatch(geometry);
            const float height = 6.4f;
            batch.Box(limestone,new Vector3(0f,.25f,0f),new Vector3(1.2f,.5f,1.2f));
            for (int corner = 0; corner < 4; corner++)
                batch.Box(yellow,new Vector3(corner % 2 == 0 ? -.24f : .24f,height * .5f,
                    corner < 2 ? -.24f : .24f),new Vector3(.08f,height,.08f));
            for (int i = 0; i < 6; i++)
            {
                float y = .5f + i;
                batch.Box(yellow,new Vector3(0f,y,0f),new Vector3(.58f,.07f,.58f));
                batch.Beam(yellow,new Vector3(-.24f,y,-.24f),new Vector3(.24f,y + .9f,-.24f),.055f);
            }
            batch.Box(yellow,new Vector3(-2.1f,height,0f),new Vector3(6.4f,.18f,.35f));
            batch.Box(yellow,new Vector3(-2.1f,height + .55f,0f),new Vector3(6.4f,.08f,.3f));
            for (int i = 0; i < 7; i++)
                batch.Beam(yellow,new Vector3(-5f + i * .85f,height,0f),
                    new Vector3(-4.6f + i * .85f,height + .55f,0f),.07f);
            batch.Box(limestone,new Vector3(.8f,height + .15f,0f),new Vector3(1f,.8f,.8f));
            batch.Box(glass,new Vector3(-.45f,height - .15f,0f),new Vector3(.65f,.65f,.55f));
            batch.Beam(dark,new Vector3(-4f,height,0f),new Vector3(-4f,2.8f,0f),.035f);
            batch.Beam(yellow,new Vector3(-4f,2.8f,0f),new Vector3(-3.8f,2.55f,0f),.09f);
            return batch.Build("Lattice tower crane",parent,pos);
        }

        private void Palm(CityMeshBatch batch, Vector3 pos, float height, float yaw)
        {
            Vector3 top = pos + new Vector3(.16f,height,.07f);
            batch.Beam(terracotta,pos,pos + new Vector3(.03f,height * .55f,0f),.16f);
            batch.Beam(terracotta,pos + new Vector3(.03f,height * .55f,0f),top,.12f);
            for (int i = 0; i < 7; i++)
                batch.Add(geometry.Leaf,leaf,top,new Vector3(height * .58f,height * .48f,height * .58f),
                    Quaternion.Euler(i % 2 == 0 ? -12f : 9f,yaw + i * 360f / 7f,0f));
            batch.Add(geometry.Cone,leaf,top + Vector3.up * .13f,new Vector3(.3f,.55f,.3f),Quaternion.identity);
        }

        private void Bench(CityMeshBatch batch, Vector3 pos, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0f,yaw,0f);
            batch.Add(geometry.Box,terracotta,pos + Vector3.up * .28f,new Vector3(.9f,.12f,.34f),rotation);
            batch.Add(geometry.Box,terracotta,pos + rotation * new Vector3(0f,.5f,.15f),new Vector3(.9f,.3f,.08f),rotation);
            for (int i = -1; i <= 1; i += 2)
                batch.Add(geometry.Box,dark,pos + rotation * new Vector3(i * .3f,.14f,0f),
                    new Vector3(.07f,.28f,.28f),rotation);
        }

        private void Lamp(CityMeshBatch batch, Vector3 pos, float yaw)
        {
            Vector3 arm = Quaternion.Euler(0f,yaw,0f) * new Vector3(.45f,0f,0f);
            batch.Round(teal,pos + new Vector3(0f,1.35f,0f),new Vector3(.085f,2.7f,.085f));
            batch.Beam(teal,pos + Vector3.up * 2.65f,pos + Vector3.up * 2.65f + arm,.07f);
            batch.Box(cream,pos + Vector3.up * 2.62f + arm,new Vector3(.3f,.13f,.2f),yaw);
        }

        private void Boat(CityMeshBatch batch, Vector3 pos, float yaw, float scale)
        {
            Quaternion rot = Quaternion.Euler(0f,yaw,0f);
            batch.Add(geometry.Box,teal,pos,new Vector3(.85f,.38f,2.5f) * scale,rot);
            batch.Add(geometry.Cone,teal,pos + rot * new Vector3(0f,0f,1.48f * scale),
                new Vector3(.85f,.9f,.38f) * scale,rot * Quaternion.Euler(90f,0f,0f));
            batch.Add(geometry.Box,cream,pos + rot * new Vector3(0f,.36f,-.3f) * scale,
                new Vector3(.7f,.55f,.8f) * scale,rot);
            batch.Add(geometry.Box,glass,pos + rot * new Vector3(0f,.42f,.12f) * scale,
                new Vector3(.53f,.24f,.035f) * scale,rot);
            batch.Beam(terracotta,pos + Vector3.up * .4f * scale,pos + Vector3.up * 2f * scale,.06f);
        }

        private void BuildFactorySite()
        {
            var batch = new CityMeshBatch(geometry);
            batch.Box(sidewalk,new Vector3(0f,.17f,0f),new Vector3(7.6f,.25f,12f));
            batch.Box(asphalt,new Vector3(-1.4f,.32f,0f),new Vector3(2.4f,.1f,11f));
            for (int i = 0; i < 4; i++)
            {
                batch.Box(white,new Vector3(-1.3f,.38f,-4f + i * 2.3f),new Vector3(2f,.025f,.08f));
                batch.Box(iron,new Vector3(2.3f,.55f,-3.8f + i * 2.3f),new Vector3(1.1f,.7f,1.5f));
                batch.Box(i % 2 == 0 ? teal : terracotta,new Vector3(2.3f,.94f,-3.8f + i * 2.3f),
                    new Vector3(1f,.06f,1.4f));
            }
            factorySite = batch.Build("Recycling depot • dispatch apron",cityRoot,new Vector3(36f,0f,-35f));
            factoryHit = AddHit(factorySite,0,-3,new Vector3(0f,1f,0f),new Vector3(7.6f,2f,12f));
        }

        private void ReplaceFactory()
        {
            ReleaseVisual(factory);
            var batch = new CityMeshBatch(geometry);
            if (factoryLevel <= 0)
            {
                batch.Box(iron,new Vector3(.6f,.5f,2.4f),new Vector3(3f,.8f,2.5f));
                batch.Box(teal,new Vector3(.6f,1f,2.4f),new Vector3(3.1f,.15f,2.6f));
                batch.Box(yellow,new Vector3(.6f,.7f,1.1f),new Vector3(.65f,.5f,.08f));
            }
            else
            {
                Workshop(batch,new Vector3(5f,0f,5.8f));
                batch.Box(teal,new Vector3(0f,1.45f,-1.9f),new Vector3(3.2f,.5f,.1f));
                // Three-arrow recycle emblem, deliberately simple geometry.
                for (int i = 0; i < 3; i++)
                    batch.Box(white,new Vector3(-.35f + i * .35f,1.45f,-1.97f),new Vector3(.25f,.08f,.03f),i * 120f);
                batch.Beam(iron,new Vector3(1.9f,.65f,-2f),new Vector3(1.9f,1.5f,1.4f),.35f);
                batch.Box(dark,new Vector3(1.9f,1.03f,-.3f),new Vector3(.7f,.12f,3.7f));
                for (int level = 1; level < factoryLevel; level++)
                    batch.Round(level % 2 == 0 ? cream : teal,
                        new Vector3(-1.3f + (level - 1) % 2 * 1.5f,1.6f,3.3f + (level - 1) / 2 * 1.2f),
                        new Vector3(1f,2.7f,1f));
                RubblePile(batch,new Vector3(-1.7f,.15f,-3f),.9f,781);
            }
            factory = batch.Build("Recycling factory • level " + factoryLevel,factorySite.transform,
                new Vector3(0f,.25f,1.5f),true);
        }

        private void BuildSelection()
        {
            var ring = new CityMeshBatch(geometry);
            Outline(ring,yellow,1f,1f,.06f);
            selection = ring.Build("Selected plot • saffron outline",cityRoot,Vector3.zero);
            selection.SetActive(false);
            var districtRing = new CityMeshBatch(geometry);
            Outline(districtRing,teal,1f,1f,.018f);
            districtSelection = districtRing.Build("Focused district • teal border",cityRoot,Vector3.zero);
        }

        private static void Outline(CityMeshBatch batch, Material material, float width, float depth, float line)
        {
            batch.Box(material,new Vector3(0f,.015f,-depth * .5f),new Vector3(width,.025f,line));
            batch.Box(material,new Vector3(0f,.015f,depth * .5f),new Vector3(width,.025f,line));
            batch.Box(material,new Vector3(-width * .5f,.015f,0f),new Vector3(line,.025f,depth));
            batch.Box(material,new Vector3(width * .5f,.015f,0f),new Vector3(line,.025f,depth));
        }

        public Vector3 DistrictPosition(int index)
        {
            if (districts == null || districts.Length == 0) return transform.position;
            return transform.TransformPoint(districts[Mathf.Clamp(index,0,districts.Length - 1)].center);
        }

        public void FocusDistrict(int index)
        {
            if (districts == null || index < 0 || index >= districts.Length) return;
            bool changed = selectedDistrict != index;
            selectedDistrict = index;
            DistrictView view = districts[index];
            districtSelection.transform.localPosition = view.center + Vector3.up * .32f;
            districtSelection.transform.localScale = index == 10 ? new Vector3(3.5f,1f,83f) : new Vector3(25.6f,1f,14.9f);
            if (changed) SetSelectedPlot(-1);
            else SetSelectedPlot(selectedPlot);
        }

        public void SetSelectedPlot(int index)
        {
            selectedPlot = index;
            if (selection == null || districts == null || selectedDistrict < 0) return;
            selection.SetActive(index != -1);
            if (index == -1) return;
            DistrictView district = districts[selectedDistrict];
            if (index == -3)
            {
                selection.transform.localPosition = new Vector3(36f,.46f,-35f);
                selection.transform.localScale = new Vector3(7.7f,1f,12.1f);
            }
            else if (index == -2)
            {
                selection.transform.position = district.rubble.transform.position + transform.up * .2f;
                selection.transform.localScale = new Vector3(2.3f,1f,2.2f);
            }
            else if (index >= 0 && index < district.plots.Length)
            {
                PlotView plot = district.plots[index];
                selection.transform.position = plot.anchor.position + transform.up * .18f;
                selection.transform.localScale = new Vector3(plot.size.x * .9f,1f,plot.size.z * .88f);
            }
            else selection.SetActive(false);
        }

        public void PlayFinale()
        {
            if (finale || cityRoot == null) return;
            finale = true;
            // Visual celebration only: never alters completion flags or writes a save.
            var batch = new CityMeshBatch(geometry);
            for (int i = 0; i < 18; i++)
            {
                Vector3 p = new Vector3(-32.8f,.4f,-42f + i * 5f);
                batch.Beam(teal,p,p + Vector3.up * 3.1f,.06f);
                batch.Box(i % 2 == 0 ? terracotta : teal,p + new Vector3(.25f,2.8f,0f),
                    new Vector3(.5f,.45f,.035f));
                batch.Beam(cream,p + new Vector3(0f,2.9f,0f),p + new Vector3(0f,2.9f,4.5f),.025f);
                for (int j = 0; j < 4; j++)
                    batch.Add(geometry.Cone,j % 2 == 0 ? yellow : cream,
                        p + new Vector3(0f,2.74f,.6f + j),new Vector3(.2f,.3f,.15f),Quaternion.Euler(180f,0f,0f));
            }
            batch.Build("Finale • corniche celebration bunting",cityRoot,Vector3.zero);
            selection.SetActive(false);
            districtSelection.SetActive(false);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= Refresh;
                session.PlotSelected -= SetSelectedPlot;
            }
            geometry?.Dispose();
        }
    }
}