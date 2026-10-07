// Runs only in the separate CDP test profile; the player's real save is untouched.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1440, 1000), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  if (await b.evaluate(`!!document.querySelector('[data-a="enter"]')`)) await b.click('[data-a="enter"]');
  original = await b.evaluate(`JSON.parse(localStorage.getItem('${key}'))`);
  const equipment = await b.evaluate(`(async () => (await import('/src/catalog.ts')).EQUIPMENT)()`);
  const s = structuredClone(original), now = Date.now(), d = s.districts[0];
  s.lastSeen = now; s.currentDistrict = d.id; s.jobs = [];
  s.sequence = 4;
  s.units = ['excavator', 'truck', 'bulldozer'].map((kind, i) => ({ id: `unit-${i + 1}`, kind, level: 1, purchasePrice: equipment[kind].cost }));
  d.camera = { x: 0, y: 450, zoom: 1.6 };
  for (const id of [18, 19, 20, 26, 28, 34, 35, 36, 42, 43, 44]) d.plots[id].status = 'empty';
  Object.assign(d.plots[0], { status: 'built', buildingId: 'recycling', incomeAt: now });
  Object.assign(d.plots[27], { status: 'rubble' });
  for (const [id, buildingId] of [[18, 'wheat'], [19, 'olive'], [20, 'palms']])
    Object.assign(d.plots[id], { status: 'built', buildingId, orientation: 0, incomeAt: now - 3500000 });
  for (const [id, buildingId, fraction, orientation] of [[28, 'housing_6', .45, 0], [36, 'villa', .74, 1], [26, 'strawberry', .3, 0]])
    Object.assign(d.plots[id], { status: 'building', buildingId, orientation,
      startedAt: now - fraction * 600000, endsAt: now + (1 - fraction) * 600000, incomeAt: 0 });
  s.jobs = [{ id: 'job-4', districtId: d.id, plotId: 27, unitIds: s.units.map(u => u.id),
    start: now - 15000, arrival: now - 1500, workEnd: now + 58500, returnEnd: now + 148500,
    cleared: false, value: 30000, originPlotId: 0 }];
  await b.evaluate(`(async () => { (await import('/src/save.ts')).validateSave(${JSON.stringify(s)}); localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(s))}); })()`);
  await b.send('Page.reload'); await b.until(`!!document.querySelector('#boot.done')`); await b.wait(1800);
  await b.until(`!!window.__newgazaArtScene?.fleetAnimation`);
  if (await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`)) await b.click('[data-a="hide-obj"]');
  for (let i = 0; i < 4; i++) {
    await b.screenshot(`/tmp/newgaza-animated-city-${i}.png`);
    await b.wait(1100);
  }
  const debug = await b.evaluate(`(async () => {
    const c=window.__newgazaArtScene;
    return { actors:c.fleetAnimation.actors.size,
      frames:[...c.fleetAnimation.actors.values()].map(a=>a.a.frame.name),
      sites:c.siteAnimation.views.size, textureCount:c.textures.getTextureKeys().length };
  })()`);
  assert.equal(debug.actors, 3); assert.ok(debug.frames.every(f => f.includes('-work-') || f.includes('-fill-')));
  assert.ok(debug.sites >= 6); assert.ok(debug.textureCount < 600);
  await b.click('[data-a="dlg"][data-id="fleet"]');
  assert.equal(await b.evaluate(`document.querySelectorAll('img[data-veh]').length`), 3);
  assert.equal(await b.evaluate(`[...document.querySelectorAll('img[data-veh]')].every(i=>i.complete && i.naturalWidth>0)`), true);
  await b.click('[data-a="close-dlg"]');
  await b.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await b.wait(900); await b.screenshot('/tmp/newgaza-animation-phone.png');
  assert.equal(await b.evaluate(`document.documentElement.scrollWidth <= 390`), true);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('Actual work clips, three coordinated machines, front/rear construction, crop/tree farms, previews and phone layout:', debug);
} finally {
  if (original) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(original))})`);
  b.close();
}
