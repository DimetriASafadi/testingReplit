import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
const root = new URL('../', import.meta.url);
const m = JSON.parse(readFileSync(new URL('src/machinery-manifest.json', root)));
assert.equal(Object.keys(m.frames).length, 276);
let bytes = 0;
for (const kind of ['excavator', 'truck', 'bulldozer']) {
  const atlas = JSON.parse(readFileSync(new URL(`public/art/machinery/${kind}.json`, root)));
  assert.ok(Math.max(atlas.meta.size.w, atlas.meta.size.h) <= 4096);
  assert.equal(m.clips[kind + ':drive'].length, 32);
  assert.equal(m.clips[kind + ':work'].length, kind === 'excavator' ? 72 : 36);
  for (const [clip, keys] of Object.entries(m.clips)) if (clip.startsWith(kind + ':')) {
    for (const key of keys) {
      assert.ok(atlas.frames[key], `${key} absent from atlas`);
      const f = atlas.frames[key].frame;
      assert.ok(f.x + f.w <= atlas.meta.size.w && f.y + f.h <= atlas.meta.size.h);
      assert.ok(m.frames[key].tip.every(v => Number.isFinite(v) && v > 0 && v < m.size));
    }
  }
  bytes += statSync(new URL(`public/art/machinery/${kind}.webp`, root)).size;
}
assert.ok(bytes < 2.5 * 1024 * 1024, 'Machine payload exceeds mobile art budget');
const dig = m.clips['excavator:work'].slice(8, 13).map(k => m.frames[k].worldTip[2]);
assert.ok(Math.min(...dig) >= 0 && Math.min(...dig) < .12, 'Bucket must touch rubble, not float/penetrate');
// Source Z-up geometry and runtime isometric coordinates have the same metres-per-unit.
const metres = 64 / (m.size / m.ortho * Math.SQRT1_2 * .66);
for (const key of m.clips['excavator:work'].slice(44, 50)) {
  const [x, y, z] = m.frames[key].worldTip;
  const bedX = x - .715 * metres; // actual curb spacing between excavator and truck
  assert.ok(bedX > -2.98 && bedX < 1.07 && Math.abs(y) < 1.03 && z > 1.30,
    'Release point must be over the actual dump bed, above its floor');
}
assert.equal(m.clips['truck:loaded'].length, 32);
assert.equal(m.clips['truck:fill'].length, 4);
console.log(`276 articulated frames, 16 headings, real bucket/bed contact; ${(bytes / 1048576).toFixed(2)} MiB machinery payload.`);
