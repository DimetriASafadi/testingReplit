// Artistic district surroundings (not cadastral). Painted once into the ground canvas.
import { Painter, blob, rect, seeded } from './ground';
import type { DistrictEnvironment } from './district-environment';
import { SPR } from './realart';
import { ART_SC, ruinScale } from './art-scale';

type Pt = [number, number];
export const TERRAIN_BACKDROP = '#a39274';
export interface TerrainInfo { style: string; vegetation: string; water: boolean; seed: number }
type Stamp = (id: number, lx: number, ly: number, w: number, op: number, mir: boolean) => void;
type Spr = (key: string) => CanvasImageSource | null;

/** inside lots, roads and project-site margins: nothing may be painted here */
const inPlay = (x: number, y: number, max: number, pad = 0) => x > -1.2 - pad && y > -1.2 - pad && x < max + 3.4 + pad && y < max + 3.4 + pad;
const isFront = (x: number, y: number, max: number) => x > max + 3.4 || y > max + 3.4;

/** sprite stamped into the ground at its footprint centre; feather=true blends its ground patch away */
function sprite(P: Painter, get: Spr, key: string, lx: number, ly: number, wc: number, op: number, mir: boolean, feather: boolean) {
  const img = get(key), m = SPR[key]; if (!img || !m) return;
  const hc = wc * m.h / m.w; const w = Math.ceil(wc), h = Math.ceil(hc);
  const t = document.createElement('canvas'); t.width = w; t.height = h; const c = t.getContext('2d')!;
  c.drawImage(img, 0, 0, w, h);
  if (feather) {
    c.globalCompositeOperation = 'destination-in'; c.save(); c.translate(w / 2, h * .52); c.scale(1, h / w);
    const g = c.createRadialGradient(0, 0, w * .12, 0, 0, w * .5); g.addColorStop(0, 'rgba(0,0,0,1)'); g.addColorStop(.55, 'rgba(0,0,0,.95)'); g.addColorStop(1, 'rgba(0,0,0,0)');
    c.fillStyle = g; c.fillRect(-w, -w, w * 2, w * 2); c.restore();
  }
  const [px, py] = P.pt(lx, ly); const o = P.ctx; o.save(); o.globalAlpha = op; o.translate(px, py); o.scale(mir ? -1 : 1, 1);
  o.drawImage(t, -w / 2, -h * m.ay); o.restore();
}

/** soft organic dry-soil / crop patch with weathered edge and wobbling rows */
function patch(P: Painter, cx: number, cy: number, rx: number, ry: number, r: () => number, tone: string, rows: boolean) {
  for (let i = 0; i < 3; i++) {
    const k = 1.18 - i * .17;
    P.fill(blob(cx, cy, rx * k, ry * k, r, 16), 'earth', 7 + r() * 3, i === 0 ? .5 : .75, r() * 5, r() * 5, i === 2 ? tone : tone.replace(/[\d.]+\)$/, '.12)'));
  }
  if (!rows) return;
  const alongX = r() > .5, sp = .2 + r() * .07;
  const ext = alongX ? ry : rx;
  for (let t = -ext * .8; t < ext * .8; t += sp) {
    const half = (alongX ? rx : ry) * Math.sqrt(Math.max(0, 1 - (t / ext) ** 2)) * .85;
    let s = -half; while (s < half - .3) {
      const l = .6 + r() * 1.6, e = Math.min(half, s + l), w1 = Math.sin(s * 1.7 + t * 3) * .04;
      const a: Pt = alongX ? [cx + s, cy + t + w1] : [cx + t + w1, cy + s], b: Pt = alongX ? [cx + e, cy + t + w1] : [cx + t + w1, cy + e];
      P.stroke(a, b, r() > .3 ? 'rgba(62,70,34,.34)' : 'rgba(80,60,36,.26)', 1.2);
      P.stroke([a[0] + (alongX ? 0 : .05), a[1] + (alongX ? .05 : 0)], [b[0] + (alongX ? 0 : .05), b[1] + (alongX ? .05 : 0)], 'rgba(190,170,120,.18)', .8);
      s = e + .15 + r() * .4;
    }
  }
}

