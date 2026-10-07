import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWorkerRoute, sampleWorkerRoute } from '../src/construction-worker-motion';

test('workers visit every side without crossing a building or leaving the lot', () => {
  for (const radius of [.34,.48,.65]) for (const orientation of [0,1] as const) for (let i=0;i<4;i++) {
    const route=createWorkerRoute(radius,orientation,i), positions=[];
    const r=Math.max(.3,radius-i*.014);
    for(let t=0;t<route.duration;t+=80) {
      const p=sampleWorkerRoute(route,t,0,0);positions.push(p);
      assert.ok(Math.abs(p.x)<=.65+1e-8 && Math.abs(p.y)<=.65+1e-8);
      assert.ok(Math.abs(Math.max(Math.abs(p.x),Math.abs(p.y))-r)<1e-8, 'stay on the perimeter');
      assert.ok(p.frame>=0 && p.frame<8);
    }
    assert.ok(Math.max(...positions.map(p=>p.x))-Math.min(...positions.map(p=>p.x))>r*1.9);
    assert.ok(Math.max(...positions.map(p=>p.y))-Math.min(...positions.map(p=>p.y))>r*1.9);
    for(const role of ['walk','carry','hammer']) assert.ok(positions.some(p=>p.role===role));
  }
});
test('all segment boundaries and the cycle seam are continuous', () => {
  const route=createWorkerRoute(.6,0,0);
  for(const leg of route.legs) for(const t of [leg.walkEnd,leg.end]) {
    const a=sampleWorkerRoute(route,t-.1,0,0), b=sampleWorkerRoute(route,t+.1,0,0);
    assert.ok(Math.hypot(a.x-b.x,a.y-b.y)<.0001);
  }
});
test('rear-facing buildings mirror the route, with deterministic staggered crew phases', () => {
  const front=createWorkerRoute(.5,0,0),back=createWorkerRoute(.5,1,0);
  let separation=0;
  for(let t=0;t<front.duration;t+=150) {
    const a=sampleWorkerRoute(front,t,27,0),b=sampleWorkerRoute(back,t,27,0);
    assert.equal(a.x,b.y);assert.equal(a.y,b.x);assert.equal(a.role,b.role);
    assert.deepEqual(a,sampleWorkerRoute(front,t,27,0));
    const c=sampleWorkerRoute(front,t,27,1);
    separation+=Math.hypot(a.x-c.x,a.y-c.y);
  }
  assert.ok(separation/(front.duration/150)>.25);
});
