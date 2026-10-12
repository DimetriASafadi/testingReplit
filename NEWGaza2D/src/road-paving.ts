/** Pure geometry for the finished asphalt coat (no Phaser / canvas).
 *  Rules:
 *  - Contiguous paved segments on one street line merge into a single run: no seam, border or cap at shared joints.
 *  - A junction is paved only when BOTH crossing streets have a paved arm touching it; then the runs reach the
 *    node centre and a square patch + curved curb returns (fillets, only between two paved arms) fill the cross.
 *  - At an unpaved junction a run stops at the near edge of the cross street ('open' butt end, dusty feather).
 *  - At a mid-street joint next to an unpaved segment, the run ends with a short tapered dusty cap.
 *  - Centre dashes use a world-aligned phase and keep out of every junction (half-width + clearance). */
import type { RoadSegment } from './roads';

export const ROAD_HALF = .30;
export const FILLET_R = .2;
export const CAP_LEN = .16;
export const DASH_PERIOD = .55, DASH_LEN = .24, DASH_CLEAR = .18;
export type EndKind = 'node' | 'open' | 'joint';
export interface PavedRun { axis: 'x' | 'y'; fixed: number; s0: number; s1: number; start: EndKind; end: EndKind;
  /** painted asphalt extent along the axis (excluding taper caps) */
  p0: number; p1: number; edges: [number, number][]; dashes: [number, number][] }
export interface PavedNode { x: number; y: number; fillets: [number, number][] }
export interface PavingPlan { runs: PavedRun[]; nodes: PavedNode[] }

const EPS = 1e-6;
const near = (a: number, b: number) => Math.abs(a - b) < EPS;
const axisOf = (r: RoadSegment): 'x' | 'y' => near(r.a[0], r.b[0]) ? 'x' : 'y';

export function pavingPlan(roadX: number[], roadY: number[], segments: RoadSegment[], paved: Set<string>): PavingPlan {
  const segs = segments.filter(s => paved.has(s.id)).map(r => {
    const axis = axisOf(r), fixed = axis === 'x' ? r.a[0] : r.a[1];
    const s0 = axis === 'x' ? Math.min(r.a[1], r.b[1]) : Math.min(r.a[0], r.b[0]);
    const s1 = axis === 'x' ? Math.max(r.a[1], r.b[1]) : Math.max(r.a[0], r.b[0]);
    return { axis, fixed, s0, s1 };
  });
  // arm(axis,fixed,s,dir): paved segment on that line touching s going dir
  const arm = (axis: 'x' | 'y', fixed: number, s: number, dir: 1 | -1) =>
    segs.some(g => g.axis === axis && near(g.fixed, fixed) && (dir > 0 ? near(g.s0, s) : near(g.s1, s)));
  const nodePaved = (x: number, y: number) => (arm('x', x, y, 1) || arm('x', x, y, -1)) && (arm('y', y, x, 1) || arm('y', y, x, -1));
  const crossOf = (axis: 'x' | 'y') => axis === 'x' ? roadY : roadX;
  const isNode = (axis: 'x' | 'y', s: number) => crossOf(axis).some(c => near(c, s));
  const nodeXY = (axis: 'x' | 'y', fixed: number, s: number): [number, number] => axis === 'x' ? [fixed, s] : [s, fixed];

  const nodes: PavedNode[] = [];
  for (const x of roadX) for (const y of roadY) if (nodePaved(x, y)) {
    const fillets: [number, number][] = [];
    for (const sx of [-1, 1] as const) for (const sy of [-1, 1] as const)
      if (arm('x', x, y, sy) && arm('y', y, x, sx)) fillets.push([sx, sy]);
    nodes.push({ x, y, fillets });
  }

  const runs: PavedRun[] = [];
  const lines = new Map<string, typeof segs>();
  for (const g of segs) { const k = g.axis + ':' + g.fixed; (lines.get(k) ?? lines.set(k, []).get(k)!).push(g); }
  for (const list of lines.values()) {
    list.sort((a, b) => a.s0 - b.s0);
    const { axis, fixed } = list[0];
    const kind = (s: number): EndKind => !isNode(axis, s) ? 'joint' : nodePaved(...nodeXY(axis, fixed, s)) || (arm(axis,fixed,s,1)&&arm(axis,fixed,s,-1)) ? 'node' : 'open';
    let cur: { s0: number; s1: number } | null = null;
    const close = () => { if (cur) runs.push(finish(axis, fixed, cur.s0, cur.s1, kind(cur.s0), kind(cur.s1), crossOf(axis))); cur = null; };
    for (const g of list) {
      if (cur && near(cur.s1, g.s0) && kind(g.s0) !== 'open') cur.s1 = g.s1;
      else { close(); cur = { s0: g.s0, s1: g.s1 }; }
    }
    close();
  }
  return { runs, nodes };
}

