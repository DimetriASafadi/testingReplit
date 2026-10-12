// Function-specific parcel dressing: sidewalks, parking, courts, ambulance bays,
// ablution fountains, plus peripheral displacement tents and paver artwork.
// All drawing is in the yard canvas space of lot-landscape (plot centre TEX_W/2, 380).
// Light is fixed from the upper left: shadows always fall to +x/+y on screen.
import type Phaser from 'phaser';
import { TEX_W, U, makeCanvas } from './art';
import { seeded } from './ground';
import type { Orientation } from './model';
import { rotateLotPoint } from './orientation';

export type Pt = [number, number];
export type ParcelFlavor = 'garden' | 'retail' | 'dining' | 'civic' | 'school' | 'medical' | 'worship' | 'housing' | 'industry' | 'leisure';

const ANCHOR = 380;
const pr = (lx: number, ly: number): Pt => [TEX_W / 2 + (lx - ly) * U, ANCHOR + (lx + ly) * U / 2];

const FLAVORS: Record<string, ParcelFlavor> = {
  falafel: 'dining', shawarma: 'dining', western_food: 'dining', cafe: 'dining', luxury_restaurant: 'dining', snack_kiosk: 'dining',
  school: 'school', hospital: 'medical', mosque: 'worship', church: 'worship', sports_club: 'leisure', recreation_park: 'leisure',
  housing_4: 'housing', housing_6: 'housing', housing_complex: 'housing', modern_housing: 'housing', traditional_housing: 'housing', housing_tower: 'housing',
  work: 'industry', equipment_store: 'industry', recycling: 'industry', glass: 'industry', cement: 'industry', steel: 'industry', asphalt: 'industry', food_factory: 'industry', water_treatment: 'industry',
  municipality: 'civic', traditional_mall: 'civic', modern_mall: 'civic',
};
export function parcelFlavor(buildingId: string, yard: 'garden' | 'patio' | 'service' | 'none'): ParcelFlavor {
  return FLAVORS[buildingId] ?? (yard === 'service' ? 'civic' : yard === 'patio' ? 'retail' : 'garden');
}

/** Shared paver pattern (limestone/terrazzo blocks with joint lines), cached as a texture key. */
export function paverPattern(scene: Phaser.Scene | null, tone: 'limestone' | 'grey' = 'limestone', size = 64): HTMLCanvasElement {
  const key = `paver:${tone}:${size}`;
  if (scene?.textures.exists(key)) return scene.textures.get(key).getSourceImage() as HTMLCanvasElement;
  const c = makeCanvas(size, size), x = c.getContext('2d')!, r = seeded(size * 7 + tone.length);
  const base = tone === 'limestone' ? [214, 201, 176] : [176, 172, 160];
  x.fillStyle = `rgb(${base})`; x.fillRect(0, 0, size, size);
  const n = 4, s = size / n;
  for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) {
    const off = j % 2 ? s / 2 : 0, v = (r() - .5) * 18;
    x.fillStyle = `rgb(${base.map(b => Math.round(b + v)).join(',')})`;
    x.fillRect(i * s + off + 1, j * s + 1, s - 2, s - 2);
    if (off) x.fillRect(off - s + 1, j * s + 1, s - 2, s - 2);
  }
  x.globalAlpha = .08; for (let k = 0; k < size * 3; k++) { x.fillStyle = r() > .5 ? '#fff' : '#3b3326'; x.fillRect(r() * size, r() * size, 1, 1); }
  if (scene) scene.textures.addCanvas(key, c);
  return c;
}

/** Fill an isometric lot polygon with projected pavers. */
export function fillPavers(x: CanvasRenderingContext2D, poly: Pt[], tone: 'limestone' | 'grey' = 'limestone', alpha = 1, painted?: CanvasImageSource & { width: number } | null) {
  const pat = painted ?? paverPattern(null, tone);
  x.save(); x.globalAlpha = alpha; x.beginPath();
  poly.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath(); x.clip();
  const k = U * (painted ? .9 : .5) / pat.width; x.setTransform(k, k / 2, -k, k / 2, TEX_W / 2, ANCHOR);
  x.fillStyle = x.createPattern(pat, 'repeat')!; x.fillRect(-2000, -2000, 4000, 4000); x.restore();
}

