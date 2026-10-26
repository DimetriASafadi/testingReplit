import { ORIENTATION_FACES } from './orientation';
import type { Orientation } from './model';

/** Preserve the subject and save when a legacy quartet is incomplete. Never substitute another building. */
export function availableBuildingFace(stem: string, orientation: Orientation, exists: (key: string) => boolean) {
  const keyFor = (o: Orientation) => stem === 'cattle' && o === 0 ? 'farm_cattle' : `${stem}_${ORIENTATION_FACES[o]}`;
  const requested = keyFor(orientation);
  if (exists(requested)) return { key: requested, missing: false };
  // Keep the same building. No mirrored art (which would reverse sunlight).
  const alternatives: Orientation[] = orientation === 3 ? [1, 0, 2] : [0, 1, 3];
  for (const o of alternatives) if (exists(keyFor(o))) return { key: keyFor(o), missing: true };
  throw new Error(`Missing all artwork for building: ${stem}`);
}