/** wandering track, never ruler-straight */
function track(P: Painter, from: Pt, to: Pt, r: () => number, w = 3) {
  const n = Math.max(4, Math.round(Math.hypot(to[0] - from[0], to[1] - from[1]) / .6)); const ph = r() * 6, am = .25 + r() * .35;
  let prev: Pt = from;
  for (let i = 1; i <= n; i++) {
    const t = i / n, dx = to[0] - from[0], dy = to[1] - from[1], l = Math.hypot(dx, dy) || 1;
    const o = Math.sin(t * 5 + ph) * am * Math.sin(Math.PI * t);
    const p: Pt = [from[0] + dx * t - dy / l * o, from[1] + dy * t + dx / l * o];
    const fade = 1 - t * .55;
    P.stroke(prev, p, `rgba(196,176,136,${.5 * fade})`, w * (1.4 - t * .5)); P.stroke(prev, p, `rgba(128,106,74,${.22 * fade})`, w * .5);
    prev = p;
  }
}

export function paintSurroundings(P: Painter, env: Readonly<DistrictEnvironment>, id: string, max: number,
  b: { x0: number; y0: number; x1: number; y1: number }, stamp: Stamp, get: Spr): TerrainInfo {
  const r = seeded(env.seed * 31 + id.length);
  const coastal = env.style === 'coastal', urban = env.style === 'urban';
  const shore = (ly: number) => -3.6 + 0.32 * Math.sin(ly * 0.7) + 0.18 * Math.sin(ly * 1.9 + 1);
  const wp = (a: number, c: number): Pt[] => { const p: Pt[] = []; for (let ly = b.y0; ly <= b.y1; ly += 0.5) p.push([shore(ly) + a, ly]); for (let ly = b.y1; ly >= b.y0; ly -= 0.5) p.push([shore(ly) + c, ly]); return p; };
  const landX0 = coastal ? -3.2 : b.x0;
  P.fill(rect(-400, -400, 400, 400), 'earth', 9, 1, 0, 0);
  if (coastal) {
    P.fill(rect(-400, -400, 0, 400), 'water', 11, 1, 0, 0); P.fill(rect(-400, -400, 0, 400), 'water', 17.3, .35, 2.2, 5.1);
    for (let i = 0; i < 5; i++) P.shade(wp(-12, -1 - i * 1.6), 'rgba(6,40,58,.1)');
    for (let i = 0; i < 3; i++) P.shade(wp(-1.5 + i * .3, .1), 'rgba(150,215,205,.12)');
    P.fill(wp(0, 400), 'earth', 9, 1, 0, 0);
  }
  P.fill(rect(landX0, b.y0, b.x1, b.y1), 'earth', 13.7, .45, 3.1, 5.7);
  const W = b.x1 - landX0, H = b.y1 - b.y0;
  for (let i = 0; i < 90; i++) { const lx = landX0 + r() * W, ly = b.y0 + r() * H;
    P.fill(blob(lx, ly, 1 + r() * 3, .8 + r() * 2.4, r), r() > .5 ? 'gravel' : 'earth', 6 + r() * 3, .25 + r() * .3, r() * 4, r() * 4); }
  for (let i = 0; i < 40; i++) P.shade(blob(landX0 + r() * W, b.y0 + r() * H, 2 + r() * 4, 1.5 + r() * 3, r), `rgba(${r() > .5 ? '120,100,72' : '176,160,128'},.09)`);
  if (coastal) {
    for (let i = 0; i < 6; i++) P.fill(wp(-.2, 2.9 - i * .45), 'sand', 7.3, .34, 1.7, .4);
    P.fill(wp(-.3, .45), 'sand', 7.3, .75, 0, 0, 'rgba(70,56,40,.3)');
    for (let ly = b.y0; ly < b.y1; ly += .5) { const a: Pt = [shore(ly) - .2, ly], c: Pt = [shore(ly + .5) - .2, ly + .5]; P.stroke(a, c, 'rgba(244,250,246,.75)', 1.4); }
  }
  // land-use field: smooth noise decides crops / grove / fallow / built-up
  const s = env.seed, nz = (x: number, y: number) => .5 + .22 * Math.sin(x * .41 + s) + .2 * Math.sin(y * .33 + s * 1.7) + .16 * Math.sin((x + y) * .17 + s * .3) + .12 * Math.sin((x - y) * .23 + s * 2.1);
  const sc = ART_SC * .5; // same world scale as in-city sprites, in ground-canvas px
  const groveKeys = env.vegetation === 'olive' ? ['olive_front', 'olive_back', 'farm_orchard'] : env.vegetation === 'citrus' ? ['citrus_front', 'citrus_back', 'farm_orchard']
    : env.vegetation === 'palms' ? ['palms_front', 'palms_back', 'trees_front'] : ['olive_front', 'citrus_front', 'trees_front', 'orn_front', 'farm_orchard'];
  const cropKeys = ['farm_field', 'wheat_front', 'veg_front', 'straw_front', 'corn_back', 'wheat_back'];
  const queue: { d: number; fn: () => void }[] = [];
  const q = (x: number, y: number, fn: () => void) => queue.push({ d: x + y, fn });

  // urban fabric: side streets continue the road grid, blocks aligned with it
  if (urban) {
    const L = 14;
    for (let c = 0; c <= max; c += 2) for (const [vx, y0, y1] of [[1, max + .4, max + .4 + 5 + (c * 7 % 6)], [1, -.4 - 5 - (c * 5 % 6), -.4], [0, max + .4, max + .4 + 5 + (c * 3 % 6)], [0, -.4 - 4 - (c * 11 % 7), -.4]] as [number, number, number][]) {
      const pa: Pt = vx ? [c, y0] : [y0, c], pb: Pt = vx ? [c, y1] : [y1, c];
      track(P, pa, pb, r, 4); P.stroke(pa, pb, 'rgba(46,46,48,.5)', 3);
    }
    for (let bx = -L; bx < max + L; bx += 2) for (let by = -L; by < max + L; by += 2) {
      if (inPlay(bx + 1, by + 1, max, .6)) continue;
      const dist = Math.max(-1.2 - bx, -1.2 - by, bx - max - 3.4, by - max - 3.4);
      if (dist > 11 || r() < .1 + dist * .04 + (nz(bx, by) > .6 ? env.farmland * .5 : 0)) continue;
      if (coastal && bx < shore(by) + 4.5) continue;
      const cx = bx + 1 + (r() - .5) * .3, cy = by + 1 + (r() - .5) * .3, front = isFront(cx, cy, max);
      P.fill(blob(cx, cy, 1, 1, r, 12), r() > .5 ? 'gravel' : 'sand', 6, .55 - dist * .02, bx, by, 'rgba(110,98,80,.2)');
      stamp(Math.floor(r() * 4), cx, cy, 120, .5, r() > .5);
      if (r() > .3 && dist < 8) {
        const k = front ? `ruin${Math.floor(r() * 4)}` : `ruin${Math.floor(r() * 12)}`, m = SPR[k];
        if (m) q(cx, cy, () => sprite(P, get, k, cx, cy, m.w * ruinScale(m.h, ART_SC) * .5, Math.max(.35, .88 - dist * .05), r() > .5, false));
      }
    }
  }

  // organic patches over a jittered scatter (denser close to the play area, thinning outward)
  const step = 2.1;
  for (let x = landX0 + 1; x < b.x1 - 1; x += step) for (let y = b.y0 + 1; y < b.y1 - 1; y += step) {
    const cx = x + (r() - .5) * step * .9, cy = y + (r() - .5) * step * .9;
    if (inPlay(cx, cy, max, 1.1)) continue;
    if (coastal && cx < shore(cy) + 4.2) continue;
    const dist = Math.max(-1.2 - cx, -1.2 - cy, cx - max - 3.4, cy - max - 3.4), n = nz(cx, cy);
    const farm = urban ? env.farmland * 1.2 : .45 + env.farmland * .55;
    const front = isFront(cx, cy, max);
    if (n > 1 - farm * .75 || (urban && n > .8 && r() > .5)) {
      const u = r();
      if (u < .42) { // orchard: overlapping sprite cluster on a soil patch
        const kn = 3 + Math.floor(r() * 3); patch(P, cx, cy, 1.6, 1.2, r, 'rgba(110,100,60,.3)', false);
        for (let i = 0; i < kn; i++) { const ox = cx + (r() - .5) * 2.2, oy = cy + (r() - .5) * 1.7; if (inPlay(ox, oy, max, .5)) continue;
          const k = groveKeys[Math.floor(r() * groveKeys.length)], w = SPR[k].w * sc * (.55 + r() * .25), mir = r() > .5;
          q(ox, oy, () => sprite(P, get, k, ox, oy, w, .95, mir, true)); }
      } else if (u < .86) { // crop field: procedural rows or painted field sprite
        patch(P, cx, cy, 1.4 + r() * 1.5, 1.1 + r() * 1.1, r, ['rgba(120,128,60,.36)', 'rgba(150,120,70,.34)', 'rgba(96,110,52,.38)'][Math.floor(r() * 3)], true);
        if (r() > .55) { const k = cropKeys[Math.floor(r() * cropKeys.length)], w = SPR[k].w * sc * .8; q(cx, cy, () => sprite(P, get, k, cx, cy, w, .8, r() > .5, true)); }
      } else if (!urban || r() > .5) { // farmstead
        const k = r() > .5 ? 'smallhouse_front' : 'house_back', m = SPR[k];
        patch(P, cx, cy, 1.3, 1, r, 'rgba(150,130,96,.3)', false);
        if (!front) q(cx, cy, () => sprite(P, get, k, cx, cy, m.w * sc * .7, .92, r() > .5, false));
        else stamp(10, cx, cy, 110, .55, false);
      }
    } else if (!urban) { // fallow dry ground
      P.fill(blob(cx, cy, 1.4 + r(), 1 + r(), r, 12), 'sand', 6, .3, r() * 3, r() * 3);
      stamp(10, cx, cy, 90 + r() * 40, .45, r() > .5);
    } else if (dist > 2.5 && r() > .55) stamp(10, cx, cy, 110, .5, r() > .5);
    // roadside growth
    if (dist < 9 && r() > .4) { const tx = cx + (r() - .5) * 2.2, ty = cy + (r() - .5) * 2.2; if (!inPlay(tx, ty, max, .4)) { const k = r() > .5 ? 'trees_front' : groveKeys[0]; const w = SPR[k].w * sc * (.22 + r() * .14); q(tx, ty, () => sprite(P, get, k, tx, ty, w, .9, r() > .5, true)); } }
  }
  // wandering lanes, fewer and shorter, joining the perimeter roads
  if (!urban) for (let k = 2; k <= max; k += 6) {
    track(P, [max + .4, k], [max + 6 + r() * 7, k + (r() - .5) * 5], r, 3); track(P, [k, max + .4], [k + (r() - .5) * 5, max + 6 + r() * 7], r, 3);
    track(P, [k, -.4], [k + (r() - .5) * 5, -5 - r() * 8], r, 3); if (!coastal) track(P, [-.4, k], [-5 - r() * 8, k + (r() - .5) * 5], r, 3);
  }
  queue.sort((a, c) => a.d - c.d).forEach(o => o.fn());

  // irregular rim fade: broken edge, no straight board perimeter
  const c = P.ctx; c.save(); c.globalCompositeOperation = 'destination-out';
  const N = 10;
  for (let i = 0; i < N; i++) { const a = ((i + 1) / N) ** 2 * .2, d = 1.6 * (N - i);
    const edges = [rect(b.x0, b.y0, b.x1, b.y0 + d), rect(b.x0, b.y1 - d, b.x1, b.y1), rect(b.x1 - d, b.y0, b.x1, b.y1)]; if (!coastal) edges.push(rect(b.x0, b.y0, b.x0 + d, b.y1));
    edges.forEach(e => P.shade(e, `rgba(0,0,0,${a})`)); }
  for (let i = 0; i < 160; i++) { const side = Math.floor(r() * (coastal ? 3 : 4)); const t = r(), d = r() * 5;
    const bx = side === 0 ? b.x0 + t * (b.x1 - b.x0) : side === 1 ? b.x0 + t * (b.x1 - b.x0) : side === 2 ? b.x1 - d : b.x0 + d;
    const by = side === 0 ? b.y0 + d : side === 1 ? b.y1 - d : b.y0 + t * (b.y1 - b.y0);
    P.shade(blob(bx, by, 1.5 + r() * 2.5, 1.5 + r() * 2.5, r, 10), `rgba(0,0,0,${.15 + r() * .25})`); }
  c.restore();
  return { style: env.style, vegetation: env.vegetation, water: coastal, seed: env.seed };
}