/** Sidewalk ring with raised curb along the parcel edge. Flat: never blocks door/vehicle access. */
export function drawSidewalk(x: CanvasRenderingContext2D, edge: number, width = .13, painted?: HTMLImageElement | null) {
  const E = edge, I = edge - width;
  const band: Pt[][] = [
    [[-E, -E], [E, -E], [I, -I], [-I, -I]], [[E, -E], [E, E], [I, I], [I, -I]],
    [[E, E], [-E, E], [-I, I], [I, I]], [[-E, E], [-E, -E], [-I, -I], [-I, I]],
  ];
  for (const b of band) fillPavers(x, b, 'limestone', .92, painted);
  const line = (poly: Pt[], col: string, w: number, dy = 0) => {
    x.save(); x.strokeStyle = col; x.lineWidth = w; x.lineJoin = 'round'; x.beginPath();
    poly.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b + dy) : x.moveTo(a, b + dy); }); x.closePath(); x.stroke(); x.restore();
  };
  const outer: Pt[] = [[-E, -E], [E, -E], [E, E], [-E, E]], inner: Pt[] = [[-I, -I], [I, -I], [I, I], [-I, I]];
  line(outer, 'rgba(70,62,50,.45)', 2.4, 1.2); line(outer, 'rgba(232,224,206,.95)', 1.6);
  line(inner, 'rgba(96,84,64,.3)', 1);
}

const shadow = (x: CanvasRenderingContext2D, px: number, py: number, rw: number, rh: number, a = .2) => {
  x.save(); x.fillStyle = `rgba(40,34,26,${a})`; x.beginPath(); x.ellipse(px + rw * .25, py + rh * .3, rw, rh, 0, 0, Math.PI * 2); x.fill(); x.restore();
};

/** small isometric car at plot point, palette from local streets (white taxis, faded colours) */
export function car(x: CanvasRenderingContext2D, p: Pt, along: 'x' | 'y', col: string) {
  const [px, py] = pr(...p), L = .17, W = .085;
  const c: Pt[] = along === 'x' ? [[p[0] - L, p[1] - W], [p[0] + L, p[1] - W], [p[0] + L, p[1] + W], [p[0] - L, p[1] + W]]
    : [[p[0] - W, p[1] - L], [p[0] + W, p[1] - L], [p[0] + W, p[1] + L], [p[0] - W, p[1] + L]];
  shadow(x, px, py + 2, 13, 5, .28);
  const poly = (q: Pt[], dy: number, fill: string) => { x.beginPath(); q.forEach((v, i) => { const [a, b] = pr(...v); i ? x.lineTo(a, b - dy) : x.moveTo(a, b - dy); }); x.closePath(); x.fillStyle = fill; x.fill(); };
  poly(c, 0, 'rgba(30,28,24,.55)'); poly(c, 4, col);
  const roof = c.map(([a, b]) => [p[0] + (a - p[0]) * .55, p[1] + (b - p[1]) * .6] as Pt); poly(roof, 7, 'rgba(60,72,80,.75)');
  poly(roof.map(([a, b]) => [p[0] + (a - p[0]) * .8, p[1] + (b - p[1]) * .8] as Pt), 8, col);
}

/** Full-lot housing: kerbside parallel parking in the free band between the
 * building footprint (~.77) and the sidewalk, on the two screen-front edges only
 * (never under the building), skipping the rotated entrance. */
export function drawKerbParking(x: CanvasRenderingContext2D, entry: Pt, n: number) {
  const r = seeded(n * 53 + 11), cols = ['#e8e3d6', '#c9c2b0', '#8a2f28', '#3f5560', '#d7cfb8', '#5d6a4e', '#b9b4a6'];
  const B = .87, spots: [Pt, 'x' | 'y'][] = [];
  for (const t of [-.62, -.26, .1, .46]) { spots.push([[t, B], 'x']); spots.push([[B, t], 'y']); }
  for (const [p, along] of spots) {
    if (Math.hypot(p[0] - entry[0], p[1] - entry[1]) < .5 || r() < .3) continue;
    car(x, p, along, cols[Math.floor(r() * cols.length)]);
  }
}

