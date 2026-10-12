import test from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, SAVE_KEY, districtProgress, newGame } from '../src/engine';
import { roadsFor, pavingRoute, along } from '../src/roads';
import { validateSave } from '../src/save';
import { WHEEL_COOLDOWN, WHEEL_PRIZES, wheelResult } from '../src/rewards';
import { projectIncome, workDuration } from '../src/economy';
import { projects } from '../src/catalog';
const setup=()=>{
 let raw:string|null=null;let fail=false;
 const storage={getItem:()=>raw,setItem:(_k:string,v:string)=>{if(fail)throw Error('quota');raw=v;}};
 const store=new GameStore(storage,Date.now()+100000);
 store.dispatch({type:'enter',districtId:'shujaiya'});
 return{store,storage,setFail:()=>fail=true};
};
test('road factory, equipment, charge, offline paving and return survive reload exactly once',()=>{
 const{store,storage}=setup(),r=roadsFor(store.state.districts[0].id)[0];
 assert.throws(()=>store.dispatch({type:'pave',roadId:r.id}));
 const d=store.state.districts[0];
 Object.assign(d.plots[0],{status:'built',buildingId:'asphalt'});
 store.dispatch({type:'buyPaver'});const before=store.state.coins;
 store.dispatch({type:'pave',roadId:r.id});const w=d.roadWorks![0];
 assert.equal(before-store.state.coins,120);assert.throws(()=>store.dispatch({type:'pave',roadId:r.id}));
 assert.throws(()=>store.dispatch({type:'rotateBuilding',plotId:0}));
 const restored=new GameStore(storage,w.workEnd);
 assert.deepEqual(restored.state.districts[0].pavedRoads,[r.id]);
 restored.tick(w.returnEnd);assert.equal(restored.state.districts[0].roadWorks!.length,0);
 restored.tick(w.returnEnd+1000);assert.equal(restored.state.districts[0].pavedRoads!.length,1);
 validateSave(restored.state);
});
test('wallet transactions persist before success and reject duplicates/failed storage',()=>{
 const{store,storage,setFail}=setup();store.state.gems=20;
 const before=store.state.coins;
 store.dispatch({type:'exchange',pack:0,requestId:'test-order-1'});
 assert.equal(store.state.coins,before+10000);assert.equal(store.state.gems,18);
 assert.throws(()=>store.dispatch({type:'exchange',pack:0,requestId:'test-order-1'}));
 assert.equal(new GameStore(storage,store.state.lastSeen).state.coins,before+10000);
 setFail();assert.throws(()=>store.dispatch({type:'exchange',pack:0,requestId:'test-order-2'}));
 assert.equal(store.state.coins,before+10000);assert.equal(store.state.gems,18);
});
test('wheel weighted results, persisted reward and two-hour cooldown',()=>{
 assert.equal(WHEEL_PRIZES.reduce((s,p)=>s+p.weight,0),100);
 assert.equal(wheelResult(0),0);assert.equal(wheelResult(.99),7);
 const{store,storage}=setup();const before=store.state.coins;
 store.dispatch({type:'spin'});const w=store.state.wheel!;
 assert.equal(store.state.coins,before+WHEEL_PRIZES[w.result].coins);
 assert.equal(w.nextAt-w.startedAt,WHEEL_COOLDOWN);
 const reloaded=new GameStore(storage,w.startedAt+5000);
 assert.equal(reloaded.state.coins,store.state.coins);
 assert.throws(()=>reloaded.dispatch({type:'spin'}));
 reloaded.tick(w.nextAt);reloaded.dispatch({type:'spin'});
 validateSave(reloaded.state);
});
test('legacy progress retains rewards, deadlines and wallet; only rebuilt paved district reaches 100',()=>{
 const s=newGame(Date.now()+100000);delete s.expansionVersion;s.coins=123456;s.currentDistrict=s.districts[0].id;
 s.districts[0].claimed=true;s.districts[1].unlocked=true;
 let raw=JSON.stringify(s);
 const store=new GameStore({getItem:()=>raw,setItem:(_k,v)=>raw=v},s.lastSeen);
 assert.equal(store.state.coins,123456);assert.equal(districtProgress(store.state.districts[0]),100);
 const d=store.state.districts[1];for(const p of d.plots)p.status='built';for(const p of d.projects)p.status='complete';
 assert.ok(districtProgress(d)<100);d.pavedRoads=roadsFor(d.id).map(r=>r.id);assert.equal(districtProgress(d),100);
 d.plots[0].status='empty';assert.ok(districtProgress(d)<100);
});
test('difficulty selected once changes only new timers and retains profitable repeatable farming',()=>{
 const{store}=setup();store.dispatch({type:'difficulty',difficulty:'hard'});
 assert.throws(()=>store.dispatch({type:'difficulty',difficulty:'easy'}));
 const d=store.state.districts[0],farm=projects(false).find(p=>p.id==='farm')!;
 assert.ok(projectIncome(d,farm)>farm.cost);
 store.dispatch({type:'place',plotId:0,buildingId:'school',orientation:0});
 assert.equal(d.plots[0].endsAt-d.plots[0].startedAt,workDuration(d,60000));
});
test('pavers leave interior lots along parcel seams, never neighbouring building interiors',()=>{
 const d=newGame().districts[0],road=roadsFor(d.id)[0];
 for(const p of d.plots){
  const route=pavingRoute(d,p,road);
  for(let f=0;f<1;f+=.004){
   const [x,y]=along(route,f).p;
   assert.equal(d.plots.find(q=>q.id!==p.id&&x>q.x+.28&&x<q.x+1.72&&y>q.y+.28&&y<q.y+1.72),undefined);
  }
 }
});
