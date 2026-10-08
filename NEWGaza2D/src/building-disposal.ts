import { building } from './catalog';
import type { Plot } from './model';

/** Legacy saves did not record the paid price; use their catalog price. */
export const buildingSaleValue = (plot: Plot) =>
  Math.floor((plot.paidCost ?? building(plot.buildingId).cost) / 2);

/** Completed hourly income belongs to the player, even when removing a site. */
export const buildingAccruedIncome = (plot: Plot, now: number) =>
  plot.status === 'built'
    ? Math.max(0, Math.floor((now - plot.incomeAt) / 3600000)) * building(plot.buildingId).income
    : 0;