/** Painted parking stalls with cars, aligned to a lot edge strip. */
export function drawParking(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, from: Pt, to: Pt, cars: number, n: number) {
  const r = seeded(n * 31 + cars), cols = ['#e8e3d6', '#c9c2b0', '#8a2f28', '#3f5560', '#d7cfb8', '#5d6a4e'];
  const strip: Pt[] = [[from[0], from[1] - .12], [to[0], to[1] - .12], [to[0], to[1] + .12], [from[0], from[1] + .12]].map(p => m(p as Pt));
  x.save(); x.beginPath(); strip.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath();
  x.fillStyle = 'rgba(92,90,84,.55)'; x.fill(); x.restore();
  const k = 4, dx = (to[0] - from[0]) / k;
  for (let i = 0; i <= k; i++) {
    const [a, b] = pr(...m([from[0] + dx * i, from[1] - .12])), [c2, d] = pr(...m([from[0] + dx * i, from[1] + .12]));
    x.save(); x.strokeStyle = 'rgba(240,234,214,.8)'; x.lineWidth = 1; x.beginPath(); x.moveTo(a, b); x.lineTo(c2, d); x.stroke(); x.restore();
  }
  const mm = m([1, 0]), along: 'x' | 'y' = Math.abs(mm[0]) > .5 ? 'y' : 'x';
  for (let i = 0; i < Math.min(cars, k); i++) if (r() > .2) car(x, m([from[0] + dx * (i + .5), from[1]]), along, cols[Math.floor(r() * cols.length)]);
}

/** School basketball/play court with painted lines. */
export function drawCourt(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, c: Pt, s: number, tint = 'rgba(176,96,72,.85)') {
  const q: Pt[] = ([[c[0] - s, c[1] - s * .6], [c[0] + s, c[1] - s * .6], [c[0] + s, c[1] + s * .6], [c[0] - s, c[1] + s * .6]] as Pt[]).map(m);
  const path = (pts: Pt[]) => { x.beginPath(); pts.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath(); };
  x.save(); path(q); x.fillStyle = tint; x.fill(); x.strokeStyle = 'rgba(245,240,226,.9)'; x.lineWidth = 1.1; x.stroke();
  const [a0, b0] = pr(...m([c[0], c[1] - s * .6])), [a1, b1] = pr(...m([c[0], c[1] + s * .6])); x.beginPath(); x.moveTo(a0, b0); x.lineTo(a1, b1); x.stroke();
  const [cx, cy] = pr(...m(c)); x.beginPath(); x.ellipse(cx, cy, 5, 2.5, 0, 0, Math.PI * 2); x.stroke();
  for (const e of [-1, 1]) { const [hx, hy] = pr(...m([c[0] + e * s * .92, c[1]])); x.fillStyle = '#4a4a44'; x.fillRect(hx - .6, hy - 11, 1.2, 11); x.fillStyle = '#f2ede0'; x.fillRect(hx - 3, hy - 14, 6, 4); }
  x.restore();
}

/** Ambulance drop-off: hatched bay plus a parked white ambulance. */
export function drawAmbulanceBay(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, c: Pt) {
  const q: Pt[] = ([[c[0] - .2, c[1] - .14], [c[0] + .2, c[1] - .14], [c[0] + .2, c[1] + .14], [c[0] - .2, c[1] + .14]] as Pt[]).map(m);
  x.save(); x.beginPath(); q.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath();
  x.fillStyle = 'rgba(96,94,88,.6)'; x.fill(); x.clip(); x.strokeStyle = 'rgba(232,196,72,.75)'; x.lineWidth = 1.4;
  const [cx, cy] = pr(...m(c)); for (let i = -30; i <= 30; i += 5) { x.beginPath(); x.moveTo(cx + i - 10, cy + 10); x.lineTo(cx + i + 10, cy - 10); x.stroke(); }
  x.restore();
  const mm = m([1, 0]); car(x, m(c), Math.abs(mm[0]) > .5 ? 'y' : 'x', '#f1efe8');
  const [ax, ay] = pr(...m(c)); x.fillStyle = '#b8322a'; x.fillRect(ax - 2.5, ay - 11, 5, 1.6); x.fillRect(ax - .8, ay - 12.6, 1.6, 5);
}

