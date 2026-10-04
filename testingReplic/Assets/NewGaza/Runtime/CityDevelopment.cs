using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NewGaza
{
    internal sealed class CityDevelopmentParcel
    {
        internal string id;
        internal string sourceBuildingId;
        internal int district, plot;
        internal Vector3 position;
        internal float width, depth;
        internal BoxCollider collider;
    }

    public sealed class CityDevelopment : MonoBehaviour
    {
        private GameSession session;
        private CityWorld world;
        private CityCamera cameraControl;
        private CityDevelopmentParcel[] parcels;
        private readonly HashSet<string> replacedSources = new HashSet<string>(StringComparer.Ordinal);
        private CityDevelopmentUI ui;
        private CityRegionOverlay regions;
        private CitySampleBuildings buildings;
        private CityBuildingDefinition chosen;
        private GameObject ghost;
        private Vector3 preview;
        private bool hasPreview;
        private int turn;
        private string selectedSite, selectedBuilding;
        public CityDevelopmentService Rules { get; private set; }
        public bool Placing => chosen != null;
        public int FocusDistrict { get; private set; }
        public string PlacementProblem { get; private set; }
        public CityBuildingDefinition Chosen => chosen;
        public int Rotation => turn;
        public string SelectedSite => selectedSite;
        public string SelectedBuilding => selectedBuilding;

        public Vector3 WorkPosition
        {
            get
            {
                var site = Parcel(Rules.Data.activeRubbleId);
                if (site != null) return site.position;
                return world.AggregateRubblePosition(session.State.jobDistrict);
            }
        }

        public Vector3 DepotPosition
        {
            get
            {
                var building = Rules.Building(Rules.Data.dispatchDepotId);
                return building != null ? DepotPoint(building) : world.CentralDepotPosition;
            }
        }

        private static Vector3 DepotPoint(PlacedBuildingState building)
        {
            var definition = CityBuildingCatalog.Find(building.definitionId);
            return new Vector3(building.x, 0, building.z) + Quaternion.Euler(0, building.quarterTurn * 90, 0) *
                new Vector3(0, 0, definition.depthMeters / 40f + .18f);
        }

        public void Initialize(GameSession game, CityWorld city, CityCamera camera)
        {
            session = game; world = city; cameraControl = camera;
            Rules = new CityDevelopmentService(session.Economy);
            parcels = world.DevelopmentParcels();
            foreach (var parcel in parcels)
                if (parcel.sourceBuildingId != null) replacedSources.Add(parcel.sourceBuildingId);
            regions = new GameObject("Translucent geographic region cells").AddComponent<CityRegionOverlay>();
            regions.transform.SetParent(transform, false);
            regions.Initialize(world);
            buildings = new CitySampleBuildings(world.DevelopmentGeometry, transform);
            ui = new GameObject("Building shop and local work controls").AddComponent<CityDevelopmentUI>();
            ui.transform.SetParent(transform, false);
            ui.Initialize(this, session);
            session.Economy.ClearingCrewReady = () => world.Fleet.WorkCrewReady;
            session.Economy.LegacyLandReady = LegacyLandReady;
            session.Changed += Refresh;
            Refresh();
            // Persisted placements must still be safe against each other; never delete invalid saves.
            for (int i = 0; i < Rules.Data.buildings.Length; i++)
            {
                var saved = Rules.Data.buildings[i];
                string unsafeLand = ValidateLand(CityBuildingCatalog.Find(saved.definitionId), saved.district,
                    saved.x, saved.z, saved.quarterTurn);
                if (unsafeLand != null) throw new InvalidOperationException("الموقع المحفوظ غير صالح: " + unsafeLand + "؛ لم يُحذف الحفظ");
                for (int j = i + 1; j < Rules.Data.buildings.Length; j++)
                {
                    var a = Rules.Data.buildings[i]; var def = CityBuildingCatalog.Find(a.definitionId);
                    float width = (a.quarterTurn == 0 ? def.widthMeters : def.depthMeters) / 20f;
                    float depth = (a.quarterTurn == 0 ? def.depthMeters : def.widthMeters) / 20f;
                    if (CityDevelopmentService.Overlaps(a.x, a.z, width, depth, Rules.Data.buildings[j]))
                        throw new InvalidOperationException("تداخل في مواقع المباني المحفوظة؛ لم يتم حذف الحفظ");
                }
            }
            world.Refresh();
        }

        private CityDevelopmentParcel Parcel(string id)
        {
            if (id == null) return null;
            foreach (var parcel in parcels) if (parcel.id == id) return parcel;
            return null;
        }

        private bool LegacyLandReady(int district, string projectId)
        {
            foreach (var parcel in parcels)
                if (parcel.district == district && Rules.Site(parcel.id).projectId == projectId)
                {
                    if (!Rules.Site(parcel.id).cleared) return false;
                    foreach (var building in Rules.Data.buildings)
                        if (CityDevelopmentService.Overlaps(parcel.position.x, parcel.position.z,
                            parcel.width, parcel.depth, building)) return false;
                    return true;
                }
            return false;
        }

        private void Update()
        {
            if (session == null || !session.Ready) return;
            FocusDistrict = DistrictAt(cameraControl.AudioFocus);
            int hovered = FocusDistrict;
            var mouse = Mouse.current;
            if (mouse != null && !CityCamera.ModalOpen &&
                cameraControl.TryGroundPoint(mouse.position.ReadValue(), out var ground))
            {
                hovered = DistrictAt(ground);
            }
            regions.Highlight(FocusDistrict, hovered);
            ui.Refresh();
            buildings.Animate();
        }

        private void Refresh()
        {
            if (Rules == null) return;
            buildings.Refresh(Rules.Data.buildings, session.Now, session.State);
            foreach (var parcel in parcels)
            {
                var project = session.Economy.FindProject(parcel.district, Rules.Site(parcel.id).projectId);
                if (Rules.Site(parcel.id).cleared && !project.completed && project.startedUtc == 0 && parcel.collider != null)
                {
                    parcel.collider.center = new Vector3(0, .015f, 0);
                    var size = parcel.collider.size; size.y = .03f; parcel.collider.size = size;
                }
            }
            if (chosen != null && hasPreview) UpdatePreview();
            ui.Refresh();
        }

        public int DistrictAt(Vector3 point)
        {
            if (!OnLand(point.x, point.z)) return -1;
            float best = float.MaxValue; int result = -1;
            for (int d = 0; d < GameCatalog.Districts.Length; d++)
            {
                Vector3 centre = world.DistrictPosition(d);
                float distance = (new Vector2(point.x - centre.x, point.z - centre.z)).sqrMagnitude;
                if (distance < best) { best = distance; result = d; }
            }
            return result;
        }

        private bool OnLand(float x, float z)
        {
            return x >= world.MapMinX && x <= world.MapMaxX && z >= world.MapMinZ &&
                z <= world.MapMaxZ && x > CityRegionOverlay.CoastX(z) + .02f;
        }

        public void OpenStore()
        {
            ClearSelection();
            ui.OpenStore();
        }

        public void SetHomeVisible(bool visible)
        {
            if (visible) { ClearSelection(); ui.CloseStore(); }
            ui.SetHomeVisible(visible);
        }

        public void ChooseBuilding(string id)
        {
            chosen = CityBuildingCatalog.Find(id);
            if (chosen == null) { session.Notify("المبنى غير معروف"); return; }
            selectedSite = selectedBuilding = null; turn = 0; hasPreview = true;
            preview = cameraControl.AudioFocus;
            ui.CloseStore();
            UpdatePreview();
            session.Notify("اضغط على الأرض لتحديد الموقع، ثم أكّد البناء. يمكنك تدوير المساحة.");
        }

        public void PreviewAt(Vector3 point)
        {
            preview = point; hasPreview = true; UpdatePreview();
        }

        public void Rotate()
        {
            if (chosen == null) return;
            turn = 1 - turn; UpdatePreview();
        }

        private void UpdatePreview()
        {
            PlacementProblem = ValidateLand(chosen, DistrictAt(preview), preview.x, preview.z, turn);
            if (string.IsNullOrEmpty(PlacementProblem))
                foreach (var building in Rules.Data.buildings)
                    if (CityDevelopmentService.Overlaps(preview.x, preview.z,
                        (turn == 0 ? chosen.widthMeters : chosen.depthMeters) / 20f,
                        (turn == 0 ? chosen.depthMeters : chosen.widthMeters) / 20f, building))
                    { PlacementProblem = "المساحة تتداخل مع مبنى أو مشروع آخر"; break; }
            if (ghost != null) { ghost.SetActive(false); buildings.ReleasePreview(ghost); ghost = null; }
            ghost = buildings.Preview(chosen, preview, turn, string.IsNullOrEmpty(PlacementProblem));
            ui.Refresh();
        }

        public void Confirm()
        {
            if (chosen == null || !hasPreview) return;
            var definition = chosen; int district = DistrictAt(preview);
            bool built = false;
            session.Perform(e =>
            {
                var result = Rules.Build(definition.id, district, preview.x, preview.z, turn, session.Now, ValidateLand);
                built = result.success;
                return result;
            });
            if (built) Cancel();
        }

        public void Cancel()
        {
            chosen = null; hasPreview = false;
            if (ghost != null) { ghost.SetActive(false); buildings.ReleasePreview(ghost); ghost = null; }
            ui.Refresh();
        }

        public void ClearSelection()
        {
            selectedSite = selectedBuilding = null;
            Cancel();
            session.SelectPlot(-1);
        }

        public bool SelectParcel(int district, int plot)
        {
            var definition = GameCatalog.Districts[district].projects[plot];
            var legacy = session.Economy.FindProject(district, definition.id);
            if (legacy.completed || legacy.startedUtc > 0) { selectedSite = selectedBuilding = null; return false; }
            Cancel();
            selectedSite = CityDevelopmentService.SiteId(district, plot); selectedBuilding = null;
            session.SelectPlot(-1);
            ui.Refresh();
            return true;
        }

        public bool TryPick(Ray ray)
        {
            if (!Physics.Raycast(ray, out var hit, 1600)) return false;
            foreach (var building in Rules.Data.buildings)
                if (buildings.OwnsHit(building.id, hit.collider.transform))
                {
                    if (!session.State.districts[building.district].unlocked) return true;
                    Cancel(); selectedSite = null; selectedBuilding = building.id;
                    session.SelectPlot(-1); ui.Refresh();
                    return true;
                }
            return false;
        }

        public void StartClear()
        {
            var parcel = Parcel(selectedSite);
            if (parcel == null) return;
            string depot = null; float shortest = float.MaxValue;
            Action<string, Vector3> consider = (id, position) =>
            {
                var route = world.Roads.FindEquipmentRoute(position, parcel.position,
                    roadId => RoadEconomy.SpeedMultiplier(RoadEconomy.GetLevel(session.State, roadId)));
                if (route != null && route.IsReachable && route.Length < shortest)
                { shortest = route.Length; depot = id; }
            };
            if (session.State.factoryLevel > 0 && !Rules.Data.dynamicFactoryProvided)
                consider("central", world.CentralDepotPosition);
            foreach (var building in Rules.Data.buildings)
                if (building.completed && CityBuildingCatalog.Find(building.definitionId).EquipmentDepot)
                    consider(building.id, DepotPoint(building));
            if (depot == null) { session.Notify("ابنِ مصنعًا أو مخزنًا جاهزًا للمعدات أولاً"); return; }
            string idToClear = selectedSite;
            session.Perform(e =>
            {
                var result = Rules.Clear(idToClear, depot, session.Now);
                if (result.success) world.Fleet.BeginDepotDispatch();
                return result;
            });
        }

        public void Collect()
        {
            if (selectedBuilding != null) session.Perform(e => Rules.Collect(selectedBuilding, session.Now));
        }

        public void ClaimRegion()
        {
            int d = FocusDistrict;
            if (d >= 0) session.Perform(e => e.ClaimDistrictReward(d, session.Now));
        }

        public void BuildOnSelectedLand()
        {
            var parcel = Parcel(selectedSite);
            if (parcel == null) return;
            // Keep the selected site as the initial store location.
            Vector3 target = parcel.position;
            ui.OpenStore(target);
        }

        internal string ValidateLand(CityBuildingDefinition definition, int district, float x, float z, int rotation)
        {
            if (definition == null || district < 0 || district >= session.State.districts.Length)
                return "اختر أرضًا داخل المدينة، بعيدًا عن البحر";
            if (!session.State.districts[district].unlocked) return "الحي مقفل؛ أكمل الحي السابق واستلم مكافأته";
            float width = (rotation == 0 ? definition.widthMeters : definition.depthMeters) / 20f;
            float depth = (rotation == 0 ? definition.depthMeters : definition.widthMeters) / 20f;
            // Dense road sampling includes the entire footprint, not just its centre.
            int nx = Mathf.CeilToInt(width / .08f), nz = Mathf.CeilToInt(depth / .08f);
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float px = x - width * .5f + width * i / nx;
                    float pz = z - depth * .5f + depth * j / nz;
                    var point = new Vector3(px, 0, pz);
                    if (!OnLand(px, pz)) return "جزء من المساحة خارج الأرض أو داخل البحر";
                    if (DistrictAt(point) != district) return "يجب أن تقع المساحة كاملة داخل حي واحد";
                    if (world.Roads.TryPick(point, .035f, out var road)) return "المساحة تتقاطع مع شارع؛ اختر موقعًا آخر";
                }
            foreach (var parcel in parcels)
                if (Mathf.Abs(x - parcel.position.x) < (width + parcel.width) * .5f &&
                    Mathf.Abs(z - parcel.position.z) < (depth + parcel.depth) * .5f)
                {
                    if (!Rules.Site(parcel.id).cleared) return "أزل دمار المبنى الموجود في هذه المساحة أولاً";
                    var project = session.Economy.FindProject(parcel.district, Rules.Site(parcel.id).projectId);
                    if (project.completed || project.startedUtc > 0) return "المساحة مشغولة بمشروع سابق";
                }
            foreach (var source in world.DevelopmentMap.buildings)
            {
                if (replacedSources.Contains(source.FeatureId)) continue;
                if (PolygonIntersects(source.outline, x, z, width, depth)) return "المساحة مشغولة بمبنى من الخريطة";
            }
            foreach (var area in world.DevelopmentMap.areas)
                if ((area.kind == "water" || area.kind == "sea") && PolygonIntersects(area.points, x, z, width, depth))
                    return "لا يمكن البناء على الماء";
            Vector3 depot = world.CentralDepotPosition;
            if (!Rules.Data.dynamicFactoryProvided &&
                Mathf.Abs(x - depot.x) < width * .5f + .5f && Mathf.Abs(z - depot.z) < depth * .5f + .5f)
                return "هذا الموقع محجوز للمصنع المركزي";
            return null;
        }

        private static bool PolygonIntersects(CityBasemapPoint[] polygon, float x, float z, float width, float depth)
        {
            float xmin = x - width * .5f, xmax = x + width * .5f, zmin = z - depth * .5f, zmax = z + depth * .5f;
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                var a = polygon[previous]; var b = polygon[i];
                if (a.x >= xmin && a.x <= xmax && a.z >= zmin && a.z <= zmax) return true;
                if ((a.z > z) != (b.z > z) && x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x) inside = !inside;
                float low = 0, high = 1;
                if (ClipSegment(a.x, b.x - a.x, xmin, xmax, ref low, ref high) &&
                    ClipSegment(a.z, b.z - a.z, zmin, zmax, ref low, ref high)) return true;
            }
            return inside;
        }

        private static bool ClipSegment(float origin, float delta, float min, float max, ref float low, ref float high)
        {
            if (Mathf.Abs(delta) < .000001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            low = Mathf.Max(low, Mathf.Min(a, b)); high = Mathf.Min(high, Mathf.Max(a, b));
            return low <= high;
        }

        private void OnDestroy()
        {
            if (session != null) session.Changed -= Refresh;
            buildings?.Dispose();
            if (session != null && session.Economy != null) session.Economy.ClearingCrewReady = null;
            if (session != null && session.Economy != null) session.Economy.LegacyLandReady = null;
        }
    }
}