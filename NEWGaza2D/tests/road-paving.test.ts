import test from 'node:test';
import assert from 'node:assert/strict';
import { edgeIntervals, FILLET_R, pavingPlan, nodeOutlines, runOutline, ROAD_HALF, DASH_CLEAR } from '../src/road-paving';
import type { RoadSegment } from '../src/roads';
// grid: x-lines at 0,4 ; y-lines at 0,4 ; each street cut in two 2-long segments
const X=[0,4],Y=[0,4];
const segs:RoadSegment[]=[];
for(const f of X)for(const [p,q] of [[0,2],[2,4]])segs.push({id:`x-${f}-${p}`,a:[f,p],b:[f,q]});
for(const f of Y)for(const [p,q] of [[0,2],[2,4]])segs.push({id:`y-${f}-${p}`,a:[p,f],b:[q,f]});
const plan=(ids:string[])=>pavingPlan(X,Y,segs,new Set(ids));
test('adjacent paved segments merge into one seamless run', () => {
  const p=plan(['x-0-0','x-0-2']);
  assert.equal(p.runs.length,1);assert.equal(p.runs[0].s0,0);assert.equal(p.runs[0].s1,4);
});
test('junction paved only when both streets have paved arms', () => {
  assert.equal(plan(['x-0-0','x-0-2']).nodes.length,0);
  const p=plan(['x-0-0','y-0-0']);
  assert.equal(p.nodes.length,1);assert.deepEqual(p.nodes[0].fillets,[[1,1]]);
  assert.equal(p.runs.find(r=>r.axis==='x')!.start,'node');
  assert.equal(nodeOutlines(p.nodes[0]).length,2);
});
test('unpaved junction: run stops at cross-street edge; mid joint gets taper cap', () => {
  const r=plan(['x-0-0']).runs[0];
  assert.equal(r.start,'open');assert.equal(r.p0,ROAD_HALF);assert.equal(r.end,'joint');
  assert.ok(runOutline(r).length>4);
});
test('partial paving never covers unpurchased segment', () => {
  const r=plan(['x-0-0']).runs[0];
  assert.ok(Math.max(...runOutline(r).map(p=>p[1]))<2.2);
});
test('dashes keep out of junctions and are world aligned', () => {
  const all=plan(segs.map(s=>s.id));
  for(const r of all.runs)for(const [a,b] of r.dashes)for(const c of Y.concat(X)){
    assert.ok(b<=c-ROAD_HALF-DASH_CLEAR+1e-9||a>=c+ROAD_HALF+DASH_CLEAR-1e-9,`dash ${a}-${b} near ${c}`);
  }
  const a=plan(['x-0-0','x-0-2']).runs[0].dashes,b=plan(['x-4-0','x-4-2']).runs[0].dashes;
  assert.deepEqual(a,b);
});
test('edge lines never pass through any crossing, including internal nodes of merged runs', () => {
  const all=plan(segs.map(s=>s.id)), keep=ROAD_HALF+FILLET_R;
  for(const r of all.runs)for(const [a,b] of r.edges)for(const c of [0,4])assert.ok(b<=c-keep+1e-9||a>=c+keep-1e-9,`edge ${a}-${b} through ${c}`);
  assert.deepEqual(edgeIntervals(0,8,[0,4,8]),[[.5,3.5],[4.5,7.5]]);
  assert.deepEqual(edgeIntervals(.3,4,[0,4]),[[.5,3.5]]);
});
