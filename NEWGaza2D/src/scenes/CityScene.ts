import Phaser from 'phaser';
import { building, projects } from '../catalog';
import type { GameState, Job, Plot, Unit } from '../model';
import { ANCHOR_Y, DIRS, TEX_H, TEX_W, U, VEH_SIZE, drawBuilt, drawConstruction, drawEmpty, drawRubble, drawProjectSite, drawVehicle, makeCanvas } from '../art';

export interface CityHooks { getState(): GameState; onSelect(plotId: number | null): void; onCamera(c: { x: number; y: number; zoom: number }): void }

const iso = (lx: number, ly: number) => ({ x: (lx - ly) * U, y: (lx + ly) * U / 2 });
const unIso = (wx: number, wy: number) => ({ lx: wy / U + wx / (2 * U), ly: wy / U - wx / (2 * U) });
const DRAG = 9;

interface PlotView { key: string; img: Phaser.GameObjects.Image; extra?: Phaser.GameObjects.Container; bar?: Phaser.GameObjects.Graphics; coin?: Phaser.GameObjects.Container }

export class CityScene extends Phaser.Scene {
  hooks!: CityHooks;
  private district: string | null = null;
  private views = new Map<number, PlotView>();
  private vehicles = new Map<string, Phaser.GameObjects.Image>();
  private sel!: Phaser.GameObjects.Graphics;
  private layer!: Phaser.GameObjects.Container;
  private selected: number | null = null;
  private down: { x: number; y: number; sx: number; sy: number } | null = null;
  private dragged = false; private pinch: { d: number; z: number } | null = null;
  private dust!: Phaser.GameObjects.Particles.ParticleEmitter;
  private roadG!: Phaser.GameObjects.Graphics; private roadStage = -1; private maxL = 16;
  private sites = new Map<string, { key: string; img: Phaser.GameObjects.Image; label: Phaser.GameObjects.Text; bar: Phaser.GameObjects.Graphics }>();

  constructor() { super('city'); }

  create() {
    this.hooks = this.registry.get('hooks');
    this.cameras.main.setBackgroundColor('#d9c39a');
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
    this.children.removeAll(true); this.views.clear(); this.vehicles.clear();
    this.dust = this.add.particles(0, 0, 'dot', { speed: { min: 6, max: 26 }, angle: { min: 200, max: 340 }, scale: { start: 0.9, end: 2.4 }, alpha: { start: 0.55, end: 0 }, lifespan: 1300, tint: [0xcdbb98, 0xb8ae9a, 0xe8dcc4], emitting: false }).setDepth(1e6);
    const d = st.districts.find(x => x.id === this.district)!;
    const max = Math.max(...d.plots.map(p => Math.max(p.x, p.y))) + 2;
    const c = iso(max / 2, max / 2);
    const size = max * U * 3.2;
    this.maxL = max;
    const g = this.add.graphics().setDepth(-9e5);
    const quad = (x0: number, y0: number, x1: number, y1: number, col: number, a = 1) => { const pts = [iso(x0, y0), iso(x1, y0), iso(x1, y1), iso(x0, y1)]; g.fillStyle(col, a).fillPoints(pts.map(p => new Phaser.Math.Vector2(p.x, p.y)), true); };
    // base earth, coast to the west (low lx), groves/scrub to the east
    g.fillStyle(0xc9b28a, 1).fillRect(c.x - size, c.y - size, size * 2, size * 2);
    quad(-40, -40, -4.2, max + 40, 0x1f6f78); quad(-40, -40, -6, max + 40, 0x175e66); quad(-40, -40, -9, max + 40, 0x114e56);
    quad(-4.2, -40, -3.6, max + 40, 0x7fbdb6, 0.8); quad(-3.6, -40, -1.4, max + 40, 0xe6d6b2);
    for (let i = 0; i < 18; i++) quad(-4.3 + i * 0.01, i * 3 - 8, -4.1, i * 3 - 6.5, 0xeaf2ea, 0.5);
    quad(max + 2.2, -40, max + 40, max + 40, 0xb4a274, 0.9); quad(-40, max + 2.2, max + 40, max + 40, 0xb9a47a, 0.9);
    if (this.textures.exists('sand')) this.add.tileSprite(c.x, c.y, size * 2, size * 2, 'sand').setDepth(-8.9e5).setAlpha(0.16).setTileScale(1.6);
    let seed = d.id.length * 97 + d.id.charCodeAt(0); const rnd = () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280; };
    const g2 = this.add.graphics().setDepth(-8.8e5);
    for (let i = 0; i < 90; i++) { const lx = rnd() * (max + 12) - 3, ly = rnd() * (max + 12) - 6; const p = iso(lx, ly); const east = lx > max + 1 || ly > max + 1;
      const pal = east ? [0x8a8f5a, 0x9a9a62, 0x7d8a52, 0xa88a5e] : [0xb39a72, 0xbfa984, 0xa89478, 0xc8b894, 0x9c8a74];
      g2.fillStyle(pal[i % pal.length], east ? 0.45 : 0.28).fillEllipse(p.x, p.y, 60 + rnd() * 240, 30 + rnd() * 110); }
    for (let i = 0; i < 60; i++) { const lx = max + 2.5 + rnd() * 8, ly = rnd() * (max + 10) - 2; const p = iso(lx, ly); g2.fillStyle(0x5f7a3a, 0.8).fillCircle(p.x, p.y - 6, 6 + rnd() * 6); g2.fillStyle(0x7d9a4a, 0.8).fillCircle(p.x - 2, p.y - 9, 4 + rnd() * 4); }
    this.roadG = this.add.graphics().setDepth(-8e5); this.roadStage = -1;
    this.sites.clear();
    this.layer = this.add.container(0, 0);
    this.sel = this.add.graphics().setDepth(5e5);
    const cam = this.cameras.main;
    cam.setBounds(c.x - size * 0.7, c.y - size * 0.5, size * 1.4, size);
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

