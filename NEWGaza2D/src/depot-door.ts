import Phaser from 'phaser';
import type { Plot } from './model';
import { U } from './art';

import { depotPortal, type DepotPoint } from './fleet-motion';
export { depotPortal, type DepotPoint } from './fleet-motion';

const OPEN_MS = 650, HALF_W = 24, DOOR_H = 35, JAMB = 5, LINTEL = 7, DEPTH = 15;
const iso = (p: DepotPoint): DepotPoint => [(p[0] - p[1]) * U, (p[0] + p[1]) * U / 2];

interface View { interior: Phaser.GameObjects.Graphics; frame: Phaser.GameObjects.Graphics; shutter: Phaser.GameObjects.Graphics;
  open: number; last: number; drawn: number; key: string }

export class DepotDoors {
  private views = new Map<number, View>();
  constructor(private scene: Phaser.Scene) {}

  update(plot: Plot, requestedOpen: boolean, now: number, openSince = now): void {
    const [tx, ty] = iso(depotPortal(plot).threshold);
    const key = `${plot.orientation}:${tx}:${ty}`;
    let v = this.views.get(plot.id);
    if (!v) {
      v = { interior: this.scene.add.graphics(), frame: this.scene.add.graphics(), shutter: this.scene.add.graphics(),
        open: requestedOpen ? Math.max(0, Math.min(1, (now - openSince) / OPEN_MS)) : 0,
        last: now, drawn: -1, key: '' };
      this.views.set(plot.id, v);
    }
    const dt = Math.max(0, Math.min(500, now - v.last)); v.last = now;
    const step = dt / OPEN_MS;
    v.open = requestedOpen ? Math.min(1, v.open + step) : Math.max(0, v.open - step);
    v.interior.setDepth(ty - .6); v.frame.setDepth(ty + 1); v.shutter.setDepth(ty + 1.01);
    if (v.key !== key) { v.key = key; v.drawn = -1; this.drawStatic(v, tx, ty); }
    if (Math.abs(v.open - v.drawn) > .002) { v.drawn = v.open; this.drawShutter(v, tx, ty); }
  }

  private drawStatic(v: View, tx: number, ty: number) {
    const L: DepotPoint = [tx - HALF_W, ty - HALF_W / 2], R: DepotPoint = [tx + HALF_W, ty + HALF_W / 2];
    const up = (p: DepotPoint, h: number): DepotPoint => [p[0], p[1] - h];
    const back: DepotPoint = [DEPTH, -DEPTH / 2];
    const poly = (g: Phaser.GameObjects.Graphics, pts: DepotPoint[]) => { g.beginPath(); g.moveTo(pts[0][0], pts[0][1]); for (const p of pts.slice(1)) g.lineTo(p[0], p[1]); g.closePath(); g.fillPath(); };
    const sh = (p: DepotPoint, d: DepotPoint): DepotPoint => [p[0] + d[0], p[1] + d[1]];
    const i = v.interior.clear();
    // dark cavity: back wall, floor receding inward, side wall
    i.fillStyle(0x222522, 1); poly(i, [L, R, up(R, DOOR_H), up(L, DOOR_H)]);
    i.fillStyle(0x131615, .94); poly(i, [L, R, sh(R, back), sh(L, back)]);
    i.fillStyle(0x383b38, .92); poly(i, [L, sh(L, back), up(sh(L, back), DOOR_H), up(L, DOOR_H)]);
    i.lineStyle(1, 0x7c7e76, .17);
    for (let h = 6; h < DOOR_H; h += 6) i.lineBetween(L[0] + 4, L[1] - h - 2, R[0] - 3, R[1] - h - 2);
    i.fillStyle(0x7e7a6e, .16); poly(i, [up(L, DOOR_H), up(R, DOOR_H), up(R, DOOR_H - 5), up(L, DOOR_H - 5)]);
    i.fillStyle(0x2a2a27, .35); poly(i, [sh(L, [4, -2]), sh(R, [-4, -2]), sh(R, [2, -1]), sh(L, [2, -1])]);
    // faint light spill on floor near threshold
    i.fillStyle(0x6b6558, .12); poly(i, [L, R, sh(R, [5, -2.5]), sh(L, [5, -2.5])]);
    const f = v.frame.clear();
    // stone sill + step
    f.fillStyle(0x5d5a52, 1); poly(f, [sh(L, [-JAMB, JAMB / 2]), sh(R, [JAMB, -JAMB / 2 + JAMB]), sh(R, [JAMB, 3]), sh(L, [-JAMB, 3])].map((p, k) => k === 1 ? [R[0] + JAMB, R[1] + 2.5] as DepotPoint : p));
    f.fillStyle(0x3e3c37, 1); poly(f, [sh(L, [-JAMB, 3]), [R[0] + JAMB, R[1] + 3], [R[0] + JAMB, R[1] + 5.5], sh(L, [-JAMB, 5.5])]);
    // jambs: steel posts with lit/shaded face
    f.fillStyle(0x6c6f70, 1); poly(f, [sh(L, [-JAMB, 0]), L, up(L, DOOR_H + LINTEL), up(sh(L, [-JAMB, 0]), DOOR_H + LINTEL)]);
    f.fillStyle(0x4a4d4e, 1); poly(f, [R, sh(R, [JAMB, 0]), up(sh(R, [JAMB, 0]), DOOR_H + LINTEL), up(R, DOOR_H + LINTEL)]);
    f.fillStyle(0x2c2e2f, .6); f.fillRect(L[0] - JAMB + 1, L[1] - DOOR_H - 2, 1.2, DOOR_H + 2);
    // lintel beam with rivets and rust streak
    f.fillStyle(0x56595a, 1); poly(f, [up(sh(L, [-JAMB, 0]), DOOR_H), up(sh(R, [JAMB, 0]), DOOR_H), up(sh(R, [JAMB, 0]), DOOR_H + LINTEL), up(sh(L, [-JAMB, 0]), DOOR_H + LINTEL)]);
    f.fillStyle(0x7d8081, .7); poly(f, [up(sh(L, [-JAMB, 0]), DOOR_H + LINTEL), up(sh(R, [JAMB, 0]), DOOR_H + LINTEL), up(sh(R, [JAMB, 0]), DOOR_H + LINTEL - 1.5), up(sh(L, [-JAMB, 0]), DOOR_H + LINTEL - 1.5)]);
    f.fillStyle(0x1f2021, .55); poly(f, [up(L, DOOR_H), up(R, DOOR_H), up(R, DOOR_H - 3), up(L, DOOR_H - 3)]);
    f.fillStyle(0x8a5a3a, .35); f.fillRect(tx - 14, ty - 7 - DOOR_H - 2, 3, 10);
    f.fillStyle(0x2b2c2d, .7);
    for (let k = 0; k < 6; k++) { const px = L[0] + 4 + k * 8.4; f.fillRect(px, L[1] + (px - L[0]) / 2 - DOOR_H - 4, 1.4, 1.4); }
  }

