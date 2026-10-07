// Isometric ground painter: top-down photo textures are affine-projected (2:1) onto a half-resolution canvas.
import { U } from './art';

export const GK = 0.5; // canvas px per world px
export interface GroundFrame { ox: number; oy: number; w: number; h: number }
type Pt = [number, number];
const wx = (lx: number, ly: number) => (lx - ly) * U, wy = (lx: number, ly: number) => (lx + ly) * U / 2;

export function frameFor(lx0: number, ly0: number, lx1: number, ly1: number): GroundFrame {
  const ox = wx(lx0, ly1), oy = wy(lx0, ly0); return { ox, oy, w: Math.ceil((wx(lx1, ly0) - ox) * GK), h: Math.ceil((wy(lx1, ly1) - oy) * GK) };
}
export class Painter {
  ctx: CanvasRenderingContext2D; private pats = new Map<string, CanvasPattern>();
  constructor(public cv: HTMLCanvasElement, public f: GroundFrame, private imgs: Record<string, CanvasImageSource | undefined>) { this.ctx = cv.getContext('2d')!; }
  pt(lx: number, ly: number): Pt { return [(wx(lx, ly) - this.f.ox) * GK, (wy(lx, ly) - this.f.oy) * GK]; }
  path(poly: Pt[]) { const c = this.ctx; c.beginPath(); poly.forEach(([x, y], i) => { const [a, b] = this.pt(x, y); i ? c.lineTo(a, b) : c.moveTo(a, b); }); c.closePath(); }
  /** fill a logical-space polygon with a ground texture covering T logical units per tile; (sx,sy) offset the tile phase */
  fill(poly: Pt[], tex: string, T: number, alpha = 1, sx = 0, sy = 0, tint?: string) {
    const img = this.imgs[tex]; const c = this.ctx; if (!img) { c.save(); this.path(poly); c.fillStyle = tint ?? '#a89878'; c.globalAlpha = alpha; c.fill(); c.restore(); return; }
    let pat = this.pats.get(tex); if (!pat) { pat = c.createPattern(img, 'repeat')!; this.pats.set(tex, pat); }
    c.save(); this.path(poly); c.clip(); c.globalAlpha = alpha;
    const sc = (T * U / 1024) * GK; const [ex, ey] = this.pt(sx, sy);
    c.setTransform(sc, sc / 2, -sc, sc / 2, ex, ey); c.fillStyle = pat; c.fillRect(-9000, -9000, 18000, 18000);
    c.restore();
    if (tint) { c.save(); this.path(poly); c.fillStyle = tint; c.fill(); c.restore(); }
  }
  shade(poly: Pt[], col: string) { const c = this.ctx; c.save(); this.path(poly); c.fillStyle = col; c.fill(); c.restore(); }
  stroke(a: Pt, b: Pt, col: string, wpx: number) { const c = this.ctx; const [x0, y0] = this.pt(...a), [x1, y1] = this.pt(...b); c.save(); c.strokeStyle = col; c.lineWidth = wpx; c.lineCap = 'round'; c.beginPath(); c.moveTo(x0, y0); c.lineTo(x1, y1); c.stroke(); c.restore(); }
}
export const rect = (x0: number, y0: number, x1: number, y1: number): Pt[] => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
export function blob(cx: number, cy: number, rx: number, ry: number, r: () => number, n = 9): Pt[] {
  const pts: Pt[] = []; for (let i = 0; i < n; i++) { const a = (i / n) * Math.PI * 2; const k = 0.7 + r() * 0.5; pts.push([cx + Math.cos(a) * rx * k, cy + Math.sin(a) * ry * k]); } return pts;
}
/** rounded-corner logical polygon (curbs, sidewalks, lot bases) */
export function roundRect(x0: number, y0: number, x1: number, y1: number, r: number, n = 5): Pt[] {
  const pts: Pt[] = []; const cs: [number, number, number][] = [[x1 - r, y0 + r, -90], [x1 - r, y1 - r, 0], [x0 + r, y1 - r, 90], [x0 + r, y0 + r, 180]];
  for (const [cx, cy, a0] of cs) for (let i = 0; i <= n; i++) { const a = (a0 + (90 * i) / n) * Math.PI / 180; pts.push([cx + Math.cos(a) * r, cy + Math.sin(a) * r]); }
  return pts;
}
/** deterministic seeded rng, stable per district/plot */
export function seeded(n: number) { let s = (n | 0) % 233280 || 1; return () => { s = (s * 9301 + 49297) % 233280; return s / 233280; }; }