function finish(axis: 'x' | 'y', fixed: number, s0: number, s1: number, start: EndKind, end: EndKind, cross: number[]): PavedRun {
  const p0 = start === 'open' ? s0 + ROAD_HALF : s0, p1 = end === 'open' ? s1 - ROAD_HALF : s1;
  const edges = edgeIntervals(p0, p1, cross);
  const keep = ROAD_HALF + DASH_CLEAR, lo = p0 + .05, hi = p1 - .05, dashes: [number, number][] = [];
  for (let k = Math.floor(lo / DASH_PERIOD) - 1; k * DASH_PERIOD < hi; k++) {
    let a = Math.max(lo, k * DASH_PERIOD), b = Math.min(hi, k * DASH_PERIOD + DASH_LEN);
    for (const c of cross) { if (a < c + keep && b > c - keep) { if (a >= c - keep) a = Math.max(a, c + keep); else b = Math.min(b, c - keep); } }
    if (b - a > .06) dashes.push([a, b]);
  }
  return { axis, fixed, s0, s1, start, end, p0, p1, edges, dashes };
}

/** Logical-space outline of a run including tapered caps on 'joint' ends. */
export function runOutline(r: PavedRun): [number, number][] {
  const h = ROAD_HALF, t = h * .55;
  const pts: [number, number][] = [];
  const P = (s: number, o: number): [number, number] => r.axis === 'x' ? [r.fixed + o, s] : [s, r.fixed + o];
  pts.push(P(r.p0, -h));
  pts.push(P(r.p1, -h));
  if (r.end === 'joint') { pts.push(P(r.p1 + CAP_LEN * .6, -h * .8), P(r.p1 + CAP_LEN, -t * .5), P(r.p1 + CAP_LEN, t * .5), P(r.p1 + CAP_LEN * .6, h * .8)); }
  pts.push(P(r.p1, h));
  pts.push(P(r.p0, h));
  if (r.start === 'joint') { pts.push(P(r.p0 - CAP_LEN * .6, h * .8), P(r.p0 - CAP_LEN, t * .5), P(r.p0 - CAP_LEN, -t * .5), P(r.p0 - CAP_LEN * .6, -h * .8)); }
  return pts;
}

/** Junction patch: square of the road width plus one fillet polygon per paved quadrant. */
export function nodeOutlines(n: PavedNode, steps = 6): [number, number][][] {
  const h = ROAD_HALF, R = FILLET_R;
  const out: [number, number][][] = [[[n.x - h, n.y - h], [n.x + h, n.y - h], [n.x + h, n.y + h], [n.x - h, n.y + h]]];
  for (const [sx, sy] of n.fillets) {
    const cx = n.x + sx * h, cy = n.y + sy * h, ox = cx + sx * R, oy = cy + sy * R;
    const poly: [number, number][] = [[cx, cy]];
    for (let i = 0; i <= steps; i++) { const a = (i / steps) * Math.PI / 2; poly.push([ox - sx * R * Math.sin(a), oy - sy * R * Math.cos(a)]); }
    out.push(poly);
  }
  return out;
}

/** Edge-line spans along [p0,p1] with a keep-out of ROAD_HALF+FILLET_R around EVERY crossing (incl. internal ones of merged runs). */
export function edgeIntervals(p0: number, p1: number, cross: number[], keep = ROAD_HALF + FILLET_R): [number, number][] {
  let spans: [number, number][] = [[p0, p1]];
  for (const c of cross) spans = spans.flatMap(([a, b]): [number, number][] => b <= c - keep || a >= c + keep ? [[a, b]] : [[a, Math.min(b, c - keep)], [Math.max(a, c + keep), b]]);
  return spans.filter(([a, b]) => b - a > .05);
}
