import Phaser from 'phaser';
import { building } from './catalog';
import type { Plot } from './model';
import { SPR, builtKey } from './realart';
import { WK_FOOT, WK_FRAMES, WK_H, WK_W, drawWorker, type WorkerRole } from './workers';
import { isFarmDef, constructionStageFor, farmLattice } from './construction-art';

/**
 * Integration (CityScene):
 *   create/buildWorld:  this.sa = new SiteAnimation(this, (lx, ly) => iso(lx, ly), SC);
 *   update():           this.sa.update(d.plots, Date.now());   // every frame, pooled, no per-frame textures
 *   buildWorld start:   this.sa.resetDistrict();               // scene shutdown: this.sa.destroy();
 * Remove addCrane/tickWorkers/PlotView.workers/extra for building plots (this class owns them).
 * Everything is placed in plot-local lattice coords: plot diamond is 1.4 units, lattice (u,v in -1..1) * 0.77.
 */
type Pt = [number, number];
interface Lv { w: Phaser.GameObjects.Image; last: number }
interface Drop { img: Phaser.GameObjects.Image; on: boolean; t0: number; x: number; y: number; vx: number; gy: number }
interface Pulse { img: Phaser.GameObjects.Image; t0: number; on: boolean }
interface PV {
  kind: string; workers: Lv[]; crops: Phaser.GameObjects.Image[]; trees: Phaser.GameObjects.Image[]; animals: Phaser.GameObjects.Image[];
  crane?: Phaser.GameObjects.Graphics; load?: Phaser.GameObjects.Graphics; ground?: Phaser.GameObjects.Graphics;
  drops: Drop[]; pulses: Pulse[]; lastDrop: number; nextPulse: number;
}
interface Patch { id: number; canvas: HTMLCanvasElement; w: number; h: number; ox: number; oy: number; cells: number[][] }
const TREE_KIND = ['citrus', 'olive', 'palms', 'protective_trees', 'ornamental_trees'];
const CROP_KIND: Record<string, string> = { wheat: 'wheat', corn: 'corn', vegetables: 'leaf', strawberry: 'straw' };
const lerp = (a: Pt, b: Pt, k: number): Pt => [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k];
const hash = (n: number) => { const s = Math.sin(n * 91.7) * 43758.5; return s - Math.floor(s); };
const loc = (p: Plot, u: number, v: number): Pt => [p.x + 1 + u * 0.77, p.y + 1 + v * 0.77];
const ease = (k: number) => k * k * (3 - 2 * k);
const clamp01 = (k: number) => Math.min(1, Math.max(0, k));

export class SiteAnimation {
  private views = new Map<number, PV>();
  private dust?: Phaser.GameObjects.Particles.ParticleEmitter;
  private patchCache = new Map<string, Patch[]>();
  private handCache = new Map<string, { x: number; y: number }>();
  constructor(private scene: Phaser.Scene, private iso: (lx: number, ly: number) => { x: number; y: number }, private sc = 0.88, private maxWorkers = 28) {}

