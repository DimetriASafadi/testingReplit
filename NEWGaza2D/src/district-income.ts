import { projects } from './catalog';
import { buildingIncomeDetails } from './building-income';
import { activityDuration } from './game-timing';
import type { DistrictState } from './model';

/** Only earned income: neither inventory sales nor district completion rewards. */
export function districtIncome(district: DistrictState, now: number) {
  const plots = district.plots.flatMap(plot => {
    if (plot.status !== 'built') return [];
    const income = buildingIncomeDetails(plot, now);
    return income.amount > 0 ? [{ plot, periods: income.periods, amount: income.amount }] : [];
  });
  const definitions = projects(district.id === 'rashid');
  const investments = district.projects.flatMap(project => {
    const def = definitions.find(d => d.id === project.id);
    if (!def || def.income <= 0) return [];
    if (!def.period) return project.status === 'ready'
      ? [{ project, periods: 0, period: 0, amount: def.income }] : [];
    const interval = activityDuration(def.period * 1000);
    const periods = Number(now >= project.incomeAt + interval);
    return project.status === 'complete' && periods > 0
      ? [{ project, periods, period: interval, amount: periods * def.income }] : [];
  });
  return {
    plots, investments,
    count: plots.length + investments.length,
    total: [...plots, ...investments].reduce((sum, entry) => sum + entry.amount, 0),
  };
}
