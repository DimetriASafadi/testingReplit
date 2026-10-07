import test from 'node:test';
import assert from 'node:assert/strict';
import { SpriteOpacityCache, spriteAlphaAt } from '../src/sprite-opacity';

test('opacity is read only once per immutable source, not once per probe', () => {
  let reads = 0;
  const cache = new SpriteOpacityCache(() => {
    reads++; return { width: 2, height: 2, alpha: new Uint8Array([0,90,91,255]) };
  });
  const a = {} as HTMLCanvasElement, mask = cache.prepare(a);
  for (let i = 0; i < 10000; i++) {
    assert.equal(cache.prepare(a), mask);
    assert.equal(spriteAlphaAt(mask, 1, 1), 255);
  }
  assert.equal(reads, 1);
  assert.equal(spriteAlphaAt(mask, 1, 0) > 90, false);
  assert.equal(spriteAlphaAt(mask, 0, 1) > 90, true);
  cache.prepare({} as HTMLCanvasElement); // a replaced texture source gets fresh data
  assert.equal(reads, 2);
});
test('opacity coordinates preserve transparent holes, pixel flooring and image bounds', () => {
  const mask = { width: 2, height: 2, alpha: new Uint8Array([0,90,91,255]) };
  assert.equal(spriteAlphaAt(mask, .99, .99), 0);
  assert.equal(spriteAlphaAt(mask, 1.9, 1.9), 255);
  for (const [x,y] of [[-1,0],[0,-1],[2,0],[0,2],[NaN,0]]) assert.equal(spriteAlphaAt(mask,x,y),0);
});