  private tex(key: string, w: number, h: number, draw: (c: CanvasRenderingContext2D) => void) {
    const t = this.scene.textures; if (!t.exists(key)) { const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d')!); t.addCanvas(key, cv); } return key;
  }
  private wTex(role: WorkerRole, v: number, f: number) { return this.tex(`w:${role}:${v % 3}:${f}`, WK_W, WK_H, c => { const h = drawWorker(c, role, f, v); this.handCache.set(`${role}:${v % 3}:${f}`, h); }); }
  private hand(role: WorkerRole, v: number, f: number) {
    const k = `${role}:${v % 3}:${f}`; if (!this.handCache.has(k)) this.wTex(role, v, f);
    if (!this.handCache.has(k)) { const cv = document.createElement('canvas'); cv.width = WK_W; cv.height = WK_H; this.handCache.set(k, drawWorker(cv.getContext('2d')!, role, f, v)); }
    return this.handCache.get(k)!;
  }
  /** crop tuft; lv 0 green, 1 ripening, 2 mature (wheat/corn gold, strawberry red berries) */
  private crop(kind: string, lv: number) {
    return this.tex(`sa:crop:${kind}:${lv}`, 16, 26, c => {
      c.lineCap = 'round';
      const stalk = kind === 'wheat' ? ['#7ea044', '#a9a642', '#c9a84a'][lv] : kind === 'corn' ? ['#5f8f34', '#7fa23a', '#9aa03a'][lv] : ['#4f8a3a', '#5c9440', '#4f8a3a'][lv];
      const n = kind === 'leaf' || kind === 'straw' ? 5 : 4;
      for (let i = 0; i < n; i++) {
        const x = 3 + i * (10 / n) + 1, top = kind === 'corn' ? 2 : kind === 'leaf' || kind === 'straw' ? 13 : 7;
        c.strokeStyle = stalk; c.lineWidth = kind === 'leaf' || kind === 'straw' ? 3 : 1.5;
        c.beginPath(); c.moveTo(x, 25); c.quadraticCurveTo(x + (i - n / 2) * 1.2, 15, x + (i - n / 2) * 1.8, top + i % 2 * 2); c.stroke();
        if (kind === 'wheat') { c.fillStyle = ['#a7b34e', '#cdb552', '#e0bf55'][lv]; c.fillRect(x + (i - n / 2) * 1.8 - 1, top - 3, 2.4, 6); }
        if (kind === 'corn' && lv > 0) { c.fillStyle = ['', '#9fb050', '#e1c14a'][lv]; c.fillRect(x + 1, 11, 2.4, 5); }
      }
      if (kind === 'straw' && lv > 0) { c.fillStyle = lv === 1 ? '#d97a62' : '#c42f33'; for (const [x, y] of [[4, 20], [10, 21], [7, 17]]) { c.beginPath(); c.arc(x, y, lv === 1 ? 1.6 : 2.3, 0, 7); c.fill(); } }
    });
  }
  /** Foliage fragments cut from the real front/rear farm render (connected green cells), cached per building+orientation. */
  private patches(id: string, o: 0 | 1) {
    const bk = builtKey(id, o); const hit = this.patchCache.get(bk); if (hit) return hit;
    const list: Patch[] = []; this.patchCache.set(bk, list);
    const m = SPR[bk]; const tk = 's:' + bk; if (!m || !this.scene.textures.exists(tk)) return list;
    const img = this.scene.textures.get(tk).getSourceImage() as HTMLImageElement; const W = img.width, H = img.height; if (!W || !H) return list;
    const cv = document.createElement('canvas'); cv.width = W; cv.height = H; const c = cv.getContext('2d', { willReadFrequently: true })!; c.drawImage(img, 0, 0);
    const px = c.getImageData(0, 0, W, H).data; const CELL = 6, gw = Math.ceil(W / CELL), gh = Math.ceil(H / CELL); const green = new Uint8Array(gw * gh);
    for (let gy = 0; gy < gh; gy++) for (let gx = 0; gx < gw; gx++) {
      let n = 0, t = 0; for (let y = gy * CELL; y < Math.min(H, gy * CELL + CELL); y += 2) for (let x = gx * CELL; x < Math.min(W, gx * CELL + CELL); x += 2) { const i = (y * W + x) * 4; t++; if (px[i + 3] > 32 && px[i + 1] > px[i] * 1.08 && px[i + 1] > px[i + 2] * 1.1) n++; }
      green[gy * gw + gx] = t && n / t > 0.4 ? 1 : 0;
    }
    const seen = new Uint8Array(gw * gh); const comps: { cells: number[]; x0: number; y0: number; x1: number; y1: number }[] = [];
    for (let s0 = 0; s0 < green.length; s0++) {
      if (!green[s0] || seen[s0]) continue; const st = [s0]; seen[s0] = 1; const cm = { cells: [] as number[], x0: gw, y0: gh, x1: 0, y1: 0 };
      while (st.length) { const k = st.pop()!; const x = k % gw, y = (k - x) / gw; cm.cells.push(k); cm.x0 = Math.min(cm.x0, x); cm.x1 = Math.max(cm.x1, x); cm.y0 = Math.min(cm.y0, y); cm.y1 = Math.max(cm.y1, y);
        for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) { const nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue; const j = ny * gw + nx; if (green[j] && !seen[j]) { seen[j] = 1; st.push(j); } } }
      if (cm.cells.length >= 4) comps.push(cm);
    }
    comps.sort((p, q) => q.cells.length - p.cells.length);
    const k = m.w / W; // image px -> manifest px
    comps.slice(0, 8).forEach((cm, idx) => {
      const M = 4, bx = Math.max(0, cm.x0 * CELL - M), by = Math.max(0, cm.y0 * CELL - M), bw = Math.min(W, (cm.x1 + 1) * CELL + M) - bx, bh = Math.min(H, (cm.y1 + 1) * CELL + M) - by;
      if (bw < 8 || bh < 8 || bw > W * 0.5) return;
      const mask = document.createElement('canvas'); mask.width = bw; mask.height = bh; const mc = mask.getContext('2d')!;
      for (const cell of cm.cells) { const x = (cell % gw) * CELL + CELL / 2 - bx, y = Math.floor(cell / gw) * CELL + CELL / 2 - by; const g = mc.createRadialGradient(x, y, 0, x, y, CELL * 0.95); g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(0.65, 'rgba(255,255,255,.85)'); g.addColorStop(1, 'rgba(255,255,255,0)'); mc.fillStyle = g; mc.fillRect(x - CELL, y - CELL, CELL * 2, CELL * 2); }
      mc.globalCompositeOperation = 'source-in'; mc.drawImage(cv, bx, by, bw, bh, 0, 0, bw, bh);
      list.push({ id: idx, canvas: mask, w: bw, h: bh, ox: (bx + bw / 2 - W / 2) * k, oy: (by + bh - H * m.ay) * k, cells: cm.cells.map(cell => [(cell % gw) * CELL + CELL / 2 - bx, Math.floor(cell / gw) * CELL + CELL / 2 - by]) });
    });
    return list;
  }
  /** patch texture with <=1px fruit accents (fruit species only) at stage lv 0..3 */
  private patchTex(bk: string, pt: Patch, kind: string, lv: number) {
    const fruit = (kind === 'citrus' || kind === 'olive' || kind === 'palms') && lv > 0 ? lv : 0;
    return this.tex(`sa:leaf:${bk}:${pt.id}:${fruit}`, pt.w, pt.h, c => {
      c.drawImage(pt.canvas, 0, 0); if (!fruit) return;
      c.fillStyle = kind === 'citrus' ? ['', '#9ab04a', '#e0a42a', '#e0761f'][fruit] : kind === 'olive' ? ['', '#8fa24a', '#556b34', '#3a2f3a'][fruit] : ['', '#d9b24a', '#c97a2a', '#8f4a22'][fruit];
      pt.cells.forEach(([x, y], i) => { if (hash(i + pt.id * 13) < 0.35 + fruit * 0.1) c.fillRect(Math.round(x + (hash(i * 3) - 0.5) * 4), Math.round(y + (hash(i * 5) - 0.5) * 4), 1, 1); });
    });
  }
  private cow(i: number, f: number) {
    return this.tex(`sa:cow:${i}:${f}`, 44, 30, c => {
      const col = ['#e8e2d4', '#6b4a36', '#b98862'][i % 3]; const sw = Math.sin(f / 4 * Math.PI * 2) * 3;
      c.fillStyle = 'rgba(30,22,14,.28)'; c.beginPath(); c.ellipse(22, 27, 15, 3, 0, 0, 7); c.fill();
      c.strokeStyle = '#3a2c22'; c.lineWidth = 2.4; for (const [x, s] of [[12, sw], [16, -sw], [28, -sw], [32, sw]] as number[][]) { c.beginPath(); c.moveTo(x, 19); c.lineTo(x + s, 27); c.stroke(); }
      c.fillStyle = col; c.beginPath(); c.ellipse(22, 15, 14, 7.5, 0, 0, 7); c.fill();
      c.beginPath(); c.ellipse(37, 11 + Math.abs(sw) * .1, 5, 4, 0, 0, 7); c.fill();
      c.fillStyle = i === 0 ? '#3a3430' : '#d8cdb8'; c.beginPath(); c.ellipse(18, 13, 4, 3, 0, 0, 7); c.fill();
      c.fillStyle = '#2b2320'; c.fillRect(39, 9, 1.6, 1.6);
    });
  }
  private emitter() {
    if (this.dust && this.dust.active) return this.dust;
    const k = this.tex('sa:dot', 8, 8, c => { const g = c.createRadialGradient(4, 4, 0, 4, 4, 4); g.addColorStop(0, 'rgba(225,212,184,1)'); g.addColorStop(1, 'rgba(225,212,184,0)'); c.fillStyle = g; c.fillRect(0, 0, 8, 8); });
    this.dust = this.scene.add.particles(0, 0, k, { speed: { min: 4, max: 14 }, angle: { min: 210, max: 330 }, scale: { start: 0.5, end: 1.3 }, alpha: { start: 0.45, end: 0 }, lifespan: 700, emitting: false }).setDepth(1e6);
    return this.dust;
  }
  private puff(p: { x: number; y: number }) { this.emitter().emitParticleAt(p.x, p.y - 3, 2); }