/** Ablution fountain: octagonal stone basin with water, for mosque/church courtyards. */
export function drawFountain(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, c: Pt) {
  const [px, py] = pr(...m(c)); shadow(x, px, py + 1, 12, 5);
  x.save(); x.fillStyle = '#cdbb98'; x.beginPath(); x.ellipse(px, py - 2, 11, 5.5, 0, 0, Math.PI * 2); x.fill();
  x.fillStyle = '#a99674'; x.fillRect(px - 11, py - 2, 22, 3); x.beginPath(); x.ellipse(px, py + 1, 11, 5.5, 0, 0, Math.PI); x.fill();
  x.fillStyle = '#6f9aa0'; x.beginPath(); x.ellipse(px, py - 2.5, 8, 4, 0, 0, Math.PI * 2); x.fill();
  x.fillStyle = 'rgba(255,255,255,.5)'; x.beginPath(); x.ellipse(px - 2, py - 3.5, 3, 1.2, 0, 0, Math.PI * 2); x.fill();
  x.fillStyle = '#d9c9a7'; x.fillRect(px - 1, py - 8, 2, 6); x.restore();
}

/** Benches with a shade sail, for leisure plots. */
export function drawShadeSeating(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, c: Pt) {
  const [px, py] = pr(...m(c)); shadow(x, px + 4, py + 2, 15, 5);
  x.save(); x.fillStyle = '#7d6b52'; x.fillRect(px - 12, py - 4, 9, 2.5); x.fillRect(px + 3, py - 4, 9, 2.5);
  x.fillStyle = '#5b5143'; for (const d of [-11, -5, 4, 10]) x.fillRect(px + d, py - 2, 1, 3);
  x.fillStyle = '#5c5a50'; x.fillRect(px - .6, py - 16, 1.2, 14);
  x.fillStyle = 'rgba(234,226,206,.92)'; x.beginPath(); x.moveTo(px - 15, py - 14); x.lineTo(px + 1, py - 20); x.lineTo(px + 15, py - 13); x.lineTo(px - 1, py - 9); x.closePath(); x.fill();
  x.strokeStyle = 'rgba(120,108,84,.6)'; x.lineWidth = .8; x.stroke(); x.restore();
}

/** Industrial service yard: stacked pallets and a skip. */
export function drawServiceYard(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, c: Pt) {
  const [px, py] = pr(...m(c)); shadow(x, px, py, 16, 5);
  x.save();
  for (let k = 0; k < 3; k++) { x.fillStyle = k % 2 ? '#9a8158' : '#b0956a'; x.fillRect(px - 14, py - 4 - k * 3, 11, 3); }
  x.fillStyle = '#5f6b5a'; x.beginPath(); x.moveTo(px + 1, py - 9); x.lineTo(px + 15, py - 9); x.lineTo(px + 13, py); x.lineTo(px + 3, py); x.closePath(); x.fill();
  x.fillStyle = '#4b5548'; x.fillRect(px + 1, py - 10, 14, 2); x.restore();
}

/** Draw the accessory set for a flavor in the free parcel corners (lot coordinates before orientation). */
export function dressParcel(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, flavor: ParcelFlavor, edge: number, n: number) {
  const I = edge - .2;
  switch (flavor) {
    case 'school': drawCourt(x, m, [-I + .22, I - .12], .2); break;
    case 'medical': drawAmbulanceBay(x, m, [I - .22, I - .08]); drawParking(x, m, [-I, -I + .02], [-I + .55, -I + .02], 3, n); break;
    case 'worship': drawFountain(x, m, [-I + .12, I - .1]); break;
    case 'housing': case 'civic': drawParking(x, m, [-I, I - .02], [-I + .6, I - .02], 4, n); break;
    case 'industry': drawServiceYard(x, m, [-I + .15, I - .1]); break;
    case 'leisure': drawShadeSeating(x, m, [-I + .15, I - .1]); break;
    default: break;
  }
}

