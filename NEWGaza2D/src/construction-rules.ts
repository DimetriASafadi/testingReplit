import type { BuildingDef, DistrictState } from './model';

/** Seconds. Explicit exceptions precede the size bands, so factories never
 * accidentally inherit the time of a two-storey house. */
export function constructionSeconds(id: string, category: BuildingDef['category'], floors: number) {
  if (id === 'recycling' || id === 'equipment_store') return 120;
  if (category === 'industry') return 1800;
  if (id === 'housing_complex' || floors >= 10) return 1800;
  if (floors >= 8) return 1500;
  if (floors >= 6) return 1200;
  if (floors >= 3) return 900;
  return floors >= 2 ? 600 : 300;
}
export const clearingSeconds = (floors: number) => floors <= 2 ? 120 : 180;
export const STREET_WORK_MS = 8 * 60_000;
/** Preparation occupies half of the work time (sand, then aggregate).
 * Paver travel after preparation is deliberately outside this budget. */
export const STREET_STAGE_SHARES = { sand: .25, aggregate: .25, asphalt: .5 } as const;
export const actualWorkDuration = (d: DistrictState, ms: number) =>
  Math.round(ms * ({ easy: 1, medium: 1.15, hard: 1.3 }[d.difficulty ?? 'medium']));
