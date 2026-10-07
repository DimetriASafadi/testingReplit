type Point = readonly [number, number];
const PERIMETER: readonly Point[] = [[-1, 1], [.35, 1], [1, 1], [1, -.28], [1, -1], [-.32, -1], [-1, -1], [-1, .3]];
const DWELL = [800, 2400, 0, 2200, 0, 2000, 0, 2600];
interface Leg { from: Point; to: Point; start: number; walkEnd: number; end: number; distance: number; before: number; station: number }
export interface WorkerRoute { legs: Leg[]; duration: number }
export interface WorkerPose { x: number; y: number; role: 'walk' | 'carry' | 'hammer'; frame: number; dir: number }

/** Immutable site-local perimeter. Never cut through the building or leave its lot. */
export function createWorkerRoute(radius: number, orientation: 0 | 1, worker: number): WorkerRoute {
  const r = Math.max(.3, Math.min(.65, radius) - worker * .014);
  const points = PERIMETER.map(([x, y]): Point => orientation ? [y * r, x * r] : [x * r, y * r]);
  const legs: Leg[] = [];
  let clock = 0, distance = 0;
  for (let i = 0; i < points.length; i++) {
    const station = (i + 1) % points.length, from = points[i], to = points[station];
    const length = Math.hypot(to[0] - from[0], to[1] - from[1]);
    const walkEnd = clock + length / .19 * 1000, end = walkEnd + DWELL[station];
    legs.push({ from, to, start: clock, walkEnd, end, distance: length, before: distance, station });
    clock = end; distance += length;
  }
  return { legs, duration: clock };
}

/** Saved construction age drives movement, so reload does not reset the crew. */
export function sampleWorkerRoute(route: WorkerRoute, elapsed: number, plot: number, worker: number): WorkerPose {
  const t = ((elapsed + plot * 911 + worker * 7100) % route.duration + route.duration) % route.duration;
  const leg = route.legs.find(l => t < l.end)!;
  if (t < leg.walkEnd) {
    const u = (t - leg.start) / (leg.walkEnd - leg.start), k = u * u * (3 - 2 * u);
    const x = leg.from[0] + (leg.to[0] - leg.from[0]) * k, y = leg.from[1] + (leg.to[1] - leg.from[1]) * k;
    return { x, y, role: leg.station === 1 ? 'carry' : 'walk',
      frame: Math.floor((leg.before + leg.distance * k) / .12 * 8) % 8,
      dir: Math.sign((leg.to[0] - leg.from[0]) - (leg.to[1] - leg.from[1])) || 1 };
  }
  const [x, y] = leg.to, role = leg.station === 0 ? 'carry' : 'hammer';
  return { x, y, role, frame: role === 'carry' ? 0 : Math.floor((t - leg.walkEnd) / 110) % 8,
    dir: Math.sign(y - x) || 1 };
}
