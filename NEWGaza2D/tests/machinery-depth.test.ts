import test from 'node:test';
import assert from 'node:assert/strict';
import { machineryDepth } from '../src/machinery-depth';

test('a turning chassis is behind the facade before its centre crosses the building centre', () => {
  const buildingDepth = 448, groundY = (8 + 6.14) * 32;
  assert.ok(groundY + .4 > buildingDepth, 'old centre-only sorting incorrectly put it on top');
  for (const kind of ['excavator','truck','bulldozer'] as const) {
    for (const heading of [0, .2, Math.PI / 4, Math.PI / 2, -Math.PI / 2]) {
      assert.ok(machineryDepth(kind, heading, groundY, 224, 13.2, .66) < buildingDepth);
      assert.ok(machineryDepth(kind, heading, 528, 224, 13.2, .66) > buildingDepth,
        'a fully foreground vehicle must remain in front');
    }
  }
});

test('depth tracks position immediately, handles heading wrap, and scales with artwork', () => {
  const d = (h: number, y = 452, scale = .66) => machineryDepth('truck', h, y, 224, 13.2, scale);
  assert.equal(d(.2, 453) - d(.2, 452), 1);
  assert.ok(Math.abs(d(-.1) - d(Math.PI * 2 - .1)) < 1e-10);
  assert.ok(Math.abs(d(Math.PI * 2 + .1) - d(.1)) < 1e-10);
  assert.ok(d(0, 452, 1) < d(0, 452, .66));
  // Adjacent crossfade samples agree at the heading boundary: no one-frame
  // reappearance just because the driving atlas moves to the next pair.
  for (let i = 0; i < 16; i++) {
    const h = i * Math.PI / 8;
    assert.ok(Math.abs(d(h - 1e-7) - d(h + 1e-7)) < 1e-4);
  }
});
