import { building } from './catalog';
import { buildingIncomePeriod } from './game-timing';
import type { Plot } from './model';

/** A single payout waits for collection; only collection restarts the clock. */
export function buildingIncomeDetails(plot: Plot, now: number) {
  const perPeriod = building(plot.buildingId).income;
  const period = buildingIncomePeriod();
  const periods = plot.status === 'built' && perPeriod > 0
    ? Number(now >= plot.incomeAt + period) : 0;
  return {
    perPeriod, period, periods,
    amount: periods * perPeriod,
    nextAt: plot.incomeAt + period,
  };
}
