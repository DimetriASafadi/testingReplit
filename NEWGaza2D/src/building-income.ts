import { building } from './catalog';
import { buildingIncomePeriod } from './game-timing';
import type { Plot } from './model';

/** Shared by the building panel, individual/bulk collection and disposal.
 * Keep unfinished fractions: collecting does not restart the income clock. */
export function buildingIncomeDetails(plot: Plot, now: number) {
  const perPeriod = building(plot.buildingId).income;
  const period = buildingIncomePeriod();
  const periods = plot.status === 'built' && perPeriod > 0
    ? Math.max(0, Math.floor((now - plot.incomeAt) / period)) : 0;
  return {
    perPeriod, period, periods,
    amount: periods * perPeriod,
    nextAt: plot.incomeAt + (periods + 1) * period,
  };
}
