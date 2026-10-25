import Phaser from 'phaser';
import { districtOverview, MAX_CITY_ZOOM } from '../camera-overview';
import { building, projects } from '../catalog';
import { buildingIncomePeriod } from '../game-timing';
import type { GameState, Plot, Orientation } from '../model';
import { TEX_H, TEX_W, U, makeCanvas } from '../art';
import { GROUND_KEYS, SPR, SPRITE_KEYS, builtKey, constructKey, groundUrl, isFarm, ruinKey, siteKey, sprUrl } from '../realart';
import { Painter, blob, frameFor, rect, roundRect, seeded, GK } from '../ground';
import { constructionCanvas, constructionStageFor } from '../construction-art';
import { ART_SC, builtScale, ruinScale } from '../art-scale';
import { SpriteOpacityCache, spriteAlphaAt, type SpriteOpacity } from '../sprite-opacity';
import { SiteAnimation } from '../site-animation';
import { FleetAnimation, preloadMachinery } from '../fleet-animation';
import { districtEnvironment } from '../district-environment';
import { paintSurroundings, TERRAIN_BACKDROP } from '../district-terrain';
import { buildingFootprint } from '../building-footprint';
import { buildingShadow } from '../building-shadow';
import { lotLandscape } from '../lot-landscape';
import { ENVIRONMENT_IDS, environmentUrl, groundStamp } from '../environment-art';
import { clearingProgress, clearingStage } from '../clearing-progress';
import { CLEARING_META, CLEARING_SIZE, clearingCanvas, preloadClearing } from '../clearing-art';
import { cityLayout } from '../city-layout';
import { CanvasContacts, nativeTouchIds, releaseStaleTouches } from '../canvas-contacts';
import { gameAudio } from '../game-audio';
import { installGroundTiles } from '../ground-tiles';
import { RoadAnimation } from '../road-animation';
import { roadAt } from '../roads';
import { preloadExpansionArt } from '../expansion-art';
import { DistrictLife, preloadLifeArt } from '../district-life';
import { PIXEL_DENSITY as DENSITY } from '../render-quality';

export interface CityHooks { getState(): GameState; onSelect(plotId: number | null): void; onRoadSelect?(roadId:string):void; onCamera(c: { x: number; y: number; zoom: number }): void; onWorldState?(state: 'loading' | 'ready' | 'error', message?: string): void }

// Projects with a dedicated parcel-free site sprite. Others (flag pole) are drawn by DistrictLife; never fall back to a factory.
const SITE_PROJECTS = new Set(['water', 'power', 'housing', 'park', 'services', 'farm', 'commerce', 'industry']);
const iso = (lx: number, ly: number) => ({ x: (lx - ly) * U, y: (lx + ly) * U / 2 });
const unIso = (wx: number, wy: number) => ({ lx: wy / U + wx / (2 * U), ly: wy / U - wx / (2 * U) });
const DRAG = 9; const ANCHOR_LOT = 380; const SC = ART_SC;

interface PlotView { key: string; img: Phaser.GameObjects.Image; opacity?: SpriteOpacity; bounds?: Phaser.Geom.Rectangle; shadow?: Phaser.GameObjects.Image; skirt?: Phaser.GameObjects.Image; bar?: Phaser.GameObjects.Graphics; coin?: Phaser.GameObjects.Container; yard?: Phaser.GameObjects.Image; yardFront?: Phaser.GameObjects.Image; yardKey?: string }

export class CityScene extends Phaser.Scene {
  private readonly spriteOpacity = new SpriteOpacityCache();
  hooks!: CityHooks;
  private district: string | null = null;
  private worldReady = false;
  private worldQueued = false;
  private worldError = false;
  private groundTextures: string[] = [];
  private roadTextures: string[] = [];
  private roadImages: Phaser.GameObjects.Image[] = [];
  private views = new Map<number, PlotView>();
  private siteAnimation!: SiteAnimation;
  private fleetAnimation!: FleetAnimation;
  private roadAnimation!: RoadAnimation;
  private districtLife!: DistrictLife;
  private sel!: Phaser.GameObjects.Graphics;
  private layer!: Phaser.GameObjects.Container;
  private selected: number | null = null;
  private contacts = new CanvasContacts<Phaser.Input.Pointer>();
  private down: { x: number; y: number; sx: number; sy: number } | null = null;
  private dragged = false; private pinch: { d: number; z: number } | null = null;
  private dust!: Phaser.GameObjects.Particles.ParticleEmitter;
  private roadImg?: Phaser.GameObjects.Image; private roadStage = -1; private maxL = 16; private frame!: ReturnType<typeof frameFor>; private groundKey = ''; terrainInfo?: { style: string; vegetation: string; water: boolean; seed: number };
  private sites = new Map<string, { key: string; img: Phaser.GameObjects.Image; label: Phaser.GameObjects.Text; bar: Phaser.GameObjects.Graphics }>();
  private projectPlots: Plot[] = [];
  private occlusion = new Set<number>();
  private occlusionAt = 0;
  private clearingStages = new Map<number, number>();
  private clearingAt = 0;
  private minimumZoom = 0.35;
  private animationAt = 0;
  private textureSweepAt = 0;

  constructor() { super('city'); }

