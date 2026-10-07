import Phaser from 'phaser';
import { building, projects } from '../catalog';
import type { GameState, Plot } from '../model';
import { TEX_H, TEX_W, U, makeCanvas } from '../art';
import { GROUND_KEYS, SPR, SPRITE_KEYS, builtKey, constructKey, groundUrl, isFarm, ruinKey, siteKey, sprUrl } from '../realart';
import { Painter, blob, frameFor, rect, roundRect, seeded, GK } from '../ground';
import { constructionCanvas, constructionStageFor } from '../construction-art';
import { SiteAnimation } from '../site-animation';
import { FleetAnimation, preloadMachinery } from '../fleet-animation';
import { ENVIRONMENT_IDS, environmentUrl, groundStamp } from '../environment-art';

export interface CityHooks { getState(): GameState; onSelect(plotId: number | null): void; onCamera(c: { x: number; y: number; zoom: number }): void }

const iso = (lx: number, ly: number) => ({ x: (lx - ly) * U, y: (lx + ly) * U / 2 });
const unIso = (wx: number, wy: number) => ({ lx: wy / U + wx / (2 * U), ly: wy / U - wx / (2 * U) });
const DRAG = 9; const ANCHOR_LOT = 380; const SC = 0.88;

interface PlotView { key: string; img: Phaser.GameObjects.Image; shadow?: Phaser.GameObjects.Image; skirt?: Phaser.GameObjects.Image; bar?: Phaser.GameObjects.Graphics; coin?: Phaser.GameObjects.Container }

export class CityScene extends Phaser.Scene {
  hooks!: CityHooks;
  private district: string | null = null;
  private views = new Map<number, PlotView>();
  private siteAnimation!: SiteAnimation;
  private fleetAnimation!: FleetAnimation;
  private sel!: Phaser.GameObjects.Graphics;
  private layer!: Phaser.GameObjects.Container;
  private selected: number | null = null;
  private down: { x: number; y: number; sx: number; sy: number } | null = null;
  private dragged = false; private pinch: { d: number; z: number } | null = null;
  private dust!: Phaser.GameObjects.Particles.ParticleEmitter;
  private roadImg?: Phaser.GameObjects.Image; private roadStage = -1; private maxL = 16; private frame!: ReturnType<typeof frameFor>; private groundKey = '';
  private sites = new Map<string, { key: string; img: Phaser.GameObjects.Image; label: Phaser.GameObjects.Text; bar: Phaser.GameObjects.Graphics }>();
  private projectPlots: Plot[] = [];
  private occlusion = new Set<number>();
  private occlusionAt = 0;

  constructor() { super('city'); }

  create() {
    this.hooks = this.registry.get('hooks');
    this.siteAnimation = new SiteAnimation(this, iso, SC);
    this.fleetAnimation = new FleetAnimation(this);
    this.events.once(Phaser.Scenes.Events.SHUTDOWN, () => { this.siteAnimation.destroy(); this.fleetAnimation.destroy(); });
    this.cameras.main.setBackgroundColor('#b3a483');
    this.input.addPointer(1);
    if (!this.textures.exists('dot')) { const g = this.make.graphics({}, false); g.fillStyle(0xe8dcc4, 1).fillCircle(4, 4, 4); g.generateTexture('dot', 8, 8); g.destroy(); }
    this.dust = this.add.particles(0, 0, 'dot', { speed: { min: 6, max: 26 }, angle: { min: 200, max: 340 }, scale: { start: 0.9, end: 2.4 }, alpha: { start: 0.55, end: 0 }, lifespan: 1300, tint: [0xcdbb98, 0xb8ae9a, 0xe8dcc4], emitting: false });
    this.dust.setDepth(1e6);
    this.input.on('pointerdown', (p: Phaser.Input.Pointer) => this.pDown(p));
    this.input.on('pointermove', (p: Phaser.Input.Pointer) => this.pMove(p));
    this.input.on('pointerup', (p: Phaser.Input.Pointer) => this.pUp(p));
    this.input.on('wheel', (_p: unknown, _o: unknown, _dx: number, dy: number) => { this.zoomBy(dy > 0 ? 0.9 : 1.1); this.saveCam(); });
    this.sync();
  }

  tex(key: string, w: number, h: number, draw: (c: CanvasRenderingContext2D) => void) {
    if (!this.textures.exists(key)) { const c = makeCanvas(w, h); draw(c.getContext('2d')!); this.textures.addCanvas(key, c); }
    return key;
  }

