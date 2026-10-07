import test from 'node:test';
import assert from 'node:assert/strict';
import { CONSTRUCTION_STAGES, constructionStageFor, stageHeight, stageFinish, farmLattice } from '../src/construction-art';

test('stage clamp covers 0..7 for any progress', () => {
  assert.equal(constructionStageFor(-5), 0); assert.equal(constructionStageFor(0), 0);
  assert.equal(constructionStageFor(1), CONSTRUCTION_STAGES - 1); assert.equal(constructionStageFor(9), CONSTRUCTION_STAGES - 1);
  const seen = new Set<number>(); for (let i = 0; i <= 1000; i++) seen.add(constructionStageFor(i / 1000));
  assert.equal(seen.size, CONSTRUCTION_STAGES);
});
test('height and finish are monotonic and bounded', () => {
  for (let s = 1; s < CONSTRUCTION_STAGES; s++) { assert.ok(stageHeight(s) >= stageHeight(s - 1)); assert.ok(stageFinish(s) >= stageFinish(s - 1)); }
  assert.equal(stageHeight(-3), 0); assert.equal(stageHeight(99), 1); assert.equal(stageFinish(99), 1);
  for (let s = 0; s < CONSTRUCTION_STAGES; s++) assert.ok(stageFinish(s) <= stageHeight(s) + 1e-9);
});
test('farm lattice stays inside the plot diamond', () => {
  for (const id of ['wheat', 'olive', 'palms', 'cattle', 'zoo', 'corn']) { const l = farmLattice(id); assert.ok(l.spots.length > 0); for (const [u, v] of l.spots) assert.ok(Math.abs(u) <= 0.7 + 1e-9 && Math.abs(v) <= 0.7 + 1e-9); }
});
