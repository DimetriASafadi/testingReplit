import Phaser from 'phaser';
import type { Plot } from './model';
import { U } from './art';
import { depotGeometry, type DepotPoint } from './depot-geometry';

export { depotPortal } from './fleet-motion';
export type { DepotPoint } from './depot-geometry';

const OPEN_MS = 650;
const iso = (p: DepotPoint): DepotPoint => [(p[0] - p[1]) * U, (p[0] + p[1]) * U / 2];
const factoryDepth = (p: Plot) => iso([p.x + 1, p.y + 1])[1] + 8;

interface View { cavity: Phaser.GameObjects.Graphics; frame: Phaser.GameObjects.Graphics; shutter: Phaser.GameObjects.Graphics;
  maskShape: Phaser.GameObjects.Graphics; mask: Phaser.Display.Masks.GeometryMask;
  open: number; last: number; drawn: number; key: string }

const poly = (g: Phaser.GameObjects.Graphics, pts: DepotPoint[]) => {
  g.beginPath(); g.moveTo(pts[0][0], pts[0][1]); for (let i = 1; i < pts.length; i++) g.lineTo(pts[i][0], pts[i][1]); g.closePath(); g.fillPath();
};

export class DepotDoors {
  private views = new Map<number, View>();
  constructor(private scene: Phaser.Scene) {}

  private view(plot: Plot, requestedOpen: boolean, now: number, openSince: number): View {
    let v = this.views.get(plot.id);
    if (!v) {
      const maskShape = this.scene.make.graphics({}, false);
      maskShape.setVisible(false);
      v = { cavity: this.scene.add.graphics(), frame: this.scene.add.graphics(), shutter: this.scene.add.graphics(),
        maskShape, mask: maskShape.createGeometryMask(),
        open: requestedOpen ? Math.max(0, Math.min(1, (now - openSince) / OPEN_MS)) : 0,
        last: now, drawn: -1, key: '' };
      this.views.set(plot.id, v);
    }
    return v;
  }

  private frameOf(plot: Plot) {
    const g = depotGeometry(plot);
    const [tx, ty] = iso(g.threshold);
    const o = iso(g.outward), ol = Math.hypot(o[0], o[1]);
    return { g, tx, ty, out: [o[0] / ol, o[1] / ol] as DepotPoint };
  }

  passage(plot: Plot): { mask: Phaser.Display.Masks.GeometryMask; depth: number } {
    if (!this.views.has(plot.id)) this.update(plot, false, 0);
    const v = this.views.get(plot.id)!;
    return { mask: v.mask, depth: factoryDepth(plot) + (depotGeometry(plot).visible ? .08 : -.08) };
  }

  private drawMask(v: View, plot: Plot) {
    const { g, tx, ty, out } = this.frameOf(plot);
    const hw = g.halfWidth, H = g.height, s = g.slope;
    const at = (d: number, lat: number): DepotPoint => [tx + out[0] * d + lat, ty + out[1] * d + s * lat];
    const m = v.maskShape.clear(); m.fillStyle(0xffffff, 1);
    // Extrude the apron UP by the vehicle clearance as well: a cab outside the doorway
    // must not lose its roof just because that roof projects above the ground polygon.
    const floor = [at(0, -hw), at(0, hw), at(50, hw + 18), at(220, hw + 90), at(220, -hw - 90), at(50, -hw - 18)];
    const points = [...floor, ...floor.map(([x,y],i): DepotPoint => [x,y-(i<2?H:96)])].sort((a,b)=>a[0]-b[0] || a[1]-b[1]);
    const cross = (a: DepotPoint,b: DepotPoint,c: DepotPoint) => (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);
    const half = (pts: DepotPoint[]) => {
      const chain: DepotPoint[] = [];
      for (const p of pts) { while(chain.length>1 && cross(chain.at(-2)!,chain.at(-1)!,p)<=0) chain.pop(); chain.push(p); }
      chain.pop(); return chain;
    };
    poly(m, [...half(points), ...half([...points].reverse())]);
  }

  update(plot: Plot, requestedOpen: boolean, now: number, openSince = now): void {
    const { g, tx, ty } = this.frameOf(plot);
    const key = `${plot.orientation}:${tx}:${ty}`;
    const v = this.view(plot, requestedOpen, now, openSince);
    const dt = Math.max(0, Math.min(500, now - v.last)); v.last = now;
    const step = dt / OPEN_MS;
    v.open = requestedOpen ? Math.min(1, v.open + step) : Math.max(0, v.open - step);
    // The authored door is already closed in the sprite. Draw an overlay only when
    // opening a visible near facade; never paint a doorway through a rear wall.
    const shown = g.visible && v.open > .001;
    v.cavity.setVisible(shown); v.shutter.setVisible(shown); v.frame.setVisible(shown);
    if (v.key !== key) {
      const d = factoryDepth(plot);
      v.cavity.setDepth(d + .04); v.shutter.setDepth(d + .15); v.frame.setDepth(d + .16);
      v.key = key; v.drawn = -1; this.drawStatic(v, plot);
      this.drawMask(v, plot);
    }
    if (Math.abs(v.open - v.drawn) > .002) { v.drawn = v.open; this.drawShutter(v, tx, ty, g.halfWidth, g.height, g.slope); }
  }

