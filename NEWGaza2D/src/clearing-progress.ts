import type { Job } from './model';

/** Visual progress starts at actual arrival, not dispatch. Engine owns completion and rewards. */
export function clearingProgress(job: Pick<Job, 'arrival' | 'workEnd'>, now: number) {
  return Math.max(0, Math.min(1, (now - job.arrival) / Math.max(1, job.workEnd - job.arrival)));
}
export function clearingStage(progress: number): 0 | 1 | 2 | 3 | 4 {
  return progress < .16 ? 0 : progress < .36 ? 1 : progress < .64 ? 2 : progress < .88 ? 3 : 4;
}