  create() {
    this.hooks = this.registry.get('hooks');
    this.siteAnimation = new SiteAnimation(this, iso, SC);
    this.fleetAnimation = new FleetAnimation(this);
    this.roadAnimation = new RoadAnimation(this);
    this.districtLife = new DistrictLife(this);
    this.events.once(Phaser.Scenes.Events.SHUTDOWN, () => { this.siteAnimation.destroy(); this.fleetAnimation.destroy(); this.roadAnimation.reset(); this.districtLife.reset(); });
    this.cameras.main.setBackgroundColor(TERRAIN_BACKDROP);
    this.input.addPointer(1);
    if (!this.textures.exists('dot')) { const g = this.make.graphics({}, false); g.fillStyle(0xe8dcc4, 1).fillCircle(4, 4, 4); g.generateTexture('dot', 8, 8); g.destroy(); }
    this.dust = this.add.particles(0, 0, 'dot', { speed: { min: 6, max: 26 }, angle: { min: 200, max: 340 }, scale: { start: 0.9, end: 2.4 }, alpha: { start: 0.55, end: 0 }, lifespan: 1300, tint: [0xcdbb98, 0xb8ae9a, 0xe8dcc4], emitting: false });
    this.dust.setDepth(1e6);
    this.input.on('pointerdown', (p: Phaser.Input.Pointer) => this.pDown(p));
    this.input.on('pointermove', (p: Phaser.Input.Pointer) => this.pMove(p));
    this.input.on('pointerup', (p: Phaser.Input.Pointer) => this.pUp(p));
    this.input.on('pointerupoutside', (p: Phaser.Input.Pointer) => this.pUp(p));
    const reset = () => this.resetGestures();
    const uiDown = (e: PointerEvent) => { if (e.target !== this.game.canvas) reset(); };
    const touchStart = (e: TouchEvent) => {
      const live = nativeTouchIds(e)!;
      releaseStaleTouches(this.input.manager.pointers, live, new Set(Array.from(e.changedTouches, t => t.identifier)));
      this.contacts.live(live);
      if (e.target !== this.game.canvas) reset();
    };
    const visibility = () => { if (document.hidden) reset(); };
    document.addEventListener('pointerdown', uiDown, true);
    document.addEventListener('touchstart', touchStart, { capture: true, passive: true });
    document.addEventListener('visibilitychange', visibility);
    window.addEventListener('blur', reset);
    let viewportWidth = this.cameras.main.width, viewportHeight = this.cameras.main.height;
    const resize = (size: Phaser.Structs.Size) => {
      if (size.width === viewportWidth && size.height === viewportHeight) return;
      const cam = this.cameras.main;
      // CameraManager already resized the camera; scroll still refers to the
      // previous viewport. Recover its ground focus, not the new screen centre.
      const x = cam.scrollX + viewportWidth / 2;
      const y = cam.scrollY + viewportHeight / 2;
      const wasOverview = cam.zoom <= this.minimumZoom * 1.02;
      viewportWidth = size.width; viewportHeight = size.height;
      this.minimumZoom = this.overview().zoom;
      cam.setZoom(wasOverview ? this.minimumZoom : Phaser.Math.Clamp(cam.zoom, this.minimumZoom, MAX_CITY_ZOOM * DENSITY));
      this.syncCameraBounds();
      cam.centerOn(x, y);
      if (wasOverview && this.district) this.centerOverview();
      reset();
      if (this.district && this.worldReady) this.saveCam();
    };
    this.scale.on(Phaser.Scale.Events.RESIZE, resize);
    this.events.once(Phaser.Scenes.Events.SHUTDOWN, () => {
      reset();
      document.removeEventListener('pointerdown', uiDown, true);
      document.removeEventListener('touchstart', touchStart, true);
      document.removeEventListener('visibilitychange', visibility);
      window.removeEventListener('blur', reset);
      this.scale.off(Phaser.Scale.Events.RESIZE, resize);
    });
    this.input.on('wheel', (_p: unknown, _o: unknown, _dx: number, dy: number) => { this.zoomBy(dy > 0 ? 0.9 : 1.1); this.saveCam(); });
    this.sync();
  }

  tex(key: string, w: number, h: number, draw: (c: CanvasRenderingContext2D) => void) {
    if (!this.textures.exists(key)) { const c = makeCanvas(w, h); draw(c.getContext('2d')!); this.textures.addCanvas(key, c); }
    return key;
  }

  private buildWorld(st: GameState) {
    this.resetGestures();
    this.siteAnimation.resetDistrict(); this.fleetAnimation.resetDistrict();
    this.roadAnimation.reset();
    this.districtLife.reset();
    this.occlusion.clear(); this.occlusionAt = 0;
    this.clearingStages.clear(); this.clearingAt = 0;
    const oldTex = [...this.groundTextures, ...this.roadTextures];
    this.children.removeAll(true); this.views.clear(); this.roadImg = undefined;
    for (const k of oldTex) if (this.textures.exists(k)) this.textures.remove(k);
    this.groundTextures = []; this.roadTextures = []; this.roadImages = [];
    this.groundKey = '';
    this.dust = this.add.particles(0, 0, 'dot', { speed: { min: 6, max: 26 }, angle: { min: 200, max: 340 }, scale: { start: 0.9, end: 2.4 }, alpha: { start: 0.55, end: 0 }, lifespan: 1300, tint: [0xcdbb98, 0xb8ae9a, 0xe8dcc4], emitting: false }).setDepth(1e6);
    const d = st.districts.find(x => x.id === this.district)!;
    const max = cityLayout(d.id).extent;
    const c = iso(max / 2, max / 2);
    const size = max * U * 3.2;
    this.maxL = max;
    const g = this.add.graphics().setDepth(-9e5);
    const quad = (x0: number, y0: number, x1: number, y1: number, col: number, a = 1) => { const pts = [iso(x0, y0), iso(x1, y0), iso(x1, y1), iso(x0, y1)]; g.fillStyle(col, a).fillPoints(pts.map(p => new Phaser.Math.Vector2(p.x, p.y)), true); };
    g.fillStyle(0xa39274, 1).fillRect(c.x - size * 1.5, c.y - size * 1.5, size * 3, size * 3);
    this.paintGround(d.id, max);
    this.roadStage = -1; this.roadImg = undefined;
    this.sites.clear();
    this.layer = this.add.container(0, 0);
    this.sel = this.add.graphics().setDepth(5e5);
    const cam = this.cameras.main;
    cam.setBounds(this.frame.ox - 120, this.frame.oy - 120, this.frame.w / GK + 240, this.frame.h / GK + 240);
    this.minimumZoom = this.overview().zoom;
    if (d.camera) {
      cam.setZoom(Phaser.Math.Clamp(d.camera.zoom * DENSITY, this.minimumZoom, MAX_CITY_ZOOM * DENSITY));
      this.syncCameraBounds();
      cam.centerOn(d.camera.x, d.camera.y);
      if (cam.zoom <= this.minimumZoom * 1.02) this.centerOverview();
    }
    else { cam.setZoom(Phaser.Math.Clamp(Math.min(this.scale.width / (max * U * 2.0), this.scale.height / (max * U * 1.1)), 0.45 * DENSITY, DENSITY)); this.syncCameraBounds(); cam.centerOn(c.x, c.y); }
  }

