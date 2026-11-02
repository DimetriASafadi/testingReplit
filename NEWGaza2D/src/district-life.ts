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
const MAX_STAFF = 8, MAX_RESIDENTS = 16, MAX_LAMPS = 96, MAX_BOATS = 4;
const STAFF_IDS = new Set(['work', 'equipment_store', 'recycling', 'glass', 'cement', 'steel', 'asphalt', 'food_factory', 'water_treatment', 'wood_factory', 'household_shop']);
const HOUSING_IDS = new Set(['small_house', 'medium_house', 'villa', 'housing_4', 'housing_6', 'housing_complex', 'modern_housing', 'traditional_housing', 'housing_tower', 'tourist_villa']);
const PERSON_SCALE = .3;
interface LifeArt { key: string; url: string; ox: number; oy: number; scale: number; poleTop?: number }
const ART_BASE = `${import.meta.env.BASE_URL}art/life/`;
// Generated masters live in art-src/new; runtime WebP measured: lamp foot at 84% height,
// plaza pole foot at 55.7% and finial at 9.8% of the 360px frame.
// Anchors measured from alpha bboxes of the 1024px masters (see docs/life-art-audit.md).
const LAMP_ART: LifeArt = { key: 'life:lamp-art', url: ART_BASE + 'streetlamp.webp?v=2', ox: .508, oy: .88, scale: .34 };
// Plaza: anchor = pole foot (512,570); finial top at y=94.
const PLAZA_ART: LifeArt = { key: 'life:flag-plaza', url: ART_BASE + 'flagplaza.webp?v=2', ox: .5, oy: .557, scale: .6, poleTop: .095 };
// Foundation site: diamond centre (511,540) of a 982px-wide pad, scaled to the plaza's footprint.
const SITE_ART: LifeArt = { key: 'life:flag-site', url: ART_BASE + 'flagsite.webp?v=2', ox: .5, oy: .53, scale: .51 };
// Roadside date palm: planter centre (512,880) of 1024 master.
const PALM_ART: LifeArt = { key: 'life:palm', url: ART_BASE + 'palm.webp?v=1', ox: .5, oy: .86, scale: .3 };
const BOAT_ART: LifeArt = { key: 'life:boat-art', url: ART_BASE + 'boat.webp?v=2', ox: .5, oy: .55, scale: .3 };
// Low-rise fringe house: ground diamond centre (512,780).
const HOUSE_ART: LifeArt = { key: 'life:lowrise', url: ART_BASE + 'lowrise.webp?v=2', ox: .5, oy: .76, scale: .5 };
export function preloadLifeArt(scene: Phaser.Scene) {
  for(const art of [LAMP_ART,PLAZA_ART,SITE_ART,BOAT_ART,HOUSE_ART,PALM_ART])scene.load.image(art.key,art.url);
}
const hash = (n: number) => { const x = Math.sin(n * 127.1 + 311.7) * 43758.5453; return x - Math.floor(x); };

