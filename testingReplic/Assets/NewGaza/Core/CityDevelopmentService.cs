using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    // Unity-independent rules. Terrain/road queries are supplied by the world at confirmation.
    public sealed class CityDevelopmentService
    {
        private readonly EconomyService economy;
        public GameState State => economy.State;
        public CityDevelopmentState Data => State.development;
        public const int MaximumBuildings = 1024;

        public CityDevelopmentService(EconomyService economy)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            if (State.development == null)
                State.development = new CityDevelopmentState { legacyProgress = true };
            if (!Data.initialized)
            {
                if (Data.schema != 1 || Data.buildings == null || Data.buildings.Length != 0 ||
                    Data.rubble == null || Data.rubble.Length != 0 || Data.activeRubbleId != null)
                    throw new InvalidOperationException("بيانات البناء غير صالحة؛ تم الحفاظ على الحفظ");
                var sites = new List<RubbleSiteState>();
                for (int d = 0; d < State.districts.Length; d++)
                {
                    var district = State.districts[d];
                    int clearedCount = district.clearedLoads * district.projects.Length / GameCatalog.Districts[d].rubbleLoads;
                    for (int p = 0; p < district.projects.Length; p++)
                        sites.Add(new RubbleSiteState { id = SiteId(d, p), district = d,
                            projectId = district.projects[p].id,
                            cleared = p < clearedCount || district.projects[p].completed || district.projects[p].startedUtc > 0 });
                }
                Data.rubble = sites.ToArray();
                Data.initialized = true;
            }
            Validate(State);
        }

        public static string SiteId(int district, int plot) =>
            GameCatalog.Districts[district].id + "/" + GameCatalog.Districts[district].projects[plot].id;

        public RubbleSiteState Site(string id) => Array.Find(Data.rubble, s => s.id == id);
        public PlacedBuildingState Building(string id) => Array.Find(Data.buildings, b => b.id == id);

        public ActionResult Build(string definitionId, int district, float x, float z, int turn,
            long now, Func<CityBuildingDefinition, int, float, float, int, string> validateLand)
        {
            if (now < State.lastSeenUtc || now < 0) return ActionResult.Fail("وقت الجهاز غير صالح");
            economy.Tick(now);
            var definition = CityBuildingCatalog.Find(definitionId);
            if (definition == null) return ActionResult.Fail("المبنى غير معروف");
            if (district < 0 || district >= State.districts.Length || !State.districts[district].unlocked)
                return ActionResult.Fail("البناء مسموح داخل الأحياء المفتوحة فقط");
            if (!Finite(x) || !Finite(z) || (turn != 0 && turn != 1)) return ActionResult.Fail("موقع المبنى غير صالح");
            if (Data.buildings.Length >= MaximumBuildings) return ActionResult.Fail("وصلت إلى سعة المدينة");
            if (validateLand == null) return ActionResult.Fail("لم تُحمّل بيانات الأرض");
            string blocked = validateLand(definition, district, x, z, turn);
            if (!string.IsNullOrEmpty(blocked)) return ActionResult.Fail(blocked);
            float width = (turn == 0 ? definition.widthMeters : definition.depthMeters) / 20f;
            float depth = (turn == 0 ? definition.depthMeters : definition.widthMeters) / 20f;
            foreach (var building in Data.buildings)
                if (Overlaps(x, z, width, depth, building)) return ActionResult.Fail("المساحة تتداخل مع مبنى آخر أو موقع بناء");
            if (State.coins < definition.cost || State.stock.concrete < definition.concrete || State.stock.iron < definition.iron)
                return ActionResult.Fail("العملات أو الخرسانة أو الحديد غير كافية");
            if (now > long.MaxValue - definition.duration) return ActionResult.Fail("وقت الجهاز غير صالح");
            var placed = new PlacedBuildingState { id = Guid.NewGuid().ToString("N"), definitionId = definition.id,
                district = district, x = x, z = z, quarterTurn = turn, startedUtc = now, finishUtc = now + definition.duration };
            var expanded = new PlacedBuildingState[Data.buildings.Length + 1];
            Array.Copy(Data.buildings, expanded, Data.buildings.Length);
            expanded[expanded.Length - 1] = placed;
            State.coins -= definition.cost;
            State.stock.concrete -= definition.concrete;
            State.stock.iron -= definition.iron;
            Data.buildings = expanded;
            return ActionResult.Ok("بدأ بناء " + definition.name + " في الموقع المحدد");
        }

        public ActionResult Clear(string siteId, string depotId, long now)
        {
            if (now < State.lastSeenUtc || now < 0) return ActionResult.Fail("وقت الجهاز غير صالح");
            economy.Tick(now);
            var site = Site(siteId);
            if (site == null || site.cleared) return ActionResult.Fail("هذا الموقع نظيف بالفعل أو غير معروف");
            if (!State.districts[site.district].unlocked) return ActionResult.Fail("الحي مقفل");
            if (depotId != "central")
            {
                var depot = Building(depotId);
                if (depot == null || !depot.completed || !CityBuildingCatalog.Find(depot.definitionId).EquipmentDepot)
                    return ActionResult.Fail("لا يوجد مصنع أو مخزن معدات جاهز للانطلاق");
            }
            else if (State.factoryLevel == 0 || Data.dynamicFactoryProvided)
                return ActionResult.Fail("المصنع المركزي غير متاح");
            var result = economy.StartSalvage(site.district, now);
            if (!result.success) return result;
            Data.activeRubbleId = siteId;
            Data.dispatchDepotId = depotId;
            Data.crewArrived = false;
            return ActionResult.Ok("انطلقت الآليات إلى المبنى المهدّم عبر الشوارع؛ تبدأ الإزالة بعد وصولها");
        }

        public ActionResult Collect(string buildingId, long now)
        {
            if (now < State.lastSeenUtc || now < 0) return ActionResult.Fail("وقت الجهاز غير صالح");
            economy.Tick(now);
            var building = Building(buildingId);
            long amount = PendingIncome(building, State.lastSeenUtc);
            if (amount <= 0) return ActionResult.Fail("لا يوجد دخل جاهز بعد");
            if (State.coins > long.MaxValue - amount) return ActionResult.Fail("الرصيد ممتلئ");
            long hours = (State.lastSeenUtc - building.lastIncomeUtc) / 3600;
            State.coins += amount;
            building.lastIncomeUtc += hours * 3600;
            return ActionResult.Ok("تم جمع دخل المبنى");
        }

        public static long PendingIncome(PlacedBuildingState building, long now)
        {
            if (building == null || !building.completed || now <= building.lastIncomeUtc) return 0;
            long hours = (now - building.lastIncomeUtc) / 3600;
            long income = CityBuildingCatalog.Find(building.definitionId).hourlyIncome;
            return income == 0 ? 0 : hours > long.MaxValue / income ? long.MaxValue : hours * income;
        }

        public static void Advance(GameState state, long effective)
        {
            if (state.development == null || !state.development.initialized) return;
            foreach (var building in state.development.buildings)
                if (!building.completed && building.finishUtc <= effective)
                {
                    building.completed = true;
                    building.lastIncomeUtc = building.finishUtc;
                    if (CityBuildingCatalog.Find(building.definitionId).category == BuildingCategory.Recycling && state.factoryLevel == 0)
                    {
                        state.factoryLevel = 1;
                        state.development.dynamicFactoryProvided = true;
                    }
                }
        }

        public static void CompleteSalvage(GameState state)
        {
            var data = state.development;
            if (data == null || !data.initialized) return;
            RubbleSiteState site = string.IsNullOrEmpty(data.activeRubbleId)
                ? Array.Find(data.rubble, s => s.district == state.jobDistrict && !s.cleared)
                : Array.Find(data.rubble, s => s.id == data.activeRubbleId);
            if (site != null) site.cleared = true;
            data.activeRubbleId = null;
            data.crewArrived = false;
        }

        // Real geographic east/west (x), not the array index or the camera rotation.
        public static int[] Needs(int district)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int d = 0; d < GameCatalog.NeighborhoodCount; d++)
            { float x = GameGeography.DistrictPoint(d).x; min = Math.Min(min, x); max = Math.Max(max, x); }
            float west = (max - GameGeography.DistrictPoint(district).x) / Math.Max(.001f, max - min);
            if (west < .34f) return new[] { 2, 2, 2, 1, 0, 0 };
            if (west < .67f) return new[] { 3, 1, 1, 2, 1, 1 };
            return new[] { 4, 0, 1, 3, 2, 2 };
        }

        // Housing, agriculture, industry, commerce, recreation, urban/tourist housing.
        public static int[] Supplied(GameState state, int district)
        {
            var supplied = new int[6];
            if (state.development != null && state.development.initialized)
                foreach (var building in state.development.buildings)
                    if (building.district == district && building.completed)
                    {
                        var definition = CityBuildingCatalog.Find(building.definitionId);
                        if (definition.category == BuildingCategory.Residential)
                        {
                            supplied[0] += Math.Max(1, definition.floors / 2);
                            if (definition.floors >= 4 || definition.id == "tourist_villa") supplied[5]++;
                        }
                        if (definition.category == BuildingCategory.Agricultural && definition.id != "ornamental_trees" && definition.id != "protective_trees") supplied[1]++;
                        if (definition.category == BuildingCategory.Economic || definition.category == BuildingCategory.Recycling) supplied[2]++;
                        if (definition.category == BuildingCategory.Commercial) supplied[3]++;
                        if (definition.category == BuildingCategory.Recreation || definition.id == "ornamental_trees") supplied[4]++;
                    }
            foreach (var project in state.districts[district].projects)
                if (project.completed)
                {
                    if (project.id == "housing") supplied[0]++;
                    if (project.id == "farm") supplied[1]++;
                    if (project.id == "industry") supplied[2]++;
                    if (project.id == "commerce") supplied[3]++;
                    if (project.id == "park") supplied[4]++;
                }
            return supplied;
        }

        public static float Progress(GameState state, int district)
        {
            if (!state.districts[district].unlocked) return 0;
            if (state.districts[district].rewardClaimed) return 1;
            int sites = 0, cleared = 0;
            foreach (var site in state.development.rubble)
                if (site.district == district) { sites++; if (site.cleared) cleared++; }
            int[] needs = Needs(district), supplied = Supplied(state, district);
            int total = 0, met = 0;
            for (int n = 0; n < needs.Length; n++) { total += needs[n]; met += Math.Min(needs[n], supplied[n]); }
            return .3f * (sites == 0 ? 1f : (float)cleared / sites) + .7f * (total == 0 ? 1f : (float)met / total);
        }

        public static bool Complete(GameState state, int district)
        {
            if (state.development == null || !state.development.initialized) return false;
            foreach (var site in state.development.rubble) if (site.district == district && !site.cleared) return false;
            var needs = Needs(district); var supplied = Supplied(state, district);
            for (int n = 0; n < needs.Length; n++) if (supplied[n] < needs[n]) return false;
            return true;
        }

        public static bool Overlaps(float x, float z, float width, float depth, PlacedBuildingState building)
        {
            var definition = CityBuildingCatalog.Find(building.definitionId);
            float bw = (building.quarterTurn == 0 ? definition.widthMeters : definition.depthMeters) / 20f;
            float bd = (building.quarterTurn == 0 ? definition.depthMeters : definition.widthMeters) / 20f;
            return Math.Abs(x - building.x) < (width + bw) * .5f + .03f &&
                Math.Abs(z - building.z) < (depth + bd) * .5f + .03f;
        }

        public static void Validate(GameState state)
        {
            var data = state.development;
            if (data == null) return;
            const string error = "بيانات مواقع البناء غير صالحة؛ لم تتم إعادة ضبط الحفظ";
            if (data.schema != 1 || data.buildings == null || data.rubble == null || data.buildings.Length > MaximumBuildings)
                throw new InvalidOperationException(error);
            if (!data.initialized)
            {
                if (data.buildings.Length != 0 || data.rubble.Length != 0 || data.activeRubbleId != null || data.dispatchDepotId != null)
                    throw new InvalidOperationException(error);
                return;
            }
            int expected = 0;
            foreach (var district in GameCatalog.Districts) expected += district.projects.Length;
            if (data.rubble.Length != expected) throw new InvalidOperationException(error);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var site in data.rubble)
            {
                if (site == null || site.district < 0 || site.district >= state.districts.Length ||
                    Array.Find(GameCatalog.Districts[site.district].projects, p => p.id == site.projectId) == null ||
                    site.id != GameCatalog.Districts[site.district].id + "/" + site.projectId || !ids.Add(site.id) ||
                    (site.cleared && !state.districts[site.district].unlocked)) throw new InvalidOperationException(error);
            }
            ids.Clear();
            foreach (var building in data.buildings)
            {
                var definition = building == null ? null : CityBuildingCatalog.Find(building.definitionId);
                if (definition == null || string.IsNullOrEmpty(building.id) || !ids.Add(building.id) ||
                    building.district < 0 || building.district >= state.districts.Length || !state.districts[building.district].unlocked ||
                    !Finite(building.x) || !Finite(building.z) || Math.Abs(building.x) > 10000 || Math.Abs(building.z) > 10000 ||
                    (building.quarterTurn != 0 && building.quarterTurn != 1) || building.startedUtc < 0 ||
                    building.startedUtc > state.lastSeenUtc || building.finishUtc <= building.startedUtc ||
                    building.finishUtc - building.startedUtc != definition.duration ||
                    (building.completed && (building.finishUtc > state.lastSeenUtc || building.lastIncomeUtc < building.finishUtc || building.lastIncomeUtc > state.lastSeenUtc)) ||
                    (!building.completed && building.lastIncomeUtc != 0)) throw new InvalidOperationException(error);
            }
            if (data.activeRubbleId != null)
            {
                var site = Array.Find(data.rubble, s => s.id == data.activeRubbleId);
                if (site == null || site.cleared || site.district != state.jobDistrict || state.jobStage == JobStage.Idle || data.dispatchDepotId == null)
                    throw new InvalidOperationException(error);
            }
            else if (data.crewArrived) throw new InvalidOperationException(error);
            if (data.dispatchDepotId != null && data.dispatchDepotId != "central")
            {
                var depot = Array.Find(data.buildings, b => b.id == data.dispatchDepotId);
                if (depot == null || !depot.completed || !CityBuildingCatalog.Find(depot.definitionId).EquipmentDepot)
                    throw new InvalidOperationException(error);
            }
        }

        public static void ValidateSpatial(GameState state)
        {
            if (state.development == null || !state.development.initialized) return;
            var buildings = state.development.buildings;
            var widths = new float[buildings.Length]; var depths = new float[buildings.Length];
            for (int i = 0; i < buildings.Length; i++)
            {
                var definition = CityBuildingCatalog.Find(buildings[i].definitionId);
                widths[i] = (buildings[i].quarterTurn == 0 ? definition.widthMeters : definition.depthMeters) / 20f;
                depths[i] = (buildings[i].quarterTurn == 0 ? definition.depthMeters : definition.widthMeters) / 20f;
            }
            for (int i = 0; i < buildings.Length; i++)
                for (int j = i + 1; j < buildings.Length; j++)
                    if (Math.Abs(buildings[i].x - buildings[j].x) < (widths[i] + widths[j]) * .5f + .03f &&
                        Math.Abs(buildings[i].z - buildings[j].z) < (depths[i] + depths[j]) * .5f + .03f)
                        throw new InvalidOperationException("تداخل في مواقع المباني المحفوظة؛ لم تتم إعادة ضبط تقدمك");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}