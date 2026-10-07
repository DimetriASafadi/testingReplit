import test from 'node:test';
import assert from 'node:assert/strict';
import { buildingFootprint, buildingMaxHeight } from '../src/building-footprint';

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
