import { building, projects } from './catalog';
import type { GameState } from './model';

/** Temporary playtesting override. Keep the original catalog/economy intact. */
export let MINUTE_TEST_TIMERS = true;
export const TEST_MINUTE = 60000;
/** Unit fixtures can still exercise the normal game rules. */
export function setMinuteTestTimers(enabled: boolean) { MINUTE_TEST_TIMERS = enabled; }
export const activityDuration = (normalMs: number) => MINUTE_TEST_TIMERS ? TEST_MINUTE : normalMs;
export const buildingIncomePeriod = () => activityDuration(3600000);
export const incomeIntervalLabel = () => MINUTE_TEST_TIMERS ? 'دقيقة' : 'ساعة';
export const timingProfile = () => MINUTE_TEST_TIMERS ? 'minute-test' as const : 'normal' as const;

/** Convert long pending work once; reloads must never restart its countdown.
 * Keep construction progress and earned (uncollected) income across the change.
 * Moving convoys retain their saved trajectory/deadlines; travel is distance-based,
 * not a work-countdown override. */
export function applyTimingProfile(state: GameState, now: number) {
  const wanted = timingProfile();
  if (state.timerProfile === wanted) return;
  const oldIncomePeriod = state.timerProfile === 'minute-test' ? TEST_MINUTE : 3600000;
  const convertIncome = (at: number) =>
    Math.max(0, now - Math.floor(Math.max(0, now - at) / oldIncomePeriod * buildingIncomePeriod()));
  const retime = (p: { startedAt: number; endsAt: number }, normalMs: number) => {
    const duration = activityDuration(normalMs);
    const fraction = Math.max(0, Math.min(1, (now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt)));
    p.startedAt = Math.max(0, now - Math.floor(fraction * duration));
    p.endsAt = p.startedAt + duration;
  };
  for (const d of state.districts) {
    for (const p of d.plots) {
      if (p.status === 'building') retime(p, building(p.buildingId).duration * 1000);
      if (p.status === 'built' && building(p.buildingId).income > 0) p.incomeAt = convertIncome(p.incomeAt);
    }
    for (const p of d.projects) {
      const def = projects(d.id === 'rashid').find(x => x.id === p.id)!;
      if (p.status === 'building') retime(p, def.duration * 1000);
      if (p.status === 'complete' && def.period > 0) p.incomeAt = convertIncome(p.incomeAt);
    }
  }
  state.timerProfile = wanted;
}
