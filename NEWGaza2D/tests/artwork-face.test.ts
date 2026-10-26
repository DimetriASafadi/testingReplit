import test from 'node:test';
import assert from 'node:assert/strict';
import { availableBuildingFace } from '../src/artwork-face';
import type { Orientation } from '../src/model';

test('incomplete legacy views cannot prevent a saved city from opening', () => {
  const keys = new Set(['veg_front', 'veg_back']);
  for (const orientation of [0, 1, 2, 3] as Orientation[]) {
    const face = availableBuildingFace('veg', orientation, k => keys.has(k));
    assert.ok(keys.has(face.key));
    assert.equal(face.missing, orientation >= 2);
  }
  assert.equal(availableBuildingFace('veg', 3, k => keys.has(k)).key, 'veg_back');
});
test('complete quartets retain their exact four views and cattle keeps its historic front key', () => {
  const keys = new Set(['farm_cattle', 'cattle_right', 'cattle_back', 'cattle_left']);
  assert.deepEqual([0, 1, 2, 3].map(o => availableBuildingFace('cattle', o as Orientation, k => keys.has(k))),
    ['farm_cattle', 'cattle_back', 'cattle_right', 'cattle_left'].map(key => ({ key, missing: false })));
});
test('missing whole subjects fail explicitly, never showing a generic building', () => {
  assert.throws(() => availableBuildingFace('veg', 2, () => false), /Missing all artwork/);
});