  sync() {
    if (!this.hooks || !this.sys.isActive()) return;
    const st = this.hooks.getState();
    if (!st.currentDistrict) return;
    // Difficulty is a lightweight pre-entry step. Do not rasterize terrain,
    // upload textures or build all plot views behind its interactive dialog.
    if (!st.districts.find(d => d.id === st.currentDistrict)?.difficultyChosen) return;
    if (st.currentDistrict !== this.district || !this.worldReady) {
      if (this.worldQueued || (this.worldError && st.currentDistrict === this.district)) return;
      this.worldQueued = true; this.worldReady = false; this.worldError = false;
      this.hooks.onWorldState?.('loading');
      // Give the loading panel a painted frame before the expensive initial render.
      requestAnimationFrame(() => requestAnimationFrame(() => {
        this.worldQueued = false;
        const current = this.hooks.getState();
        if (!current.currentDistrict || !this.sys.isActive()) { this.hooks.onWorldState?.('ready'); return; }
        // The player may have changed districts since this frame was queued.
        if (!current.districts.find(d => d.id === current.currentDistrict)?.difficultyChosen) {
          this.hooks.onWorldState?.('ready');
          return;
        }
        try {
          this.district = current.currentDistrict; this.selected = null;
          this.buildWorld(current);
          this.syncWorld(current);
          this.worldReady = true;
          this.hooks.onWorldState?.('ready');
        } catch (error) { this.failWorld(error); }
      }));
      return;
    }
    try { this.syncWorld(st); } catch (error) { this.failWorld(error); }
  }

  retryWorld() { this.worldError = false; this.worldReady = false; this.sync(); }

  private failWorld(error: unknown) {
    this.worldReady = false; this.worldError = true;
    console.error('City rendering failed', error);
    this.hooks.onWorldState?.('error', error instanceof Error ? error.message : String(error));
  }

  private syncWorld(st: GameState) {
    const d = st.districts.find(x => x.id === this.district)!;
    const now = Date.now();
    this.syncClearing(st, now, false);
    for (const p of d.plots) this.syncPlot(p, now);
    this.drawSel(d.plots);
    const rp = d.projects.find(p => p.id === 'road');
    const rs = !rp || rp.status === 'idle' ? 0 : rp.status === 'building' ? 1 + Math.min(2, Math.floor(3 * Phaser.Math.Clamp((now - rp.startedAt) / Math.max(1, rp.endsAt - rp.startedAt), 0, 0.999))) : 4;
    if (rs !== this.roadStage) { this.roadStage = rs; this.drawRoads(0); }
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
    const cv = makeCanvas(f.w, f.h); const P = new Painter(cv, f, this.imgs());
    const gi = (k: string) => this.textures.exists(k) ? this.textures.get(k).getSourceImage() as HTMLImageElement : null;
    const info = paintSurroundings(P, districtEnvironment(did), did, max, { x0: LX0, y0: LY0, x1: LX1, y1: LY1 }, (id, lx, ly, w, op, mir) => {
      const img = gi('env:' + id); if (!img) return; const [px, py] = P.pt(lx, ly); P.ctx.save(); P.ctx.setTransform(1, 0, 0, 1, 0, 0);
      groundStamp(P.ctx, img, id, px, py, w * GK * 1.6, op, mir); P.ctx.restore(); }, k => gi('s:' + k));
    this.terrainInfo = info; this.registry.set('terrain', info);
    cv.dataset.terrain = `${info.style}:${info.water ? 'sea' : 'nowater'}`;
    installGroundTiles(this, cv, f, `ground:${did}`, -8.9e5, this.groundTextures, []);
    this.groundKey = this.groundTextures[0];
  }

