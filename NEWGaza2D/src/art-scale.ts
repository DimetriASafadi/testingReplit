import { SPR } from './realart';

/** Baseline art scale and calibrated world-height caps (uniform scale, never vertical squash). */
export const ART_SC = 0.72;
export const RUIN_MAX_H = 155;
export const BUILT_MAX_H = 187;

/** scale for a finished building sprite (and every construction stage derived from it) */
export const builtScale = (finalKey: string, base = ART_SC) => { const m = SPR[finalKey]; return m ? Math.min(base, BUILT_MAX_H / m.h) : base; };
export const ruinScale = (h: number, base = ART_SC) => Math.min(base, RUIN_MAX_H / h);