  private buildWorld(st: GameState) {
    this.siteAnimation.resetDistrict(); this.fleetAnimation.resetDistrict();
    this.occlusion.clear(); this.occlusionAt = 0;
    this.children.removeAll(true); this.views.clear();
    this.dust = this.add.particles(0, 0, 'dot', { speed: { min: 6, max: 26 }, angle: { min: 200, max: 340 }, scale: { start: 0.9, end: 2.4 }, alpha: { start: 0.55, end: 0 }, lifespan: 1300, tint: [0xcdbb98, 0xb8ae9a, 0xe8dcc4], emitting: false }).setDepth(1e6);
    const d = st.districts.find(x => x.id === this.district)!;
    const max = Math.max(...d.plots.map(p => Math.max(p.x, p.y))) + 2;
    const c = iso(max / 2, max / 2);
    const size = max * U * 3.2;
    this.maxL = max;
    const g = this.add.graphics().setDepth(-9e5);
    const quad = (x0: number, y0: number, x1: number, y1: number, col: number, a = 1) => { const pts = [iso(x0, y0), iso(x1, y0), iso(x1, y1), iso(x0, y1)]; g.fillStyle(col, a).fillPoints(pts.map(p => new Phaser.Math.Vector2(p.x, p.y)), true); };
    g.fillStyle(0xb7a47e, 1).fillRect(c.x - size * 1.5, c.y - size * 1.5, size * 3, size * 3);
    this.paintGround(d.id, max);
    this.roadStage = -1; this.roadImg = undefined;
    this.sites.clear();
    this.layer = this.add.container(0, 0);
    this.sel = this.add.graphics().setDepth(5e5);
    const cam = this.cameras.main;
    cam.setBounds(this.frame.ox - 120, this.frame.oy - 120, this.frame.w / GK + 240, this.frame.h / GK + 240);
    if (d.camera) { cam.setZoom(d.camera.zoom); cam.centerOn(d.camera.x, d.camera.y); }
    else { cam.setZoom(Math.min(1, this.scale.width / (max * U * 2.2))); cam.centerOn(c.x, c.y); }
  }

  sync() {
    if (!this.hooks || !this.sys.isActive()) return;
    const st = this.hooks.getState();
    if (!st.currentDistrict) return;
    if (st.currentDistrict !== this.district) { this.district = st.currentDistrict; this.selected = null; this.buildWorld(st); }
    const d = st.districts.find(x => x.id === this.district)!;
    const now = Date.now();
    for (const p of d.plots) this.syncPlot(p, now);
    this.drawSel(d.plots);
    const rp = d.projects.find(p => p.id === 'road');
    const rs = !rp || rp.status === 'idle' ? 0 : rp.status === 'building' ? 1 + Math.min(2, Math.floor(3 * Phaser.Math.Clamp((now - rp.startedAt) / Math.max(1, rp.endsAt - rp.startedAt), 0, 0.999))) : 4;
    if (rs !== this.roadStage) { this.roadStage = rs; this.drawRoads(rs); }
    this.syncSites(d, now);
  }

