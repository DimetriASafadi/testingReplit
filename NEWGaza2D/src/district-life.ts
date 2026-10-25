// District life layer: factory staff stepping out of their workplace, displaced families
// around the peripheral tent camp who move into finished housing after 40% progress,
// progressive street lighting tied to the power project, the Palestinian flag pole,
// and per-district fringe dressing (Rashid coast road, boats, greenhouses, low-rise).
// Every collection is bounded; nothing grows with time or save size.
import Phaser from 'phaser';
import { U } from './art';
import { cityLayout } from './city-layout';
import { districtEnvironment } from './district-environment';
import { districtProgress } from './engine';
import type { DistrictState, Plot } from './model';
import { rotateLotPoint } from './orientation';
import { pavingRoute, pathLength, type RoadSegment } from './roads';
import { WK_FOOT, WK_FRAMES, WK_H, WK_W, drawWorker } from './workers';

type P2 = [number, number];
const iso = (lx: number, ly: number) => ({ x: (lx - ly) * U, y: (lx + ly) * U / 2 });
const MAX_STAFF = 8, MAX_RESIDENTS = 16, MAX_LAMPS = 44, MAX_BOATS = 4;
const STAFF_IDS = new Set(['work', 'equipment_store', 'recycling', 'glass', 'cement', 'steel', 'asphalt', 'food_factory', 'water_treatment', 'wood_factory', 'household_shop']);
const HOUSING_IDS = new Set(['small_house', 'medium_house', 'villa', 'housing_4', 'housing_6', 'housing_complex', 'modern_housing', 'traditional_housing', 'housing_tower', 'tourist_villa']);
const PERSON_SCALE = .3;
const hash = (n: number) => { const x = Math.sin(n * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };

/** Shared exterior tent placement (lot coordinates). RoadAnimation should use this so people and tents agree. */
export function tentSpot(i: number, max: number, count = 28): P2 {
  const t = (i + 1) / (count + 1);
  return i % 2 ? [-3.5 - (i % 3) * 1.6, t * max] : [t * max, -4 - (i % 3) * 1.4];
}
/** Front entrance of a plot's building, in lot coordinates, honoring orientation. */
export function plotEntrance(p: Plot, out = 0): P2 {
  const [ex, ey] = rotateLotPoint([.55 + out, .55 + out], p.orientation);
  return [p.x + 1 + ex, p.y + 1 + ey];
}

interface Walker { img: Phaser.GameObjects.Image; outfit: 'worker' | 'civilian'; v: number }
interface Resident extends Walker { tent: P2; threshold: number; moveStart: number; route: P2[] | null; len: number; home: string; done: boolean; last: P2 }
const lerp = (a: P2, b: P2, t: number): P2 => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
function along(path: P2[], t: number): { p: P2; dir: number } {
  const segs = path.length - 1, f = Phaser.Math.Clamp(t, 0, .9999) * segs, i = Math.floor(f);
  const a = path[i], b = path[i + 1];
  const sa = iso(a[0], a[1]), sb = iso(b[0], b[1]);
  return { p: lerp(a, b, f - i), dir: sb.x >= sa.x ? 1 : -1 };
}

function alongLen(path: P2[], t: number): { p: P2; dir: number } {
  let left = Phaser.Math.Clamp(t, 0, 1) * pathLength(path);
  for (let i = 1; i < path.length; i++) {
    const a = path[i - 1], b = path[i], len = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (left <= len || i === path.length - 1) { const sa = iso(a[0], a[1]), sb = iso(b[0], b[1]); return { p: lerp(a, b, len ? Math.min(1, left / len) : 1), dir: sb.x >= sa.x ? 1 : -1 }; }
    left -= len;
  }
  return { p: path[0], dir: 1 };
}

function segmentAt(path: P2[], t: number) {
  let left = t * pathLength(path);
  for (let i = 1; i < path.length; i++) { const len = Math.hypot(path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1]); if (left <= len) return i - 1; left -= len; }
  return path.length - 2;
}