  private drawShutter(v: View, tx: number, ty: number) {
    const g = v.shutter.clear();
    const e = v.open * v.open * (3 - 2 * v.open); // eased
    const h = DOOR_H * (1 - e);
    if (h < .8) return;
    const slat = 3.5, n = Math.ceil(h / slat);
    for (let k = 0; k < n; k++) {
      const top = DOOR_H - k * slat, bot = Math.max(DOOR_H - h, top - slat);
      const shade = k % 2 ? 0x73777a : 0x666a6d;
      const tone = Phaser.Display.Color.IntegerToColor(shade).darken(Math.round((k / 10) * 4)).color;
      g.fillStyle(tone, 1);
      const a = (hh: number, s: number) => [tx + s * HALF_W, ty + s * HALF_W / 2 - hh] as DepotPoint;
      g.beginPath(); const p = [a(top, -1), a(top, 1), a(bot, 1), a(bot, -1)];
      g.moveTo(p[0][0], p[0][1]); for (const q of p.slice(1)) g.lineTo(q[0], q[1]); g.closePath(); g.fillPath();
      g.fillStyle(0x1c1d1e, .55); g.fillRect(tx - HALF_W, ty - HALF_W / 2 - bot - .2, 0, 0);
      g.lineStyle(1, 0x2a2c2e, .5); g.lineBetween(p[3][0], p[3][1], p[2][0], p[2][1]);
    }
    // worn patches and bottom bar with handle
    const bot = DOOR_H - h;
    g.fillStyle(0x8a5a3a, .22); g.fillRect(tx - 12, ty - 6 - Math.min(DOOR_H - 6, bot + 8), 7, 5);
    g.fillStyle(0x3a3d3f, 1);
    g.beginPath(); g.moveTo(tx - HALF_W, ty - HALF_W / 2 - bot); g.lineTo(tx + HALF_W, ty + HALF_W / 2 - bot);
    g.lineTo(tx + HALF_W, ty + HALF_W / 2 - bot + 2.5); g.lineTo(tx - HALF_W, ty - HALF_W / 2 - bot + 2.5); g.closePath(); g.fillPath();
    g.fillStyle(0xa0a3a2, .8); g.fillRect(tx - 1.5, ty - bot + 0.3, 3, 1.2);
  }

  retain(ids: Set<number>): void {
    for (const [id, v] of this.views) if (!ids.has(id)) { this.destroy(v); this.views.delete(id); }
  }
  reset(): void { for (const v of this.views.values()) this.destroy(v); this.views.clear(); }
  private destroy(v: View) { v.interior.destroy(); v.frame.destroy(); v.shutter.destroy(); }
}
