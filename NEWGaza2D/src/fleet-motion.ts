import type { EquipmentKind, Job, Plot } from './model';
import { depotPortal, type DepotPoint } from './depot-geometry';
import { cityLayout, plotBlock } from './city-layout';
export { depotPortal, type DepotPoint } from './depot-geometry';

export const CONVOY_GAP = 1.7;
const OPEN_WAIT = 750, TURN_WAIT = 550, INSIDE_FINISH = 250;
const ORDER: EquipmentKind[] = ['excavator', 'bulldozer', 'truck'];
const clamp = (n: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, n));
const routes = new Map<string, readonly DepotPoint[]>();
const lengths = new WeakMap<readonly DepotPoint[], number>();
function rounded(points: DepotPoint[]) {
  const clean: DepotPoint[] = [];
  for (const p of points) {
    if (clean.length && Math.hypot(p[0] - clean.at(-1)![0], p[1] - clean.at(-1)![1]) < .00001) continue;
    while (clean.length >= 2) {
      const a = clean.at(-2)!, b = clean.at(-1)!;
      const ux = b[0] - a[0], uy = b[1] - a[1], vx = p[0] - b[0], vy = p[1] - b[1];
      if (Math.abs(ux * vy - uy * vx) > .00001 || ux * vx + uy * vy <= 0) break;
      clean.pop();
    }
    clean.push(p);
  }
  const result: DepotPoint[] = [clean[0]];
  for (let i = 1; i < clean.length - 1; i++) {
    const a = clean[i - 1], b = clean[i], c = clean[i + 1];
    const l1 = Math.hypot(b[0] - a[0], b[1] - a[1]), l2 = Math.hypot(c[0] - b[0], c[1] - b[1]);
    const r = Math.min(.24, l1 * .35, l2 * .35);
    const entry: DepotPoint = [b[0] - (b[0] - a[0]) / l1 * r, b[1] - (b[1] - a[1]) / l1 * r];
    const exit: DepotPoint = [b[0] + (c[0] - b[0]) / l2 * r, b[1] + (c[1] - b[1]) / l2 * r];
    result.push(entry);
    for (let j = 1; j <= 6; j++) {
      const t = j / 6, s = 1 - t;
      result.push([s * s * entry[0] + 2 * s * t * b[0] + t * t * exit[0],
        s * s * entry[1] + 2 * s * t * b[1] + t * t * exit[1]]);
    }
  }
  result.push(clean.at(-1)!);
  return result;
}
export const workDock = (p: Plot, kind: EquipmentKind): DepotPoint => kind === 'excavator'
  // Painted tools reach into the lot; the truck stays on its right-hand street.
  ? [p.x + 1.50, p.y + 1.03]
  : kind === 'truck' ? [p.x + 1.65, p.y + 1.75] : [p.x + .95, p.y + .05];
const workApproach = (p: Plot, kind: EquipmentKind, lane: number): DepotPoint =>
  [workDock(p, kind)[0], p.y + 2 + lane];
export function fleetRoute(source: Plot, target: Plot, kind: EquipmentKind, returning = false) {
  const p = depotPortal(source), lane = returning ? -.14 : .14;
  const key = `${source.layoutId ?? 'legacy'}:${source.x}:${source.y}:${source.orientation}:${target.x}:${target.y}:${kind}:${returning}`;
  const saved = routes.get(key); if (saved) return saved;
  const street: DepotPoint = [p.outside[0] + p.outward[0] * lane, p.outside[1] + p.outward[1] * lane];
  const approach = workApproach(target, kind, lane);
  const joins: DepotPoint[] = source.orientation === 0
    ? (Math.abs(street[1] - approach[1]) < .00001 ? [] :
      [[target.x + 2 + lane, street[1]], [target.x + 2 + lane, approach[1]]])
    : [[street[0], approach[1]]];
  const sb = plotBlock(source), tb = plotBlock(target);
  let points: DepotPoint[] = [p.inside, p.threshold, street, ...joins, approach, workDock(target, kind)];
  if (sb && tb) {
    const directExit = source.orientation === 0 ? source.y + 2 === sb.y1 : source.x + 2 === sb.x1;
    const exit: DepotPoint = source.orientation === 0
      ? [directExit ? p.outside[0] : source.x + 2, sb.roadY + lane]
      : [sb.roadX + lane, directExit ? p.outside[1] : source.y + 2];
    const directEntry = target.y + 2 === tb.y1;
    const dock = workDock(target, kind), entry: DepotPoint = [directEntry ? dock[0] : target.x + 2, tb.roadY + lane];
    const bends: DepotPoint[] = [];
    if (source.orientation === 1) bends.push([exit[0], entry[1]]);
    else if (Math.abs(exit[1] - entry[1]) > .00001) {
      const roads = cityLayout(source.layoutId!).roadX;
      const score = (x: number) => Math.abs(exit[0] - x) + Math.abs(entry[0] - x);
      const cross = roads.reduce((best, x) => score(x) < score(best) ? x : best) + lane;
      bends.push([cross, exit[1]], [cross, entry[1]]);
    }
    points = [p.inside, p.threshold, p.outside,
      ...(!directExit ? [[source.x + 2, source.y + 2] as DepotPoint] : []),
      exit, ...bends, entry,
      ...(!directEntry ? [[target.x + 2, target.y + 2] as DepotPoint] : []), approach, dock];
  }
  const path = rounded(points);
  if (returning) path.reverse();
  const cached = Object.freeze(path);
  lengths.set(cached, cached.slice(1).reduce((n, q, i) => n + Math.hypot(q[0] - cached[i][0], q[1] - cached[i][1]), 0));
  if (routes.size >= 256) routes.delete(routes.keys().next().value!);
  routes.set(key, cached); return cached;
}
export const routeLength = (points: readonly DepotPoint[]) => lengths.get(points) ?? points.slice(1)
  .reduce((n, p, i) => n + Math.hypot(p[0] - points[i][0], p[1] - points[i][1]), 0);