/** Respectful displacement tent sprite (canvas texture key). Variants differ in canvas tone and size. */
export function drawnTentFallback(scene: Phaser.Scene, variant: number): string {
  const v = ((variant % 4) + 4) % 4, key = `tent:${v}`;
  if (scene.textures.exists(key)) return key;
  const W = 72, H = 52, c = makeCanvas(W, H), x = c.getContext('2d')!;
  const tones = [['#e9e4d6', '#c9c1ab', '#a79f88'], ['#dcd6c2', '#b9b19a', '#958d76'], ['#d8dfe0', '#b3bcbd', '#8d9798'], ['#e4d9c0', '#c3b391', '#9e8e6d']][v];
  const cx = W / 2, by = 40, hw = 22 + v * 1.5, ht = 22 + (v % 2) * 3;
  x.fillStyle = 'rgba(40,34,26,.24)'; x.beginPath(); x.ellipse(cx + 7, by + 2, hw + 4, 8, 0, 0, Math.PI * 2); x.fill();
  // ridge tent: left face lit (sun upper left), right face shaded, front gable
  x.fillStyle = tones[0]; x.beginPath(); x.moveTo(cx - hw, by - 2); x.lineTo(cx - 6, by - ht - 4); x.lineTo(cx + 2, by - ht); x.lineTo(cx - hw + 10, by + 4); x.closePath(); x.fill();
  x.fillStyle = tones[1]; x.beginPath(); x.moveTo(cx + 2, by - ht); x.lineTo(cx - 6, by - ht - 4); x.lineTo(cx + hw - 4, by - 8); x.lineTo(cx + hw + 2, by - 3); x.closePath(); x.fill();
  x.fillStyle = tones[2]; x.beginPath(); x.moveTo(cx - hw + 10, by + 4); x.lineTo(cx + 2, by - ht); x.lineTo(cx + hw + 2, by - 3); x.closePath(); x.fill();
  x.fillStyle = 'rgba(60,52,40,.55)'; x.beginPath(); x.moveTo(cx + 2, by - ht + 6); x.lineTo(cx - 3, by + 1); x.lineTo(cx + 8, by - 1); x.closePath(); x.fill();
  x.strokeStyle = 'rgba(90,80,62,.6)'; x.lineWidth = .8; x.beginPath(); x.moveTo(cx - 6, by - ht - 4); x.lineTo(cx + 2, by - ht); x.stroke();
  x.strokeStyle = 'rgba(110,100,80,.35)'; for (const d of [-12, -4]) { x.beginPath(); x.moveTo(cx + d, by - 4); x.lineTo(cx + d + 4, by - ht + 2); x.stroke(); }
  // guy ropes and a water jerrycan
  x.strokeStyle = 'rgba(80,70,55,.45)'; x.beginPath(); x.moveTo(cx - hw, by - 2); x.lineTo(cx - hw - 6, by + 5); x.moveTo(cx + hw + 2, by - 3); x.lineTo(cx + hw + 8, by + 3); x.stroke();
  x.fillStyle = v % 2 ? '#3f6f86' : '#c7a245'; x.fillRect(cx + hw - 6, by + 1, 5, 6); x.fillStyle = 'rgba(0,0,0,.2)'; x.fillRect(cx + hw - 3, by + 1, 2, 6);
  scene.textures.addCanvas(key, c);
  return key;
}
/** Tent anchor in its texture: origin (0.5, by/H). */
export const DRAWN_TENT_ORIGIN = { x: .5, y: 40 / 52 } as const;

/** Deterministic tent spots in a ring of world points; caller filters plots/roads. */
export function tentScatter(seed: number, count: number, minX: number, maxX: number, minY: number, maxY: number) {
  const r = seeded(seed * 131 + 17), out: { x: number; y: number; variant: number; fadeAt: number }[] = [];
  for (let i = 0; i < count; i++) out.push({ x: minX + r() * (maxX - minX), y: minY + r() * (maxY - minY), variant: Math.floor(r() * 4), fadeAt: .15 + r() * .8 });
  return out;
}
/** Opacity for a tent given district progress 0..1: fades out gradually, gone at completion. */
export function tentAlpha(fadeAt: number, progress: number) {
  if (progress >= 1) return 0;
  return Math.max(0, Math.min(1, (fadeAt - progress) / .12 + 1)) * (1 - progress * .25);
}

export function orientMapper(orient: Orientation) { return (p: Pt): Pt => rotateLotPoint(p, orient); }
