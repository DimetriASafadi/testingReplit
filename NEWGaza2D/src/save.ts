import { BUILDINGS, DISTRICTS, EQUIPMENT, projects } from './catalog';
import type { GameState } from './model';
import { cityLayout, LOT_COUNT } from './city-layout';
import { isOrientation } from './orientation';
import { MIN_CAMERA_ZOOM } from './camera-overview';
import { roadsFor } from './roads';
import { isDifficulty, PAVER_COST } from './economy';
import { WHEEL_COOLDOWN, WHEEL_PRIZES } from './rewards';

/** Reject damaged domain data; leave the original storage entry untouched. */
export function validateSave(value: unknown): asserts value is GameState {
  const require = (condition: unknown): void => { if (!condition) throw new Error('Invalid save') };
  const integer = (n: unknown) => Number.isSafeInteger(n) && (n as number) >= 0;
  require(value && typeof value === 'object');
  const s = value as GameState;
  require(s.version === 1 && integer(s.coins) && integer(s.lastSeen) && integer(s.sequence));
  require(s.gems === undefined || integer(s.gems));
  require(s.expansionVersion===undefined || s.expansionVersion===1);
  require(s.exchanges===undefined || (Array.isArray(s.exchanges)&&s.exchanges.every(x=>typeof x==='string'&&x.length>=8&&x.length<=100)&&new Set(s.exchanges).size===s.exchanges.length));
  if(s.wheel)require(integer(s.wheel.startedAt)&&s.wheel.startedAt<=s.lastSeen&&s.wheel.nextAt===s.wheel.startedAt+WHEEL_COOLDOWN&&integer(s.wheel.result)&&!!WHEEL_PRIZES[s.wheel.result]);
  if(s.pavers!==undefined){
    require(Array.isArray(s.pavers));
    require(new Set(s.pavers.map(p=>p.id)).size===s.pavers.length);
    for(const p of s.pavers)require(/^paver-[1-9]\d*$/.test(p.id)&&Number(p.id.slice(6))<=s.sequence&&p.cost===PAVER_COST);
  }
  require(s.inventory && ['concrete','iron','wood','other','value'].every(k => integer(s.inventory[k as keyof typeof s.inventory])));
  require(s.inventory.value === s.inventory.concrete * 20 + s.inventory.iron * 60 + s.inventory.wood * 35 + s.inventory.other * 10);
  require(Array.isArray(s.districts));
  // Only the exact former roster is accepted, not arbitrary missing/unknown districts.
  const roster = s.districts.length === DISTRICTS.length - 1 && !s.districts.some(d => d?.id === 'shati')
    ? DISTRICTS.filter(d => d.id !== 'shati') : DISTRICTS;
  require(s.districts.length === roster.length);
  require(Array.isArray(s.units) && Array.isArray(s.jobs));
  const unique = (ids: string[]) => ids.every(id => typeof id === 'string' && id.length > 0) && new Set(ids).size === ids.length;
  require(unique(s.units.map(u => u.id)) && unique(s.jobs.map(j => j.id)));
  for (const item of [...s.units, ...s.jobs]) {
    const match = /^(?:unit|job)-([1-9]\d*)$/.exec(item.id);
    require(match && integer(Number(match[1])) && Number(match[1]) <= s.sequence);
  }
  for (const u of s.units) require(EQUIPMENT[u.kind] && u.purchasePrice === EQUIPMENT[u.kind].cost && integer(u.level) && u.level >= 1 && u.level <= 4);
  for (const [i,d] of s.districts.entries()) {
    require(d.difficulty===undefined || isDifficulty(d.difficulty));
    require(d.difficultyChosen===undefined || typeof d.difficultyChosen==='boolean');
    require(d.celebrated===undefined || typeof d.celebrated==='boolean');
    const roadIds=new Set(roadsFor(d.id).map(r=>r.id));
    if(d.pavedRoads!==undefined)require(Array.isArray(d.pavedRoads)&&new Set(d.pavedRoads).size===d.pavedRoads.length&&d.pavedRoads.every(id=>roadIds.has(id)));
    if(d.roadWorks!==undefined){
      require(Array.isArray(d.roadWorks)&&new Set(d.roadWorks.map(w=>w.roadId)).size===d.roadWorks.length);
      for(const w of d.roadWorks){
        require(roadIds.has(w.roadId)&&s.pavers?.some(p=>p.id===w.machineId));
        require(integer(w.factoryId)&&d.plots[w.factoryId]?.status==='built'&&d.plots[w.factoryId]?.buildingId==='asphalt');
        require([w.start,w.arrival,w.workEnd,w.returnEnd].every(integer)&&w.start<=s.lastSeen&&w.arrival>w.start&&w.workEnd>w.arrival&&w.returnEnd>w.workEnd);
        require(Array.isArray(w.route)&&w.route.length>=2&&w.route.every(p=>Array.isArray(p)&&p.length===2&&p.every(n=>Number.isFinite(n)&&Math.abs(n)<200)));
      }
    }
    require(d && d.id === roster[i].id && typeof d.unlocked === 'boolean' && typeof d.claimed === 'boolean');
    require(!d.claimed || d.unlocked);
    const predecessor = s.districts[i - 1];
    // Grandfather access to Rashid earned before Shati was inserted.
    require(d.unlocked === (i === 0 || predecessor.claimed) ||
      (d.id === 'rashid' && d.unlocked && predecessor?.id === 'shati' && predecessor.unlocked));
    require(d.camera === null || (typeof d.camera === 'object' && Number.isFinite(d.camera.x) && Number.isFinite(d.camera.y) && d.camera.zoom >= MIN_CAMERA_ZOOM && d.camera.zoom <= 4));
    require(Array.isArray(d.plots) && [64, LOT_COUNT].includes(d.plots.length) && Array.isArray(d.projects) && d.projects.length === 9);
    const legacy = d.plots.length === 64;
    if (!legacy) require([cityLayout(d.id), cityLayout(d.id,1)].some(layout =>
      d.plots.every((p,j) => p && p.x === layout.cells[j].x && p.y === layout.cells[j].y)));
    for (const [j,p] of d.plots.entries()) {
      require(p.incomeFactor===undefined || [1,.7,.45,.25].includes(p.incomeFactor));
      require(p.accelerated === undefined || typeof p.accelerated === 'boolean');
      require(p && p.id === j && (legacy
        ? p.layoutId === undefined && p.x === (j % 8) * 2 && p.y === Math.floor(j / 8) * 2
        : p.layoutId === d.id));
      require(['rubble','empty','building','built'].includes(p.status) && BUILDINGS.some(b => b.id === p.buildingId));
      require(isOrientation(p.orientation) && integer(p.startedAt) && integer(p.endsAt) && integer(p.incomeAt));
      require(p.paidCost === undefined || integer(p.paidCost));
      require(p.status !== 'building' || (p.endsAt > p.startedAt && p.startedAt <= s.lastSeen));
    }
    const defs = projects(d.id === 'rashid');
    for (const [j,p] of d.projects.entries()) {
      require(p.gemRewarded === undefined || typeof p.gemRewarded === 'boolean');
      require(p && p.id === defs[j].id && ['idle','building','ready','complete'].includes(p.status));
      require(integer(p.startedAt) && integer(p.endsAt) && integer(p.incomeAt));
      require(p.status !== 'building' || (p.endsAt > p.startedAt && p.startedAt <= s.lastSeen));
    }
  }
  for (const u of s.units) if (u.depotIds !== undefined) {
    require(u.depotIds && typeof u.depotIds === 'object' && !Array.isArray(u.depotIds));
    for (const [districtId, plotId] of Object.entries(u.depotIds)) {
      const d = s.districts.find(d => d.id === districtId);
      require(d && integer(plotId));
      const p = d?.plots.find(p => p.id === plotId);
      require(p?.status === 'built' && p.buildingId === 'recycling');
    }
  }
  require(s.currentDistrict === null || s.districts.some(d => d.id === s.currentDistrict && d.unlocked));
  const busy: string[] = [];
  const sites: string[] = [];
  for (const j of s.jobs) {
    const d = s.districts.find(d => d.id === j.districtId);
    require(d?.unlocked && integer(j.plotId) && j.plotId < d!.plots.length && integer(j.originPlotId) && j.originPlotId < d!.plots.length);
    const origin = d!.plots[j.originPlotId];
    require(origin.status === 'built' && origin.buildingId === (j.recyclingPlotId === undefined ? 'recycling' : 'equipment_store'));
    if (j.recyclingPlotId !== undefined) {
      require(j.insideUnloading === undefined || typeof j.insideUnloading === 'boolean');
      require(integer(j.recyclingPlotId) && j.recyclingPlotId < d!.plots.length);
      const recycler = d!.plots[j.recyclingPlotId];
      require(recycler.status === 'built' && recycler.buildingId === 'recycling');
      require([j.toolsReturnEnd,j.unloadAt,j.unloadStartAt,j.unloadEnd,j.truckHomeDepartureAt].every(integer));
      require(j.toolsReturnEnd! > j.workEnd && j.toolsReturnEnd! <= j.returnEnd);
      require(j.unloadAt! > j.workEnd && j.unloadStartAt! >= j.unloadAt! && j.unloadEnd! - j.unloadStartAt! === 6000);
      require(j.truckHomeDepartureAt! >= j.unloadEnd! && j.returnEnd > j.truckHomeDepartureAt!);
      require(Number.isFinite(j.travelSpeed) && j.travelSpeed! > 0 && typeof j.delivered === 'boolean');
      require(j.unloadQueueDistance === undefined || (Number.isFinite(j.unloadQueueDistance) && j.unloadQueueDistance > 0));
    }
    require(Array.isArray(j.unitIds) && j.unitIds.length === 3 && unique(j.unitIds));
    const team = j.unitIds.map(id => s.units.find(u => u.id === id));
    require(team.every(Boolean) && new Set(team.map(u => u!.kind)).size === 3);
    require(team.every(u => u!.depotIds?.[d!.id] === undefined || u!.depotIds[d!.id] === (j.recyclingPlotId ?? j.originPlotId) ||
      (u!.kind !== 'truck' && j.toolsReturnEnd !== undefined && s.lastSeen >= j.toolsReturnEnd)));
    require([j.start,j.arrival,j.workEnd,j.returnEnd,j.value].every(integer));
    require(j.start <= s.lastSeen && j.start < j.arrival && [60000,69000,78000].includes(j.workEnd - j.arrival) && j.returnEnd > j.workEnd);
    require(j.departureAt === undefined || (integer(j.departureAt) && j.departureAt >= j.start && j.departureAt < j.arrival));
    require(j.returnStartAt === undefined || (integer(j.returnStartAt) && j.returnStartAt >= j.workEnd && j.returnStartAt < j.returnEnd));
    require(typeof j.cleared === 'boolean' && (j.cleared || d!.plots[j.plotId].status === 'rubble'));
    busy.push(...j.unitIds.filter(id => {
      const u = s.units.find(u => u.id === id)!;
      return u.kind === 'truck' || j.toolsReturnEnd === undefined || s.lastSeen < j.toolsReturnEnd;
    })); sites.push(`${j.districtId}:${j.plotId}`);
  }
  require(new Set(busy).size === busy.length && new Set(sites).size === sites.length);
  const pavingBusy=s.districts.flatMap(d=>(d.roadWorks??[]).map(w=>w.machineId));
  require(new Set(pavingBusy).size===pavingBusy.length);
}
