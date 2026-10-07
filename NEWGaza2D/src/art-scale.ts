import { SPR } from './realart';
import { buildingFootprint, buildingMaxHeight } from './building-footprint';
import { U, PLOT } from './art';

/** Baseline art scale and calibrated world-height caps (uniform scale, never vertical squash). */
export const ART_SC = 0.72;
export const RUIN_MAX_H = 155;
export const BUILT_MAX_H = 187;

/** scale for a finished building sprite (and every construction stage derived from it) */
export const builtScale = (finalKey: string, base = ART_SC) => {
  const m = SPR[finalKey], footprint = base * buildingFootprint(finalKey).factor;
  if (!m) return footprint;
  // Ground-bearing scenery must fit both axes of the plot, not only total height.
  const groundWidth = m.ground?.w ?? m.w;
  const groundHeight = m.ground?.h ?? groundWidth / 2;
  return Math.min(footprint, buildingMaxHeight(finalKey) / m.h,
    (PLOT * U * 2) / groundWidth, (PLOT * U) / groundHeight);
};
export const ruinScale = (h: number, base = ART_SC) => Math.min(base, RUIN_MAX_H / h);
