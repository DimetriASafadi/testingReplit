import test from 'node:test';
import assert from 'node:assert/strict';
import { groundTileRects } from '../src/ground-tiles';

test('expanded district textures fit minimum WebGL limits without losing pixels', () => {
  for (const [width, height] of [[4105, 2053], [4608, 2304], [2048, 2048], [27, 13]]) {
    const tiles = groundTileRects(width, height);
    assert.equal(tiles.reduce((sum, t) => sum + t.width * t.height, 0), width * height);
    for (const t of tiles) {
      assert(t.width <= 2048 && t.height <= 2048);
      assert(t.x + t.width <= width && t.y + t.height <= height);
      for (const other of tiles) if (other !== t)
        assert(t.x + t.width <= other.x || other.x + other.width <= t.x ||
          t.y + t.height <= other.y || other.y + other.height <= t.y);
    }
  }
  assert.equal(groundTileRects(4105, 2053).length, 6);
  assert.throws(() => groundTileRects(0, 2053));
});
