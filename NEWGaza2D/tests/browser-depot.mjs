// An isolated browser profile; replace only its temporary local save, then restore it.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1280, 840), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`JSON.parse(localStorage.getItem('${key}'))`);
  const equipment = await b.evaluate(`(async()=> (await import('/src/catalog.ts')).EQUIPMENT)()`);
  const initial = structuredClone(original), d = initial.districts[0], now = Date.now();
  initial.lastSeen = now; initial.currentDistrict = d.id; initial.sequence = 4; initial.jobs = [];
  initial.units = ['excavator', 'bulldozer', 'truck'].map((kind, i) =>
    ({ id: `unit-${i + 1}`, kind, level: 1, purchasePrice: equipment[kind].cost }));
  Object.assign(d.plots[0], { status: 'built', buildingId: 'recycling', orientation: 0, incomeAt: now });
  d.camera = { x: 0, y: 185, zoom: 1.55 };
  const target = d.plots.filter(p => p.id !== 0 && p.status === 'rubble')
    .sort((a, z) => a.x + a.y - z.x - z.y)[0] ?? d.plots[1];
  assert.ok(target);
  target.status = 'rubble';
  const load = async (state, name) => {
    await b.evaluate(`(async()=>{(await import('/src/save.ts')).validateSave(${JSON.stringify(state)});localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(state))})})()`);
    await b.evaluate(`window.__depotProbeBeforeReload=true`);
    await b.send('Page.reload'); await b.until(`!window.__depotProbeBeforeReload`);
    await b.until(`!!document.querySelector('#boot.done')`);
    await b.until(`!!window.__newgazaArtScene?.fleetAnimation?.doors?.views?.size`);
    await b.wait(650); await b.screenshot(`/tmp/newgaza-depot-${name}.png`);
    return await b.evaluate(`(()=>{const f=window.__newgazaArtScene.fleetAnimation;return {
      visible:f.actors.size,open:f.doors.views.get(0).open,
      positions:[...f.actors.values()].map(a=>[a.a.x,a.a.y,a.a.alpha,a.a.frame.name])
    }})()`);
  };
  const idle = await load(initial, 'idle');
  assert.equal(idle.visible, 0); assert.equal(idle.open, 0);
  const leaving = structuredClone(initial), stamp = Date.now();
  leaving.lastSeen = stamp; leaving.jobs = [{ id: 'job-4', districtId: d.id, plotId: target.id,
    unitIds: initial.units.map(u => u.id), start: stamp - 1700, arrival: stamp + 18300,
    workEnd: stamp + 78300, returnEnd: stamp + 168300, cleared: false,
    originPlotId: 0, value: 30000 }];
  const outward = await load(leaving, 'leaving');
  assert.ok(outward.visible >= 1 && outward.visible <= 3, JSON.stringify(outward));
  assert.equal(outward.open, 1);
  const returning = structuredClone(initial), back = Date.now();
  returning.lastSeen = back; returning.districts[0].plots[target.id].status = 'empty';
  returning.jobs = [{ id: 'job-4', districtId: d.id, plotId: target.id,
    unitIds: initial.units.map(u => u.id), start: back - 99000, arrival: back - 79000,
    workEnd: back - 19000, returnEnd: back + 12000, cleared: true,
    originPlotId: 0, value: 30000 }];
  const inward = await load(returning, 'returning');
  assert.ok(inward.visible > 0 && inward.open > .9, JSON.stringify(inward));
  await b.wait(13000);
  const closed = await b.evaluate(`(()=>{const f=window.__newgazaArtScene.fleetAnimation;return {
    visible:f.actors.size,open:f.doors.views.get(0).open
  }})()`);
  assert.equal(closed.visible, 0); assert.equal(closed.open, 0);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('Factory shut when idle; consecutive departure and return; all vehicles hidden and door shut after completion:', { idle, outward, inward, closed });
} finally {
  if (original) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(original))})`);
  b.close();
}
