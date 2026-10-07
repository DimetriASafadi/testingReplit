import type { EquipmentKind, Job, Plot } from './model';

export type DepotPoint = [number, number];
export const CONVOY_GAP = 1.7;
const OPEN_WAIT = 750, TURN_WAIT = 550, INSIDE_FINISH = 250;
const ORDER: EquipmentKind[] = ['excavator', 'bulldozer', 'truck'];
const clamp = (n: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, n));
export function depotPortal(p: Plot) {
  const back = p.orientation === 1;
  return { inside: [p.x + 1, p.y + (back ? 1.2 : .8)] as DepotPoint,
    threshold: [p.x + 1, p.y + (back ? .4 : 1.6)] as DepotPoint,
    outside: [p.x + 1, p.y + (back ? 0 : 2)] as DepotPoint };
}
export const workDock = (p: Plot, kind: EquipmentKind): DepotPoint => kind === 'excavator'
  ? [p.x + 1.38, p.y + 1.92]
  : kind === 'truck' ? [p.x + 2.095, p.y + 1.92] : [p.x + .72, p.y + 1.92];
export function fleetRoute(source: Plot, target: Plot, kind: EquipmentKind, returning = false) {
  const p = depotPortal(source), lane = returning ? -.14 : .14;
  const points: DepotPoint[] = [p.inside, p.threshold, p.outside,
    [p.outside[0], p.outside[1] + lane], [source.x + 4 + lane, p.outside[1] + lane],
    [source.x + 4 + lane, target.y + 2 + lane], [target.x + 3, target.y + 2 + lane], workDock(target, kind)];
  const clean = points.filter((p, i) => !i || Math.hypot(p[0] - points[i - 1][0], p[1] - points[i - 1][1]) > .00001);
  return returning ? clean.reverse() : clean;
}
export const routeLength = (points: DepotPoint[]) => points.slice(1)
  .reduce((n, p, i) => n + Math.hypot(p[0] - points[i][0], p[1] - points[i][1]), 0);
export function routePoint(points: DepotPoint[], distance: number): DepotPoint {
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
}
/** Arc-distance spacing: same speed on shared roads, not three independent 0..1 lerps. */
export function convoyMotion(j: Job, source: Plot, target: Plot, kind: EquipmentKind, now: number): ConvoyMotion {
  const slot = ORDER.indexOf(kind);
  if (now >= j.arrival && now < j.workEnd) {
    const position = workDock(target, kind);
    return { position, ahead: position, visibility: 1, moving: false, stage: 'work', reverse: false };
  }
  const returning = now >= j.workEnd, t = timing(j, source, target, returning);
  const points = fleetRoute(source, target, kind, returning), length = routeLength(points);
  const remaining = Math.max(0, t.speed * (t.end - now) - (2 - slot) * CONVOY_GAP);
  const raw = returning ? length - remaining : t.speed * (now - t.start - t.wait) - slot * CONVOY_GAP;
  const distance = clamp(raw, 0, length), position = routePoint(points, distance), ahead = routePoint(points, distance + .04);
  const visibility = returning ? clamp((remaining - .28) / .55, 0, 1) : clamp((raw - .4) / .55, 0, 1);
  const finalLeg = Math.hypot(position[0] - target.x - 3, position[1] - target.y - 2 - .14)
    <= Math.hypot(workDock(target, kind)[0] - target.x - 3, workDock(target, kind)[1] - target.y - 2 - .14) + .001;
  return { position, ahead, visibility, moving: raw > 0 && raw < length,
    stage: returning ? (visibility ? 'return' : 'inside') : (visibility ? 'out' : 'waiting'),
    reverse: !returning && kind === 'truck' && finalLeg && ahead[0] < position[0] };
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
