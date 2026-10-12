import test from 'node:test';
import assert from 'node:assert/strict';
import { buildingFootprint, buildingMaxHeight, buildingRenderScale } from '../src/building-footprint';
import manifest from '../src/realistic-manifest.json';

test('compact houses leave usable land while apartments and factories keep their full footprint', () => {
  assert.equal(buildingFootprint('smallhouse_front').factor, .62);
  assert.ok(buildingFootprint('house_front').factor > buildingFootprint('smallhouse_front').factor);
  assert.ok(buildingFootprint('villa_front').factor > buildingFootprint('house_front').factor);
  for (const key of ['apt4_front','h6_front','complex_front','modern_front','tower_front','recycling_front','steel_front','olive_front']) {
    assert.deepEqual(buildingFootprint(key), { factor: 1, yard: 'none' }, key);
  }
  assert.equal(buildingFootprint('smallhouse_front').yard, 'garden');
  assert.equal(buildingFootprint('kiosk_front').yard, 'patio');
});
test('published four-face sprites share parcel/scale; six pending types retain their original two faces', () => {
  const sprites = manifest as Record<string, { w: number; h: number; ay: number; ground?: { w: number; h: number } }>;
  const fronts = Object.keys(sprites).filter(k => k.endsWith('_front') && !['ind_front', 'shop_front'].includes(k));
  fronts.push('farm_cattle');
  assert.equal(fronts.length, 48);
  const pending: string[] = [];
  for (const front of fronts) {
    const stem = front === 'farm_cattle' ? 'cattle' : front.replace(/_front$/, '');
    const names = [front, ...['right', 'back', 'left'].map(face => `${stem}_${face}`)];
    if (!sprites[names[1]] || !sprites[names[3]]) {
      pending.push(stem);
      for (const key of [front, `${stem}_back`]) {
        assert.ok(sprites[key], key);
        assert.ok(sprites[key].w <= 512 && sprites[key].h <= 512, key);
      }
      continue;
    }
    for (const key of names) {
      assert.ok(sprites[key], key);
      assert.ok(sprites[key].w > 0 && sprites[key].h > 0 && sprites[key].w <= 512 && sprites[key].h <= 512, key);
      assert.ok(sprites[key].ay > 0 && sprites[key].ay <= 1, key);
      assert.deepEqual(buildingFootprint(key), buildingFootprint(front), key);
      assert.equal(buildingMaxHeight(key), buildingMaxHeight(front), key);
      assert.equal(sprites[key].ground?.w, sprites[front].ground?.w, key);
      assert.equal(sprites[key].ground?.h, sprites[front].ground?.h, key);
      assert.equal(buildingRenderScale(key, sprites[key]), buildingRenderScale(front, sprites[front]), key);
    }
  }
  // These have not passed visual review: never silently register bad/squashed
  // right/left sprites merely to make the requested 42/42 count look complete.
  assert.deepEqual(pending.sort(), ['cattle', 'cement', 'citrus', 'trad', 'trees', 'veg']);
});
test('single-floor retail has modest roof height and furnished land in both orientations',()=>{
  for(const stem of ['foodshop','clothes','shoe','falafel','shawarma','western','cafe','kiosk']){
    assert.ok(buildingMaxHeight(stem+'_front')<=86);
    assert.equal(buildingMaxHeight(stem+'_front'),buildingMaxHeight(stem+'_back'));
    assert.equal(buildingFootprint(stem+'_front').yard,'patio');
    assert.deepEqual(buildingFootprint(stem+'_front'),buildingFootprint(stem+'_back'));
    assert.ok(buildingMaxHeight(stem+'_front')<buildingMaxHeight('apt4_front'));
  }
  assert.equal(buildingFootprint('muni_back').yard,'service');
});
test('rotation preserves house scale and landscape category', () => {
  for (const stem of ['smallhouse','house','villa','tourist','kiosk','apt4','h6']) {
    assert.deepEqual(buildingFootprint(`${stem}_front`), buildingFootprint(`${stem}_back`));
    const f = buildingFootprint(`${stem}_front`).factor;
    assert.ok(f > 0 && f <= 1);
  }
});
