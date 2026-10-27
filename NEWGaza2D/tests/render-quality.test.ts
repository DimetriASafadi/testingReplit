import test from 'node:test';
import assert from 'node:assert/strict';
import { chooseRenderProfile, renderDensity } from '../src/render-quality';
test('phone framebuffer is sharp without unlimited high-DPI allocations',()=>{
  assert.equal(renderDensity(3,390,844),2);
  assert.equal(renderDensity(1,390,844),1);
  const r=renderDensity(3,1200,1600);
  assert.ok(r>=1&&r<2);
  assert.ok(1200*1600*r*r<=2500001);
  assert.equal(renderDensity(3,844,390),renderDensity(3,390,844));
});

test('4GB phone selects the low-memory profile without reducing source artwork size',()=>{
  const p=chooseRenderProfile({memoryGb:4,cores:8,dpr:2.75,width:390,height:844});
  assert.equal(p.tier,'low');
  assert.equal(p.pixelDensity,1.25);
  assert.equal(p.groundTileSize,1024);
  assert.equal(p.plotBatchSize,1);
  assert.equal(p.parallelDownloads,2);
});

test('unknown devices use balanced settings; high-memory phones retain sharper output',()=>{
  const unknown=chooseRenderProfile({cores:8,dpr:2.5,width:390,height:844});
  const high=chooseRenderProfile({memoryGb:8,cores:8,dpr:2.5,width:390,height:844});
  assert.equal(unknown.tier,'balanced');
  assert.equal(unknown.pixelDensity,1.5);
  assert.equal(high.tier,'high');
  assert.equal(high.pixelDensity,2);
  assert.equal(high.groundTileSize,2048);
  assert.equal(high.plotBatchSize,4);
});
