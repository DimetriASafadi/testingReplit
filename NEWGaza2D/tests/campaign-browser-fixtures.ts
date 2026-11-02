/** Disposable browser fixtures. Never import into the application. */
import { GameStore, SAVE_KEY } from '../src/engine';
import { BUILDINGS } from '../src/catalog';
import { roadsFor } from '../src/roads';
import { validateSave } from '../src/save';

export function campaignBrowserFixtures(now=Date.now()) {
  const values=new Map<string,string>();
  const store=new GameStore({getItem:k=>values.get(k)??null,setItem:(k,v)=>{values.set(k,v);}},now);
  store.dispatch({type:'enter',districtId:'zeitoun'});
  const s=store.state,d=s.districts[0];
  s.coins=500000;d.difficultyChosen=true;
  d.plots.forEach((p,i)=>Object.assign(p,{status:'built',buildingId:BUILDINGS[i%BUILDINGS.length].id,orientation:i%4,startedAt:0,endsAt:0,incomeAt:now}));
  d.plots[0].buildingId='equipment_store';d.plots[1].buildingId='asphalt';d.plots[2].buildingId='recycling';
  store.dispatch({type:'buy',kind:'bulldozer'});
  store.dispatch({type:'buyPaver'});
  store.dispatch({type:'pave',roadId:roadsFor(d.id)[0].id});
  const paving=JSON.parse(values.get(SAVE_KEY)!);
  validateSave(paving);
  const complete=structuredClone(s);
  complete.jobs=[];complete.districts[0].roadWorks=[];
  complete.districts[0].pavedRoads=roadsFor(d.id).map(r=>r.id);
  complete.districts[0].projects.forEach(p=>Object.assign(p,{status:'complete',gemRewarded:true}));
  complete.coins=500000;complete.gems=0;
  complete.districts[0].celebrated=false;complete.districts[0].completionRewarded=false;
  validateSave(complete);
  return {paving,complete};
}
