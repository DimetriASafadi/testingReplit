import type { Plot } from './model';
export type DepotPoint = [number, number];

/**
 * Calibrated to the recycling front/rear artwork at its normal .72 scale.
 * Shared by the doorway and convoy: never place an unrelated gate on the pavement.
 */
export function depotGeometry(p: Plot) {
  const back = p.orientation === 1;
  const outward: DepotPoint = back ? [1, 0] : [0, 1];
  const threshold: DepotPoint = [p.x + (back ? 1.11 : .54), p.y + (back ? 1.03 : 1.2)];
  const inside: DepotPoint = [threshold[0] - outward[0] * .75, threshold[1] - outward[1] * .75];
  const outside: DepotPoint = back ? [p.x + 2, threshold[1]] : [threshold[0], p.y + 2];
  return { inside, threshold, outside, outward, halfWidth: 14, height: 28, slope: back ? -.35 : .5 };
}
export const depotPortal = depotGeometry;
