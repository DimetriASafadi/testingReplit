import type { DistrictState, EquipmentKind, GameState, Unit } from './model';

export const warehousePlots = (d: DistrictState) =>
  d.plots.filter(p => p.status === 'built' && p.buildingId === 'equipment_store');
export function roadUnitBusy(state: GameState, id: string, now = state.lastSeen) {
  return state.districts.some(d => d.roadWorks?.some(w =>
    w.preparation?.unitId === id && now < w.preparation.dozerReturnEnd));
}
export function warehouseCapacity(state: GameState, d: DistrictState, kind: EquipmentKind) {
  const warehouses = warehousePlots(d);
  const units = state.units.filter(u => u.kind === kind);
  return { used: units.length, total: warehouses.length ? units.length : 0 };
}
export function purchaseWarehouse(state: GameState, d: DistrictState, kind: EquipmentKind) {
  return warehousePlots(d).sort((a,b)=>state.units.filter(u=>u.kind===kind&&u.home?.districtId===d.id&&u.home.warehouseId===a.id).length-
    state.units.filter(u=>u.kind===kind&&u.home?.districtId===d.id&&u.home.warehouseId===b.id).length)[0];
}
/** Ownership is global. Home is only the current dispatch/return portal.
 * Every active mission pins its portal until that machine actually returns. */
export function assignWarehouseHomes(state: GameState) {
  const destination=state.districts.find(d=>d.id===state.currentDistrict);
  const options=destination?warehousePlots(destination):[];
  const idle:Unit[]=[];
  for (const u of state.units) {
    const active = state.jobs.find(j => j.unitIds.includes(u.id) &&
      state.lastSeen < (u.kind === 'truck' ? j.returnEnd : (j.toolsReturnEnd??j.returnEnd)));
    if (active) { u.home = { districtId: active.districtId, warehouseId: active.originPlotId }; continue; }
    const road=state.districts.flatMap(d=>(d.roadWorks??[]).map(w=>({d,w}))).find(({w})=>w.preparation?.unitId===u.id && state.lastSeen<w.preparation.dozerReturnEnd);
    if(road) { u.home={districtId:road.d.id,warehouseId:road.w.preparation!.warehouseId};continue; }
    if(options.length) { delete u.home; idle.push(u); }
  }
  for(const u of idle) {
    const load = (id: number) => state.units.filter(v => v.kind === u.kind && v.home?.districtId === destination!.id && v.home.warehouseId === id).length;
    options.sort((a,b) => load(a.id)-load(b.id) || a.id-b.id);
    u.home = { districtId: destination!.id, warehouseId: options[0].id };
  }
}
export const localUnit = (u: Unit, d: DistrictState) => !u.home || u.home.districtId === d.id;