export class DistrictLife {
  private did = '';
  private staff: (Walker & { plotId: number; offset: number })[] = [];
  private residents: Resident[] = [];
  private lamps: Phaser.GameObjects.Image[] = [];
  private lampOrder: number[] = [];
  private lampLit = -1;
  private fringe?: Phaser.GameObjects.Graphics;
  private boats: Phaser.GameObjects.Image[] = [];
  private flag?: Phaser.GameObjects.Graphics;
  private flagAt: P2 = [0, 0];
  private staffSig = '';
  private progressAt = 0; private progress = 0;
  private max = 16;

  constructor(private scene: Phaser.Scene) {}

  private tex(key: string, w: number, h: number, draw: (c: CanvasRenderingContext2D) => void) {
    const t = this.scene.textures; if (!t.exists(key)) { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d')!); t.addCanvas(key, cv); } return key;
  }
  private personTex(outfit: 'worker' | 'civilian', v: number, f: number) {
    return this.tex(`life:${outfit}:${v}:${f}`, WK_W, WK_H, c => drawWorker(c, 'walk', f, v, outfit));
  }
  private person(outfit: 'worker' | 'civilian', v: number): Walker {
    return { outfit, v, img: this.scene.add.image(0, 0, this.personTex(outfit, v, 0)).setOrigin(.5, WK_FOOT / WK_H).setScale(PERSON_SCALE).setVisible(false) };
  }
  private pose(w: Walker, at: P2, dir: number, moving: boolean, now: number, alpha = 1) {
    const s = iso(at[0], at[1]); const frame = moving ? Math.floor(now / 120 + w.v * 3) % WK_FRAMES : 0;
    w.img.setTexture(this.personTex(w.outfit, w.v, frame)).setPosition(s.x, s.y).setDepth(s.y + .5).setFlipX(dir < 0).setAlpha(alpha).setVisible(alpha > .02);
  }

  reset() {
    for (const w of [...this.staff, ...this.residents]) w.img.destroy();
    for (const l of this.lamps) l.destroy();
    for (const b of this.boats) b.destroy();
    this.fringe?.destroy(); this.flag?.destroy();
    this.staff = []; this.residents = []; this.lamps = []; this.lampOrder = []; this.boats = [];
    this.fringe = undefined; this.flag = undefined; this.did = ''; this.staffSig = ''; this.lampLit = -1; this.progressAt = 0;
  }

  private build(d: DistrictState) {
    this.reset(); this.did = d.id;
    const layout = cityLayout(d.id); this.max = layout.extent; const max = this.max;
    const env = districtEnvironment(d.id);
    // Residents around the tent camp (mirrors RoadAnimation tent placement).
    for (let i = 0; i < MAX_RESIDENTS; i++) {
      const tent = tentSpot(Math.floor(i * 28 / MAX_RESIDENTS), max, 28);
      const r: Resident = { ...this.person('civilian', i % 5), tent, threshold: 40 + i * 58 / (MAX_RESIDENTS - 1), moveStart: 0, route: null, len: 0, home: '', done: false, last: tent };
      this.residents.push(r);
    }
    // Streetlamps: kerb-side along each road, alternating sides, bounded.
    const spots: P2[] = [];
    const step = Math.max(2.4, (layout.roadX.length + layout.roadY.length) * max / MAX_LAMPS);
    let k = 0;
    for (const c of layout.roadX) for (let s = 1; s < max; s += step) spots.push([c + (k++ % 2 ? .48 : -.48), s]);
    for (const c of layout.roadY) for (let s = 1 + step / 2; s < max; s += step) spots.push([s, c + (k++ % 2 ? .48 : -.48)]);
    const lampKey = this.tex('life:lamp', 20, 64, x => {
      x.fillStyle = 'rgba(30,22,14,.3)'; x.beginPath(); x.ellipse(10, 61, 6, 2, 0, 0, Math.PI * 2); x.fill();
      x.fillStyle = '#4b5056'; x.fillRect(8.6, 10, 2.8, 51); x.fillStyle = '#7d848b'; x.fillRect(9, 10, 1, 51);
      x.fillStyle = '#3c4146'; x.fillRect(7, 56, 6, 5);
      x.strokeStyle = '#4b5056'; x.lineWidth = 2.2; x.beginPath(); x.moveTo(10, 12); x.quadraticCurveTo(10, 5, 16, 6); x.stroke();
      x.fillStyle = '#2f3337'; x.beginPath(); x.ellipse(16, 7, 4, 2, 0, 0, Math.PI * 2); x.fill();
      x.fillStyle = '#f6e3a2'; x.beginPath(); x.ellipse(16, 8.4, 2.8, 1.1, 0, 0, Math.PI * 2); x.fill();
    });
    for (const [lx, ly] of spots.slice(0, MAX_LAMPS)) {
      const s = iso(lx, ly);
      this.lamps.push(this.scene.add.image(s.x, s.y, lampKey).setOrigin(.5, 61 / 64).setScale(.62).setDepth(s.y).setVisible(false));
    }
    this.lampOrder = this.lamps.map((_, i) => i).sort((a, b) => hash(a + env.seed) - hash(b + env.seed));
    // Flag at the district's outer corner, clear of the tent rows and the coast road.
    this.flagAt = [max + 1.2, -1.4];
    this.flag = this.scene.add.graphics().setDepth(iso(this.flagAt[0], this.flagAt[1]).y).setVisible(false);
    this.paintFringe(env, max);
  }

  private paintFringe(env: ReturnType<typeof districtEnvironment>, max: number) {
    const g = this.fringe = this.scene.add.graphics().setDepth(-7.9e5);
    const quad = (pts: P2[], col: number, a = 1) => g.fillStyle(col, a).fillPoints(pts.map(([x, y]) => { const s = iso(x, y); return new Phaser.Math.Vector2(s.x, s.y); }), true);
    const line = (a: P2, b: P2, col: number, w: number, al = 1) => { const p = iso(a[0], a[1]), q = iso(b[0], b[1]); g.lineStyle(w, col, al).lineBetween(p.x, p.y, q.x, q.y); };
    if (env.rashidRoad) {
      // Al-Rashid coastal road: runs north-south along the shoreline on the geographic west (low lx).
      const y0 = -15, y1 = max + 15, a = -2.75, b = -1.55;
      quad([[a - .18, y0], [b + .18, y0], [b + .18, y1], [a - .18, y1]], 0xcdbb98, .85);
      quad([[a, y0], [b, y0], [b, y1], [a, y1]], 0x3b3a38, 1);
      line([a + .05, y0], [a + .05, y1], 0xd8d0bc, 1.6, .8); line([b - .05, y0], [b - .05, y1], 0xd8d0bc, 1.6, .8);
      const mid = (a + b) / 2;
      for (let s = y0; s < y1; s += .7) line([mid, s], [mid, s + .32], 0xf0e6c8, 1.6, .9);
      // promenade kerb + palms stubs on the sea side
      quad([[a - .55, y0], [a - .18, y0], [a - .18, y1], [a - .55, y1]], 0xd9cfb8, .9);
      for (let s = y0 + 1; s < y1; s += 3.1) { const p = iso(a - .36, s); g.fillStyle(0x6b5a3e, 1).fillRect(p.x - 1, p.y - 14, 2.4, 14); g.fillStyle(0x4e6b3a, 1).fillEllipse(p.x, p.y - 15, 14, 6); g.fillStyle(0x5f7f45, 1).fillEllipse(p.x + 2, p.y - 16, 8, 3.4); }
      const boatKey = this.tex('life:boat', 46, 22, x => {
        x.fillStyle = 'rgba(10,30,40,.25)'; x.beginPath(); x.ellipse(23, 18, 20, 3.2, 0, 0, Math.PI * 2); x.fill();
        x.fillStyle = '#e9e2d0'; x.beginPath(); x.moveTo(3, 10); x.lineTo(43, 10); x.lineTo(37, 17); x.lineTo(8, 17); x.closePath(); x.fill();
        x.fillStyle = '#2f6f8f'; x.fillRect(6, 13, 34, 2.4); x.fillStyle = '#b44a2e'; x.fillRect(18, 4, 9, 6); x.fillStyle = '#5a4630'; x.fillRect(29, 1, 1.4, 9);
      });
      for (let i = 0; i < MAX_BOATS; i++) { const s = iso(-6.5 - (i % 2) * 2.4, max * (.15 + i * .23)); this.boats.push(this.scene.add.image(s.x, s.y, boatKey).setScale(.9).setDepth(-7.8e5).setFlipX(i % 2 === 1)); }
    } else if (env.fringe === 'greenhouses') {
      for (let i = 0; i < 6; i++) { const x0 = max + 3.2 + (i % 2) * 2.2, y0 = 1 + Math.floor(i / 2) * (max / 3.2);
        quad([[x0, y0], [x0 + 1.7, y0], [x0 + 1.7, y0 + 3.4], [x0, y0 + 3.4]], 0xdfe6dc, .78);
        for (let s = y0 + .3; s < y0 + 3.4; s += .45) line([x0, s], [x0 + 1.7, s], 0x9aa79a, 1, .7); }
    } else if (env.fringe === 'orchard-walls') {
      for (const ly of [2, max * .45, max * .8]) { line([max + 2.6, ly], [max + 8, ly], 0xb9a888, 3, .9); line([max + 2.6, ly], [max + 2.6, ly + 3], 0xb9a888, 3, .9); }
    } else if (env.fringe === 'low-rise') {
      for (let i = 0; i < 7; i++) { const x0 = max + 3 + (i % 2) * 1.6, y0 = .5 + i * (max / 7), s = iso(x0, y0), h = 10 + hash(i + env.seed) * 16;
        quad([[x0, y0], [x0 + 1.1, y0], [x0 + 1.1, y0 + 1.1], [x0, y0 + 1.1]], 0x8f8170, .55);
        g.fillStyle(0xcfc3aa, .9).fillRect(s.x - 14, s.y + 4 - h, 14, h); g.fillStyle(0xb5a88e, .9).fillRect(s.x, s.y + 4 - h, 14, h); }
    }
  }

  /** Road-first walk: outer street -> block street -> parcel seam -> the real front entrance. Never crosses other lots. */
  private residentRoute(d: DistrictState, home: Plot, from: P2): P2[] {
    const l = cityLayout(d.id), near = (arr: number[], v: number) => arr.reduce((a, b) => Math.abs(b - v) < Math.abs(a - v) ? b : a);
    const west = from[0] < Math.min(...l.roadX) - .5 && from[0] < from[1];
    // Street entering the district on the camp's side; its outer end is the endpoint.
    const r: RoadSegment = west
      ? (() => { const y = near(l.roadY, from[1]); return { id: 'life', a: [Math.min(...l.roadX), y], b: [Math.min(...l.roadX) + 1, y] }; })()
      : (() => { const x = near(l.roadX, from[0]); return { id: 'life', a: [x, Math.min(...l.roadY)], b: [x, Math.min(...l.roadY) + 1] }; })();
    const inbound = pavingRoute(d, home, r).reverse() as P2[];
    inbound[inbound.length - 1] = plotEntrance(home, 0);
    const approach: P2 = west ? [from[0], r.a[1]] : [r.a[0], from[1]];
    return [from, approach, ...inbound].filter((v, i, a) => i === 0 || Math.hypot(v[0] - a[i - 1][0], v[1] - a[i - 1][1]) > .001);
  }

  private syncStaff(d: DistrictState) {
    const plots = d.plots.filter(p => p.status === 'built' && STAFF_IDS.has(p.buildingId)).slice(0, MAX_STAFF);
    const sig = plots.map(p => `${p.id}:${p.orientation}`).join('|');
    if (sig === this.staffSig) return; this.staffSig = sig;
    for (const w of this.staff) w.img.destroy();
    this.staff = plots.map((p, i) => ({ ...this.person('worker', p.id % 3), plotId: p.id, offset: hash(p.id) * 24000 + i * 3100 }));
  }

  update(d: DistrictState, now: number) {
    if (this.did !== d.id) this.build(d);
    if (now >= this.progressAt) { this.progressAt = now + 500; this.progress = districtProgress(d); this.syncStaff(d); }
    const byId = new Map(d.plots.map(p => [p.id, p]));
    // Factory staff: exit through the entrance, stroll along the kerb, return inside.
    for (const w of this.staff) {
      const p = byId.get(w.plotId); if (!p || p.status !== 'built') { w.img.setVisible(false); continue; }
      const cycle = 26000, t = ((now + w.offset) % cycle) / cycle;
      if (t > .62) { w.img.setVisible(false); continue; } // inside working
      const door = plotEntrance(p, 0), out = plotEntrance(p, .42);
      const side = rotateLotPoint([-.7, .97], p.orientation); const far: P2 = [p.x + 1 + side[0], p.y + 1 + side[1]];
      const k = t / .62; const path: P2[] = [door, out, far, out, door];
      const { p: at, dir } = along(path, k);
      const fade = Math.min(1, k * 14, (1 - k) * 14);
      this.pose(w, at, dir, true, now, fade);
    }
    // Residents: wander by the tents before 40%, then walk to a finished home's entrance and go inside.
    const homes = d.plots.filter(p => p.status === 'built' && HOUSING_IDS.has(p.buildingId));
    const camp = Math.max(.15, Math.pow(1 - this.progress / 100, 1.2));
    this.residents.forEach((r, i) => {
      if (r.done) { r.img.setVisible(false); return; }
      if (this.progress >= 40 && this.progress >= r.threshold && homes.length) {
        const homeSig = (p: Plot) => `${p.id}:${p.orientation}:${p.buildingId}`;
        const cur = r.home ? homes.find(p => homeSig(p) === r.home) : undefined;
        if (!r.route || !cur) {
          // New target, or the old home was sold/rotated mid-walk: re-route from where the resident stands.
          const home = homes[i % homes.length]; r.home = homeSig(home);
          const fresh = this.residentRoute(d, home, r.tent);
          if (r.route) {
            // Retrace the streets already walked back to the camp edge, then take the new home's road route.
            const k = segmentAt(r.route, Phaser.Math.Clamp((now - r.moveStart) / (r.len * 1400 + 1), 0, 1));
            r.route = [r.last, ...r.route.slice(0, k + 1).reverse(), ...fresh.slice(1)];
          } else r.route = fresh;
          r.len = pathLength(r.route); r.moveStart = now;
        }
        const t = (now - r.moveStart) / (r.len * 1400 + 1);
        if (t >= 1) { r.done = true; r.img.setVisible(false); return; }
        const { p: at, dir } = alongLen(r.route, t); r.last = at;
        this.pose(r, at, dir, true, now, Math.min(1, (1 - t) * 20));
        return;
      }
      // idle near tents: short back-and-forth paced walk
      const ph = (now / 9000 + hash(i) * 7) % 2, k = ph < 1 ? ph : 2 - ph;
      const a: P2 = [r.tent[0] + .6, r.tent[1] - .4], b: P2 = [r.tent[0] - .2 + hash(i + 3), r.tent[1] + .7];
      const at = lerp(a, b, Phaser.Math.Easing.Sine.InOut(k));
      const sa = iso(a[0], a[1]), sb = iso(b[0], b[1]);
      const pausing = Math.abs(k - .5) > .46;
      this.pose(r, at, (ph < 1 ? 1 : -1) * (sb.x >= sa.x ? 1 : -1), !pausing, now, Math.min(1, camp + .25));
    });
    // Street lighting progressive with the power project.
    const power = d.projects.find(p => p.id === 'power');
    const frac = !power || power.status === 'idle' ? 0 : power.status === 'building' ? Phaser.Math.Clamp((now - power.startedAt) / Math.max(1, power.endsAt - power.startedAt), 0, 1) : 1;
    const lit = Math.floor(frac * this.lamps.length);
    if (lit !== this.lampLit) {
      this.lampLit = lit; const on = new Set(this.lampOrder.slice(0, lit));
      this.lamps.forEach((l, i) => l.setVisible(on.has(i)).setTint(frac >= 1 ? 0xffffff : 0xd8d8d8));
    }
    // Boats bob gently.
    this.boats.forEach((b, i) => b.setAngle(Math.sin(now / 900 + i) * 2.2));
    // Palestinian flag.
    const fp = d.projects.find(p => p.id === 'flag');
    const fs = !fp || fp.status === 'idle' ? 0 : fp.status === 'building' ? Phaser.Math.Clamp((now - fp.startedAt) / Math.max(1, fp.endsAt - fp.startedAt), 0, 1) : 1;
    this.drawFlag(fs, now);
  }

  private drawFlag(f: number, now: number) {
    const g = this.flag; if (!g) return;
    g.clear(); if (f <= 0) { g.setVisible(false); return; } g.setVisible(true);
    const s = iso(this.flagAt[0], this.flagAt[1]), H = 96;
    g.fillStyle(0x1e1a14, .28).fillEllipse(s.x + 26, s.y + 6, 70, 9);
    g.fillStyle(0xbfb49c, 1).fillRect(s.x - 9, s.y - 3, 18, 6); g.fillStyle(0x9d927b, 1).fillRect(s.x - 9, s.y + 1, 18, 3);
    g.fillStyle(0x8e9399, 1).fillRect(s.x - 1.4, s.y - H, 2.8, H); g.fillStyle(0xd6dadf, 1).fillRect(s.x - 1, s.y - H, 1, H); g.fillStyle(0xd9b44a, 1).fillCircle(s.x, s.y - H - 2, 2.6);
    // Raised along the pole during construction.
    const top = s.y - H + 2 + (1 - f) * (H - 34), W = 46, FH = 26, N = 12;
    const wave = (u: number) => Math.sin(u * 5.2 - now / 260) * 3.2 * u + Math.sin(u * 9 - now / 170) * .9 * u;
    const pt = (u: number, v: number) => new Phaser.Math.Vector2(s.x + 1.4 + u * W, top + v * FH + wave(u));
    const band = (v0: number, v1: number, col: number) => {
      const pts: Phaser.Math.Vector2[] = [];
      for (let i = 0; i <= N; i++) pts.push(pt(i / N, v0));
      for (let i = N; i >= 0; i--) pts.push(pt(i / N, v1));
      g.fillStyle(col, 1).fillPoints(pts, true);
    };
    band(0, 1 / 3, 0x15181a); band(1 / 3, 2 / 3, 0xf3efe6); band(2 / 3, 1, 0x167a3c);
    const tri: Phaser.Math.Vector2[] = [pt(0, 0)]; for (let i = 0; i <= 4; i++) tri.push(pt(i * .1 / 1, i / 8)); for (let i = 4; i >= 0; i--) tri.push(pt(i * .1, 1 - i / 8)); tri.push(pt(0, 1));
    g.fillStyle(0xc8202c, 1).fillPoints(tri, true);
    // fold shading
    for (let i = 1; i < N; i += 2) { const a = pt(i / N, 0), b = pt(i / N, 1); g.lineStyle(1.2, 0x000000, .08 + .06 * Math.sin(i - now / 260)).lineBetween(a.x, a.y, b.x, b.y); }
  }
}