  /** 0 destroyed, 1 graded dirt, 2 gravel, 3 fresh unmarked asphalt, 4 complete marked roads */
  private drawRoads(stage: number) {
    const g = this.roadG.clear(); const max = this.maxL;
    let seed = 7 + (this.district?.length ?? 0) * 13; const rnd = () => { seed = (seed * 9301 + 49297) % 233280; return seed / 233280; };
    const quad = (x0: number, y0: number, x1: number, y1: number, col: number, a = 1) => { const pts = [iso(x0, y0), iso(x1, y0), iso(x1, y1), iso(x0, y1)]; g.fillStyle(col, a).fillPoints(pts.map(p => new Phaser.Math.Vector2(p.x, p.y)), true); };
    const both = (fn: (r: number, vertical: boolean) => void) => { for (let k = 0; k <= max / 2; k++) { fn(2 * k, true); fn(2 * k, false); } };
    const strip = (r: number, v: boolean, w: number, col: number, a = 1) => v ? quad(r - w, -w, r + w, max + w, col, a) : quad(-w, r - w, max + w, r + w, col, a);
    const seg = (r: number, v: boolean, s0: number, s1: number, w0: number, w1: number, col: number, a = 1) => v ? quad(r + w0, s0, r + w1, s1, col, a) : quad(s0, r + w0, s1, r + w1, col, a);
    if (stage === 0) {
      both((r, v) => strip(r, v, 0.3, 0xa89a82));
      both((r, v) => { for (let s = 0; s < max; s += 0.35) { const t = rnd(); if (t < 0.45) seg(r, v, s, s + 0.2 + rnd() * 0.3, -0.25 + rnd() * 0.1, 0.05 + rnd() * 0.2, 0x5f5953, 0.9); else if (t < 0.6) seg(r, v, s, s + 0.25, -0.15, 0.15, 0x6b5a48, 0.7); else if (t < 0.75) seg(r, v, s, s + 0.15, -0.28 + rnd() * 0.3, -0.1 + rnd() * 0.3, 0x8a8378, 1); } });
      both((r, v) => { for (let s = 0.5; s < max; s += 1.2 + rnd()) { const p = v ? iso(r + (rnd() - 0.5) * 0.4, s) : iso(s, r + (rnd() - 0.5) * 0.4); g.fillStyle(0x9b958a).fillTriangle(p.x - 14, p.y, p.x, p.y - 9 - rnd() * 6, p.x + 12, p.y); g.fillStyle(0x7d766c).fillTriangle(p.x, p.y - 8, p.x + 12, p.y, p.x + 2, p.y); g.lineStyle(1, 0x2b2320, 0.5).lineBetween(p.x - 20, p.y + 4, p.x + 18, p.y - 6); } });
    } else if (stage === 1) {
      both((r, v) => strip(r, v, 0.34, 0x9c7f58)); both((r, v) => strip(r, v, 0.26, 0xa98c62));
      both((r, v) => { for (let i = -2; i <= 2; i++) seg(r, v, 0, max, i * 0.1 - 0.01, i * 0.1 + 0.01, 0x8a6e4a, 0.5); });
    } else if (stage === 2) {
      both((r, v) => strip(r, v, 0.34, 0xb8a684)); both((r, v) => strip(r, v, 0.27, 0x9a958c));
      both((r, v) => { for (let s = 0; s < max; s += 0.12) seg(r, v, s, s + 0.04, -0.25 + rnd() * 0.4, -0.2 + rnd() * 0.45, rnd() > 0.5 ? 0xb5b0a6 : 0x7f7a72, 0.7); });
    } else {
      both((r, v) => strip(r, v, 0.37, stage === 4 ? 0xd9ccb0 : 0xbfae8c)); both((r, v) => strip(r, v, 0.28, stage === 4 ? 0x4a4642 : 0x3f3c3a));
      if (stage === 4) {
        both((r, v) => { seg(r, v, -0.37, max + 0.37, 0.28, 0.3, 0xa8322d, 0.9); seg(r, v, -0.37, max + 0.37, -0.3, -0.28, 0xa8322d, 0.9); for (let s = 0.2; s < max; s += 0.5) { if (s % 2 > 1.7 || s % 2 < 0.3) continue; seg(r, v, s, s + 0.22, -0.018, 0.018, 0xf1e6d0, 0.9); } });
        for (let i = 0; i <= max / 2; i++) for (let j = 0; j <= max / 2; j++) { for (let z = -0.2; z <= 0.2; z += 0.1) { quad(2 * i + z - 0.02, 2 * j - 0.36, 2 * i + z + 0.02, 2 * j - 0.3, 0xf1e6d0, 0.85); }
          if ((i + j) % 2 === 0) { const p = iso(2 * i + 0.4, 2 * j + 0.4); g.fillStyle(0x3a3532).fillRect(p.x - 1, p.y - 30, 2, 30); g.fillStyle(0xf2d06b).fillCircle(p.x + 4, p.y - 30, 3); }
          if ((i * 3 + j) % 4 === 1) { const p = iso(2 * i - 0.4, 2 * j + 0.4); g.fillStyle(0x6a4a2a).fillRect(p.x - 1, p.y - 10, 2, 10); g.fillStyle(0x4f7a3a).fillCircle(p.x, p.y - 14, 7); } }
      }
    }
  }

