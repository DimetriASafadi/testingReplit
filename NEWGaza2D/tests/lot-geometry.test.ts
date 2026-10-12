import test from 'node:test';
import assert from 'node:assert/strict';
import { LAND_WORLD_EDGE, PLOT_WORLD_W, YARD_EDGE, YARD_H, YARD_W, landWorldWidth, yardBounds } from '../src/lot-geometry';

test('dressed land reaches .94 world, fits the 360x470 yard canvas and keeps a road seam', () => {
  const b = yardBounds();
  assert.ok(b.left >= 0 && b.right <= YARD_W && b.top >= 0 && b.bottom <= YARD_H, JSON.stringify(b));
  assert.ok(landWorldWidth() / PLOT_WORLD_W >= .9);
  assert.ok(LAND_WORLD_EDGE < 1 && landWorldWidth() < PLOT_WORLD_W);
  assert.ok(Math.abs(YARD_EDGE * .72 - LAND_WORLD_EDGE) < 1e-9);
});
test('regression: the old 260px canvas cannot hold the full parcel (doubly-shrunk yard)', () => {
  const old = yardBounds(YARD_EDGE, 260);
  assert.ok(old.left < 0 && old.right > 260);
  assert.ok(landWorldWidth(.98) / PLOT_WORLD_W < .75);
});
