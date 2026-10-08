import type { Plot } from './model';
import manifest from './realistic-manifest.json';
import { rotateLotPoint } from './orientation';
import { buildingRenderScale } from './building-footprint';
export type DepotPoint = [number, number];

/**
 * Calibrated to the four-view recycling artwork, not a separate gate icon.
 * Shared by the doorway and convoy: never place an unrelated gate on the pavement.
 */
export function depotGeometry(p: Plot) {
  const side = p.orientation === 0 || p.orientation === 1 ? 'front' : 'right';
  const key = `recycling_${side}`;
  const m = (manifest as Record<string, { w: number; h: number; ay: number; ground?: { w: number; h: number } }>)[key];
  const scale = buildingRenderScale(key, m);
  const aperture = side === 'front'
    ? { x: 50, y: 101, hw: 13.5, h: 35, slope: .47 }
    : { x: 155, y: 109, hw: 10.5, h: 39, slope: -.56 };
  // Calibration is expressed at 196px sprite width; preserve it if packing shrinks a sheet.
  const k = m.w / 196;
  const dx = (aperture.x * k - m.w / 2) * scale;
  const dy = (aperture.y * k - m.h * m.ay) * scale;
  let offset: DepotPoint = [dy / 64 + dx / 128, dy / 64 - dx / 128];
  if (p.orientation === 1 || p.orientation === 3) offset = [-offset[0], -offset[1]];
  const outward = rotateLotPoint([0, 1], p.orientation);
  const threshold: DepotPoint = [p.x + 1 + offset[0], p.y + 1 + offset[1]];
  const inside: DepotPoint = [threshold[0] - outward[0] * .75, threshold[1] - outward[1] * .75];
  const outside: DepotPoint = outward[0]
    ? [p.x + (outward[0] > 0 ? 2 : 0), threshold[1]]
    : [threshold[0], p.y + (outward[1] > 0 ? 2 : 0)];
  return { inside, threshold, outside, outward, visible: p.orientation === 0 || p.orientation === 2,
    halfWidth: aperture.hw * k * scale, height: aperture.h * k * scale, slope: aperture.slope };
}
export const depotPortal = depotGeometry;
