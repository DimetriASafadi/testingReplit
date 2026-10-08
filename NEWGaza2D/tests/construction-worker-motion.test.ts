import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createWorkerRoute, sampleWorkerRoute } from '../src/construction-worker-motion';
import { ORIENTATION_CYCLE, rotateLotPoint } from '../src/orientation';

test('workers visit every side without crossing a building or leaving the lot', () => {
  for (const radius of [.34,.48,.65]) for (const orientation of ORIENTATION_CYCLE) for (let i=0;i<4;i++) {
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
test('the same perimeter rotates with the building on all four faces, keeping its timing and worker identity', () => {
  const front = createWorkerRoute(.48, 0, 0);
  for (const o of ORIENTATION_CYCLE) {
    const route = createWorkerRoute(.48, o, 0);
    assert.equal(route.duration, front.duration);
    for (let i = 0; i < route.legs.length; i++) {
      assert.deepEqual(route.legs[i].from, rotateLotPoint(front.legs[i].from, o));
      assert.deepEqual(route.legs[i].to, rotateLotPoint(front.legs[i].to, o));
      assert.equal(route.legs[i].station, front.legs[i].station);
    }
  }
});
test('all segment boundaries and the cycle seam are continuous', () => {
  const route=createWorkerRoute(.6,0,0);
  for(const leg of route.legs) for(const t of [leg.walkEnd,leg.end]) {
    const a=sampleWorkerRoute(route,t-.1,0,0), b=sampleWorkerRoute(route,t+.1,0,0);
    assert.ok(Math.hypot(a.x-b.x,a.y-b.y)<.0001);
  }
});
test('rear-facing buildings turn the route 180 degrees, with deterministic staggered crew phases', () => {
  const front=createWorkerRoute(.5,0,0),back=createWorkerRoute(.5,1,0);
  let separation=0;
  for(let t=0;t<front.duration;t+=150) {
    const a=sampleWorkerRoute(front,t,27,0),b=sampleWorkerRoute(back,t,27,0);
    assert.ok(Math.abs(a.x+b.x)<1e-12);assert.ok(Math.abs(a.y+b.y)<1e-12);
    assert.equal(a.role,b.role);assert.equal(a.frame,b.frame);
    assert.deepEqual(a,sampleWorkerRoute(front,t,27,0));
    const c=sampleWorkerRoute(front,t,27,1);
    separation+=Math.hypot(a.x-c.x,a.y-c.y);
  }
  assert.ok(separation/(front.duration/150)>.25);
});