  /** 0 destroyed, 1 graded dirt, 2 gravel, 3 fresh unmarked asphalt, 4 complete marked roads */
  private drawRoads(stage: number) {
    const max = this.maxL; const f = this.frame; const cv = makeCanvas(f.w, f.h); const P = new Painter(cv, f, this.imgs());
    const r = this.seedRnd(7 + (this.district?.length ?? 0) * 13);
    const layout = cityLayout(this.district!);
    const strip = (c: number, v: boolean, w: number, s0 = -0.4, s1 = max + 0.4) => v ? rect(c - w, s0, c + w, s1) : rect(s0, c - w, s1, c + w);
    const each = (fn: (c: number, v: boolean) => void) => { layout.roadX.forEach(c => fn(c, true)); layout.roadY.forEach(c => fn(c, false)); };
    const at = (c: number, v: boolean, s: number, o: number): [number, number] => v ? [c + o, s] : [s, c + o];
    if (stage === 0) {
      { const lines: [number, boolean][] = []; each((c, v) => lines.push([c, v]));
        // single-path unions: crossings share one surface, no stacked strip alpha
        P.fillMany(lines.map(([c, v]) => strip(c, v, 0.42)), 'sand', 8, 0.18);
        P.fillMany(lines.map(([c, v]) => strip(c, v, 0.34)), 'sand', 8, 0.3);
        P.fillMany(lines.map(([c, v]) => strip(c, v, 0.27)), 'asphalt', 7, 0.96); }
      const nearCross = (v: boolean, s: number) => (v ? layout.roadY : layout.roadX).some(k => Math.abs(k - s) < .4);
      each((c, v) => { for (let s = .12; s < max; s += .42) {
        if (r() > .3 && !nearCross(v, s) && !nearCross(v, s + .26)) for (const edge of [-.29, .29])
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
      each((c, v) => { P.fill(strip(c, v, stage === 4 ? 0.46 : 0.43), 'sand', 8, 0.6); P.fill(strip(c, v, 0.35), 'asphalt', 7, 1, 0, 0, stage === 3 ? 'rgba(10,10,12,.2)' : undefined); });
      if (stage === 4) each((c, v) => {
        for (const o of [-0.34, 0.34]) P.stroke(at(c, v, -0.4, o), at(c, v, max + 0.4, o), 'rgba(205,195,170,.8)', 2.2);
        for (let s = 0.1; s < max; s += 0.55) P.stroke(at(c, v, s, 0), at(c, v, s + 0.24, 0), 'rgba(236,228,205,.85)', 2.2);
      });
    }
    // road edge darkening for crisp shoulders
    if (stage >= 3) each((c, v) => { for (const o of [-0.34, 0.34]) P.stroke(at(c, v, -0.4, o), at(c, v, max + 0.4, o), 'rgba(15,15,15,.35)', 1.2); });
    if (stage >= 3) each((c, v) => { for (let s = 0; s < max; s += 0.22) { const t = r(); const o = (r() < 0.5 ? -1 : 1) * (0.26 + r() * 0.08); const [x, y] = at(c, v, s + r() * 0.2, o);
      if (t < 0.5) P.fill(blob(x, y, 0.03 + r() * 0.06, 0.025 + r() * 0.04, r, 6), t < 0.25 ? 'gravel' : 'earth', 2, 0.85, r() * 3, r() * 3);
      else if (t < 0.62) { const [x2, y2] = at(c, v, s + 0.15 + r() * 0.3, (r() - 0.5) * 0.4); P.stroke([x, y], [x2, y2], 'rgba(18,18,18,.4)', 0.8); } } });
    // Shared ground/curbs wrap a whole block, never separate every adjacent lot.
    for (const block of layout.blocks) {
      const bx = block.x0, by = block.y0;
      const b = roundRect(bx + .12, by + .12, block.x1 - .12, block.y1 - .12, .2);
      if (stage >= 3) { P.shade(roundRect(bx, by, block.x1, block.y1, .25), 'rgba(40,34,28,.45)'); P.fill(b, 'sand', 6, 0.9, bx, by, 'rgba(120,100,76,.22)'); }
      else P.fill(b, r() > 0.5 ? 'gravel' : 'earth', 5, 0.45, bx, by, 'rgba(183,177,164,.1)');
      for (let i = 0; i < 5 * block.plotIds.length; i++) { const a = r() * 4; const x = bx + .3 + r() * (block.x1 - bx - .6), y = by + .3 + r() * (block.y1 - by - .6);
        P.fill(blob(x, y, 0.1 + r() * 0.16, 0.07 + r() * 0.1, r, 8), r() > 0.5 ? 'gravel' : 'earth', 1.6, stage >= 3 ? 0.5 : 0.85, a, a, 'rgba(60,48,36,.2)'); }
    }
    for (const image of this.roadImages) image.destroy();
    for (const key of this.roadTextures) if (this.textures.exists(key)) this.textures.remove(key);
    this.roadImages = []; this.roadTextures = [];
    installGroundTiles(this, cv, f, `roads:${this.district}`, -8e5, this.roadTextures, this.roadImages);
    this.roadImg = this.roadImages[0];
  }

  private siteSpots() { const m = this.maxL; return [[m + 1, 1], [m + 1, 5], [m + 1, 9], [m + 1, 13], [1, m + 1], [5, m + 1], [9, m + 1], [13, m + 1]] as [number, number][]; }
  private syncSites(d: { id: string; projects: { id: string; status: string; startedAt: number; endsAt: number }[] }, now: number) {
    const defs = projects(d.id === 'rashid').filter(p => p.id !== 'road' && p.id !== 'flag' && SITE_PROJECTS.has(p.id)); const spots = this.siteSpots();
    this.projectPlots = [];
    defs.forEach((def, i) => {
      const ps = d.projects.find(p => p.id === def.id); if (!ps) return;
      const f = ps.status === 'building' ? Phaser.Math.Clamp((now - ps.startedAt) / Math.max(1, ps.endsAt - ps.startedAt), 0, 1) : 0;
      const stage = ps.status === 'idle' ? 0 : ps.status === 'building' ? 1 : 2; const q = stage === 1 ? constructionStageFor(f) : 0;
      const key = `ps:${def.id}:${stage}:${q}`; const [lx, ly] = spots[i] ?? [this.maxL + 1, 1 + i * 2]; const c = iso(lx, ly);
      const sk = siteKey(def.id, stage as 0 | 1 | 2, q);
      const projectBuilding: Record<string, string> = { water: 'water_treatment', power: 'work', housing: 'housing_4', park: 'ornamental_trees', services: 'municipality', farm: 'wheat', commerce: 'modern_mall', industry: 'steel' };
      if (stage === 1) this.projectPlots.push({ id: 1000 + i, x: lx - 1, y: ly - 1, status: 'building',
        buildingId: projectBuilding[def.id], orientation: 0, startedAt: ps.startedAt, endsAt: ps.endsAt, incomeAt: 0 });
      const tk = stage === 1 ? this.constructionTex(projectBuilding[def.id], 0, q) : sk ? 's:' + sk : this.skirtTex(i % 3, true);
      let v = this.sites.get(def.id);
      if (!v) { const img = this.place(tk, c.x, c.y, c.y);
        const label = this.add.text(c.x, c.y + 44, '', {
          fontFamily: '"Cairo Black Numbers", "Cairo Black", system-ui, sans-serif', fontStyle: '900', fontSize: '12.1875px',
          color: '#f5f8ff', stroke: '#ffffff', strokeThickness: 0.4,
          shadow: { offsetX: 0, offsetY: 2, color: '#000000', blur: 2, stroke: false, fill: true },
          backgroundColor: 'rgba(9,29,53,0.86)', padding: { x: 8, y: 3 }, rtl: true,
        }).setOrigin(0.5).setDepth(6e5).setAlpha(0.9);
        v = { key, img, label, bar: this.add.graphics().setDepth(6e5) }; this.sites.set(def.id, v); }
      else if (v.key !== key) { this.applyTex(v.img, tk); v.key = key }
      const st = stage === 0 ? 'لم يبدأ' : stage === 1 ? `قيد الإنشاء ${Math.round(f * 100)}٪` : ps.status === 'ready' ? 'جاهز للجمع' : 'مكتمل';
      v.label.setText(`${def.name} · ${st}`);
      v.bar.clear(); if (stage === 1) v.bar.fillStyle(0x2b2320, 0.75).fillRoundedRect(c.x - 40, c.y + 58, 80, 8, 4).fillStyle(0xf2d06b).fillRoundedRect(c.x - 38, c.y + 60, 76 * f, 4, 2);
    });
  }

  private plotKey(p: Plot, now: number) {
    if (p.status === 'rubble') return `r:${p.buildingId}:${p.id % 5}:${this.clearingStages.get(p.id) ?? 0}`;
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
  /** sprites are anchored on the ground-footprint centre (ay from manifest), never the building centre */
  private place(tk: string, x: number, y: number, depth: number) {
    const img = this.add.image(x, y, tk).setDepth(depth).setScale(SC); this.applyTex(img, tk); return img;
  }
  private applyTex(img: Phaser.GameObjects.Image, tk: string) {
    img.setTexture(tk); const m = SPR[tk.startsWith('s:') ? tk.slice(2) : tk];
    img.setOrigin(0.5, m ? m.ay : ANCHOR_LOT / TEX_H);
    const raw = tk.startsWith('s:') ? tk.slice(2) : tk;
    img.setScale(!m ? SC : raw.startsWith('ruin') ? ruinScale(m.h, SC) : /_(front|back|right|left)$/.test(raw) || raw === 'farm_cattle' || raw.startsWith('construction:') ? builtScale(raw.startsWith('construction:') ? builtKey(raw.split(':')[1], Number(raw.split(':')[2]) as Orientation) : raw, SC) : SC);
  }
  private constructionTex(id: string, orientation: Orientation, stage: number) {
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
    if (t === 'r') {
      const ruin = ruinKey(id, Number(a) + plotId), stage = Number(b) || 0;
      if (!stage) return 's:' + ruin;
      const family = (Number(a) + plotId) % 2, key = `clearing:${ruin}:${family}:${stage}`;
      if (!this.textures.exists(key)) {
        const pile = `pile-${family}-${stage <= 2 ? 'dense' : stage === 3 ? 'half' : 'few'}`;
        const canvas = clearingCanvas(this.textures.get('s:' + ruin).getSourceImage() as HTMLImageElement,
          SPR[ruin], this.textures.get('clear:' + pile).getSourceImage() as HTMLImageElement,
          CLEARING_META[pile], stage === 1);
        this.textures.addCanvas(key, canvas); SPR[key] = { ...CLEARING_SIZE };
      }
      return key;
    }
    if (t === 'e') return this.skirtTex(Number(id) % 3, true);
    if (t === 'b') return 's:' + builtKey(id, Number(a) as Orientation);
    return this.constructionTex(id, Number(a) as Orientation, Number(b));
  }

  private syncPlot(p: Plot, now: number) {
    const key = this.plotKey(p, now); let v = this.views.get(p.id);
    const c = iso(p.x + 1, p.y + 1);
    if (!v || v.key !== key) {
      const [t, id, a, b] = key.split(':'); const tk = this.plotTex(t, id, a, b, p.id);
      if (v) { this.applyTex(v.img, tk); v.key = key; v.bar?.destroy(); v.bar = undefined; }
      else { const img = this.place(tk, c.x, c.y, c.y); v = { key, img }; this.views.set(p.id, v); }
      if(t==='b'||t==='c'){
        const finalKey=builtKey(id,Number(a) as 0|1),s=buildingShadow(this,finalKey);
        if(!v.shadow)v.shadow=this.add.image(c.x,c.y,s.key).setDepth(-190000);
        else v.shadow.setTexture(s.key);
        v.shadow.setOrigin(s.originX,s.originY).setScale(builtScale(finalKey));
        v.shadow.setAlpha(t==='b'?1:Math.min(1,.18+Number(b)*.14));
      }
      // All land lies under the common shadow layer. Per-plot depth here
      // would slice a tall building's cast shadow at the next lot boundary.
      v.img.setDepth(t==='e'?-200005:t==='r' && Number(b)>0?-185000:c.y);
      if (t !== 'e') {
        const skirt = this.skirtTex(p.id % 3, false, t !== 'r');
        if (!v.skirt) v.skirt = this.add.image(c.x, c.y, skirt).setOrigin(0.5, ANCHOR_LOT / TEX_H).setDepth(-210000).setScale(SC);
        else v.skirt.setTexture(skirt);
      } else if (v.skirt) { v.skirt.destroy(); v.skirt = undefined; }
      if (v.shadow && (t === 'e' || t === 'r')) { v.shadow.destroy(); v.shadow = undefined; }
      v.img.setFlipX(false).setAlpha(1);
      const fp = t === 'b' || t === 'c' ? buildingFootprint(builtKey(id, Number(a) as Orientation)) : null;
      this.syncYard(v, p, c, fp, t === 'b' ? 2 : t === 'c' && Number(b) >= 5 ? 1 : 0, Number(a) as Orientation);
      const source = v.img.texture.getSourceImage();
      if (!(source instanceof HTMLImageElement || source instanceof HTMLCanvasElement)) throw new Error('Unsupported plot sprite source');
      v.opacity = this.spriteOpacity.prepare(source);
      v.bounds = v.img.getBounds();
      if (t === 'c') v.bar = this.add.graphics().setDepth(6e5);
    }
    // ready-to-collect badge
    const ready = p.status === 'built' && building(p.buildingId).income > 0 && now >= p.incomeAt + buildingIncomePeriod();
    if (ready && !v.coin) {
      const g = this.add.graphics(); g.fillStyle(0x2b2320, 0.25).fillEllipse(0, 22, 26, 8); g.fillStyle(0xc58a1c).fillCircle(0, 0, 13); g.fillStyle(0xf2d06b).fillCircle(0, -1, 10); g.lineStyle(2, 0xa06c10).strokeCircle(0, -1, 6);
      v.coin = this.add.container(c.x, c.y - 120, [g]).setDepth(6e5);
      this.tweens.add({ targets: v.coin, y: c.y - 132, yoyo: true, repeat: -1, duration: 900, ease: 'Sine.inOut' });
    } else if (!ready && v.coin) { v.coin.destroy(); v.coin = undefined; }
  }


  /** compact buildings only: bounded courtyard on the free part of the lot; removed for ruins/empty plots */
  private syncYard(v: PlotView, p: Plot, c: { x: number; y: number }, fp: { factor: number; yard: string } | null, level: 0 | 1 | 2, orient: Orientation) {
    if (!fp || building(p.buildingId).category==='farm' || p.buildingId==='zoo') { v.yard?.destroy(); v.yardFront?.destroy(); v.yard = v.yardFront = undefined; return; }
    const y = lotLandscape(this, fp.yard as 'none'|'garden'|'patio'|'service', fp.factor, orient, level, p.id,p.buildingId);
    if (!v.yard) v.yard = this.add.image(c.x, c.y, y.back).setOrigin(0.5, ANCHOR_LOT / TEX_H).setDepth(-200000).setScale(SC); else v.yard.setTexture(y.back);
    if (y.front) { if (!v.yardFront) v.yardFront = this.add.image(c.x, c.y, y.front).setOrigin(0.5, ANCHOR_LOT / TEX_H).setDepth(c.y + 2).setScale(SC); else v.yardFront.setTexture(y.front); }
    else { v.yardFront?.destroy(); v.yardFront = undefined; }
  }

  private drawSel(plots: Plot[]) {
    this.sel.clear();
    const p = plots.find(x => x.id === this.selected); if (!p) return;
    const pts = [iso(p.x + 0.28, p.y + 0.28), iso(p.x + 1.72, p.y + 0.28), iso(p.x + 1.72, p.y + 1.72), iso(p.x + 0.28, p.y + 1.72)].map(q => new Phaser.Math.Vector2(q.x, q.y));
    this.sel.lineStyle(4, 0xf2d06b, 1).strokePoints(pts, true); this.sel.lineStyle(10, 0xf2d06b, 0.25).strokePoints(pts, true);
  }

  select(id: number | null) { this.selected = id; const st = this.hooks.getState(); const d = st.districts.find(x => x.id === this.district); if (d) this.drawSel(d.plots); }

  private syncClearing(state: GameState, now: number, animate: boolean) {
    const stages = new Map<number, number>();
    for (const job of state.jobs) if (job.districtId === this.district && !job.cleared && now >= job.arrival)
      stages.set(job.plotId, clearingStage(clearingProgress(job, now)));
    const old = this.clearingStages; this.clearingStages = stages;
    if (!animate) return;
    const d = state.districts.find(x => x.id === this.district)!;
    for (const p of d.plots) if ((old.get(p.id) ?? 0) !== (stages.get(p.id) ?? 0)) {
      this.syncPlot(p, now);
      if (p.status === 'rubble' && (stages.get(p.id) ?? 0) > 0) {
        const c = iso(p.x + 1, p.y + 1);
        this.dust.emitParticleAt(c.x - 12, c.y + 14, 14);
      }
    }
  }

  update() {
    const st = this.hooks?.getState(); if (!this.worldReady || !st?.currentDistrict || st.currentDistrict !== this.district) return;
    const frameTime=performance.now();
    if(document.hidden || frameTime<this.animationAt)return;
    this.animationAt=frameTime+1000/30-((frameTime-this.animationAt)%(1000/30));
    if(frameTime>=this.textureSweepAt){this.textureSweepAt=frameTime+5000;this.releaseUnusedStages();}
    const d = st.districts.find(x => x.id === this.district)!; const now = Date.now();
    if (now >= this.clearingAt) { this.clearingAt = now + 160; this.syncClearing(st, now, true); }
    // construction progress bars
    for (const p of d.plots) { const v = this.views.get(p.id); if (p.status === 'building' && v?.bar) { const c = iso(p.x + 1, p.y + 1); const f = Phaser.Math.Clamp((now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt), 0, 1); v.bar.clear().fillStyle(0x2b2320, 0.75).fillRoundedRect(c.x - 40, c.y + 30, 80, 9, 4).fillStyle(0xf2d06b).fillRoundedRect(c.x - 38, c.y + 32, 76 * f, 5, 2);  } }
    this.siteAnimation.update([...d.plots, ...this.projectPlots], now);
    this.fleetAnimation.update(st, now, this.roadStage);
    this.roadAnimation.update(d,now);
    this.districtLife.update(d, now);
    const cam=this.cameras.main;
    if(gameAudio.due())gameAudio.city(st,[...d.plots,...this.projectPlots],[...this.fleetAnimation.soundSources(st),...this.roadAnimation.sounds],
      {x:cam.midPoint.x,y:cam.midPoint.y,zoom:cam.zoom/DENSITY},now);
    this.revealWorkingFleet(now);
  }
  private revealWorkingFleet(now: number) {
    if (now >= this.occlusionAt) {
      this.occlusionAt = now + 160; this.occlusion.clear();
      const points = this.fleetAnimation.focusPoints();
      if (points.length) for (const [id, v] of this.views) {
        const img = v.img, bounds = v.bounds!;
        for (const p of points) {
          if (img.depth <= p.depth + 8 || !bounds.contains(p.x, p.y)) continue;
          const x = Math.floor((p.x - img.x) / img.scaleX + img.displayOriginX);
          const y = Math.floor((p.y - img.y) / img.scaleY + img.displayOriginY);
          if (v.opacity && spriteAlphaAt(v.opacity, x, y) > 90) { this.occlusion.add(id); break; }
        }
      }
    }
    for (const [id, v] of this.views) {
      const alpha = this.occlusion.has(id) ? .28 : 1;
      const old = v.img.alpha, next = Math.abs(alpha - old) < .005 ? alpha : old + (alpha - old) * .16;
      if (next !== old) v.img.setAlpha(next);
    }
  }

  // --- input ---
  private resetGestures() {
    this.contacts.clear(); this.down = null; this.pinch = null; this.dragged = false;
  }
  private beginDrag(p: Phaser.Input.Pointer, alreadyDragged = false) {
    const cam = this.cameras.main;
    this.down = { x: p.x, y: p.y, sx: cam.scrollX, sy: cam.scrollY };
    this.dragged = alreadyDragged;
  }
  private pDown(p: Phaser.Input.Pointer) {
    if (p.event.target !== this.game.canvas) return;
    this.contacts.live(nativeTouchIds(p.event)); this.contacts.start(p);
    const ps = this.contacts.live(nativeTouchIds(p.event));
    if (ps.length >= 2) { this.pinch = { d: Math.max(1, Phaser.Math.Distance.Between(ps[0].x, ps[0].y, ps[1].x, ps[1].y)), z: this.cameras.main.zoom }; this.dragged = true; return; }
    this.pinch = null; this.beginDrag(p);
  }
  private pMove(p: Phaser.Input.Pointer) {
    const cam = this.cameras.main;
    const ps = this.contacts.live(nativeTouchIds(p.event));
    if (!ps.includes(p)) return;
    if (this.pinch && ps.length >= 2) { const d = Phaser.Math.Distance.Between(ps[0].x, ps[0].y, ps[1].x, ps[1].y); this.setCityZoom(this.pinch.z * d / this.pinch.d); return; }
    if (this.pinch) { this.pinch = null; this.beginDrag(p, true); }
    if (!this.down) return;
    if (!this.dragged && Phaser.Math.Distance.Between(p.x, p.y, this.down.x, this.down.y) > DRAG) this.dragged = true;
    if (this.dragged) { cam.scrollX = this.down.sx - (p.x - this.down.x) / cam.zoom; cam.scrollY = this.down.sy - (p.y - this.down.y) / cam.zoom; }
  }
  private pUp(p: Phaser.Input.Pointer) {
    if (!this.contacts.has(p)) return;
    this.contacts.end(p);
    if (this.pinch) {
      const remaining = this.contacts.live(nativeTouchIds(p.event));
      if (remaining.length >= 2) { this.pinch = { d: Math.max(1, Phaser.Math.Distance.Between(remaining[0].x, remaining[0].y, remaining[1].x, remaining[1].y)), z: this.cameras.main.zoom }; return; }
      this.pinch = null; this.down = null; this.saveCam();
      if (remaining.length === 1) this.beginDrag(remaining[0], true);
      return;
    }
    if (p.wasCanceled || p.event.target !== this.game.canvas) { this.resetGestures(); return; }
    if (!this.down) return;
    if (this.dragged) this.saveCam();
    else {
      const w = this.cameras.main.getWorldPoint(p.x, p.y); const { lx, ly } = unIso(w.x, w.y);
      const st = this.hooks.getState(); const d = st.districts.find(x => x.id === this.district);
      const hit = d?.plots.find(q => lx >= q.x + 0.3 && lx <= q.x + 1.7 && ly >= q.y + 0.3 && ly <= q.y + 1.7) ?? null;
      const road=!hit&&d?roadAt(d.id,lx,ly):undefined;
      if(road){this.select(null);this.hooks.onRoadSelect?.(road.id);this.down=null;return;}
      if (!hit && this.selected !== null) { this.down = null; return; }
      this.select(hit?.id ?? null); this.hooks.onSelect(hit?.id ?? null);
    }
    this.down = null;
  }
  private overview() {
    const cam = this.cameras.main;
    const hud = document.querySelector('.hud-top')?.getBoundingClientRect();
    const dock = document.querySelector('.dock')?.getBoundingClientRect();
    const sideDock = !!dock && dock.height > dock.width;
    const overview=districtOverview(this.maxL * U, cam.width/DENSITY, cam.height/DENSITY, {
      left: 14, right: sideDock ? cam.width/DENSITY - dock!.left + 12 : 14,
      top: Math.max(sideDock ? 64 : 112, (hud?.bottom ?? 0) + 12),
      bottom: sideDock ? 14 : dock ? cam.height/DENSITY - dock.top + 12 : 112,
    });
    return {...overview,zoom:overview.zoom*DENSITY};
  }
  private centerOverview() {
    const o = this.overview(), c = iso(this.maxL / 2, this.maxL / 2);
    this.cameras.main.centerOn(c.x + o.offsetX, c.y - 80 + o.offsetY);
  }
  private syncCameraBounds() {
    if (!this.frame) return;
    const cam = this.cameras.main;
    // Phaser clamps centreOn against the terrain skirt. If the viewport is
    // larger than that skirt, this pushes the district toward a screen edge.
    cam.useBounds = cam.width / cam.zoom <= this.frame.w / GK + 240
      && cam.height / cam.zoom <= this.frame.h / GK + 240;
  }
  private setCityZoom(zoom: number) {
    this.minimumZoom = this.overview().zoom;
    this.cameras.main.setZoom(Phaser.Math.Clamp(zoom, this.minimumZoom, MAX_CITY_ZOOM * DENSITY));
    this.syncCameraBounds();
    if (this.cameras.main.zoom <= this.minimumZoom * 1.001) this.centerOverview();
  }
  zoomBy(f: number) { this.setCityZoom(this.cameras.main.zoom * f); }
  saveCam() {
    const cam = this.cameras.main;
    this.hooks.onCamera({ x: Math.round(cam.scrollX + cam.width / 2), y: Math.round(cam.scrollY + cam.height / 2), zoom: Math.round(cam.zoom / DENSITY * 10000) / 10000 });
  }
  private releaseUnusedStages() {
    const used=new Set<string>();
    for(const child of this.children.list)if(child instanceof Phaser.GameObjects.Image)used.add(child.texture.key);
    for(const key of this.textures.getTextureKeys()){
      if((key.startsWith('construction:')||key.startsWith('clearing:'))&&!used.has(key)){
        this.textures.remove(key);delete SPR[key];
      }
    }
  }
  focusPlot(id: number) { const st = this.hooks.getState(); const p = st.districts.find(x => x.id === this.district)?.plots.find(q => q.id === id); if (p) { const c = iso(p.x + 1, p.y + 1); this.cameras.main.pan(c.x, c.y - 40, 500, Phaser.Math.Easing.Sine.InOut); } }
}

export class BootScene extends Phaser.Scene {
  private failedFiles = new Set<string>();
  constructor() { super('boot'); }
  preload() {
    const base = import.meta.env.BASE_URL;
    const bar = document.getElementById('boot-bar'); const msg = document.getElementById('boot-msg');
    this.load.on('progress', (v: number) => { if (bar) bar.style.transform = `scaleX(${v})`; if (msg) msg.textContent = `تحميل رسوم المدينة ${Math.round(v * 100)}٪`; });
    this.load.on('loaderror', (f: Phaser.Loader.File) => { this.failedFiles.add(f.key); const e = document.getElementById('boot-err'); if (e) { e.hidden = false; e.textContent = `تعذر تحميل ملف: ${f.key}`; } });
    this.load.image('sand', base + 'art/sand-ground.jpg');
    for (const k of SPRITE_KEYS) this.load.image('s:' + k, sprUrl(k));
    for (const k of GROUND_KEYS) this.load.image('g:' + k, groundUrl(k));
    for (const id of ENVIRONMENT_IDS) this.load.image('env:' + id, environmentUrl(id));
    preloadMachinery(this);
    preloadClearing(this);
    preloadExpansionArt(this);
    preloadLifeArt(this);
  }
  create() {
    if (this.failedFiles.size) {
      const msg = document.getElementById('boot-msg');
      if (msg) msg.textContent = 'لم يكتمل تحميل الرسوم. إعادة المحاولة لا تحذف تقدمك.';
      const error = document.getElementById('boot-err');
      if (error) {
        error.hidden = false;
        const retry = document.createElement('button');
        retry.className = 'btn'; retry.textContent = 'إعادة المحاولة';
        retry.onclick = () => location.reload(); error.append(document.createElement('br'), retry);
      }
      return;
    }
    this.game.events.emit('booted'); this.scene.start('city');
  }
}
