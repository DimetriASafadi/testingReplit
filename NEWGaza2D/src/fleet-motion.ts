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
export function rounded(points: DepotPoint[]) {
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
  const alongY = !!p.outward[1], sign = alongY ? p.outward[1] : p.outward[0];
  const joins: DepotPoint[] = alongY
    ? (Math.abs(street[1] - approach[1]) < .00001 ? [] :
      [[target.x + 2 + lane, street[1]], [target.x + 2 + lane, approach[1]]])
    : [[street[0], approach[1]]];
  const sb = plotBlock(source), tb = plotBlock(target);
  let points: DepotPoint[] = [p.inside, p.threshold, street, ...joins, approach, workDock(target, kind)];
  if (sb && tb) {
    const layout = cityLayout(source.layoutId!);
    const boundary = alongY ? sign > 0 ? sb.y1 : sb.y0 : sign > 0 ? sb.x1 : sb.x0;
    const road = (alongY ? layout.roadY : layout.roadX)
      .filter(r => sign * (r - boundary) >= -1e-8)
      .sort((a, b) => Math.abs(a - boundary) - Math.abs(b - boundary))[0];
    if (road === undefined) throw new Error('Factory has no street on its exit side');
    const directExit = alongY ? (sign > 0 ? source.y + 2 : source.y) === boundary
      : (sign > 0 ? source.x + 2 : source.x) === boundary;
    const corner: DepotPoint = sign > 0 ? [source.x + 2, source.y + 2] : [source.x, source.y];
    const exit: DepotPoint = alongY ? [directExit ? p.outside[0] : corner[0], road + sign * lane]
      : [road + sign * lane, directExit ? p.outside[1] : corner[1]];
    const directEntry = target.y + 2 === tb.y1;
    const dock = workDock(target, kind), entry: DepotPoint = [directEntry ? dock[0] : target.x + 2, tb.roadY + lane];
    const bends: DepotPoint[] = [];
    if (!alongY) bends.push([exit[0], entry[1]]);
    else if (Math.abs(exit[1] - entry[1]) > .00001) {
      const roads = cityLayout(source.layoutId!).roadX;
      const score = (x: number) => Math.abs(exit[0] - x) + Math.abs(entry[0] - x);
      const cross = roads.reduce((best, x) => score(x) < score(best) ? x : best) + lane;
      bends.push([cross, exit[1]], [cross, entry[1]]);
    }
    points = [p.inside, p.threshold, p.outside,
      ...(!directExit ? [corner] : []),
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
export function routeLength(points: readonly DepotPoint[]) {
  const saved = lengths.get(points); if (saved !== undefined) return saved;
  const length = points.slice(1).reduce((n,p,i) => n + Math.hypot(p[0] - points[i][0], p[1] - points[i][1]), 0);
  lengths.set(points, length); return length;
}
export function routePoint(points: readonly DepotPoint[], distance: number): DepotPoint {
  let d = Math.max(0, distance);
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1], b = points[i], l = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (d <= l) { const f = l ? d / l : 0; return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f]; }
    d -= l;
  }
  return [...points[points.length - 1]];
}
/** Match return ground speed to departure, including lane length and convoy spacing.
 * Door/turn waits are not driving time; the final 250 ms remains inside the depot. */
export function returnTravelDuration(source: Plot, target: Plot, outgoingDuration: number) {
  const distance = (returning: boolean) => Math.max(...ORDER.map(k =>
    routeLength(fleetRoute(source, target, k, returning)))) + 2 * CONVOY_GAP;
  return Math.ceil(Math.max(1, outgoingDuration - OPEN_WAIT) *
    distance(true) / distance(false)) + TURN_WAIT + INSIDE_FINISH;
}
/** Ground units per second, independent of the work-countdown testing override.
 * Include the last vehicle's spacing, not just the straight-line plot distance. */
export const CONVOY_SPEED = 1.5;
export function outgoingTravelDuration(source: Plot, target: Plot, level = 1) {
  const distance = Math.max(...ORDER.map(k => routeLength(fleetRoute(source, target, k)))) + 2 * CONVOY_GAP;
  const speed = CONVOY_SPEED * (1 + Math.max(0, level - 1) * .35);
  return OPEN_WAIT + Math.ceil(distance / speed * 1000);
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
  stage: 'waiting' | 'out' | 'work' | 'return' | 'inside' | 'unload'; reverse: boolean;
  portal: boolean; portalPlotId?: number;
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
export function gateDemand(j: Job, source: Plot, target: Plot, now: number): { open: boolean; since: number } {
  if (j.toolsReturnEnd !== undefined) {
    const demand = gateDemand({ ...j, toolsReturnEnd: undefined, returnEnd: j.toolsReturnEnd }, source, target, now);
    const openAt = Math.max(j.truckHomeDepartureAt!, j.returnEnd - (3.2 / j.travelSpeed! + 250));
    if (now >= openAt && now < j.returnEnd) return { open: true, since: openAt };
    return demand;
  }
  const out = timing(j, source, target, false), back = timing(j, source, target, true);
  const outClose = out.start + out.wait + (2 * CONVOY_GAP + 2.5) / out.speed;
  const backOpen = Math.max(back.start, back.end - (3.2 + 2 * CONVOY_GAP) / back.speed);
  if (now >= out.start && now < Math.min(j.arrival, outClose)) return { open: true, since: out.start };
  if (now >= backOpen && now < j.returnEnd) return { open: true, since: backOpen };
  return { open: false, since: now };
}
export interface FactoryWindow { start: number; end: number }
export function factoryWindows(j: Job, source: Plot, target: Plot): FactoryWindow[] {
  if (j.toolsReturnEnd !== undefined) {
    return [...factoryWindows({ ...j, toolsReturnEnd: undefined, returnEnd: j.toolsReturnEnd }, source, target),
      ...(j.truckHomeDepartureAt !== undefined ? [{ start: Math.max(j.truckHomeDepartureAt, j.returnEnd - (6.2 / j.travelSpeed! + 950)), end: j.returnEnd + 700 }] : [])];
  }
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
