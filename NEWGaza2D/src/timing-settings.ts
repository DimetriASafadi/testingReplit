/** Production values never change when enabling the temporary playtest profile. */
export const FINAL_TIMING = {
  waterSeconds: 5 * 3600,
  powerSeconds: 5 * 3600,
  flagSeconds: 4 * 3600,
  incomeMs: 10 * 60_000,
  wheelMs: 2 * 60 * 60_000,
  adBatchMs: 5 * 60 * 60_000,
} as const;
export const TEST_DURATION_MS = 30_000;
export let TEST_TIMERS = true;
export function setTestTimers(enabled: boolean) { TEST_TIMERS = enabled; }
export const effectiveDuration = (normalMs: number) => TEST_TIMERS ? TEST_DURATION_MS : normalMs;
export const effectiveWorkDuration = (normalMs: number, multiplier: number) =>
  TEST_TIMERS ? TEST_DURATION_MS : Math.round(normalMs * multiplier);
export const wheelCooldown = () => effectiveDuration(FINAL_TIMING.wheelMs);
