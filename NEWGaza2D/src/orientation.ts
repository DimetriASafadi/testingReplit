import type { Orientation } from './model';

// Preserve old saves: 0 remains front, 1 remains back.
export const ORIENTATION_CYCLE: readonly Orientation[] = [0, 2, 1, 3];
export const ORIENTATION_LABELS: Record<Orientation, string> = { 0: 'أمام', 1: 'خلف', 2: 'يمين', 3: 'يسار' };
export const ORIENTATION_FACES: Record<Orientation, string> = { 0: 'front', 1: 'back', 2: 'right', 3: 'left' };
export function isOrientation(value: unknown): value is Orientation {
  return Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 3;
}
export function nextOrientation(value: Orientation): Orientation {
  return ORIENTATION_CYCLE[(ORIENTATION_CYCLE.indexOf(value) + 1) % 4];
}
/** Rotate on the ground plane, never rotate a rendered sprite or the sunlight. */
export function rotateLotPoint([x, y]: readonly [number, number], orientation: Orientation): [number, number] {
  switch (orientation) {
    case 0: return [x, y];
    case 2: return [y, -x];
    case 1: return [-x, -y];
    case 3: return [-y, x];
  }
}