export function routePoint(points: readonly DepotPoint[], distance: number): DepotPoint {
  let d = Math.max(0, distance);
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1], b = points[i], l = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (d <= l) { const f = l ? d / l : 0; return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f]; }
    d -= l;
  }
  return [...points[points.length - 1]];
}
function timing(j: Job, source: Plot, target: Plot, returning: boolean) {
  const max = Math.max(...ORDER.map(k => routeLength(fleetRoute(source, target, k, returning))));
  const start = returning ? (j.returnStartAt ?? j.workEnd) : (j.departureAt ?? j.start);
  const end = returning ? j.returnEnd - INSIDE_FINISH : j.arrival;
  const wait = returning ? TURN_WAIT : OPEN_WAIT;
  const speed = (max + 2 * CONVOY_GAP) / Math.max(1, end - start - wait);
  return { max, start, end, wait, speed };
}
export interface ConvoyMotion {
  position: DepotPoint; ahead: DepotPoint; visibility: number; moving: boolean;
  stage: 'waiting' | 'out' | 'work' | 'return' | 'inside'; reverse: boolean;
  portal: boolean;
}
/** Arc-distance spacing: same speed on shared roads, not three independent 0..1 lerps. */
export function convoyMotion(j: Job, source: Plot, target: Plot, kind: EquipmentKind, now: number): ConvoyMotion {
  const slot = ORDER.indexOf(kind);
  if (now >= j.arrival && now < j.workEnd) {
    const position = workDock(target, kind);
    return { position, ahead: position, visibility: 1, moving: false, stage: 'work', reverse: false, portal: false };
  }
  const returning = now >= j.workEnd, t = timing(j, source, target, returning);
  const points = fleetRoute(source, target, kind, returning), length = routeLength(points);
  const remaining = Math.max(0, t.speed * (t.end - now) - (2 - slot) * CONVOY_GAP);
  const raw = returning ? length - remaining : t.speed * (now - t.start - t.wait) - slot * CONVOY_GAP;
  const distance = clamp(raw, 0, length), position = routePoint(points, distance), ahead = routePoint(points, distance + .04);
  // Bodies stay opaque. The doorway mask clips the hidden portion rather than fading the whole machine.
  const visibility = returning ? (remaining > .001 ? 1 : 0) : (raw > 0 ? 1 : 0);
  const p = depotPortal(source), portalLength = Math.hypot(p.outside[0] - p.inside[0], p.outside[1] - p.inside[1]);
  const last = points.at(-1)!, beforeLast = points.at(-2)!;
  const finalLeg = distance >= length - Math.hypot(last[0] - beforeLast[0], last[1] - beforeLast[1]) - .001;
  return { position, ahead, visibility, moving: raw > 0 && raw < length,
    stage: returning ? (visibility ? 'return' : 'inside') : (visibility ? 'out' : 'waiting'),
     reverse: !returning && kind === 'truck' && finalLeg
       && Math.abs(ahead[0] - position[0]) < 1e-8 && ahead[1] < position[1],
     portal: visibility > 0 && (returning ? length - distance : distance) < portalLength + .7 };
}
export function gateDemand(j: Job, source: Plot, target: Plot, now: number) {
  const out = timing(j, source, target, false), back = timing(j, source, target, true);
  const outClose = out.start + out.wait + (2 * CONVOY_GAP + 2.5) / out.speed;
  const backOpen = Math.max(back.start, back.end - (3.2 + 2 * CONVOY_GAP) / back.speed);
  if (now >= out.start && now < Math.min(j.arrival, outClose)) return { open: true, since: out.start };
  if (now >= backOpen && now < j.returnEnd) return { open: true, since: backOpen };
  return { open: false, since: now };
}
export interface FactoryWindow { start: number; end: number }
export function factoryWindows(j: Job, source: Plot, target: Plot): FactoryWindow[] {
  const a = timing(j, source, target, false), b = timing(j, source, target, true);
  return [
    { start: a.start, end: a.start + a.wait + (2 * CONVOY_GAP + 2.5) / a.speed + 700 },
    // Reserve the shared approach as well as the doorway, not just the instant of entry.
    { start: Math.max(b.start, b.end - (6.2 + 2 * CONVOY_GAP) / b.speed), end: j.returnEnd + 700 },
  ];
}
export function nextFactorySlot(start: number, duration: number, windows: FactoryWindow[]) {
  let next = start;
  for (const w of [...windows].sort((a, b) => a.start - b.start))
    if (next < w.end && next + duration > w.start) next = w.end + 150;
  return Math.ceil(next);
}