  private mk(kind: string, nWorkers: number): PV {
    const sc = this.scene; const v: PV = { kind, workers: [], crops: [], trees: [], animals: [], drops: [], pulses: [], lastDrop: 0, nextPulse: 0 };
    for (let i = 0; i < nWorkers; i++) v.workers.push({ w: sc.add.image(0, 0, this.wTex('walk', i, 0)).setOrigin(0.5, WK_FOOT / WK_H).setScale(0.3), last: 0 });
    return v;
  }
  private ensureWater(v: PV) {
    if (v.drops.length) return; const dk = this.tex('sa:drop', 4, 7, c => { c.fillStyle = 'rgba(150,200,220,.9)'; c.beginPath(); c.ellipse(2, 4, 1.4, 2.6, 0, 0, 7); c.fill(); });
    const wk = this.tex('sa:wet', 24, 10, c => { const g = c.createRadialGradient(12, 5, 1, 12, 5, 11); g.addColorStop(0, 'rgba(60,44,32,.6)'); g.addColorStop(1, 'rgba(60,44,32,0)'); c.fillStyle = g; c.fillRect(0, 0, 24, 10); });
    for (let i = 0; i < 6; i++) { v.drops.push({ img: this.scene.add.image(0, 0, dk).setVisible(false), on: false, t0: 0, x: 0, y: 0, vx: 0, gy: 0 }); v.pulses.push({ img: this.scene.add.image(0, 0, wk).setVisible(false), t0: 0, on: false }); }
  }
  private killPV(v: PV) {
    [v.crane, v.load, v.ground].forEach(g => g?.destroy()); v.workers.forEach(w => w.w.destroy()); [...v.crops, ...v.trees, ...v.animals].forEach(c => c.destroy());
    v.drops.forEach(d => d.img.destroy()); v.pulses.forEach(d => d.img.destroy());
  }
  resetDistrict() { for (const v of this.views.values()) this.killPV(v); this.views.clear(); this.dust?.destroy(); this.dust = undefined; }
  /** drop cached foliage patches (call if farm textures are reloaded) */
  clearPatchCache() { this.patchCache.clear(); }
  destroy() { this.resetDistrict(); }

