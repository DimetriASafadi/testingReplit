import type { DistrictState, EquipmentKind, GameState, Plot, Unit } from './model';
import type { Job } from './model';

export const equipmentWarehouses = (d: DistrictState) =>
  d.plots.filter(p => p.buildingId === 'equipment_store' && p.status === 'built');
export function unitInJob(j: Job, u: Unit, now: number) {
  return j.unitIds.includes(u.id) && now < (u.kind !== 'truck' && j.toolsReturnEnd !== undefined ? j.toolsReturnEnd : j.returnEnd);
}

export const recyclingDepots = (d: DistrictState) =>
  d.plots.filter(p => p.buildingId === 'recycling' && p.status === 'built');

/** Balance each kind so complete clearing teams remain together. Busy teams
 * keep their original portal until their return is actually complete. */
export function balanceFleetDepots(state: GameState): void {
  for (const d of state.districts) {
    const depots = recyclingDepots(d);
    if (!depots.length) {
      // Keep the machines owned, without a dangling reference to a sold factory.
      for (const u of state.units) if (u.depotIds) delete u.depotIds[d.id];
      continue;
    }
    const pinned = new Map(state.jobs.filter(j => j.districtId === d.id)
      .flatMap(j => j.unitIds.filter(id => unitInJob(j, state.units.find(u => u.id === id)!, state.lastSeen))
        .map(id => [id, j.recyclingPlotId ?? j.originPlotId] as const)));
    for (const kind of ['excavator', 'bulldozer', 'truck'] as EquipmentKind[]) {
      const units = state.units.filter(u => u.kind === kind);
      const counts = new Map(depots.map(p => [p.id, 0]));
      const quota = new Map(depots.map((p, i) => [p.id,
        Math.floor(units.length / depots.length) + (i < units.length % depots.length ? 1 : 0)]));
      const assign = (u: Unit, id: number) => {
        (u.depotIds ??= {})[d.id] = id;
        counts.set(id, counts.get(id)! + 1);
      };
      for (const u of units) if (pinned.has(u.id)) assign(u, pinned.get(u.id)!);
      const pending: Unit[] = [];
      for (const u of units) if (!pinned.has(u.id)) {
        const id = u.depotIds?.[d.id];
        if (id !== undefined && counts.has(id) && counts.get(id)! < quota.get(id)!) assign(u, id);
        else pending.push(u);
      }
      for (const u of pending) {
        const candidates = depots.filter(p => counts.get(p.id)! < quota.get(p.id)!);
        const source = (candidates.length ? candidates : depots).reduce((best, p) =>
          counts.get(p.id)! < counts.get(best.id)! ? p : best);
        assign(u, source.id);
      }
    }
  }
}

export function availableDepotTeam(state: GameState, d: DistrictState, target: Plot) {
  const busy = new Set(state.units.filter(u => state.jobs.some(j => unitInJob(j, u, state.lastSeen))).map(u => u.id));
  const candidates = recyclingDepots(d).map(depot => ({
    depot,
    team: (['excavator', 'bulldozer', 'truck'] as EquipmentKind[]).map(kind =>
      state.units.find(u => u.kind === kind && !busy.has(u.id) && u.depotIds?.[d.id] === depot.id)),
    active: state.jobs.filter(j => j.districtId === d.id && (j.recyclingPlotId ?? j.originPlotId) === depot.id).length,
    distance: Math.abs(target.x - depot.x) + Math.abs(target.y - depot.y),
  })).filter(v => v.team.every(Boolean));
  candidates.sort((a, b) => a.active - b.active || a.distance - b.distance || a.depot.id - b.depot.id);
  const chosen = candidates[0];
  const warehouse = equipmentWarehouses(d).sort((a,b) =>
    Math.hypot(a.x - (chosen?.depot.x ?? target.x), a.y - (chosen?.depot.y ?? target.y)) -
    Math.hypot(b.x - (chosen?.depot.x ?? target.x), b.y - (chosen?.depot.y ?? target.y)) || a.id - b.id)[0];
  return chosen && warehouse ? { depot: chosen.depot, warehouse, team: chosen.team as Unit[] } : null;
}
