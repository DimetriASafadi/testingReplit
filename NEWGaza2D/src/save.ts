import { BUILDINGS, DISTRICTS, EQUIPMENT, projects } from './catalog';
import type { GameState } from './model';

/** Reject damaged domain data; leave the original storage entry untouched. */
export function validateSave(value: unknown): asserts value is GameState {
  const require = (condition: unknown): void => { if (!condition) throw new Error('Invalid save') };
  const integer = (n: unknown) => Number.isSafeInteger(n) && (n as number) >= 0;
  require(value && typeof value === 'object');
  const s = value as GameState;
  require(s.version === 1 && integer(s.coins) && integer(s.lastSeen) && integer(s.sequence));
  require(s.inventory && ['concrete','iron','wood','other','value'].every(k => integer(s.inventory[k as keyof typeof s.inventory])));
  require(s.inventory.value === s.inventory.concrete * 20 + s.inventory.iron * 60 + s.inventory.wood * 35 + s.inventory.other * 10);
  require(Array.isArray(s.districts) && s.districts.length === DISTRICTS.length);
  require(Array.isArray(s.units) && Array.isArray(s.jobs));
  const unique = (ids: string[]) => ids.every(id => typeof id === 'string' && id.length > 0) && new Set(ids).size === ids.length;
  require(unique(s.units.map(u => u.id)) && unique(s.jobs.map(j => j.id)));
  for (const item of [...s.units, ...s.jobs]) {
    const match = /^(?:unit|job)-([1-9]\d*)$/.exec(item.id);
    require(match && integer(Number(match[1])) && Number(match[1]) <= s.sequence);
  }
  for (const u of s.units) require(EQUIPMENT[u.kind] && u.purchasePrice === EQUIPMENT[u.kind].cost && integer(u.level) && u.level >= 1 && u.level <= 4);
  for (const [i,d] of s.districts.entries()) {
    require(d && d.id === DISTRICTS[i].id && typeof d.unlocked === 'boolean' && typeof d.claimed === 'boolean');
    require(!d.claimed || d.unlocked);
    require(d.unlocked === (i === 0 || s.districts[i - 1].claimed));
    require(d.camera === null || (typeof d.camera === 'object' && Number.isFinite(d.camera.x) && Number.isFinite(d.camera.y) && d.camera.zoom >= .15 && d.camera.zoom <= 4));
    require(Array.isArray(d.plots) && d.plots.length === 64 && Array.isArray(d.projects) && d.projects.length === 9);
    for (const [j,p] of d.plots.entries()) {
      require(p && p.id === j && p.x === (j % 8) * 2 && p.y === Math.floor(j / 8) * 2);
      require(['rubble','empty','building','built'].includes(p.status) && BUILDINGS.some(b => b.id === p.buildingId));
      require((p.orientation === 0 || p.orientation === 1) && integer(p.startedAt) && integer(p.endsAt) && integer(p.incomeAt));
      require(p.status !== 'building' || (p.endsAt > p.startedAt && p.startedAt <= s.lastSeen));
    }
    const defs = projects(d.id === 'rashid');
    for (const [j,p] of d.projects.entries()) {
      require(p && p.id === defs[j].id && ['idle','building','ready','complete'].includes(p.status));
      require(integer(p.startedAt) && integer(p.endsAt) && integer(p.incomeAt));
      require(p.status !== 'building' || (p.endsAt > p.startedAt && p.startedAt <= s.lastSeen));
    }
  }
  require(s.currentDistrict === null || s.districts.some(d => d.id === s.currentDistrict && d.unlocked));
  const busy: string[] = [];
  const sites: string[] = [];
  for (const j of s.jobs) {
    const d = s.districts.find(d => d.id === j.districtId);
    require(d?.unlocked && integer(j.plotId) && j.plotId < 64 && integer(j.originPlotId) && j.originPlotId < 64);
    const origin = d!.plots[j.originPlotId];
    require(origin.buildingId === 'recycling' && origin.status === 'built');
    require(Array.isArray(j.unitIds) && j.unitIds.length === 3 && unique(j.unitIds));
    const team = j.unitIds.map(id => s.units.find(u => u.id === id));
    require(team.every(Boolean) && new Set(team.map(u => u!.kind)).size === 3);
    require([j.start,j.arrival,j.workEnd,j.returnEnd,j.value].every(integer));
    require(j.start <= s.lastSeen && j.start < j.arrival && j.workEnd - j.arrival === 60000 && j.returnEnd > j.workEnd);
    require(typeof j.cleared === 'boolean' && (j.cleared || d!.plots[j.plotId].status === 'rubble'));
    busy.push(...j.unitIds); sites.push(`${j.districtId}:${j.plotId}`);
  }
  require(new Set(busy).size === busy.length && new Set(sites).size === sites.length);
}