  update(plots: Plot[], now: number) {
    const vw = this.scene.cameras.main.worldView; let budget = this.maxWorkers; const live = new Set<number>();
    for (const p of plots) {
      if (!(p.status === 'building' || (p.status === 'built' && isFarmDef(building(p.buildingId))))) continue;
      const c = this.iso(p.x + 1, p.y + 1); live.add(p.id);
      const kind = p.status === 'building' ? 'b' : 'f'; let v = this.views.get(p.id);
      if (v && v.kind !== kind) { this.killPV(v); this.views.delete(p.id); v = undefined; }
      if (!(c.x > vw.x - 200 && c.x < vw.right + 200 && c.y > vw.y - 320 && c.y < vw.bottom + 200)) { if (v) this.setVis(v, false); continue; }
      const def = building(p.buildingId);
      if (!v) {
        const n = kind === 'b' ? Math.min(4, 2 + Math.floor(def.floors / 3)) : def.id === 'cattle' || def.id === 'zoo' ? 0 : 2;
        if (budget < n) continue; v = this.mk(kind, n); this.views.set(p.id, v);
      }
      this.setVis(v, true); budget -= v.workers.length;
      if (kind === 'b' && !isFarmDef(def)) this.site(p, v, now); else this.farm(p, v, now);
      this.stepWater(v, now);
    }
    for (const [id, v] of this.views) if (!live.has(id)) { this.killPV(v); this.views.delete(id); }
  }
  private setVis(v: PV, on: boolean) { [v.crane, v.load, v.ground].forEach(g => g?.setVisible(on)); v.workers.forEach(w => w.w.setVisible(on)); [...v.crops, ...v.trees, ...v.animals].forEach(c => c.setVisible(on)); }

