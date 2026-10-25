import test from 'node:test';
import assert from 'node:assert/strict';
import { renderDensity } from '../src/render-quality';
test('phone framebuffer is sharp without unlimited high-DPI allocations',()=>{
  assert.equal(renderDensity(3,390,844),2);
  assert.equal(renderDensity(1,390,844),1);
  const r=renderDensity(3,1200,1600);
  assert.ok(r>=1&&r<2);
  assert.ok(1200*1600*r*r<=2500001);
  assert.equal(renderDensity(3,844,390),renderDensity(3,390,844));
});
