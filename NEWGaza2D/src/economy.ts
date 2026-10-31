import type { Difficulty, DistrictState, Plot } from './model';
import { effectiveWorkDuration } from './timing-settings';

/** Initial balance, intentionally centralized for the later tuning requested. */
export const DIFFICULTIES = {
  easy: { name:'سهل', income:.7, time:1 },
  medium: { name:'متوسط', income:.45, time:1.15 },
  hard: { name:'صعب', income:.25, time:1.3 },
} as const;
export const STARTING_COINS = 350000;
export const PAVER_COST = 10000;
export const ROAD_COST = 120;
export const difficultyOf = (d: DistrictState) => DIFFICULTIES[d.difficulty ?? 'medium'];
export const workDuration = (d: DistrictState, normalMs:number) => effectiveWorkDuration(normalMs,difficultyOf(d).time);
export const plotIncome = (p:Plot, base:number) => Math.floor(base*(p.incomeFactor??1));
export const projectIncome = (d:DistrictState, def:{income:number;cost:number;period:number}) =>
  def.income <= 0 ? 0 : def.period ? Math.floor(def.income*difficultyOf(d).income)
    : def.cost + Math.floor(Math.max(0,def.income-def.cost)*difficultyOf(d).income);
export function isDifficulty(v:unknown):v is Difficulty { return typeof v==='string' && Object.hasOwn(DIFFICULTIES,v); }