  private place(l: Lv, pos: Pt, role: WorkerRole, frame: number, dir: number, i: number) {
    const q = this.iso(pos[0], pos[1]); l.w.setTexture(this.wTex(role, i, frame)).setPosition(q.x, q.y).setFlipX(dir < 0).setDepth(q.y + 2); return q;
  }

  /** non-farm: crane + crew, everything inside the 1.4-unit lot */
  private site(p: Plot, v: PV, now: number) {
    const def = building(p.buildingId); const f = clamp01((now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt));
    const cx = p.x + 1, cy = p.y + 1, front = p.orientation === 0; const stageK = constructionStageFor(f);
    const tall = def.floors >= 3 && stageK < 7;
    const T = 16, ct = ((now / 1000 + p.id * 1.37) % T);
    const pileL: Pt = [cx - 0.5, cy + 0.55], siteL: Pt = front ? [cx + 0.5, cy + 0.62] : [cx + 0.62, cy + 0.5];
    const pp = this.iso(pileL[0], pileL[1]), sp = this.iso(siteL[0], siteL[1]);
    if (!v.ground) v.ground = this.scene.add.graphics();
    const gr = v.ground; gr.clear(); gr.setDepth(Math.max(pp.y, sp.y) + 0.5);
    const blocks = (x: number, y: number, n: number, a = 1) => { for (let i = 0; i < n; i++) { gr.fillStyle(0x9a958a, a).fillRect(x - 9 + (i % 3) * 6, y - 4 - Math.floor(i / 3) * 4, 6, 4); gr.fillStyle(0xb8b3a6, a).fillRect(x - 9 + (i % 3) * 6, y - 4 - Math.floor(i / 3) * 4, 6, 1); } };
    gr.fillStyle(0x2b2320, 0.25).fillEllipse(pp.x, pp.y, 30, 9); blocks(pp.x - 4, pp.y, 5); gr.fillStyle(0xc4a874).fillEllipse(pp.x + 14, pp.y + 1, 14, 5);
    const restock = clamp01((ct - 13.5) / 1); const delivered = ct > 11.2 && ct < 14 ? 1 : ct >= 14 ? 1 - (ct - 14) / 2 : 0;
    if (ct < 3 || restock > 0) blocks(pp.x + 6, pp.y - 4, 6, ct < 3 ? 1 : restock);
    if (delivered > 0.02) { gr.fillStyle(0x6b5036, delivered).fillRect(sp.x - 11, sp.y - 2, 22, 3); blocks(sp.x + 1, sp.y - 2, Math.max(1, Math.round(6 * delivered)), 1); }
    if (tall) {
      if (!v.crane) { v.crane = this.scene.add.graphics(); v.load = this.scene.add.graphics(); }
      const mast = this.iso(front ? cx + 0.62 : cx - 0.05, front ? cy - 0.05 : cy + 0.62); const B = { x: mast.x, y: mast.y };
      const side = Math.sign(sp.x - B.x) || 1; const H = Math.min(300, 80 + def.floors * 15) + 20;
      const g = v.crane, ld = v.load!; g.clear(); ld.clear(); g.setDepth(B.y + 1); ld.setDepth(B.y + 3);
      const travel = Math.min(pp.y, sp.y) - 78;
      let hx: number, hy: number, carry = false, slack = false;
      if (ct < 2.2) { hx = pp.x + 6; hy = Phaser.Math.Linear(travel, pp.y - 24, ease(ct / 2.2)); }
      else if (ct < 3) { hx = pp.x + 6; hy = pp.y - 24; carry = ct > 2.6; }
      else if (ct < 5.2) { hx = pp.x + 6; hy = Phaser.Math.Linear(pp.y - 24, travel, ease((ct - 3) / 2.2)); carry = true; }
      else if (ct < 9) { const k = ease((ct - 5.2) / 3.8); hx = Phaser.Math.Linear(pp.x + 6, sp.x + 1, k); hy = travel; carry = true; }
      else if (ct < 10.8) { hx = sp.x + 1; hy = Phaser.Math.Linear(travel, sp.y - 24, ease((ct - 9) / 1.8)); carry = true; }
      else if (ct < 11.2) { hx = sp.x + 1; hy = sp.y - 24; slack = true; }
      else { const k = ease(clamp01((ct - 11.2) / 4.8)); hx = Phaser.Math.Linear(sp.x + 1, pp.x + 6, k); hy = Phaser.Math.Linear(sp.y - 24, travel, Math.min(1, k * 2)); }
      const sway = Math.sin(now / 700) * (carry ? 1.5 : 0.5) * (ct > 5.2 && ct < 9 ? 2 : 1);
      const dir = Math.sign(hx - B.x) || side; const reach = Math.abs(hx - B.x) + 26;
      const jx0 = B.x - dir * 34, jx1 = B.x + dir * Math.max(70, reach), jy = B.y - H;
      g.fillStyle(0x2b2320).fillRect(B.x - 10, B.y - 3, 20, 5);
      for (let y = B.y; y > jy; y -= 12) g.lineStyle(1.3, 0xb8861c).lineBetween(B.x - 4, y, B.x - 4, y - 12).lineBetween(B.x + 4, y, B.x + 4, y - 12).lineBetween(B.x - 4, y - 12, B.x + 4, y);
      g.fillStyle(0xcf9a24).fillRect(Math.min(jx0, jx1), jy, Math.abs(jx1 - jx0), 4); g.fillStyle(0x6f6a62).fillRect(jx0 - 7, jy - 2, 14, 9);
      g.lineStyle(1, 0x3a3532).lineBetween(B.x, jy - 14, jx1, jy + 2).lineBetween(B.x, jy - 14, jx0, jy + 2).lineBetween(B.x, jy, B.x, jy - 14);
      g.fillStyle(0x3a3532).fillRect(hx - 3, jy + 3, 6, 4);
      const ax = hx + (slack ? 0 : sway); g.lineStyle(1, 0x2b2320).lineBetween(hx, jy + 7, ax, hy); g.fillStyle(0x3a3532).fillRect(ax - 2, hy, 4, 4);
      if (carry) { // pallet hangs from the hook by slings; its base sits 15px below the hook
        ld.fillStyle(0x6b5036).fillRect(ax - 11, hy + 13, 22, 3);
        for (let i = 0; i < 6; i++) { ld.fillStyle(0x9a958a).fillRect(ax - 9 + (i % 3) * 6, hy + 9 - Math.floor(i / 3) * 4, 6, 4); ld.fillStyle(0xb8b3a6).fillRect(ax - 9 + (i % 3) * 6, hy + 9 - Math.floor(i / 3) * 4, 6, 1); }
        ld.lineStyle(1, 0x2b2320).lineBetween(ax, hy + 4, ax - 10, hy + 13).lineBetween(ax, hy + 4, ax + 10, hy + 13);
      }
      if (ct > 10.8 && ct < 10.95) this.puff({ x: sp.x, y: sp.y });
    }
    // crew picks blocks from the delivered stack/pile, carries to the working face and lays them with the forearm tool
    v.workers.forEach((l, i) => {
      const t = ((now + i * 3300 + p.id * 911) % 14000) / 1000;
      const face: Pt = front ? [cx + 0.2 + i * 0.16, cy + 0.68] : [cx + 0.68, cy + 0.2 + i * 0.16];
      const pOff: Pt = [pileL[0] + 0.25 + i * 0.1, pileL[1] - 0.1];
      const dirOf = (a: Pt, b: Pt) => Math.sign((b[0] - a[0]) - (b[1] - a[1])) || 1;
      let pos: Pt, role: WorkerRole = 'walk', frame = 0, dir = 1;
      if (t < 4.2) { pos = lerp(pOff, face, t / 4.2); role = 'carry'; frame = Math.floor(t * 1.9 * WK_FRAMES) % WK_FRAMES; dir = dirOf(pOff, face); }
      else if (t < 9.2) { pos = face; role = 'hammer'; frame = Math.floor(now / 110 + i * 3) % WK_FRAMES; dir = front ? -1 : 1; if (frame === 2 && l.last !== 2) this.puff(this.iso(pos[0], pos[1])); }
      else if (t < 13.2) { pos = lerp(face, pOff, (t - 9.2) / 4); frame = Math.floor(t * 1.9 * WK_FRAMES) % WK_FRAMES; dir = dirOf(face, pOff); }
      else { pos = pOff; dir = 1; }
      l.last = frame; this.place(l, pos, role, frame, dir, i);
    });
  }

