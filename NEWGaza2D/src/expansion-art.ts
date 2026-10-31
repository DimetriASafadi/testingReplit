// Painted expansion assets: displacement tents and sidewalk/road pavers.
// Sources: art-src/expansion (generated, background removed) -> public/art/expansion.
import type Phaser from 'phaser';
import { PLOT, U, makeCanvas } from './art';

const V = 'exp-20261022';
const url = (f: string) => `${import.meta.env.BASE_URL}art/expansion/${f}?v=${V}`;
export const TENT_COUNT = 4;
/** Ground contact point inside each tent image (fraction of width/height). */
export const TENT_ORIGIN = { x: .5, y: .8 } as const;
/** Paver patch size in px: exactly one plot diamond (2*PLOT*U by PLOT*U). */
export const PAVER_W = Math.round(PLOT * U * 2), PAVER_H = Math.round(PLOT * U);

/** Queue painted assets. Call in preload() (or before scene.load.start()). Idempotent. */
export function preloadExpansionArt(scene: Phaser.Scene) {
  for (let i = 0; i < TENT_COUNT; i++) if (!scene.textures.exists(`exp:tent${i}`)) scene.load.image(`exp:tent${i}`, url(`tent${i}.webp`));
  if (!scene.textures.exists('exp:paver')) scene.load.image('exp:paver', url('paver.jpg'));
  for (const d of PAVER_DIRS) if (!scene.textures.exists(`exp:paver-machine:${d}`)) scene.load.image(`exp:paver-machine:${d}`, url(`paver_${d}.webp`));
  preloadProjectArt(scene);
}

/** Asphalt paving machine (painted, unbranded yellow paver: hopper, crawler tracks, rear screed). */
const PAVER_DIRS = ['se', 'sw', 'nw', 'ne'] as const;
/** Ground contact centre inside every paver machine frame. */
export const PAVER_ORIGIN = { x: .5, y: .74 } as const;
/** Native frame width 126-128 px; scale .6-.7 suits a road lane at default zoom. */
export const PAVER_SCALE = .72;
/**
 * Machine sprite key for a ground-plane travel angle in RADIANS (Math.atan2(dy, dx) in lot/world
 * coordinates, as returned by roads.along). Snapped to the four road axes:
 * +x -> screen down-right, +y -> down-left, -x -> up-left, -y -> up-right. Hopper leads, screed trails.
 */
export function paverTexture(_scene: Phaser.Scene, angleRadians: number): string {
  const q = ((Math.round(angleRadians / (Math.PI / 2)) % 4) + 4) % 4;
  return `exp:paver-machine:${PAVER_DIRS[q]}`;
}

/** Painted tent texture key for any integer index (wraps over 4 variants). */
export function tentTexture(_scene: Phaser.Scene, index: number): string {
  return `exp:tent${((Math.floor(index) % TENT_COUNT) + TENT_COUNT) % TENT_COUNT}`;
}

/**
 * Isometric paver patch, one plot diamond, pattern rotated by `angle` degrees on the ground plane
 * (snapped to 45 degree steps: 8 cached directions). Origin (0.5, 0.5) = diamond centre on the ground.
 * Returns '' if the painted source has not finished loading yet.
 */
export function pavementTexture(scene: Phaser.Scene, angle: number): string {
  const step = ((Math.round(angle / 45) % 8) + 8) % 8, key = `exp:paver:${step}`;
  if (scene.textures.exists(key)) return key;
  if (!scene.textures.exists('exp:paver')) return '';
  const img = scene.textures.get('exp:paver').getSourceImage() as HTMLImageElement;
  const c = makeCanvas(PAVER_W, PAVER_H), x = c.getContext('2d')!;
  x.beginPath(); x.moveTo(PAVER_W / 2, 0); x.lineTo(PAVER_W, PAVER_H / 2); x.lineTo(PAVER_W / 2, PAVER_H); x.lineTo(0, PAVER_H / 2); x.closePath(); x.clip();
  // ground-plane basis (iso 2:1), then rotate the pattern inside the plane
  const k = (U * .9) / img.width, a = step * Math.PI / 4, ca = Math.cos(a) * k, sa = Math.sin(a) * k;
  // world (u,v) -> screen: x = (u - v), y = (u + v)/2
  const u = [ca, sa], v = [-sa, ca];
  x.setTransform(u[0] - u[1], (u[0] + u[1]) / 2, v[0] - v[1], (v[0] + v[1]) / 2, PAVER_W / 2, PAVER_H / 2);
  x.fillStyle = x.createPattern(img, 'repeat')!; x.fillRect(-3000, -3000, 6000, 6000);
  x.setTransform(1, 0, 0, 1, 0, 0);
  // fixed sun from upper left: faint light on the NW edges, curb shade on SE edges
  const g = x.createLinearGradient(0, 0, PAVER_W, PAVER_H); g.addColorStop(0, 'rgba(255,248,230,.10)'); g.addColorStop(1, 'rgba(60,48,32,.12)');
  x.fillStyle = g; x.fillRect(0, 0, PAVER_W, PAVER_H);
  scene.textures.addCanvas(key, c);
  return key;
}

/** Convenience: draw a paver patch at a world-screen ground point. Returns null if not loaded yet. */
export function addPavement(scene: Phaser.Scene, sx: number, sy: number, angle: number, depth: number) {
  const k = pavementTexture(scene, angle); if (!k) return null;
  return scene.add.image(sx, sy, k).setOrigin(.5, .5).setDepth(depth);
}

/** Bespoke completed-project art for projects whose catalog meaning changed:
 * housing = sewer network (pump station, manholes, pipe junction),
 * commerce = security building (civil police station with walled yard).
 * Sources: art-src/expansion/{sewer,security}_site.png. Anchor = parcel diamond centre. */
export const PROJECT_ART: Record<string, { key: string; file: string; ox: number; oy: number }> = {
  housing: { key: 'exp:project:sewer', file: 'sewer_site.webp', ox: .5, oy: .66 },
  commerce: { key: 'exp:project:security', file: 'security_site.webp', ox: .5, oy: .72 },
};
/** Fit a project sprite to one plot diamond (2x2 lot units = 4U px wide). */
export const projectArtScale = (frameWidth: number) => (4 * U * .92) / Math.max(1, frameWidth);
/** Absolute URL for DOM previews (project cards) of the bespoke art. */
export const projectArtUrl = (projectId: string) => PROJECT_ART[projectId] ? url(PROJECT_ART[projectId].file) : '';
export function preloadProjectArt(scene: Phaser.Scene) {
  for (const a of Object.values(PROJECT_ART)) if (!scene.textures.exists(a.key)) scene.load.image(a.key, url(a.file));
}