  private drawStatic(v: View, plot: Plot) {
    const { g, tx, ty, out } = this.frameOf(plot);
    const hw = g.halfWidth, H = g.height, s = g.slope;
    const P = (x: number, h = 0): DepotPoint => [tx + x, ty + s * x - h];
    const back = plot.orientation === 1;
    const c = v.cavity.clear();
    const dx = -out[0] * 7, dy = -out[1] * 7;
    const sh = (p: DepotPoint): DepotPoint => [p[0] + dx, p[1] + dy];
    c.fillStyle(0x15130f, .96); poly(c, [P(-hw), P(hw), P(hw, H), P(-hw, H)]);
    c.fillStyle(0x2a251d, .9); poly(c, [P(-hw), P(hw), sh(P(hw)), sh(P(-hw))]);
    c.fillStyle(0x0b0a08, .55); poly(c, [P(-hw, H), P(hw, H), P(hw, H - 9), P(-hw, H - 9)]);
    c.fillStyle(0x8a7a5a, .12); poly(c, [P(-hw), P(-hw + 6), P(-hw + 6, H), P(-hw, H)]);
    // dusty warm light spill on the floor
    c.fillStyle(0xb09b6a, .1); poly(c, [P(-hw + 2), P(hw - 2), P(hw - 5, 0), P(-hw + 5, 0)].map((p, i) => i < 2 ? p : [p[0] - out[0] * 5, p[1] - out[1] * 5] as DepotPoint));
    const f = v.frame.clear();
    const J = 3, T = 5;
    const lit = back ? 0x77796f : 0x6e6f66, dark = back ? 0x4a4c47 : 0x45463f;
    // jambs
    f.fillStyle(lit, 1); poly(f, [P(-hw - J), P(-hw), P(-hw, H + T), P(-hw - J, H + T)]);
    f.fillStyle(dark, 1); poly(f, [P(hw), P(hw + J), P(hw + J, H + T), P(hw, H + T)]);
    f.fillStyle(0x2b2a26, .5); poly(f, [P(-hw - J), P(-hw - J + .8), P(-hw - J + .8, H + T), P(-hw - J, H + T)]);
    // lintel tucked under roofline
    f.fillStyle(0x56574f, 1); poly(f, [P(-hw - J, H), P(hw + J, H), P(hw + J, H + T), P(-hw - J, H + T)]);
    f.fillStyle(0x8b8a7c, .55); poly(f, [P(-hw - J, H + T), P(hw + J, H + T), P(hw + J, H + T - 1.2), P(-hw - J, H + T - 1.2)]);
    f.fillStyle(0x1d1c19, .5); poly(f, [P(-hw, H), P(hw, H), P(hw, H - 2), P(-hw, H - 2)]);
    // rust streaks and rivets
    f.fillStyle(0x7a4e32, .3); poly(f, [P(-6, H), P(-3, H), P(-3, H - 12), P(-6, H - 9)]);
    f.fillStyle(0x7a4e32, .2); poly(f, [P(hw - 4, H), P(hw - 2, H), P(hw - 2, H - 7), P(hw - 4, H - 6)]);
    f.fillStyle(0x24231f, .7);
    for (let x = -hw; x <= hw; x += 7) { const p = P(x, H + 2.4); f.fillRect(p[0], p[1], 1.2, 1.2); }
    // worn concrete sill
    f.fillStyle(0x6a665b, 1); poly(f, [P(-hw - J, 0), P(hw + J, 0), [P(hw + J)[0] + out[0] * 3, P(hw + J)[1] + out[1] * 3], [P(-hw - J)[0] + out[0] * 3, P(-hw - J)[1] + out[1] * 3]]);
  }

  private drawShutter(v: View, tx: number, ty: number, hw: number, H: number, s: number) {
    const g = v.shutter.clear();
    const e = v.open * v.open * (3 - 2 * v.open);
    const h = H * (1 - e);
    if (h < .8) return;
    const P = (x: number, hh: number): DepotPoint => [tx + x, ty + s * x - hh];
    const slat = 3.2, n = Math.ceil(h / slat);
    for (let k = 0; k < n; k++) {
      const top = H - k * slat, bot = Math.max(H - h, top - slat);
      g.fillStyle(Phaser.Display.Color.IntegerToColor(k % 2 ? 0x686a64 : 0x5c5e59).darken(Math.min(10, k)).color, 1);
      poly(g, [P(-hw, top), P(hw, top), P(hw, bot), P(-hw, bot)]);
      g.lineStyle(1, 0x25241f, .5); g.lineBetween(P(-hw, bot)[0], P(-hw, bot)[1], P(hw, bot)[0], P(hw, bot)[1]);
    }
    const bot = H - h;
    g.fillStyle(0x7a4e32, .2); poly(g, [P(-9, Math.min(H - 3, bot + 9)), P(-3, Math.min(H - 3, bot + 9)), P(-3, Math.min(H - 3, bot + 3)), P(-9, Math.min(H - 3, bot + 3))]);
    g.fillStyle(0x34352f, 1); poly(g, [P(-hw, bot + 2.4), P(hw, bot + 2.4), P(hw, bot), P(-hw, bot)]);
    g.fillStyle(0x9a9a8c, .7); const hp = P(-1.5, bot + 1.8); g.fillRect(hp[0], hp[1], 3, 1);
  }

  retain(ids: Set<number>): void {
    for (const [id, v] of this.views) if (!ids.has(id)) { this.destroy(v); this.views.delete(id); }
  }
  reset(): void { for (const v of this.views.values()) this.destroy(v); this.views.clear(); }
  private destroy(v: View) {
    v.cavity.destroy(); v.frame.destroy(); v.shutter.destroy();
    v.mask.destroy(); v.maskShape.destroy();
  }
}