  private startDrop(v: PV, now: number, nx: number, ny: number, vx: number, gy: number) {
    this.ensureWater(v); if (now - v.lastDrop < 130) return; const d = v.drops.find(x => !x.on); if (!d) return;
    v.lastDrop = now; Object.assign(d, { on: true, t0: now, x: nx, y: ny, vx, gy }); d.img.setVisible(true).setPosition(nx, ny).setDepth(gy + 3);
  }
  private stepWater(v: PV, now: number) {
    for (const d of v.drops) if (d.on) {
      const t = (now - d.t0) / 1000; const y = d.y + 80 * t + 0.5 * 260 * t * t; const x = d.x + d.vx * t;
      if (y >= d.gy) { d.on = false; d.img.setVisible(false); const pu = v.pulses[v.nextPulse++ % v.pulses.length]; pu.on = true; pu.t0 = now; pu.img.setPosition(x, d.gy).setDepth(d.gy - 0.5).setVisible(true); }
      else d.img.setPosition(x, y);
    }
    for (const pu of v.pulses) if (pu.on) { const k = (now - pu.t0) / 900; if (k >= 1) { pu.on = false; pu.img.setVisible(false); } else pu.img.setAlpha(0.8 * (1 - k)).setScale(0.5 + k * 0.9, (0.5 + k * 0.9)); }
  }
  /** pour from the can's actual spout */
  private pour(v: PV, l: Lv, role: WorkerRole, i: number, frame: number, q: { x: number; y: number }, flip: boolean, now: number) {
    const h = this.hand(role, i, frame); const s = 0.3; const sx = flip ? -1 : 1;
    this.startDrop(v, now, q.x + (h.x + 14 - 28) * s * sx, q.y + (h.y + 4 - WK_FOOT) * s, sx * 10, q.y + 2 + (hash(now / 130) * 3)); void l;
  }

