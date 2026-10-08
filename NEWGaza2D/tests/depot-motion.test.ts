import test from 'node:test';
import assert from 'node:assert/strict';
import { newGame } from '../src/engine';
import { depotGeometry } from '../src/depot-geometry';
import { convoyMotion, fleetRoute, routeLength, routePoint, workDock } from '../src/fleet-motion';
import type { Job } from '../src/model';
import machinery from '../src/machinery-manifest.json';
import { ORIENTATION_CYCLE, rotateLotPoint } from '../src/orientation';

test('factory door and vehicles share the same calibrated four-sided opening',()=>{
  const source=newGame(0).districts[0].plots[0], target=newGame(0).districts[0].plots[27];
  for(const orientation of ORIENTATION_CYCLE) {
    source.orientation=orientation;const g=depotGeometry(source);
    assert.ok(g.halfWidth >= 7 && g.halfWidth <= 15);
    assert.ok(g.height >= 24 && g.height <= 32);
    assert.deepEqual(g.outward, rotateLotPoint([0, 1], orientation));
    assert.equal(g.visible, orientation === 0 || orientation === 2);
    for(const kind of ['excavator','truck','bulldozer'] as const) {
      const path=fleetRoute(source,target,kind),at=routePoint(path,.75);
      assert.deepEqual(path[0],g.inside);assert.deepEqual(path.at(-1),workDock(target,kind));
      assert.ok(Math.hypot(at[0]-g.threshold[0],at[1]-g.threshold[1])<1e-8);
      const start=routePoint(path,.05);
      assert.ok((start[0]-g.inside[0])*g.outward[0]+(start[1]-g.inside[1])*g.outward[1]>0);
      const back=fleetRoute(source,target,kind,true);
      assert.deepEqual(back.at(-1),g.inside);assert.deepEqual(back[0],workDock(target,kind));
    }
  }
});
test('same-row exit joins the useful street directly rather than detouring to a fixed extra block',()=>{
  const d=newGame(0).districts[0],source=d.plots[0],target=d.plots[1];
  const route=fleetRoute(source,target,'excavator');
  assert.ok(route.every(p=>p[0]<=workDock(target,'excavator')[0]+1e-8));
  // Includes the calibrated final leg into the heap, not a fixed +4-block detour.
  assert.ok(routeLength(route)<target.x-source.x+5.25);
  assert.equal(fleetRoute(source,target,'excavator'),route,'unchanged geometry is cached');
});
test('opaque travel, masked portal transitions and continuous return preserve saved deadlines',()=>{
  const d=newGame(100000).districts[0],source=d.plots[0],target=d.plots[27];
  const j:Job={id:'job-1',districtId:d.id,plotId:27,unitIds:['unit-1','unit-2','unit-3'],originPlotId:0,
    start:100000,arrival:160000,workEnd:220000,returnEnd:310000,cleared:false,value:30000};
  for(const orientation of ORIENTATION_CYCLE) {
    source.orientation=orientation;
    for(const kind of ['excavator','truck','bulldozer'] as const){
      let portal=false,street=false;
      for(let t=j.start;t<j.returnEnd;t+=75){
        const m=convoyMotion(j,source,target,kind,t);
        assert.ok(m.visibility===0 || m.visibility===1,'no fractional opacity');
        portal ||= m.portal;
        street ||= m.moving && !m.portal;
      }
      assert.ok(portal && street);
      assert.equal(convoyMotion(j,source,target,kind,j.returnEnd-249).visibility,0);
    }
  }
});
test('truck reverses only along its final straight parking approach, never on a street corner', () => {
  const d = newGame(100000).districts[0], source = d.plots[0];
  for (const orientation of ORIENTATION_CYCLE) {
    source.orientation = orientation;
    for (const target of [d.plots[1], d.plots[27], d.plots.at(-1)!]) {
      const j: Job = { id: 'parking', districtId: d.id, plotId: target.id, unitIds: [],
        originPlotId: source.id, start: 100000, arrival: 160000, workEnd: 220000,
        returnEnd: 310000, cleared: false, value: 0 };
      let parked = false;
      for (let t = j.start; t < j.arrival; t += 50) {
        const m = convoyMotion(j, source, target, 'truck', t);
        if (!m.reverse) continue;
        parked = true;
        assert.ok(Math.abs(m.ahead[0] - m.position[0]) < 1e-8);
        assert.ok(m.ahead[1] < m.position[1]);
        assert.ok(m.position[1] >= workDock(target, 'truck')[1]);
      }
      assert.ok(parked);
    }
  }
});
test('painted dump endpoint is calibrated to the actual truck dock, in every layout', () => {
  const s = newGame(0), target = s.districts[0].plots[27];
  const frames = machinery.frames as Record<string, { tip?: number[] }>;
  const tip = frames['excavator:work_059'].tip!;
  const ex = workDock(target, 'excavator'), truck = workDock(target, 'truck');
  const bed = machinery.bed.fill0.centre;
  const expectedX = ((truck[0]-ex[0])-(truck[1]-ex[1]))*64/.66 + bed[0]-machinery.foot[0];
  const expectedY = ((truck[0]-ex[0])+(truck[1]-ex[1]))*32/.66 + bed[1]-machinery.foot[1];
  assert.ok(Math.abs(tip[0]-machinery.foot[0]-expectedX) < 3);
  assert.ok(Math.abs(tip[1]-machinery.foot[1]-expectedY) < 7);
  const legacy = { ...target, layoutId: undefined };
  assert.deepEqual(workDock(legacy, 'truck'), workDock(target, 'truck'));
});
test('working bulldozer does not occupy the excavator chassis', () => {
  const p = newGame(0).districts[0].plots[27];
  const a = workDock(p, 'excavator'), b = workDock(p, 'bulldozer');
  const dx = ((b[0]-a[0])-(b[1]-a[1]))*64, dy = ((b[0]-a[0])+(b[1]-a[1]))*32;
  assert.ok(Math.hypot(dx,dy)>55, 'separate ground-contact positions');
});
