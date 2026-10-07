import type { EquipmentKind } from './model';

// Ground-contact chassis dimensions in metres, matching the offline rigs.
// Do not include the elevated boom or the truck's tipping payload.
const FOOTPRINT: Record<EquipmentKind, [number, number]> = {
  excavator: [4.2, 3], truck: [6.4, 2.5], bulldozer: [3.45, 2.7],
};

/** Sort by the rear ground edge of the visible chassis, not its centre.
 * A rotating vehicle's tail can already be behind a facade while its centre
 * is still in front. Image height / bucket height is not ground depth.
 */
export function machineryDepth(kind: EquipmentKind, heading: number, groundY: number,
  spriteSize: number, ortho: number, scale: number) {
  const [length, width] = FOOTPRINT[kind], step = Math.PI * 2 / 16;
  const index = Math.floor(heading / step);
  const extent = (angle: number) => {
    const c = Math.cos(angle), s = Math.sin(angle);
    return length / 2 * Math.abs(c + s) + width / 2 * Math.abs(c - s);
  };
  // Each endpoint envelope includes both crossfaded headings. Interpolating
  // envelopes (rather than switching a max at every atlas boundary) keeps
  // sorting continuous while covering every visible ground corner.
  const envelope = (i: number) => Math.max(extent((i - 1) * step),
    extent(i * step), extent((i + 1) * step));
  const blend = heading / step - index;
  const start = envelope(index), rear = start + (envelope(index + 1) - start) * blend;
  const groundPixelsPerMetre = spriteSize / ortho * scale / (2 * Math.SQRT2);
  return groundY - rear * groundPixelsPerMetre + .4;
}