  private siteSpots() { const m = this.maxL; return [[m + 1, 1], [m + 1, 5], [m + 1, 9], [m + 1, 13], [1, m + 1], [5, m + 1], [9, m + 1], [13, m + 1]] as [number, number][]; }
  private syncSites(d: { id: string; projects: { id: string; status: string; startedAt: number; endsAt: number }[] }, now: number) {
    const defs = projects(d.id === 'rashid').filter(p => p.id !== 'road'); const spots = this.siteSpots();
    defs.forEach((def, i) => {
      const ps = d.projects.find(p => p.id === def.id); if (!ps) return;
      const f = ps.status === 'building' ? Phaser.Math.Clamp((now - ps.startedAt) / Math.max(1, ps.endsAt - ps.startedAt), 0, 1) : 0;
      const stage = ps.status === 'idle' ? 0 : ps.status === 'building' ? 1 : 2; const q = stage === 1 ? Math.floor(f * 3) : 0;
      const key = `ps:${def.id}:${stage}:${q}`; const [lx, ly] = spots[i] ?? [this.maxL + 1, 1 + i * 2]; const c = iso(lx, ly);
      this.tex(key, TEX_W, TEX_H, ctx => drawProjectSite(ctx, def.id, stage as 0 | 1 | 2, (q + 0.5) / 3));
      let v = this.sites.get(def.id);
      if (!v) { const img = this.add.image(c.x, c.y, key).setOrigin(0.5, ANCHOR_Y / TEX_H).setDepth(c.y);
        const label = this.add.text(c.x, c.y + 44, '', { fontFamily: 'IBM Plex Sans Arabic, sans-serif', fontSize: '13px', color: '#f1e6d0', backgroundColor: 'rgba(43,35,32,0.72)', padding: { x: 6, y: 2 }, rtl: true }).setOrigin(0.5).setDepth(6e5).setAlpha(0.9);
        v = { key, img, label, bar: this.add.graphics().setDepth(6e5) }; this.sites.set(def.id, v); }
      else if (v.key !== key) { v.img.setTexture(key); v.key = key; this.tweens.add({ targets: v.img, scaleY: { from: 0.95, to: 1 }, duration: 400, ease: 'Back.out' }); }
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
    return `c:${p.buildingId}:${p.orientation}:${prog < 0.3 ? 0 : prog < 0.68 ? 1 : 2}`;
  }

  private syncPlot(p: Plot, now: number) {
    const key = this.plotKey(p, now); let v = this.views.get(p.id);
    const c = iso(p.x + 1, p.y + 1);
    if (!v || v.key !== key) {
      const [t, id, a, b] = key.split(':');
      this.tex(key, TEX_W, TEX_H, ctx => {
        if (t === 'r') drawRubble(ctx, id, Number(a)); else if (t === 'e') drawEmpty(ctx, Number(id));
        else if (t === 'b') drawBuilt(ctx, id, Number(a) as 0 | 1); else drawConstruction(ctx, id, Number(a) as 0 | 1, Number(b));
      });
      if (v) { v.img.setTexture(key); v.key = key; v.extra?.destroy(); v.extra = undefined; v.bar?.destroy(); v.bar = undefined; this.tweens.add({ targets: v.img, scaleY: { from: 0.94, to: 1 }, duration: 380, ease: 'Back.out' }); }
      else { const img = this.add.image(c.x, c.y, key).setOrigin(0.5, ANCHOR_Y / TEX_H).setDepth(c.y); v = { key, img }; this.views.set(p.id, v); }
      if (t === 'c') this.addCrane(v, c, p);
    }
    // ready-to-collect badge
    const ready = p.status === 'built' && building(p.buildingId).income > 0 && now >= p.incomeAt + 3600000;
    if (ready && !v.coin) {
      const g = this.add.graphics(); g.fillStyle(0x2b2320, 0.25).fillEllipse(0, 22, 26, 8); g.fillStyle(0xc58a1c).fillCircle(0, 0, 13); g.fillStyle(0xf2d06b).fillCircle(0, -1, 10); g.lineStyle(2, 0xa06c10).strokeCircle(0, -1, 6);
      v.coin = this.add.container(c.x, c.y - 120, [g]).setDepth(6e5);
      this.tweens.add({ targets: v.coin, y: c.y - 132, yoyo: true, repeat: -1, duration: 900, ease: 'Sine.inOut' });
    } else if (!ready && v.coin) { v.coin.destroy(); v.coin = undefined; }
  }

  private addCrane(v: PlotView, c: { x: number; y: number }, p: Plot) {
    const def = building(p.buildingId); const h = Math.min(260, 70 + def.floors * 16);
    const side = p.orientation === 0 ? 1 : -1;
    const g = this.add.graphics();
    if (def.category !== 'farm') {
      g.fillStyle(0xc58a1c).fillRect(-3, -h, 6, h); for (let y = -h; y < 0; y += 10) g.lineStyle(1, 0x7a5410).lineBetween(-3, y, 3, y + 10);
      g.fillStyle(0x2b2320).fillRect(-8, -4, 16, 6);
    }
    const jib = this.add.graphics(); if (def.category !== 'farm') { jib.fillStyle(0xd9a227).fillRect(-20, -3, 110, 5); jib.fillStyle(0x6f6a62).fillRect(-28, -6, 14, 10); jib.lineStyle(1, 0x2b2320).lineBetween(70, 2, 70, 40); jib.fillStyle(0x9b958a).fillRect(64, 40, 12, 7); jib.y = -h; jib.scaleX = side; }
    const worker = this.add.graphics(); worker.fillStyle(0xf2a23a).fillCircle(0, -14, 4); worker.fillStyle(0x16747a).fillRect(-3, -10, 6, 10);
    worker.x = -30 * side;
    const box = this.add.container(c.x + 52 * side, c.y - 6, [g, jib, worker]).setDepth(c.y + 1);
    if (def.category !== 'farm') this.tweens.add({ targets: jib, scaleX: { from: side, to: side * 0.55 }, yoyo: true, repeat: -1, duration: 2600 + (p.id % 5) * 300, ease: 'Sine.inOut' });
    this.tweens.add({ targets: worker, x: -60 * side, yoyo: true, repeat: -1, duration: 1800, ease: 'Sine.inOut' });
    this.tweens.add({ targets: worker, y: -3, yoyo: true, repeat: -1, duration: 220 });
    v.extra = box;
    v.bar = this.add.graphics().setDepth(6e5);
  }

  private drawSel(plots: Plot[]) {
    this.sel.clear();
    const p = plots.find(x => x.id === this.selected); if (!p) return;
    const pts = [iso(p.x + 0.28, p.y + 0.28), iso(p.x + 1.72, p.y + 0.28), iso(p.x + 1.72, p.y + 1.72), iso(p.x + 0.28, p.y + 1.72)].map(q => new Phaser.Math.Vector2(q.x, q.y));
    this.sel.lineStyle(4, 0xf2d06b, 1).strokePoints(pts, true); this.sel.lineStyle(10, 0xf2d06b, 0.25).strokePoints(pts, true);
  }

  select(id: number | null) { this.selected = id; const st = this.hooks.getState(); const d = st.districts.find(x => x.id === this.district); if (d) this.drawSel(d.plots); }

  // --- jobs & vehicles ---
  private route(job: Job, plots: Plot[]) {
    const o = plots.find(p => p.id === job.originPlotId) ?? plots[0]; const t = plots.find(p => p.id === job.plotId)!;
    return [[o.x + 1, o.y + 1], [o.x + 2, o.y + 1], [o.x + 2, t.y + 2], [t.x + 1, t.y + 2], [t.x + 1, t.y + 1.15]] as [number, number][];
  }
  private along(pts: [number, number][], f: number): [number, number] {
    const segs = pts.slice(1).map((p, i) => Math.hypot(p[0] - pts[i][0], p[1] - pts[i][1])); const total = segs.reduce((a, b) => a + b, 0) || 1;
    let dist = Phaser.Math.Clamp(f, 0, 1) * total;
    for (let i = 0; i < segs.length; i++) { if (dist <= segs[i] || i === segs.length - 1) { const k = segs[i] ? dist / segs[i] : 0; return [pts[i][0] + (pts[i + 1][0] - pts[i][0]) * k, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * k]; } dist -= segs[i]; }
    return pts[pts.length - 1];
  }

  update() {
    const st = this.hooks?.getState(); if (!st?.currentDistrict || st.currentDistrict !== this.district) return;
    const d = st.districts.find(x => x.id === this.district)!; const now = Date.now();
    // construction progress bars
    for (const p of d.plots) { const v = this.views.get(p.id); if (p.status === 'building' && v?.bar) { const c = iso(p.x + 1, p.y + 1); const f = Phaser.Math.Clamp((now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt), 0, 1); v.bar.clear().fillStyle(0x2b2320, 0.75).fillRoundedRect(c.x - 40, c.y + 30, 80, 9, 4).fillStyle(0xf2d06b).fillRoundedRect(c.x - 38, c.y + 32, 76 * f, 5, 2); if (Math.random() < 0.03) this.dust.emitParticleAt(c.x + (Math.random() - 0.5) * 60, c.y, 2); } }
    const seen = new Set<string>();
    for (const job of st.jobs.filter(j => j.districtId === this.district)) {
      const pts = this.route(job, d.plots); const back = [...pts].reverse();
      job.unitIds.forEach((uid, i) => {
        const unit = st.units.find(u => u.id === uid); if (!unit) return;
        const lag = i * 1200; const t = now - lag;
        let pos: [number, number] | null; let next: [number, number] | null = null; let working = false;
        if (t < job.start) pos = pts[0];
        else if (t < job.arrival) { const f = (t - job.start) / Math.max(1, job.arrival - job.start); pos = this.along(pts, f); next = this.along(pts, f + 0.01); }
        else if (now < job.workEnd) { pos = [pts[4][0] + (i - 1) * 0.28, pts[4][1] + (i % 2) * 0.2]; working = true; }
        else if (t < job.returnEnd) { const f = (t - job.workEnd) / Math.max(1, job.returnEnd - job.workEnd); pos = this.along(back, f); next = this.along(back, f + 0.01); }
        else pos = null;
        if (!pos) return;
        seen.add(uid);
        let spr = this.vehicles.get(uid);
        if (!spr) { spr = this.add.image(0, 0, this.vTex(unit, 2)).setOrigin(0.5, 0.62); spr.setData('dir', 2); this.vehicles.set(uid, spr); }
        if (next) { const dir = ((Math.round(Math.atan2(next[1] - pos[1], next[0] - pos[0]) / (Math.PI * 2) * DIRS) % DIRS) + DIRS) % DIRS; if (dir !== spr.getData('dir')) { spr.setData('dir', dir); } spr.setTexture(this.vTex(unit, dir)); }
        else if (working) { const dir = (Math.floor(now / 1400) + i * 4) % 2 ? 1 : 3; spr.setTexture(this.vTex(unit, dir)); }
        const w = iso(pos[0], pos[1]);
        const jig = working ? Math.sin(now / 90 + i) * 1.5 : 0;
        spr.setPosition(w.x + jig, w.y + (working ? Math.abs(Math.sin(now / 160)) * -2 : 0)).setDepth(w.y + 40).setVisible(true);
        if (working && Math.random() < 0.18) this.dust.emitParticleAt(w.x + (Math.random() - 0.5) * 50, w.y - 4, 1);
      });
    }
    const depot = d.plots.find(p => p.buildingId === 'recycling' && p.status === 'built');
    if (depot) { let n = 0; for (const unit of st.units) { if (seen.has(unit.id) || st.jobs.some(j => j.unitIds.includes(unit.id))) continue;
      const slot = n++; const lx = depot.x + 2 - 0.12 + (slot % 2) * 0.24, ly = depot.y + 0.45 + Math.floor(slot / 2) * 0.32; const w = iso(lx, ly);
      let spr = this.vehicles.get(unit.id); if (!spr) { spr = this.add.image(0, 0, this.vTex(unit, 4)).setOrigin(0.5, 0.62); this.vehicles.set(unit.id, spr); }
      spr.setTexture(this.vTex(unit, 4)).setPosition(w.x, w.y).setDepth(w.y + 40).setVisible(true); seen.add(unit.id); } }
    for (const [id, s] of this.vehicles) if (!seen.has(id)) { s.destroy(); this.vehicles.delete(id); }
  }
  private vTex(u: Unit, dir: number) { return this.tex(`v:${u.kind}:${dir}:${u.level}`, VEH_SIZE, VEH_SIZE, c => drawVehicle(c, u.kind, dir, u.level)); }

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
    this.load.image('worldmap', base + 'art/world-map.jpg');
  }
  create() { this.game.events.emit('booted'); this.scene.start('city'); }
}
