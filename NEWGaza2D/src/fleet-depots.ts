import type { DistrictState, EquipmentKind, GameState, Plot, Unit } from './model';

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
      .flatMap(j => j.unitIds.map(id => [id, j.originPlotId] as const)));
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
  const busy = new Set(state.jobs.flatMap(j => j.unitIds));
  const candidates = recyclingDepots(d).map(depot => ({
    depot,
    team: (['excavator', 'bulldozer', 'truck'] as EquipmentKind[]).map(kind =>
      state.units.find(u => u.kind === kind && !busy.has(u.id) && u.depotIds?.[d.id] === depot.id)),
    active: state.jobs.filter(j => j.districtId === d.id && j.originPlotId === depot.id).length,
    distance: Math.abs(target.x - depot.x) + Math.abs(target.y - depot.y),
  })).filter(v => v.team.every(Boolean));
  candidates.sort((a, b) => a.active - b.active || a.distance - b.distance || a.depot.id - b.depot.id);
  const chosen = candidates[0];
  return chosen ? { depot: chosen.depot, team: chosen.team as Unit[] } : null;
}