/** Shared exterior tent placement (lot coordinates). RoadAnimation should use this so people and tents agree. */
export function tentSpot(i: number, max: number, count = 28, coastal=false): P2 {
  const t = (i + 1) / (count + 1);
  if(coastal)return [max+3.5+(i%3)*1.6,t*max];
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
  private plaza?: Phaser.GameObjects.Image;
  private flagSite?: Phaser.GameObjects.Image;
  private flagCrew: Walker[] = [];
  private houses: Phaser.GameObjects.Image[] = [];
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

  /** Load a generated life sprite once (runtime WebP), then run apply. Procedural fallback stays until then. */
  private art(a: LifeArt, apply: () => void) {
    // BootScene loads all bespoke art before gameplay (including offline APK).
    // Never silently substitute a different object when a required asset fails.
    if (!this.scene.textures.exists(a.key)) throw new Error(`Missing district artwork: ${a.key}`);
    apply();
  }

  reset() {
    for (const w of [...this.staff, ...this.residents]) w.img.destroy();
    for (const l of this.lamps) l.destroy();
    for (const b of this.boats) b.destroy();
    this.fringe?.destroy(); this.flag?.destroy(); this.plaza?.destroy(); this.flagSite?.destroy(); for (const w of this.flagCrew) w.img.destroy(); for (const h of this.houses) h.destroy();
    this.staff = []; this.residents = []; this.lamps = []; this.lampOrder = []; this.boats = [];
    this.fringe = undefined; this.flag = undefined; this.plaza = undefined; this.flagSite = undefined; this.flagCrew = []; this.houses = []; this.did = ''; this.staffSig = ''; this.lampLit = -1; this.progressAt = 0;
  }

  private build(d: DistrictState) {
    this.reset(); this.did = d.id;
    const layout = cityLayout(d.id); this.max = layout.extent; const max = this.max;
    const env = districtEnvironment(d.id);
    // Residents around the tent camp (mirrors RoadAnimation tent placement).
    for (let i = 0; i < MAX_RESIDENTS; i++) {
      const tent = tentSpot(Math.floor(i * 28 / MAX_RESIDENTS), max, 28,env.style==='coastal');
      const r: Resident = { ...this.person('civilian', i % 5), tent, threshold: 40 + i * 58 / (MAX_RESIDENTS - 1), moveStart: 0, route: null, len: 0, home: '', done: false, last: tent };
      this.residents.push(r);
    }
    // Streetlamps: both kerbs of every street, paired, skipping crossings and plot entrances.
    const spots: P2[] = [];
    const roads = layout.roadX.length + layout.roadY.length;
    const step = Math.max(1.8, roads * max * 2 / MAX_LAMPS);
    // Reserve every possible entrance once; rotating a building cannot put its door behind a lamp.
    const doors = d.plots.flatMap(p => ([0,1,2,3] as const).map(orientation=>plotEntrance({...p,orientation},.45)));
    const clearOf = (q: P2) => !doors.some(e => Math.hypot(e[0] - q[0], e[1] - q[1]) < .7);
    const nearCross = (v: number, cross: number[]) => cross.some(k => Math.abs(k - v) < .75);
    for (const c of layout.roadX) for (let s = 1; s < max; s += step) { if (nearCross(s, layout.roadY)) continue;
      for (const o of [-.5, .5]) { const q: P2 = [c + o, s + (o > 0 ? step / 2 : 0)]; if (q[1] < max && clearOf(q) && !nearCross(q[1], layout.roadY)) spots.push(q); } }
    for (const c of layout.roadY) for (let s = 1 + step / 4; s < max; s += step) { if (nearCross(s, layout.roadX)) continue;
      for (const o of [-.5, .5]) { const q: P2 = [s + (o > 0 ? step / 2 : 0), c + o]; if (q[0] < max && clearOf(q) && !nearCross(q[0], layout.roadX)) spots.push(q); } }
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
    this.art(LAMP_ART, () => this.lamps.forEach(l => l.setTexture(LAMP_ART.key).setOrigin(LAMP_ART.ox, LAMP_ART.oy).setScale(LAMP_ART.scale)));
    this.lampOrder = this.lamps.map((_, i) => i).sort((a, b) => hash(a + env.seed) - hash(b + env.seed));
    // Flag at the district's outer corner, clear of the tent rows and the coast road.
    this.flagAt = [max + 1.2, -1.4];
    const fd = iso(this.flagAt[0], this.flagAt[1]);
    this.flag = this.scene.add.graphics().setDepth(fd.y + 1).setVisible(false);
    this.art(PLAZA_ART, () => {
      this.plaza = this.scene.add.image(fd.x, fd.y, PLAZA_ART.key).setOrigin(PLAZA_ART.ox, PLAZA_ART.oy).setScale(PLAZA_ART.scale).setDepth(fd.y).setVisible(false);
    });
    this.art(SITE_ART, () => {
      this.flagSite = this.scene.add.image(fd.x, fd.y, SITE_ART.key).setOrigin(SITE_ART.ox, SITE_ART.oy).setScale(SITE_ART.scale).setDepth(fd.y - 2).setVisible(false);
    });
    this.flagCrew = [this.person('worker', 1), this.person('worker', 2)];
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
      // promenade kerb and individual date palms on the sea side
      quad([[a - .55, y0], [a - .18, y0], [a - .18, y1], [a - .55, y1]], 0xd9cfb8, .9);
      this.art(PALM_ART, () => { for (let k = 0, s = y0 + 1; s < y1; s += 3.1, k++) { const p = iso(a - .36, s);
        // soft contact shadow toward lower right (sun upper left), then the generated palm
        g.fillStyle(0x1e1a14, .22).fillEllipse(p.x + 6, p.y + 1, 22, 6);
        this.houses.push(this.scene.add.image(p.x, p.y, PALM_ART.key).setOrigin(PALM_ART.ox, PALM_ART.oy).setScale(PALM_ART.scale * (.92 + hash(k + 5) * .16)).setDepth(p.y)); } });
      const boatKey = this.tex('life:boat', 46, 22, x => {
        x.fillStyle = 'rgba(10,30,40,.25)'; x.beginPath(); x.ellipse(23, 18, 20, 3.2, 0, 0, Math.PI * 2); x.fill();
        x.fillStyle = '#e9e2d0'; x.beginPath(); x.moveTo(3, 10); x.lineTo(43, 10); x.lineTo(37, 17); x.lineTo(8, 17); x.closePath(); x.fill();
        x.fillStyle = '#2f6f8f'; x.fillRect(6, 13, 34, 2.4); x.fillStyle = '#b44a2e'; x.fillRect(18, 4, 9, 6); x.fillStyle = '#5a4630'; x.fillRect(29, 1, 1.4, 9);
      });
      for (let i = 0; i < MAX_BOATS; i++) { const s = iso(-6.5 - (i % 2) * 2.4, max * (.15 + i * .23)); this.boats.push(this.scene.add.image(s.x, s.y, boatKey).setScale(.9).setDepth(-7.8e5).setFlipX(i % 2 === 1)); }
      this.art(BOAT_ART, () => this.boats.forEach(b => b.setTexture(BOAT_ART.key).setOrigin(BOAT_ART.ox, BOAT_ART.oy).setScale(BOAT_ART.scale)));
    } else if (env.fringe === 'greenhouses') {
      // The former opaque pale roof quads looked like missing ground tiles.
      // Keep the agricultural fringe, but blend soil and sparse crops into the
      // existing terrain instead of covering it with featureless white sheets.
      for (let i = 0; i < 6; i++) {
        const x0 = max + 3.2 + (i % 2) * 2.2, y0 = 1 + Math.floor(i / 2) * (max / 3.2);
        for(let band=0;band<4;band++){
          const inset=band*.12;
          quad([[x0+inset,y0+inset],[x0+1.7-inset,y0+inset],
            [x0+1.7-inset,y0+3.4-inset],[x0+inset,y0+3.4-inset]],0x887c58,.055);
        }
        for(let row=0;row<7;row++){
          const y=y0+.35+row*.43;
          for(let plant=0;plant<7;plant++){
            const n=i*101+row*11+plant,t=hash(n);
            const x=x0+.22+plant*.2;
            line([x,y],[x+.12,y],0x736a47,.8,.18);
            if(t<.22)continue;
            const p=iso(x+.03,y+(hash(n+73)-.5)*.07);
            g.fillStyle(t>.65?0x657044:0x727748,.22+t*.17)
              .fillEllipse(p.x,p.y,2.2+t*2,1.3+t);
          }
        }
      }
    } else if (env.fringe === 'orchard-walls') {
      for (const ly of [2, max * .45, max * .8]) { line([max + 2.6, ly], [max + 8, ly], 0xb9a888, 3, .9); line([max + 2.6, ly], [max + 2.6, ly + 3], 0xb9a888, 3, .9); }
    } else if (env.fringe === 'low-rise') {
      // Generated low-rise Gaza houses (flat roofs, tanks, solar heaters); seeded spacing and mirroring per district.
      const spots: P2[] = [];
      for (let i = 0; i < 7; i++) spots.push([max + 3.6 + (i % 2) * 1.6 + hash(i + env.seed) * .5, 1 + i * (max / 7)]);
      this.art(HOUSE_ART, () => { for (const [i, [x0, y0]] of spots.entries()) { const s = iso(x0, y0);
        this.houses.push(this.scene.add.image(s.x, s.y, HOUSE_ART.key).setOrigin(HOUSE_ART.ox, HOUSE_ART.oy).setScale(HOUSE_ART.scale * (.9 + hash(i + 9) * .2)).setFlipX(hash(i + env.seed * 3) > .5).setDepth(s.y)); } });
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
    const lit = frac <= 0 ? 0 : Math.min(this.lamps.length, Math.ceil((.12 + .88 * frac) * this.lamps.length));
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
    const base = iso(this.flagAt[0], this.flagAt[1]);
    g.clear();
    const building = f > 0 && f < 1, done = f >= 1;
    this.flagSite?.setVisible(building); this.plaza?.setVisible(done);
    // Construction phase: dedicated foundation site (formwork, rebar, paving stock) with two crew pacing it. No pole, no cloth yet.
    this.flagCrew.forEach((w, i) => {
      if (!building) { w.img.setVisible(false); return; }
      const ph = (now / 5200 + i * .5) % 2, k = ph < 1 ? ph : 2 - ph;
      const a: P2 = i ? [this.flagAt[0] + .55, this.flagAt[1] - .5] : [this.flagAt[0] - .6, this.flagAt[1] + .45];
      const b: P2 = i ? [this.flagAt[0] + .55, this.flagAt[1] + .35] : [this.flagAt[0] + .2, this.flagAt[1] + .45];
      const at = lerp(a, b, Phaser.Math.Easing.Sine.InOut(k)), sa = iso(a[0], a[1]), sb = iso(b[0], b[1]);
      this.pose(w, at, (ph < 1 ? 1 : -1) * (sb.x >= sa.x ? 1 : -1), Math.abs(k - .5) < .44, now);
    });
    if (f <= 0) { g.setVisible(false); return; }
    g.setVisible(true);
    if (building) {
      g.fillStyle(0x2b2320, .75).fillRoundedRect(base.x - 30, base.y + 40, 60, 6, 3).fillStyle(0xf2d06b).fillRoundedRect(base.x - 29, base.y + 41, 58 * f, 4, 2);
      return;
    }
    if (this.plaza) {
      // Pole foot of the generated plaza is the anchor; flag is fully hoisted to the finial.
      const fh = this.plaza.frame.height, sc = PLAZA_ART.scale;
      const H = (PLAZA_ART.oy - PLAZA_ART.poleTop!) * fh * sc;
      this.drawCloth(g, base.x, base.y - H, H, 1, now, 1);
      return;
    }
    const s = base, H = 96;
    g.fillStyle(0x1e1a14, .28).fillEllipse(s.x + 26, s.y + 6, 70, 9);
    g.fillStyle(0xbfb49c, 1).fillRect(s.x - 9, s.y - 3, 18, 6); g.fillStyle(0x9d927b, 1).fillRect(s.x - 9, s.y + 1, 18, 3);
    g.fillStyle(0x8e9399, 1).fillRect(s.x - 1.4, s.y - H, 2.8, H); g.fillStyle(0xd6dadf, 1).fillRect(s.x - 1, s.y - H, 1, H); g.fillStyle(0xd9b44a, 1).fillCircle(s.x, s.y - H - 2, 2.6);
    this.drawCloth(g, s.x, s.y - H, H, 1, now, 1);
  }

  /** Animated Palestinian flag hoisted on a pole whose top is at (x, topY). */
  private drawCloth(g: Phaser.GameObjects.Graphics, x: number, topY: number, H: number, f: number, now: number, k: number) {
    const s = { x }; const W = 46 * k, FH = 26 * k, N = 12;
    const top = topY + 2 + (1 - f) * Math.max(0, H - FH - 8);
    const wave = (u: number) => Math.sin(u * 5.2 - now / 260) * 3.2 * u + Math.sin(u * 9 - now / 170) * .9 * u;
    const pt = (u: number, v: number) => new Phaser.Math.Vector2(s.x + 1.4 + u * W, top + v * FH + wave(u) * k);
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