  private farm(p: Plot, v: PV, now: number) {
    const def = building(p.buildingId); const bld = p.status === 'building';
    const f = bld ? clamp01((now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt)) : 1;
    const ripe = bld ? 0 : clamp01(1 - (p.incomeAt + 3600000 - now) / 3600000);
    const ready = !bld && now >= p.incomeAt + 3600000;
    const lat = farmLattice(def.id); const spots = lat.spots;
    const ck = CROP_KIND[def.id]; const treeKind = TREE_KIND.includes(def.id) ? def.id : '';
    const lvl = bld ? 0 : ready || ripe > 0.8 ? 2 : ripe > 0.4 ? 1 : 0;
    if (ck) {
      const idx = spots.map((_, i) => i).filter(i => i % 3 === 0);
      if (!v.crops.length) idx.forEach(() => v.crops.push(this.scene.add.image(0, 0, this.crop(ck, 0)).setOrigin(0.5, 1)));
      idx.forEach((si, n) => {
        const im = v.crops[n]; const [lx, ly] = loc(p, spots[si][0] * 0.9, spots[si][1] * 0.9); const q = this.iso(lx, ly);
        const g = bld ? Math.max(0, f * 1.4 - hash(si) * 0.4) : 1; im.setVisible(g > 0.1);
        const wind = Math.sin(now / 900 + q.x * 0.05 + q.y * 0.04) * 2.6 + Math.sin(now / 370 + si) * 0.8;
        im.setTexture(this.crop(ck, lvl)).setPosition(q.x, q.y).setDepth(q.y + 1).setAngle(wind).setAlpha(.48)
          .setScale(0.48 * (0.3 + 0.7 * Math.min(1, g)) * this.sc / 0.88, 0.48 * (0.25 + 0.75 * Math.min(1, g)));
      });
    }
    if (treeKind) {
      const bk = builtKey(def.id, p.orientation); const pts = this.patches(def.id, p.orientation); const c0 = this.iso(p.x + 1, p.y + 1);
      if (!v.trees.length) pts.forEach(pt => v.trees.push(this.scene.add.image(0, 0, this.patchTex(bk, pt, treeKind, 0)).setOrigin(0.5, 1)));
      const fruit = bld ? 0 : ready ? 3 : ripe > 0.66 ? 2 : ripe > 0.33 ? 1 : 0;
      pts.forEach((pt, i) => {
        const im = v.trees[i]; const g = bld ? clamp01(f * 1.5 - hash(i + p.id) * 0.5) : 1; im.setVisible(g > 0.08);
        const sway = Math.sin(now / 1400 + i * 1.9 + p.id) * 0.9; // <=1 degree; patch stays rooted on its photo position
        im.setTexture(this.patchTex(bk, pt, treeKind, fruit)).setPosition(c0.x + pt.ox * this.sc, c0.y + pt.oy * this.sc).setDepth(c0.y + 3 + i * 0.01).setAngle(sway).setScale(this.sc * (0.3 + 0.7 * g));
      });
    }
    const herd = def.id === 'cattle' || def.id === 'zoo';
    if (herd && !bld) {
      const n = def.id === 'zoo' ? 4 : 3;
      if (!v.animals.length) for (let i = 0; i < n; i++) v.animals.push(this.scene.add.image(0, 0, this.cow(i, 0)).setOrigin(0.5, 0.95).setScale(def.id === 'zoo' ? 0.7 : 0.85));
      v.animals.forEach((a, i) => {
        const t = now / 1000 * (0.12 + i * 0.03) + i * 2.1 + p.id; const pos = (tt: number): Pt => [p.x + 1 + Math.sin(tt) * 0.4, p.y + 1 + Math.cos(tt * 0.8 + i) * 0.36];
        const q = this.iso(...pos(t)), q2 = this.iso(...pos(t + 0.05)); const grazing = Math.sin(t * 2.3 + i) > 0.5;
        a.setTexture(this.cow(i, grazing ? 0 : Math.floor(now / 160) % 4)).setPosition(q.x, q.y).setDepth(q.y + 2).setFlipX(q2.x < q.x);
      });
    }
    // work paths on the plot's own rows / tree lattice
    const nS = spots.length; const R = lat.rows, C = lat.cols;
    v.workers.forEach((l, i) => {
      let role: WorkerRole, pos: Pt, dir = 1, frame: number;
      if (ck) {
        role = bld ? (i === 0 ? 'sow' : 'water') : ready ? 'harvest' : 'water';
        const row = (Math.floor(now / 6000) + i * 2) % R; const ph = (now / 6000) % 1; const k = row % 2 ? 1 - ph : ph;
        const rowV = -0.82 + 1.64 * row / (R - 1); const off = i ? 0.1 : -0.06;
        pos = ready ? loc(p, -0.5 + i * 0.9, rowV) : loc(p, -0.8 + k * 1.6, rowV + off); dir = row % 2 ? -1 : 1;
        if (ready) { dir = 1; role = 'harvest'; }
        frame = role === 'water' || role === 'sow' ? Math.floor(now / 110) % WK_FRAMES : Math.floor(now / 140) % WK_FRAMES;
      } else {
        // trees / herd: visit lattice spots one by one (walk 30%, work 70%)
        const leg = 4200; const n = Math.floor(now / leg) + i * 4 + p.id; const tt = (now % leg) / leg;
        const a = spots[n % nS], b = spots[(n + 1) % nS]; const prev: Pt = loc(p, a[0] + 0.18, a[1] + 0.2), cur: Pt = loc(p, b[0] + 0.18, b[1] + 0.2);
        const walking = tt < 0.3; pos = walking ? lerp(prev, cur, ease(tt / 0.3)) : cur; dir = Math.sign((cur[0] - prev[0]) - (cur[1] - prev[1])) || 1;
        role = herd ? 'water' : bld ? (i === 0 ? 'harvest' : 'water') : ready ? 'pick' : (i === 0 ? 'water' : 'pick');
        if (!bld && !ready && role === 'pick') role = 'water';
        if (walking) role = 'walk';
        frame = Math.floor(now / (walking ? 110 : 150)) % WK_FRAMES;
        if (herd) { pos = loc(p, 0.95, -0.5 + i * 0.6); role = 'water'; dir = -1; }
      }
      const q = this.place(l, pos, role, frame, dir, i);
      if (role === 'water') this.pour(v, l, role, i, frame, q, dir < 0, now);
      l.last = frame;
    });
  }
}
