import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const read = (p) => readFileSync(root + p, 'utf8');
const manifest = JSON.parse(read('src/realistic-manifest.json'));
const mapping = read('src/realart.ts').split('const KEY: Record<string, string> = {')[1].split('};')[0];
const keys = Object.fromEntries([...mapping.matchAll(/(\w+): '([^']+)'/g)].map(m => [m[1], m[2]]));
const ids = [...read('src/catalog.ts').matchAll(/^ \['(?:equipment|industry|housing|farm|commerce|leisure)',\[([^\]]+)\]/gm)]
  .flatMap(m => [...m[1].matchAll(/'([^']+)'/g)].map(v => v[1]));
assert.equal(ids.length, 42);
const fronts = new Set(), backs = new Set();
for (const id of ids) {
  assert(keys[id], `Missing catalog art: ${id}`);
  const front = id === 'cattle' ? 'farm_cattle' : keys[id] + '_front';
  const back = keys[id] + '_back';
  assert(manifest[front] && manifest[back], `Missing orientation: ${id}`);
  assert.notEqual(front, back);
  fronts.add(front); backs.add(back);
}
assert.equal(fronts.size, ids.length, 'Catalog entries unexpectedly alias the same front art');
assert.equal(backs.size, ids.length, 'Catalog entries unexpectedly alias the same rear art');
let bytes = 0;
for (const [key, m] of Object.entries(manifest)) {
  assert(m.w > 0 && m.h > 0 && m.w <= 260 && m.h <= 460, key);
  assert(m.ay > 0 && m.ay < 1, `Invalid footprint anchor: ${key}`);
  const path = root + `public/art/realistic/s/${key}.webp`;
  bytes += statSync(path).size;
  assert.equal(readFileSync(path).subarray(0, 4).toString(), 'RIFF', key);
}
assert(bytes < 3 * 1024 * 1024, 'Runtime sprite payload exceeds the art budget');
for (const ground of ['earth', 'sand', 'asphalt', 'gravel', 'water'])
  assert(statSync(root + `public/art/realistic/ground-${ground}.jpg`).size > 1000);
console.log(`${ids.length} unique front/rear pairs, ${Object.keys(manifest).length} valid sprites, ${(bytes / 1048576).toFixed(2)} MiB sprite payload, all ground materials present.`);
