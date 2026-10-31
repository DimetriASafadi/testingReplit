import test, { beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, newGame, SAVE_KEY, UPGRADE_BACKUP_KEY, districtComplete } from '../src/engine';
import { building, legacyProjects, projects, DISTRICTS } from '../src/catalog';
import { validateSave } from '../src/save';
import { FINAL_TIMING, setTestTimers } from '../src/timing-settings';
import { actualWorkDuration, STREET_WORK_MS } from '../src/construction-rules';
import { buildingIncomePeriod } from '../src/game-timing';
import { projectIncome } from '../src/economy';
import { roadsFor } from '../src/roads';
import { unitInJob } from '../src/fleet-depots';
import { buildingIncomeDetails } from '../src/building-income';

class Storage {
  values=new Map<string,string>(); fail=false;
  getItem(k:string){return this.values.get(k)??null;}
  setItem(k:string,v:string){if(this.fail)throw Error('quota');this.values.set(k,v);}
}
beforeEach(()=>setTestTimers(true));
function fixture() {
  const storage=new Storage(),now=Date.now()+100000,store=new GameStore(storage,now);
  store.dispatch({type:'enter',districtId:'shujaiya'});
  const d=store.state.districts[0];d.difficultyChosen=true;
  for(const [id,buildingId]of [[0,'recycling'],[1,'equipment_store'],[2,'asphalt']]as const)
    Object.assign(d.plots[id],{status:'built',buildingId});
  return {store,storage,d,now};
}
test('global purchases have no local cap, but use requires a warehouse and busy units cannot teleport',()=>{
  const {store,storage,d}=fixture();
  for(let i=0;i<10;i++)store.dispatch({type:'buy',kind:'bulldozer'});
  store.dispatch({type:'buy',kind:'excavator'});store.dispatch({type:'buy',kind:'truck'});
  store.dispatch({type:'clear',plotId:3});const j=structuredClone(store.state.jobs[0]);
  d.claimed=true;d.completionRewarded=true;
  let other=store.state.districts[1];other.unlocked=true;other.difficultyChosen=true;
  Object.assign(other.plots[0],{status:'built',buildingId:'recycling'});
  store.dispatch({type:'enter',districtId:other.id});
  assert.throws(()=>store.dispatch({type:'clear',plotId:3}),/مخزن/);
  other=store.state.districts[1];
  Object.assign(other.plots[1],{status:'built',buildingId:'equipment_store'});
  store.dispatch({type:'enter',districtId:other.id});
  assert.equal(store.state.units.filter(u=>u.home?.districtId===d.id).length,3);
  assert.throws(()=>store.dispatch({type:'clear',plotId:3}),/متاحة/);
  assert.equal(store.state.units.length,12);
  store.tick(j.toolsReturnEnd!);
  const truck=store.state.units.find(u=>u.kind==='truck')!;
  if(store.state.lastSeen<j.returnEnd)assert.equal(truck.home!.districtId,d.id);
  store.tick(j.returnEnd);
  store.dispatch({type:'clear',plotId:3});
  assert.equal(store.state.jobs[0].districtId,other.id);
  assert.equal(store.state.units.find(u=>u.id===truck.id)!.home!.districtId,other.id);
  const reopened=new GameStore(storage,store.state.lastSeen);
  assert.equal(reopened.state.units.length,12);
  for(const u of reopened.state.units)assert.ok(reopened.state.jobs.filter(j=>unitInJob(j,u,reopened.state.lastSeen)).length<=1);
  validateSave(reopened.state);
});
test('active construction speeds up once, confirms its target and rolls back on storage failure',()=>{
  const {store,storage,d}=fixture();store.state.gems=6;
  Object.assign(d.plots[4],{status:'empty'});
  store.dispatch({type:'place',plotId:4,buildingId:'small_house',orientation:0});
  store.tick(store.state.lastSeen+10000);
  const startedAt=d.plots[4].startedAt,coins=store.state.coins,raw=storage.getItem(SAVE_KEY);
  storage.fail=true;
  assert.throws(()=>store.dispatch({type:'accelerateBuilding',plotId:4,expectedStartedAt:startedAt}));
  assert.equal(store.state.gems,6);assert.equal(storage.getItem(SAVE_KEY),raw);
  storage.fail=false;
  store.dispatch({type:'accelerateBuilding',plotId:4,expectedStartedAt:startedAt});
  const p=store.state.districts[0].plots[4];
  assert.equal(store.state.gems,4);assert.equal(store.state.coins,coins);
  assert.equal(p.endsAt-store.state.lastSeen,10000);
  assert.throws(()=>store.dispatch({type:'accelerateBuilding',plotId:4,expectedStartedAt:p.startedAt}),/بالفعل/);
  const reloaded=new GameStore(storage,store.state.lastSeen);
  assert.equal(reloaded.state.gems,4);assert.equal(reloaded.state.districts[0].plots[4].endsAt,p.endsAt);
  reloaded.tick(p.endsAt);assert.throws(()=>reloaded.dispatch({type:'accelerateBuilding',plotId:4,expectedStartedAt:p.startedAt}));
  assert.equal(reloaded.state.gems,4);
});
test('legacy project replacement preserves earned funds, refunds unfinished removals exactly once, and backs up the source',()=>{
  const now=Date.now()+100000,s=newGame(now),storage=new Storage();delete s.reconstructionVersion;s.timerProfile='minute-test';
  for(const d of s.districts)d.projects=legacyProjects(d.id==='rashid').map(p=>({id:p.id,status:'idle',startedAt:0,endsAt:0,incomeAt:0}));
  const d=s.districts[0],get=(id:string)=>d.projects.find(p=>p.id===id)!;
  Object.assign(get('road'),{status:'building',startedAt:now-60000,endsAt:now+100000});
  Object.assign(get('water'),{status:'building',startedAt:now-15000,endsAt:now+45000});
  Object.assign(get('farm'),{status:'ready',startedAt:now-60000,endsAt:now-1});
  Object.assign(get('industry'),{status:'building',startedAt:now-60000,endsAt:now-1});
  Object.assign(get('commerce'),{status:'complete',startedAt:now-180000,endsAt:now-120000,incomeAt:now-120000,gemRewarded:true});
  const old=legacyProjects(),expected=s.coins+150000+['farm','industry','commerce'].reduce((n,id)=>n+projectIncome(d,old.find(p=>p.id===id)!),0);
  const raw=JSON.stringify(s);storage.setItem(SAVE_KEY,raw);
  const restored=new GameStore(storage,now),nd=restored.state.districts[0];
  assert.equal(storage.getItem(UPGRADE_BACKUP_KEY),raw);
  assert.equal(restored.state.coins,expected);assert.equal(restored.state.gems,6);
  assert.deepEqual(nd.projects.map(p=>p.id),projects().map(p=>p.id));
  assert.equal(nd.projects.find(p=>p.id==='water')!.endsAt,now+22500);
  assert.equal(nd.projects.find(p=>p.id==='commerce')!.status,'complete');
  const second=new GameStore(storage,now+1000);
  assert.equal(second.state.coins,expected);assert.equal(second.state.gems,6);
  assert.equal(second.state.districts[0].projects.find(p=>p.id==='water')!.endsAt,now+22500);
  validateSave(second.state);
});
test('failed migration backup leaves the original saved game untouched',()=>{
  const storage=new Storage(),s=newGame(Date.now()+100000);s.timerProfile='normal';
  const raw=JSON.stringify(s);storage.setItem(SAVE_KEY,raw);storage.fail=true;
  assert.throws(()=>new GameStore(storage,s.lastSeen),/احتياطية/);
  assert.equal(storage.getItem(SAVE_KEY),raw);
});
test('completed neighborhoods reward automatically once and claiming only unlocks the next one',()=>{
  const {store,storage,d}=fixture();
  for(const p of d.plots)p.status='built';
  for(const p of d.projects){p.status='complete';p.gemRewarded=true;}
  d.pavedRoads=roadsFor(d.id).map(r=>r.id);
  assert.ok(districtComplete(d));const coins=store.state.coins;
  store.tick(store.state.lastSeen);
  assert.equal(store.state.coins,coins+DISTRICTS[0].reward);assert.equal(store.state.gems,15);
  assert.equal(d.completionRewarded,true);assert.equal(d.claimed,false);
  store.dispatch({type:'celebrated'});
  const loaded=new GameStore(storage,store.state.lastSeen+1000);
  assert.equal(loaded.state.coins,store.state.coins);
  loaded.dispatch({type:'claim'});
  assert.equal(loaded.state.coins,coins+DISTRICTS[0].reward);
  assert.equal(loaded.state.currentDistrict,'tuffah');
});
test('test work is 30 seconds for every difficulty; production configuration retains final settings',()=>{
  const d=newGame().districts[0];
  for(const difficulty of ['easy','medium','hard']as const){d.difficulty=difficulty;assert.equal(actualWorkDuration(d,STREET_WORK_MS),30000);}
  assert.equal(buildingIncomePeriod(),30000);
  assert.equal(projects().find(p=>p.id==='water')!.duration,18000);
  assert.equal(projects().find(p=>p.id==='power')!.duration,18000);
  assert.equal(projects().find(p=>p.id==='flag')!.duration,14400);
  setTestTimers(false);assert.equal(buildingIncomePeriod(),600000);
  d.difficulty='hard';assert.equal(actualWorkDuration(d,STREET_WORK_MS),624000);
  assert.equal(FINAL_TIMING.wheelMs,7200000);
  for(const finale of [false,true]){
    const defs=projects(finale);assert.equal(defs.length,7);
    assert.ok(defs.every(p=>!p.prerequisite||defs.some(q=>q.id===p.prerequisite)));
    assert.ok(defs.every(p=>!p.income));
  }
});
test('old clearing and paving timelines retain the current stage and uncompressed travel on test migration',()=>{
  for(const paving of [false,true]) {
    setTestTimers(false);
    const {store,storage,d}=fixture();
    for(const kind of ['bulldozer','excavator','truck']as const)store.dispatch({type:'buy',kind});
    if(paving){store.dispatch({type:'buyPaver'});store.dispatch({type:'pave',roadId:roadsFor(d.id)[0].id});}
    else store.dispatch({type:'clear',plotId:3});
    const before=paving?d.roadWorks![0]:store.state.jobs[0];
    const now=before.arrival+5000;
    store.tick(now);
    setTestTimers(true);const loaded=new GameStore(storage,now);
    if(paving) {
      const w=loaded.state.districts[0].roadWorks![0],p=w.preparation!;
      assert.equal(p.aggregateEnd-w.arrival+w.workEnd-p.asphaltStart,30000);
      assert.ok(now>w.arrival&&now<p.sandEnd);
      assert.equal(w.arrival-w.start,before.arrival-before.start);
    }else{
      const j=loaded.state.jobs[0];assert.equal(j.workEnd-j.arrival,30000);
      assert.equal(j.arrival-j.start,before.arrival-before.start);
      assert.equal(j.unloadEnd!-j.unloadStartAt!,6000);
    }
    validateSave(loaded.state);
    assert.deepEqual(new GameStore(storage,now).state,loaded.state);
  }
});
test('income waits for collection, is a single payout after ten production minutes, and never comes from housing',()=>{
  setTestTimers(false);const {store,d}=fixture(),p=d.plots[4],now=store.state.lastSeen;
  Object.assign(p,{status:'built',buildingId:'wheat',incomeAt:now});
  assert.equal(buildingIncomeDetails(p,now+599999).amount,0);
  assert.equal(buildingIncomeDetails(p,now+600000*20).amount,building('wheat').income);
  store.tick(now+600000*20);const coins=store.state.coins;
  store.dispatch({type:'collectBuilding',plotId:4});
  assert.equal(store.state.coins-coins,building('wheat').income);
  assert.equal(buildingIncomeDetails(p,store.state.lastSeen).amount,0);
  p.buildingId='small_house';assert.equal(buildingIncomeDetails(p,store.state.lastSeen+600000*30).amount,0);
});
