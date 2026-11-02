import test from 'node:test';
import assert from 'node:assert/strict';
import {RoadCoatTiles,COAT_TILE_SIZE,coatShapeTouches,type CoatShape} from '../src/road-coat-tiles';
import {pavingPlan,splitPavingRun} from '../src/road-paving';
import {roadsFor,streetFor} from '../src/roads';
import {cityLayout} from '../src/city-layout';
const shape=(token:string,x:number,y:number,right=x+40,bottom=y+40):CoatShape=>({token,bounds:{x,y,right,bottom}});
function drain(q:RoadCoatTiles){const points:{x:number;y:number}[]=[];let p;while((p=q.next())){points.push(p);q.applied(p.x,p.y);}return points;}
test('an added road does not invalidate stable roads in other tiles',()=>{
  const q=new RoadCoatTiles(),a=shape('old',20,20),b=shape('new',800,800);
  q.reconcile([a],2048,2048);assert.deepEqual(drain(q),[{x:0,y:0}]);
  q.reconcile([a,b],2048,2048);assert.deepEqual(drain(q),[{x:768,y:768}]);
  q.reconcile([a,b],2048,2048);assert.equal(q.size,0);
});
test('replanning before completion preserves all unapplied tiles',()=>{
  const q=new RoadCoatTiles(),a=shape('a',0,0,600,10),b=shape('b',800,800);
  q.reconcile([a],2048,2048);q.applied(0,0);assert.equal(q.size,2);
  q.reconcile([a,b],2048,2048);assert.equal(q.size,3);
  const result=drain(q);assert.ok(result.some(p=>p.x===512));assert.ok(result.some(p=>p.x===768));
});
test('removed and replaced geometry invalidates both old and new extents',()=>{
  const q=new RoadCoatTiles(),a=shape('old',10,10),b=shape('replacement',600,600);
  q.reconcile([a],1024,1024);drain(q);
  q.reconcile([b],1024,1024);assert.equal(q.size,2);drain(q);
  q.reconcile([],1024,1024);assert.deepEqual(drain(q),[{x:512,y:512}]);
  q.reset();q.reconcile([],1024,1024);assert.equal(q.size,0);
});
test('diagonal roads dirty their actual footprint, not the whole bounding rectangle',()=>{
  const q=new RoadCoatTiles();
  const s:CoatShape={token:'diagonal',bounds:{x:0,y:0,right:2048,bottom:2048},
    footprints:[[[0,0],[2048,2038],[2048,2048],[0,10]]]};
  assert.equal(coatShapeTouches(s,{x:0,y:1500,right:256,bottom:1756}),false);
  assert.equal(coatShapeTouches(s,{x:700,y:700,right:956,bottom:956}),true);
  q.reconcile([s],2048,2048);const work=drain(q);
  assert.ok(work.length<32,`${work.length} tiles should be substantially below 64 bounding tiles`);
  assert.ok(work.some(p=>p.x===0&&p.y===0));assert.ok(work.some(p=>p.x===1792&&p.y===1792));
  for(const p of work){assert.ok(p.x>=0&&p.x<2048&&p.y>=0&&p.y<2048);assert.equal(p.x%COAT_TILE_SIZE,0);}
});
test('contained polygons and shoulder padding are not dropped at tile boundaries',()=>{
  const s:CoatShape={token:'patch',bounds:{x:240,y:240,right:290,bottom:290},
    footprints:[[[260,260],[270,260],[270,270],[260,270]]]};
  assert.equal(coatShapeTouches(s,{x:0,y:0,right:256,bottom:256}),true);
  assert.equal(coatShapeTouches(s,{x:256,y:256,right:512,bottom:512}),true);
});
test('finishing a crossing only changes nearby pieces, not the full merged crossing street',()=>{
  const l=cityLayout('zeitoun'),roads=roadsFor('zeitoun'),street=new Set(streetFor('zeitoun',roads[0].id).map(r=>r.id));
  const before=pavingPlan(l.roadX,l.roadY,roads,new Set(roads.filter(r=>!street.has(r.id)).map(r=>r.id)));
  const after=pavingPlan(l.roadX,l.roadY,roads,new Set(roads.map(r=>r.id)));
  const old=before.runs.flatMap(r=>splitPavingRun(r)).map(r=>JSON.stringify(r));
  const next=new Set(after.runs.flatMap(r=>splitPavingRun(r)).map(r=>JSON.stringify(r)));
  assert.ok(old.filter(r=>next.has(r)).length>old.length*.8,'at least 80% of existing road pieces must remain unchanged');
});
