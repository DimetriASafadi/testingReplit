// Visual checks run only in a disposable browser profile (CDP port 9222).
// Fixtures never touch the player's browser, production save or gameplay rules.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1440, 1000), key = 'newgaza2d-save-v1';
const saved = () => b.evaluate(`JSON.parse(localStorage.getItem('${key}'))`);
const reload = async () => {
  await b.send('Page.reload');
  await b.until(`!!document.querySelector('#boot.done')`);
  await b.wait(1800);
  if (await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`)) await b.click('[data-a="hide-obj"]');
};
const install = async (s) => {
  await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(s))})`);
  await reload();
};
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  if (await b.evaluate(`!!document.querySelector('[data-a="enter"]')`)) await b.click('[data-a="enter"]');
  const original = await saved();
  assert(original?.version === 1);
  const clean = structuredClone(original);
  clean.currentDistrict = clean.districts[0].id;
  clean.lastSeen = Date.now(); // Remove the disposable regression clock's future timestamp.
  clean.districts[0].camera = { x: 0, y: 450, zoom: 0.8 };
  clean.jobs = [];
  for (const p of clean.districts[0].plots) {
    Object.assign(p, { status: [0, 1, 8, 9, 15, 48, 55, 56, 57, 62, 63].includes(p.id) ? 'empty' : 'rubble', startedAt: 0, endsAt: 0, incomeAt: 0 });
  }
  await install(clean);
  await b.screenshot('/tmp/newgaza-art-ruined-wide.png');
  clean.districts[0].camera.zoom = 1.45;
  await install(clean);
  await b.screenshot('/tmp/newgaza-art-ruined-close.png');
  const now = Date.now(), gallery = structuredClone(clean);
  const pairs = ['small_house', 'housing_4', 'villa', 'cement', 'steel', 'water_treatment', 'modern_mall', 'municipality'];
  for (let row = 0; row < 4; row++) {
    for (let col = 0; col < 4; col++) {
      const id = (row + 2) * 8 + col + 2, p = gallery.districts[0].plots[id];
      Object.assign(p, { status: 'built', buildingId: pairs[row * 2 + Math.floor(col / 2)], orientation: col % 2, startedAt: now - 600000, endsAt: now - 1000, incomeAt: now });
    }
  }
  gallery.districts[0].camera.zoom = 1;
  await install(gallery);
  await b.screenshot('/tmp/newgaza-art-front-back.png');
  const site = structuredClone(clean);
  // A genuinely cleared block, as the player creates through normal clearance,
  // makes worker/site contact visible rather than hiding it behind foreground ruins.
  for (const id of [18, 19, 20, 26, 34, 36, 42, 43, 44]) site.districts[0].plots[id].status = 'empty';
  for (const [id, buildingId, fraction] of [[27, 'housing_6', 0.45], [28, 'cement', 0.74], [35, 'small_house', 0.15]]) {
    Object.assign(site.districts[0].plots[id], { status: 'building', buildingId, orientation: 0, startedAt: Math.floor(now - fraction * 600000), endsAt: Math.floor(now + (1 - fraction) * 600000), incomeAt: 0 });
  }
  await install(site);
  await b.screenshot('/tmp/newgaza-art-workers-0.png');
  await b.wait(430);
  await b.screenshot('/tmp/newgaza-art-workers-1.png');
  await b.wait(430);
  await b.screenshot('/tmp/newgaza-art-workers-2.png');
  const reloaded = await saved();
  assert.equal(reloaded.districts[0].plots[27].status, 'building');
  assert.equal(reloaded.coins, original.coins);
  await b.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await b.wait(900);
  await b.screenshot('/tmp/newgaza-art-mobile.png');
  assert.equal(await b.evaluate(`document.documentElement.scrollWidth <= 390`), true);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('Ruins, eight actual front/back pairs, three active construction stages, worker animation frames, phone layout and save continuity captured. No runtime errors.');
} finally { b.close(); }