  private imgs(): Record<string, CanvasImageSource | undefined> {
    const o: Record<string, CanvasImageSource | undefined> = {};
    for (const k of GROUND_KEYS) o[k] = this.textures.exists('g:' + k) ? (this.textures.get('g:' + k).getSourceImage() as HTMLImageElement) : undefined;
    return o;
  }
  private seedRnd(n: number) { let seed = n; return () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280; }; }

  /** photo-textured ground: earth base, gravel patches, coastal sand ramp, wet-sand edge, scrub to the east */
  private paintGround(did: string, max: number) {
    const LX0 = -14, LY0 = -16, LX1 = max + 16, LY1 = max + 16;
    this.frame = frameFor(LX0, LY0, LX1, LY1); const f = this.frame;
    const cv = makeCanvas(f.w, f.h); const P = new Painter(cv, f, this.imgs()); const r = this.seedRnd(did.length * 97 + did.charCodeAt(0));
    const shore = (ly: number) => -3.6 + 0.32 * Math.sin(ly * 0.7) + 0.18 * Math.sin(ly * 1.9 + 1);
    const wp = (a: number, b: number): [number, number][] => { const p: [number, number][] = []; for (let ly = LY0; ly <= LY1; ly += 0.5) p.push([shore(ly) + a, ly]); for (let ly = LY1; ly >= LY0; ly -= 0.5) p.push([shore(ly) + b, ly]); return p; };
    P.fill(rect(-400, -400, 400, 400), 'earth', 9, 1, 0, 0);
    P.fill(rect(-400, -400, 0, 400), 'water', 11, 1, 0, 0); P.fill(rect(-400, -400, 0, 400), 'water', 17.3, 0.35, 2.2, 5.1);
    for (let i = 0; i < 5; i++) P.shade(wp(-12, -1.0 - i * 1.6), 'rgba(6,40,58,.1)');
    for (let i = 0; i < 3; i++) P.shade(wp(-1.5 + i * 0.3, 0.1), 'rgba(150,215,205,.12)');
    const land = wp(0, 400);
    P.fill(land, 'earth', 9, 1, 0, 0); P.fill(land, 'earth', 13.7, 0.45, 3.1, 5.7);
    for (let i = 0; i < 42; i++) { const lx = r() * (LX1 - 0) + 0, ly = r() * (LY1 - LY0) + LY0; P.fill(blob(lx, ly, 1 + r() * 2.8, 0.8 + r() * 2.2, r), r() > 0.45 ? 'gravel' : 'earth', 6 + r() * 3, 0.3 + r() * 0.3, r() * 4, r() * 4); }
    for (let i = 0; i < 6; i++) P.fill(wp(-0.2, 2.9 - i * 0.45), 'sand', 7.3, 0.34, 1.7, 0.4);
    P.fill(wp(-0.3, 0.45), 'sand', 7.3, 0.75, 0, 0, 'rgba(70,56,40,.3)');
    for (let ly = LY0; ly < LY1; ly += 0.5) { const a: [number, number] = [shore(ly) - 0.2, ly], b: [number, number] = [shore(ly + 0.5) - 0.2, ly + 0.5]; P.stroke(a, b, 'rgba(244,250,246,.75)', 1.4); if (Math.sin(ly * 2.1) > -0.2) P.stroke([a[0] - 0.4, a[1]], [b[0] - 0.4, b[1]], 'rgba(244,250,246,.3)', 1); }
    for (let i = 0; i < 4; i++) P.shade(rect(max + 1.6 + i * 0.9, LY0, LY1 + 20, LY1), 'rgba(98,112,58,.07)');
    for (let i = 0; i < 4; i++) P.shade(rect(0, max + 1.6 + i * 0.9, LX1, LY1 + 20), 'rgba(98,112,58,.06)');
    for (let i = 0; i < 26; i++) P.shade(blob(r() * (LX1 + 20), r() * (LY1 - LY0) + LY0, 2 + r() * 4, 1.5 + r() * 3, r), `rgba(${r() > 0.5 ? '120,100,72' : '176,160,128'},.08)`);
    const key = `ground:${did}`; if (this.textures.exists(key)) this.textures.remove(key); this.textures.addCanvas(key, cv); this.groundKey = key;
    this.add.image(f.ox, f.oy, key).setOrigin(0, 0).setScale(1 / GK).setDepth(-8.9e5);
  }

  /** 0 destroyed, 1 graded dirt, 2 gravel, 3 fresh unmarked asphalt, 4 complete marked roads */
  private drawRoads(stage: number) {
    const max = this.maxL; const f = this.frame; const cv = makeCanvas(f.w, f.h); const P = new Painter(cv, f, this.imgs());
    const r = this.seedRnd(7 + (this.district?.length ?? 0) * 13);
    const lines: number[] = []; for (let k = 0; k <= max / 2; k++) lines.push(2 * k);
    const strip = (c: number, v: boolean, w: number, s0 = -0.4, s1 = max + 0.4) => v ? rect(c - w, s0, c + w, s1) : rect(s0, c - w, s1, c + w);
    const each = (fn: (c: number, v: boolean) => void) => lines.forEach(c => { fn(c, true); fn(c, false); });
    const at = (c: number, v: boolean, s: number, o: number): [number, number] => v ? [c + o, s] : [s, c + o];
    if (stage === 0) {
      each((c, v) => { P.fill(strip(c, v, 0.34), 'sand', 8, 0.35); P.fill(strip(c, v, 0.27), 'asphalt', 7, 0.96); });
      each((c, v) => { for (let s = .12; s < max; s += .42) {
        if (r() > .3) for (const edge of [-.29, .29])
          P.stroke(at(c, v, s, edge), at(c, v, s + .26, edge), 'rgba(185,180,168,.48)', 2.4);
      } });
      each((c, v) => { for (let s = 0; s < max; s += 0.5) { const t = r(); const [x, y] = at(c, v, s + r() * 0.4, (r() - 0.5) * 0.4);
        if (t < 0.28) P.fill(blob(x, y, 0.13 + r() * 0.12, 0.1 + r() * 0.1, r, 7), 'earth', 3, 0.92, 0, 0, 'rgba(30,22,14,.35)');
        else if (t < 0.55) P.fill(blob(x, y, 0.16 + r() * 0.2, 0.08 + r() * 0.1, r, 7), 'gravel', 2.2, 0.9, r() * 3, r() * 3); } });
    } else if (stage === 1) {
      each((c, v) => { P.fill(strip(c, v, 0.35), 'earth', 6, 1, 0, 0, 'rgba(90,60,30,.18)'); for (let i = -2; i <= 2; i++) { const a = at(c, v, 0, i * 0.11), b = at(c, v, max, i * 0.11); P.stroke(a, b, 'rgba(60,40,20,.3)', 2); } });
    } else if (stage === 2) {
      each((c, v) => { P.fill(strip(c, v, 0.35), 'earth', 6, 0.9); P.fill(strip(c, v, 0.3), 'gravel', 4.5, 1); });
    } else {
      each((c, v) => { P.fill(strip(c, v, stage === 4 ? 0.4 : 0.37), 'sand', 8, 0.6); P.fill(strip(c, v, 0.3), 'asphalt', 7, 1, 0, 0, stage === 3 ? 'rgba(10,10,12,.2)' : undefined); });
      if (stage === 4) each((c, v) => {
        for (const o of [-0.3, 0.3]) P.stroke(at(c, v, -0.4, o), at(c, v, max + 0.4, o), 'rgba(205,195,170,.8)', 2.2);
        for (let s = 0.1; s < max; s += 0.55) P.stroke(at(c, v, s, 0), at(c, v, s + 0.24, 0), 'rgba(236,228,205,.85)', 2.2);
      });
    }
    // road edge darkening for crisp shoulders
    if (stage >= 3) each((c, v) => { for (const o of [-0.3, 0.3]) P.stroke(at(c, v, -0.4, o), at(c, v, max + 0.4, o), 'rgba(15,15,15,.35)', 1.2); });
    if (stage >= 3) each((c, v) => { for (let s = 0; s < max; s += 0.22) { const t = r(); const o = (r() < 0.5 ? -1 : 1) * (0.26 + r() * 0.08); const [x, y] = at(c, v, s + r() * 0.2, o);
      if (t < 0.5) P.fill(blob(x, y, 0.03 + r() * 0.06, 0.025 + r() * 0.04, r, 6), t < 0.25 ? 'gravel' : 'earth', 2, 0.85, r() * 3, r() * 3);
      else if (t < 0.62) { const [x2, y2] = at(c, v, s + 0.15 + r() * 0.3, (r() - 0.5) * 0.4); P.stroke([x, y], [x2, y2], 'rgba(18,18,18,.4)', 0.8); } } });
    // rounded lot bases: curb + dusty ground under every plot, so lots read as continuous blocks
    for (let bx = 0; bx < max; bx += 2) for (let by = 0; by < max; by += 2) {
      const b = roundRect(bx + 0.32, by + 0.32, bx + 1.68, by + 1.68, 0.34);
      if (stage >= 3) { P.shade(roundRect(bx + 0.3, by + 0.3, bx + 1.7, by + 1.7, 0.36), 'rgba(40,34,28,.45)'); P.fill(b, 'sand', 6, 0.9, bx, by, 'rgba(120,100,76,.22)'); }
      else P.fill(b, r() > 0.5 ? 'gravel' : 'earth', 5, 0.45, bx, by, 'rgba(183,177,164,.1)');
      for (let i = 0; i < 5; i++) { const a = r() * 4, o = r() < 0.5 ? 0.3 : 1.7; const x = r() < 0.5 ? bx + o + (r() - 0.5) * 0.3 : bx + 0.4 + r() * 1.2, y = x > bx + 0.25 && x < bx + 1.75 && r() < 0.5 ? by + o + (r() - 0.5) * 0.3 : by + 0.4 + r() * 1.2;
        P.fill(blob(x, y, 0.1 + r() * 0.16, 0.07 + r() * 0.1, r, 8), r() > 0.5 ? 'gravel' : 'earth', 1.6, stage >= 3 ? 0.5 : 0.85, a, a, 'rgba(60,48,36,.2)'); }
    }
    const key = `roads:${this.district}`; this.roadImg?.destroy(); if (this.textures.exists(key)) this.textures.remove(key); this.textures.addCanvas(key, cv);
    this.roadImg = this.add.image(f.ox, f.oy, key).setOrigin(0, 0).setScale(1 / GK).setDepth(-8e5);
  }

  private siteSpots() { const m = this.maxL; return [[m + 1, 1], [m + 1, 5], [m + 1, 9], [m + 1, 13], [1, m + 1], [5, m + 1], [9, m + 1], [13, m + 1]] as [number, number][]; }
  private syncSites(d: { id: string; projects: { id: string; status: string; startedAt: number; endsAt: number }[] }, now: number) {
    const defs = projects(d.id === 'rashid').filter(p => p.id !== 'road'); const spots = this.siteSpots();
    this.projectPlots = [];
    defs.forEach((def, i) => {
      const ps = d.projects.find(p => p.id === def.id); if (!ps) return;
      const f = ps.status === 'building' ? Phaser.Math.Clamp((now - ps.startedAt) / Math.max(1, ps.endsAt - ps.startedAt), 0, 1) : 0;
      const stage = ps.status === 'idle' ? 0 : ps.status === 'building' ? 1 : 2; const q = stage === 1 ? constructionStageFor(f) : 0;
      const key = `ps:${def.id}:${stage}:${q}`; const [lx, ly] = spots[i] ?? [this.maxL + 1, 1 + i * 2]; const c = iso(lx, ly);
      const sk = siteKey(def.id, stage as 0 | 1 | 2, q);
      const projectBuilding: Record<string, string> = { water: 'water_treatment', power: 'work', housing: 'housing_4', park: 'ornamental_trees', services: 'municipality', farm: 'wheat', commerce: 'modern_mall', industry: 'steel' };
      if (stage === 1) this.projectPlots.push({ id: 1000 + i, x: lx - 1, y: ly - 1, status: 'building',
        buildingId: projectBuilding[def.id] ?? 'work', orientation: 0, startedAt: ps.startedAt, endsAt: ps.endsAt, incomeAt: 0 });
      const tk = stage === 1 ? this.constructionTex(projectBuilding[def.id] ?? 'work', 0, q) : sk ? 's:' + sk : this.skirtTex(i % 3, true);
      let v = this.sites.get(def.id);
      if (!v) { const img = this.place(tk, c.x, c.y, c.y);
        const label = this.add.text(c.x, c.y + 44, '', { fontFamily: 'IBM Plex Sans Arabic, sans-serif', fontSize: '13px', color: '#f1e6d0', backgroundColor: 'rgba(43,35,32,0.72)', padding: { x: 6, y: 2 }, rtl: true }).setOrigin(0.5).setDepth(6e5).setAlpha(0.9);
        v = { key, img, label, bar: this.add.graphics().setDepth(6e5) }; this.sites.set(def.id, v); }
      else if (v.key !== key) { this.applyTex(v.img, tk); v.key = key; this.tweens.add({ targets: v.img, scaleY: { from: SC * 0.95, to: SC }, duration: 400, ease: 'Back.out' }); }
      const st = stage === 0 ? 'لم يبدأ' : stage === 1 ? `قيد الإنشاء ${Math.round(f * 100)}٪` : ps.status === 'ready' ? 'جاهز للجمع' : 'مكتمل';
      v.label.setText(`${def.name} · ${st}`);
      v.bar.clear(); if (stage === 1) v.bar.fillStyle(0x2b2320, 0.75).fillRoundedRect(c.x - 40, c.y + 58, 80, 8, 4).fillStyle(0xf2d06b).fillRoundedRect(c.x - 38, c.y + 60, 76 * f, 4, 2);
    });
  }

  private plotKey(p: Plot, now: number) {
    if (p.status === 'rubble') return `r:${p.buildingId}:${p.id % 5}`;
    if (p.status === 'empty') return `e:${p.id % 7}`;
    if (p.status === 'built') return `b:${p.buildingId}:${p.orientation}`;
    const prog = Math.min(1, Math.max(0, (now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt)));
    return `c:${p.buildingId}:${p.orientation}:${constructionStageFor(prog)}`;
  }

  /** textured empty lot (gravel + survey stakes) */
  private lotTex(n: number) {
    const key = `lot:${n}`; if (this.textures.exists(key)) return key;
    const c = makeCanvas(); const x = c.getContext('2d')!; const img = this.textures.exists('g:gravel') ? this.textures.get('g:gravel').getSourceImage() as HTMLImageElement : null;
    const pr = (lx: number, ly: number): [number, number] => [TEX_W / 2 + (lx - ly) * U, 380 + (lx + ly) * U / 2]; const h = 0.74;
    x.save(); x.beginPath(); [[-h, -h], [h, -h], [h, h], [-h, h]].forEach(([a, b], i) => { const [px, py] = pr(a, b); i ? x.lineTo(px, py) : x.moveTo(px, py); }); x.closePath(); x.clip();
    if (img) { x.setTransform(U * 5 / 1024, U * 5 / 2048, -U * 5 / 1024, U * 5 / 2048, TEX_W / 2 + n * 37, 380 - 30 * n); x.fillStyle = x.createPattern(img, 'repeat')!; x.fillRect(-4000, -4000, 8000, 8000); x.setTransform(1, 0, 0, 1, 0, 0); } else { x.fillStyle = '#b9a888'; x.fill(); }
    x.fillStyle = 'rgba(150,120,80,.12)'; x.fill(); x.restore();
    x.strokeStyle = 'rgba(70,55,40,.4)'; x.lineWidth = 2; x.beginPath(); [[-h, -h], [h, -h], [h, h], [-h, h]].forEach(([a, b], i) => { const [px, py] = pr(a, b); i ? x.lineTo(px, py) : x.moveTo(px, py); }); x.closePath(); x.stroke();
    for (const [a, b] of [[-0.62, -0.62], [0.62, -0.62], [-0.62, 0.62], [0.62, 0.62]]) { const [px, py] = pr(a, b); x.fillStyle = 'rgba(30,20,10,.3)'; x.fillRect(px - 1, py, 5, 2); x.fillStyle = '#e5dcc4'; x.fillRect(px - 1.5, py - 14, 3, 14); x.fillStyle = '#b33a2a'; x.fillRect(px - 1.5, py - 14, 3, 4); }
    this.textures.addCanvas(key, c); return key;
  }
  /** soft organic soil/dust skirt that spills around a base; shared by variant, bounded count */
  private skirtTex(n: number, stakes: boolean, finished = false) {
    const key = `skirt:${finished ? 2 : stakes ? 1 : 0}:${n}`; if (this.textures.exists(key)) return key;
    const c = makeCanvas(); const x = c.getContext('2d')!; const r = seeded(n * 131 + 17);
    const pr = (lx: number, ly: number): [number, number] => [TEX_W / 2 + (lx - ly) * U, 380 + (lx + ly) * U / 2];
    const gi = (k: string) => this.textures.exists(k) ? this.textures.get(k).getSourceImage() as HTMLImageElement : null;
    const layer = (rx: number, ry: number, a: number, tex: string, tint: string) => {
      const pts = blob(0, 0, rx, ry, r, 14); const img = gi(tex);
      x.save(); x.filter = 'blur(5px)'; x.globalAlpha = a; x.beginPath(); pts.forEach(([lx, ly], i) => { const [px, py] = pr(lx, ly); i ? x.lineTo(px, py) : x.moveTo(px, py); }); x.closePath(); x.clip();
      if (img) { x.setTransform(U * 5 / 1024, U * 5 / 2048, -U * 5 / 1024, U * 5 / 2048, TEX_W / 2 + n * 41, 380 - 23 * n); x.fillStyle = x.createPattern(img, 'repeat')!; x.fillRect(-4000, -4000, 8000, 8000); x.setTransform(1, 0, 0, 1, 0, 0); }
      x.fillStyle = tint; x.fillRect(0, 0, TEX_W, 760); x.restore();
    };
    layer(1, 1, stakes ? .18 : .28, 'g:earth', 'rgba(158,151,139,.12)');
    layer(.85, .85, finished || stakes ? .24 : .42, 'g:gravel', 'rgba(163,156,145,.1)');
    const stamp = (id: number, lx: number, ly: number, width: number, opacity: number) => {
      const img = gi('env:' + id); if (!img) return;
      const [px, py] = pr(lx, ly); groundStamp(x, img, id, px, py, width, opacity, n % 2 === 1);
    };
    if (finished) {
      stamp(9, -.3, .55, 122, .25);
      stamp(10, .65, .2, 84, .16);
    } else if (stakes) {
      stamp(10, 0, 0, 154, .3);
      stamp(9, .4, .35, 60, .15);
    } else {
      stamp(n % 4, 0, 0, 175, .65);
      stamp((n + 2) % 4, -.45, .75, 64, .63);
      stamp((n + 1) % 4, .8, .45, 59, .64);
      stamp(4 + n % 4, .8, -.6, 56, .3);
    }
    // Feather the skirt itself (not just its contents): leave the road centre legible.
    const mask = makeCanvas(); const mc = mask.getContext('2d')!;
    mc.translate(TEX_W / 2, ANCHOR_LOT); mc.scale(1, .5);
    const fade = mc.createRadialGradient(0, 0, 65, 0, 0, 110);
    fade.addColorStop(0, 'white'); fade.addColorStop(.5, 'rgba(255,255,255,.9)'); fade.addColorStop(1, 'rgba(255,255,255,0)');
    mc.fillStyle = fade; mc.fillRect(-112, -112, 224, 224);
    x.globalCompositeOperation = 'destination-in'; x.drawImage(mask, 0, 0); x.globalCompositeOperation = 'source-over';
    if (stakes) for (const [a, b] of [[-0.8, 0.5], [0.85, -0.4]]) { const [px, py] = pr(a, b); x.fillStyle = 'rgba(30,20,10,.3)'; x.fillRect(px - 1, py, 5, 2); x.fillStyle = '#e5dcc4'; x.fillRect(px - 1.5, py - 12, 3, 12); x.fillStyle = '#b33a2a'; x.fillRect(px - 1.5, py - 12, 3, 3); }
    this.textures.addCanvas(key, c); return key;
  }
  private shadowTex() {
    if (this.textures.exists('shadow')) return 'shadow';
    const c = makeCanvas(160, 92); const x = c.getContext('2d')!;
    x.translate(80, 49); x.scale(1, .46);
    const g = x.createRadialGradient(0, 0, 10, 0, 0, 79);
    g.addColorStop(0, 'rgba(55,52,45,.34)'); g.addColorStop(.65, 'rgba(55,52,45,.13)'); g.addColorStop(1, 'rgba(55,52,45,0)');
    x.fillStyle = g; x.fillRect(-80, -90, 160, 180);
    this.textures.addCanvas('shadow', c); return 'shadow';
  }
  /** sprites are anchored on the ground-footprint centre (ay from manifest), never the building centre */
  private place(tk: string, x: number, y: number, depth: number) {
    const img = this.add.image(x, y, tk).setDepth(depth).setScale(SC); this.applyTex(img, tk); return img;
  }
  private applyTex(img: Phaser.GameObjects.Image, tk: string) {
    img.setTexture(tk); const m = SPR[tk.startsWith('s:') ? tk.slice(2) : tk];
    img.setOrigin(0.5, m ? m.ay : ANCHOR_LOT / TEX_H);
    img.setScale(tk.startsWith('s:ruin') && m ? Math.min(SC, 215 / m.h) : SC);
  }
  private constructionTex(id: string, orientation: 0 | 1, stage: number) {
    const key = `construction:${id}:${orientation}:${stage}`;
    if (!this.textures.exists(key)) {
      const finalKey = builtKey(id, orientation), meta = SPR[finalKey];
      const final = this.textures.get('s:' + finalKey).getSourceImage() as HTMLImageElement;
      const foundation = this.textures.get('s:' + constructKey(id, 0)).getSourceImage() as HTMLImageElement;
      const cv = constructionCanvas(final, foundation, meta, building(id), stage, orientation);
      this.textures.addCanvas(key, cv); SPR[key] = { ...meta };
    }
    return key;
  }
  private plotTex(t: string, id: string, a: string, b: string, plotId: number) {
    if (t === 'r') return 's:' + ruinKey(id, Number(a) + plotId);
    if (t === 'e') return this.skirtTex(Number(id) % 3, true);
    if (t === 'b') return 's:' + builtKey(id, Number(a) as 0 | 1);
    return this.constructionTex(id, Number(a) as 0 | 1, Number(b));
  }

  private syncPlot(p: Plot, now: number) {
    const key = this.plotKey(p, now); let v = this.views.get(p.id);
    const c = iso(p.x + 1, p.y + 1);
    if (!v || v.key !== key) {
      const [t, id, a, b] = key.split(':'); const tk = this.plotTex(t, id, a, b, p.id);
      if (v) { this.applyTex(v.img, tk); v.key = key; v.bar?.destroy(); v.bar = undefined; }
      else { const img = this.place(tk, c.x, c.y, c.y); const shadow = t === 'e' || t === 'r' ? undefined : this.add.image(c.x, c.y, this.shadowTex()).setDepth(c.y - 1).setScale(1.3); v = { key, img, shadow }; this.views.set(p.id, v); }
      if (!v.shadow && t !== 'e' && t !== 'r') v.shadow = this.add.image(c.x, c.y, this.shadowTex()).setDepth(c.y - 1).setScale(1.3);
      if (t !== 'e') {
        const skirt = this.skirtTex(p.id % 3, false, t !== 'r');
        if (!v.skirt) v.skirt = this.add.image(c.x, c.y, skirt).setOrigin(0.5, ANCHOR_LOT / TEX_H).setDepth(c.y - 2).setScale(SC);
        else v.skirt.setTexture(skirt);
      } else if (v.skirt) { v.skirt.destroy(); v.skirt = undefined; }
      if (v.shadow && (t === 'e' || t === 'r')) { v.shadow.destroy(); v.shadow = undefined; }
      v.img.setFlipX(false).setAlpha(1);
      if (t === 'c') v.bar = this.add.graphics().setDepth(6e5);
    }
    // ready-to-collect badge
    const ready = p.status === 'built' && building(p.buildingId).income > 0 && now >= p.incomeAt + 3600000;
    if (ready && !v.coin) {
      const g = this.add.graphics(); g.fillStyle(0x2b2320, 0.25).fillEllipse(0, 22, 26, 8); g.fillStyle(0xc58a1c).fillCircle(0, 0, 13); g.fillStyle(0xf2d06b).fillCircle(0, -1, 10); g.lineStyle(2, 0xa06c10).strokeCircle(0, -1, 6);
      v.coin = this.add.container(c.x, c.y - 120, [g]).setDepth(6e5);
      this.tweens.add({ targets: v.coin, y: c.y - 132, yoyo: true, repeat: -1, duration: 900, ease: 'Sine.inOut' });
    } else if (!ready && v.coin) { v.coin.destroy(); v.coin = undefined; }
  }


  private drawSel(plots: Plot[]) {
    this.sel.clear();
    const p = plots.find(x => x.id === this.selected); if (!p) return;
    const pts = [iso(p.x + 0.28, p.y + 0.28), iso(p.x + 1.72, p.y + 0.28), iso(p.x + 1.72, p.y + 1.72), iso(p.x + 0.28, p.y + 1.72)].map(q => new Phaser.Math.Vector2(q.x, q.y));
    this.sel.lineStyle(4, 0xf2d06b, 1).strokePoints(pts, true); this.sel.lineStyle(10, 0xf2d06b, 0.25).strokePoints(pts, true);
  }

  select(id: number | null) { this.selected = id; const st = this.hooks.getState(); const d = st.districts.find(x => x.id === this.district); if (d) this.drawSel(d.plots); }

  update() {
    const st = this.hooks?.getState(); if (!st?.currentDistrict || st.currentDistrict !== this.district) return;
    const d = st.districts.find(x => x.id === this.district)!; const now = Date.now();
    // construction progress bars
    for (const p of d.plots) { const v = this.views.get(p.id); if (p.status === 'building' && v?.bar) { const c = iso(p.x + 1, p.y + 1); const f = Phaser.Math.Clamp((now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt), 0, 1); v.bar.clear().fillStyle(0x2b2320, 0.75).fillRoundedRect(c.x - 40, c.y + 30, 80, 9, 4).fillStyle(0xf2d06b).fillRoundedRect(c.x - 38, c.y + 32, 76 * f, 5, 2);  } }
    this.siteAnimation.update([...d.plots, ...this.projectPlots], now);
    this.fleetAnimation.update(st, now, this.roadStage);
    this.revealWorkingFleet(now);
  }
  private revealWorkingFleet(now: number) {
    if (now >= this.occlusionAt) {
      this.occlusionAt = now + 160; this.occlusion.clear();
      const points = this.fleetAnimation.focusPoints();
      for (const [id, v] of this.views) {
        const img = v.img, bounds = img.getBounds();
        for (const p of points) {
          if (img.depth <= p.depth + 8 || !bounds.contains(p.x, p.y)) continue;
          const x = Math.floor((p.x - img.x) / img.scaleX + img.displayOriginX);
          const y = Math.floor((p.y - img.y) / img.scaleY + img.displayOriginY);
          if ((this.textures.getPixelAlpha(x, y, img.texture.key) ?? 0) > 90) { this.occlusion.add(id); break; }
        }
      }
    }
    for (const [id, v] of this.views) {
      const alpha = this.occlusion.has(id) ? .28 : 1;
      v.img.setAlpha(v.img.alpha + (alpha - v.img.alpha) * .16);
    }
  }

  // --- input ---
  private pDown(p: Phaser.Input.Pointer) {
    const ps = [this.input.pointer1, this.input.pointer2].filter(q => q?.isDown);
    if (ps.length === 2) { this.pinch = { d: Phaser.Math.Distance.Between(ps[0].x, ps[0].y, ps[1].x, ps[1].y), z: this.cameras.main.zoom }; this.dragged = true; return; }
    this.down = { x: p.x, y: p.y, sx: this.cameras.main.scrollX, sy: this.cameras.main.scrollY }; this.dragged = false;
  }
  private pMove(p: Phaser.Input.Pointer) {
    const cam = this.cameras.main;
    const ps = [this.input.pointer1, this.input.pointer2].filter(q => q?.isDown);
    if (this.pinch && ps.length === 2) { const d = Phaser.Math.Distance.Between(ps[0].x, ps[0].y, ps[1].x, ps[1].y); cam.setZoom(Phaser.Math.Clamp(this.pinch.z * d / this.pinch.d, 0.35, 2.2)); return; }
    if (!this.down || !p.isDown) return;
    if (!this.dragged && Phaser.Math.Distance.Between(p.x, p.y, this.down.x, this.down.y) > DRAG) this.dragged = true;
    if (this.dragged) { cam.scrollX = this.down.sx - (p.x - this.down.x) / cam.zoom; cam.scrollY = this.down.sy - (p.y - this.down.y) / cam.zoom; }
  }
  private pUp(p: Phaser.Input.Pointer) {
    if (this.pinch) { if (![this.input.pointer1, this.input.pointer2].some(q => q?.isDown)) { this.pinch = null; this.down = null; this.saveCam(); } return; }
    if (!this.down) return;
    if (this.dragged) this.saveCam();
    else {
      const w = this.cameras.main.getWorldPoint(p.x, p.y); const { lx, ly } = unIso(w.x, w.y);
      const st = this.hooks.getState(); const d = st.districts.find(x => x.id === this.district);
      const hit = d?.plots.find(q => lx >= q.x + 0.3 && lx <= q.x + 1.7 && ly >= q.y + 0.3 && ly <= q.y + 1.7) ?? null;
      this.select(hit?.id ?? null); this.hooks.onSelect(hit?.id ?? null);
    }
    this.down = null;
  }
  zoomBy(f: number) { const cam = this.cameras.main; cam.setZoom(Phaser.Math.Clamp(cam.zoom * f, 0.35, 2.2)); }
  saveCam() { const cam = this.cameras.main; const m = cam.midPoint; this.hooks.onCamera({ x: Math.round(m.x), y: Math.round(m.y), zoom: Math.round(cam.zoom * 100) / 100 }); }
  focusPlot(id: number) { const st = this.hooks.getState(); const p = st.districts.find(x => x.id === this.district)?.plots.find(q => q.id === id); if (p) { const c = iso(p.x + 1, p.y + 1); this.cameras.main.pan(c.x, c.y - 40, 500, Phaser.Math.Easing.Sine.InOut); } }
}

export class BootScene extends Phaser.Scene {
  constructor() { super('boot'); }
  preload() {
    const base = import.meta.env.BASE_URL;
    const bar = document.getElementById('boot-bar'); const msg = document.getElementById('boot-msg');
    this.load.on('progress', (v: number) => { if (bar) bar.style.transform = `scaleX(${v})`; if (msg) msg.textContent = `تحميل رسوم المدينة ${Math.round(v * 100)}٪`; });
    this.load.on('loaderror', (f: Phaser.Loader.File) => { const e = document.getElementById('boot-err'); if (e) { e.hidden = false; e.textContent = `تعذر تحميل ملف: ${f.key}. ستعمل اللعبة برسوم بديلة.`; } });
    this.load.image('sand', base + 'art/sand-ground.jpg');
    for (const k of SPRITE_KEYS) this.load.image('s:' + k, sprUrl(k));
    for (const k of GROUND_KEYS) this.load.image('g:' + k, groundUrl(k));
    for (const id of ENVIRONMENT_IDS) this.load.image('env:' + id, environmentUrl(id));
    preloadMachinery(this);
    this.load.image('worldmap', base + 'art/world-map.jpg');
  }
  create() { this.game.events.emit('booted'); this.scene.start('city'); }
}
