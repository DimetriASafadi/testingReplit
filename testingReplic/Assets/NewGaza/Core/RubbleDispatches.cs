using System;
using System.Collections.Generic;

namespace NewGaza.Core
{
    /// <summary>A reservation lasts through physical return, not just site cleanup.</summary>
    public static class RubbleDispatches
    {
        public static RubbleDispatchState[] Jobs(GameState state) =>
            state.development?.dispatches ?? Array.Empty<RubbleDispatchState>();
        public static bool Busy(GameState state, string unitId)
        {
            foreach (var job in Jobs(state))
                if (job.stage != JobStage.Recycling &&
                    (job.excavatorId == unitId || job.truckId == unitId || job.bulldozerId == unitId)) return true;
            // The old district-wide contract remains supported until delivered.
            if (state.jobStage != JobStage.Idle && state.equipmentUnits != null)
                foreach (string kind in new[] { "excavator", "truck", "bulldozer" })
                {
                    var first = Array.Find(state.equipmentUnits, u => u.kind == kind);
                    if (first != null && first.id == unitId) return true;
                }
            return false;
        }
        public static EquipmentUnitState[] Units(GameState state, string kind) =>
            Array.FindAll(state.equipmentUnits ?? Array.Empty<EquipmentUnitState>(), u => u.kind == kind);
        public static int AvailableTeams(GameState state)
        {
            var e = Units(state, "excavator"); var t = Units(state, "truck"); var b = Units(state, "bulldozer");
            int count = 0;
            for (int i = 0; i < Math.Min(e.Length, Math.Min(t.Length, b.Length)); i++)
                if (!Busy(state, e[i].id) && !Busy(state, t[i].id) && !Busy(state, b[i].id)) count++;
            return count;
        }
        public static RubbleDispatchState ForSite(GameState state, string siteId) =>
            Array.Find(Jobs(state), j => j.siteId == siteId);
        internal static ActionResult Start(GameState state, RubbleSiteState site, string depotId)
        {
            EquipmentEconomy.Ensure(state);
            if (ForSite(state, site.id) != null) return ActionResult.Fail("هذا الموقع له فريق يعمل بالفعل");
            var e = Units(state, "excavator"); var t = Units(state, "truck"); var b = Units(state, "bulldozer");
            if (e.Length == 0 || t.Length == 0 || b.Length == 0)
                return ActionResult.Fail("اشترِ المعدات اللازمة: حفارة وجرافة وشاحنة نقل");
            for (int i = 0; i < Math.Min(e.Length, Math.Min(t.Length, b.Length)); i++)
            {
                if (Busy(state, e[i].id) || Busy(state, t[i].id) || Busy(state, b[i].id)) continue;
                var jobs = new List<RubbleDispatchState>(Jobs(state));
                jobs.Add(new RubbleDispatchState {
                    id = Guid.NewGuid().ToString("N"), siteId = site.id, depotId = depotId, slot = i,
                    excavatorId = e[i].id, truckId = t[i].id, bulldozerId = b[i].id,
                    startedUtc = state.lastSeenUtc
                });
                state.development.dispatches = jobs.ToArray();
                return ActionResult.Ok("انطلق فريق مستقل: حفارة وجرافة وشاحنة؛ يبدأ عمل الدقيقة بعد وصوله");
            }
            return ActionResult.Fail("لا يوجد فريق متاح؛ يلزم حفارة وجرافة وشاحنة غير مشغولة، أو انتظر عودة فريق إلى المصنع");
        }
        internal static void CleanSite(GameState state, RubbleSiteState site)
        {
            if (site == null || site.cleared) return;
            site.cleared = true;
            var district = state.districts[site.district];
            if (district.clearedLoads < GameCatalog.Districts[site.district].rubbleLoads) district.clearedLoads++;
        }
        internal static void Validate(GameState state)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sites = new HashSet<string>(StringComparer.Ordinal);
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            var e = Units(state, "excavator"); var t = Units(state, "truck"); var b = Units(state, "bulldozer");
            if (Jobs(state).Length > 12000) throw new InvalidOperationException("عدد مهام الآليات غير صالح");
            foreach (var job in Jobs(state))
            {
                if (job == null || string.IsNullOrEmpty(job.id) || !ids.Add(job.id) ||
                    !sites.Add(job.siteId ?? "") || job.slot < 0 || job.slot >= Math.Min(e.Length, Math.Min(t.Length, b.Length)) ||
                    e[job.slot].id != job.excavatorId || t[job.slot].id != job.truckId || b[job.slot].id != job.bulldozerId ||
                    job.startedUtc < 0 || job.startedUtc > state.lastSeenUtc || job.finishUtc < 0 ||
                    job.stage < JobStage.Clearing || job.stage > JobStage.Recycling)
                    throw new InvalidOperationException("بيانات فرق إزالة الركام غير صالحة؛ تم الحفاظ على الحفظ");
                var site = Array.Find(state.development.rubble, s => s.id == job.siteId);
                var depot = Array.Find(state.development.buildings, d => d.id == job.depotId);
                if (site == null || (job.stage == JobStage.Clearing ? site.cleared : !site.cleared) ||
                    (job.stage != JobStage.Clearing && !job.crewArrived) ||
                    (job.stage == JobStage.Clearing && job.crewArrived &&
                        (job.finishUtc <= job.startedUtc || job.finishUtc - 60 > state.lastSeenUtc)) ||
                    (job.stage == JobStage.Clearing && !job.crewArrived && job.finishUtc != 0) ||
                    (job.depotId == "central" ? state.factoryLevel == 0 || state.development.dynamicFactoryProvided :
                        depot == null || !depot.completed || !CityBuildingCatalog.Find(depot.definitionId).EquipmentDepot))
                    throw new InvalidOperationException("موقع أو مصنع فريق الإزالة غير صالح؛ تم الحفاظ على الحفظ");
                if (job.stage != JobStage.Recycling)
                {
                    if (!reserved.Add(job.excavatorId) || !reserved.Add(job.truckId) || !reserved.Add(job.bulldozerId) ||
                        (state.jobStage != JobStage.Idle && job.slot == 0))
                        throw new InvalidOperationException("آلية محجوزة لمهمتين؛ تم الحفاظ على الحفظ");
                }
                PresentationSaveValidation.ValidateFleet(job.fleet);
            }
        }
        public static string Status(RubbleDispatchState job) => job.stage == JobStage.Clearing ?
            (job.crewArrived ? "إزالة الدمار جارية" : "الفريق في الطريق إلى الموقع") :
            job.stage == JobStage.Hauling ? "الموقع نظيف — الفريق عائد إلى المصنع وغير متاح بعد" :
            "الفريق عاد — بع الموارد لإفساح مكان للحمولة";
    }

    public sealed partial class EconomyService
    {
        public Func<RubbleDispatchState, bool> SiteCrewReady { get; set; }
        public Func<RubbleDispatchState, bool> SiteDepotReady { get; set; }
        private void TickRubbleDispatches(long effective)
        {
            var remaining = new List<RubbleDispatchState>();
            foreach (var job in RubbleDispatches.Jobs(State))
            {
                var site = Array.Find(State.development.rubble, s => s.id == job.siteId);
                if (job.stage == JobStage.Clearing)
                {
                    if (!job.crewArrived && (SiteCrewReady?.Invoke(job) ?? ClearingCrewReady?.Invoke() ?? false))
                    {
                        job.crewArrived = true;
                        job.finishUtc = AddTime(effective, 60);
                    }
                    if (job.crewArrived && job.finishUtc <= effective)
                    {
                        RubbleDispatches.CleanSite(State, site);
                        job.stage = JobStage.Hauling;
                        job.finishUtc = 0;
                    }
                }
                // Return has no wall-clock shortcut: saved roots must really reach
                // their depot. A cleaned site and a busy crew are independent.
                if (job.stage == JobStage.Hauling && (SiteDepotReady?.Invoke(job) ?? HaulingCrewReady?.Invoke() ?? false))
                    job.stage = JobStage.Recycling;
                if (job.stage == JobStage.Recycling)
                {
                    var yield = RubbleEconomy.Yield(State, site);
                    if (FitsStock(yield.concrete, yield.iron, yield.wood, yield.other))
                    {
                        AddStock(yield.concrete, yield.iron, yield.wood, yield.other);
                        continue;
                    }
                }
                remaining.Add(job);
            }
            if (State.development != null) State.development.dispatches = remaining.ToArray();
        }
    }
}
