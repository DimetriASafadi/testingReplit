import type { DistrictState, EquipmentKind, GameState, Unit } from './model';

export const warehousePlots = (d: DistrictState) =>
  d.plots.filter(p => p.status === 'built' && p.buildingId === 'equipment_store');
export function roadUnitBusy(state: GameState, id: string, now = state.lastSeen) {
  return state.districts.some(d => d.roadWorks?.some(w =>
    w.preparation?.unitId === id && now < w.preparation.dozerReturnEnd));
}
export function warehouseCapacity(state: GameState, d: DistrictState, kind: EquipmentKind) {
  const warehouses = warehousePlots(d);
  const units = state.units.filter(u => u.kind === kind && (!u.home || u.home.districtId === d.id));
  return { used: units.length, total: warehouses.length * 3 };
}
export function purchaseWarehouse(state: GameState, d: DistrictState, kind: EquipmentKind) {
  const capacity = warehouseCapacity(state, d, kind);
  if (capacity.used >= capacity.total) return undefined;
  return warehousePlots(d).find(p => state.units.filter(u =>
    u.kind === kind && u.home?.districtId === d.id && u.home.warehouseId === p.id).length < 3);
}
/** One permanent home per unit; legacy excess stays owned, never deleted.
 * Existing active trips pin the home to their departure warehouse. */
export function assignWarehouseHomes(state: GameState) {
  for (const u of state.units) {
    const active = state.jobs.find(j => j.unitIds.includes(u.id) && j.recyclingPlotId !== undefined &&
      state.lastSeen < (u.kind === 'truck' ? j.returnEnd : j.toolsReturnEnd!));
    if (active) { u.home = { districtId: active.districtId, warehouseId: active.originPlotId }; continue; }
    if (roadUnitBusy(state, u.id)) continue;
    const existing = state.districts.find(d => d.id === u.home?.districtId);
    if (existing && warehousePlots(existing).some(p => p.id === u.home!.warehouseId)) continue;
    // A sold warehouse leaves equipment owned in the same district.
    const d = existing ?? state.districts.find(d => d.id === state.currentDistrict && warehousePlots(d).length) ??
      state.districts.find(d => warehousePlots(d).length);
    if (!d) continue;
    const options = warehousePlots(d);
    if (!options.length) continue;
    const load = (id: number) => state.units.filter(v => v.kind === u.kind && v.home?.districtId === d.id && v.home.warehouseId === id).length;
    options.sort((a,b) => load(a.id)-load(b.id) || a.id-b.id);
    u.home = { districtId: d.id, warehouseId: options[0].id };
  }
}
export const localUnit = (u: Unit, d: DistrictState) => !u.home || u.home.districtId === d.id;
