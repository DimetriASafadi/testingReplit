import { legacyProjects, projects } from './catalog';
import { projectIncome } from './economy';
import { PROJECT_GEMS } from './gems';
import type { GameState } from './model';

/** Called only after validating the original data. Never writes storage itself. */
export function migrateReconstruction(state: GameState, now: number) {
  if (state.reconstructionVersion === 1) return;
  const oldPeriod = state.timerProfile === 'minute-test' ? 60_000 : 3_600_000;
  for (const d of state.districts) {
    const old = legacyProjects(d.id === 'rashid');
    for (const p of d.projects) {
      const def = old.find(v=>v.id===p.id)!;
      const removed = ['road','farm','industry'].includes(p.id);
      // A finished legacy investment has already earned its one pending payout.
      const finished = p.status === 'ready' || (p.status === 'building' && p.endsAt <= now);
      if(removed && !p.gemRewarded && (finished||p.status==='complete')) state.gems+=PROJECT_GEMS;
      if (removed && p.status === 'building' && !finished) state.coins += def.cost;
      else if ((removed || p.id === 'commerce') && finished && def.income && !def.period) {
        state.coins += projectIncome(d,def);
      }
      if (p.id==='commerce' && (p.status==='complete'||finished) && now>=(p.status==='building'?p.endsAt:p.incomeAt)+oldPeriod) {
        state.coins += projectIncome(d,def);
      }
    }
    d.projects = projects(d.id==='rashid').map(def=>{
      const previous = d.projects.find(p=>p.id===def.id);
      if (!previous) return {id:def.id,status:d.claimed?'complete' as const:'idle' as const,startedAt:0,endsAt:0,incomeAt:0,gemRewarded:d.claimed};
      return {...previous,status:previous.status==='ready'?'complete' as const:previous.status};
    });
    // Claimed neighborhoods have already been paid under the old rules.
    d.completionRewarded = d.claimed;
  }
  state.reconstructionVersion = 1;
}
