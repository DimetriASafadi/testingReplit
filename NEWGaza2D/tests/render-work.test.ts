import test from 'node:test';
import assert from 'node:assert/strict';
import { alphaBounds, renderInBatches } from '../src/render-work';

test('fully built 144-parcel load yields at least once per four plots, retaining order', async () => {
  const visited: number[] = [], progress: number[] = [];
  let yields = 0;
  const items = Array.from({ length: 144 }, (_, i) => i);
  const result = await renderInBatches(items, p => visited.push(p), () => true,
    done => progress.push(done), () => 0, async () => { yields++; });
  assert.equal(result, true);
  assert.deepEqual(visited, items);
  assert.equal(yields, 36);
  assert.equal(progress.at(-1), 144);
});

test('expensive parcels yield after one item and cancelled loads do not finish stale districts', async () => {
  let time = 0, live = true, yields = 0;
  const visited: number[] = [];
  const result = await renderInBatches([1, 2, 3], p => { visited.push(p); time += 20; },
    () => live, () => {}, () => time, async () => { yields++; live = false; });
  assert.equal(result, false); assert.equal(yields, 1); assert.deepEqual(visited, [1]);
});

test('alpha trimming keeps faint detail, a sampling gutter and logical anchor offsets', () => {
  const width = 360, height = 470, data = new Uint8ClampedArray(width * height * 4);
  for (let y = 270; y < 465; y++) for (let x = 15; x < 345; x++) data[(y * width + x) * 4 + 3] = 1;
  const b = alphaBounds(data, width, height);
  assert.deepEqual(b, { x: 13, y: 268, width: 334, height: 199 });
  assert.ok(b.width * b.height < width * height * .4);
  const originalAnchor = [180, 380], trimmedAnchor = [originalAnchor[0] - b.x, originalAnchor[1] - b.y];
  assert.deepEqual([trimmedAnchor[0] + b.x, trimmedAnchor[1] + b.y], originalAnchor);
});

test('empty and border-touching yard textures are valid without clipping', () => {
  const data = new Uint8ClampedArray(8 * 6 * 4);
  assert.deepEqual(alphaBounds(data, 8, 6), { x: 0, y: 0, width: 1, height: 1 });
  data[3] = 255; data[data.length - 1] = 1;
  assert.deepEqual(alphaBounds(data, 8, 6), { x: 0, y: 0, width: 8, height: 6 });
});
