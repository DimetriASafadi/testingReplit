import { building } from './catalog';
import type { GameState } from './model';
import { actualWorkDuration, clearingSeconds, STREET_WORK_MS } from './construction-rules';
import { effectiveDuration, FINAL_TIMING, setTestTimers, TEST_TIMERS, wheelCooldown } from './timing-settings';

// Preserve the fixture API; it now toggles the explicitly requested 30s profile.
export { TEST_TIMERS as MINUTE_TEST_TIMERS } from './timing-settings';
export const TEST_MINUTE = 60_000; // legacy profile conversion only
export const setMinuteTestTimers = setTestTimers;
export const activityDuration = effectiveDuration;
export const buildingIncomePeriod = () => effectiveDuration(FINAL_TIMING.incomeMs);
export const incomeIntervalLabel = () => TEST_TIMERS ? '30 ثانية' : '10 دقائق';
export const timingProfile = () => TEST_TIMERS ? 'test-30s' as const : 'production-v2' as const;

/** Compress only work spans, never physical travel/unloading. Anchoring the
 * piecewise timeline at now retains the current stage and its progress. */
function timeline(now: number, spans: [number,number,number][]) {
  const transform = (t:number) => t + spans.reduce((sum,[a,b,target])=>
    sum + Math.max(0,Math.min(b-a,t-a))*(target/(b-a)-1),0);
  const offset = now-transform(now);
  return (t:number) => Math.max(0,Math.round(transform(t)+offset));
}
export function applyTimingProfile(state: GameState, now: number) {
  const wanted = timingProfile();
  if (state.timerProfile === wanted) return;
  const oldIncome = state.timerProfile==='minute-test'?60_000:state.timerProfile==='test-30s'?30_000:
    state.timerProfile==='production-v2'?FINAL_TIMING.incomeMs:3_600_000;
  const newIncome = buildingIncomePeriod();
  for (const d of state.districts) {
    for (const p of d.plots) {
      if (p.status==='building' && p.endsAt>now) {
        const duration = actualWorkDuration(d,building(p.buildingId).duration*1000)/(p.accelerated?2:1);
        const retime=timeline(now,[[p.startedAt,p.endsAt,duration]]);
        p.startedAt=retime(p.startedAt); p.endsAt=retime(p.endsAt);
      }
      if (p.status==='built' && building(p.buildingId).income>0) {
        p.incomeAt=Math.max(0,now-Math.round(Math.min(1,Math.max(0,now-p.incomeAt)/oldIncome)*newIncome));
      }
    }
    // Definitions are already migrated before this method is called.
    for (const p of d.projects) if(p.status==='building' && p.endsAt>now) {
      // Avoid a catalog import cycle in timing settings; lookup here is safe.
      const def = projectDefinition(d.id,p.id);
      const duration=actualWorkDuration(d,def.duration*1000);
      const retime=timeline(now,[[p.startedAt,p.endsAt,duration]]);
      p.startedAt=retime(p.startedAt);p.endsAt=retime(p.endsAt);
    }
    for (const w of d.roadWorks??[]) if(w.workEnd>now) {
      const p=w.preparation, total=actualWorkDuration(d,STREET_WORK_MS);
      const spans:[number,number,number][]=p?[
        [w.arrival,p.sandEnd,total*.25],[p.sandEnd,p.aggregateEnd,total*.25],
        [p.asphaltStart,w.workEnd,total*.5],
      ]:[[w.arrival,w.workEnd,total]];
      const retime=timeline(now,spans);
      for(const k of ['start','arrival','workEnd','returnEnd'] as const)w[k]=retime(w[k]);
      if(p)for(const k of ['sandEnd','aggregateEnd','asphaltStart','dozerReturnEnd'] as const)p[k]=retime(p[k]);
    }
  }
  for(const j of state.jobs)if(j.workEnd>now) {
    const d=state.districts.find(d=>d.id===j.districtId)!;
    const total=actualWorkDuration(d,clearingSeconds(building(d.plots[j.plotId].buildingId).floors)*1000);
    const retime=timeline(now,[[j.arrival,j.workEnd,total]]);
    for(const k of ['start','arrival','workEnd','returnEnd','departureAt','returnStartAt','toolsReturnEnd',
      'unloadAt','unloadStartAt','unloadEnd','truckHomeDepartureAt'] as const)if(j[k]!==undefined)j[k]=retime(j[k]!);
  }
  const w=state.wheel;
  if(w && w.nextAt>now) {
    const old=state.timerProfile==='test-30s'?30_000:FINAL_TIMING.wheelMs;
    w.nextAt=now+Math.max(1,Math.round(Math.min(1,(w.nextAt-now)/old)*wheelCooldown()));
    w.cooldownMs=w.nextAt-w.startedAt;
  }
  state.timerProfile = wanted;
}
import { projects } from './catalog';
const projectDefinition=(districtId:string,id:string)=>projects(districtId==='rashid').find(p=>p.id===id)!;
