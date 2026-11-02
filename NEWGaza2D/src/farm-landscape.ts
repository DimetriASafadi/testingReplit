// Farm parcel dressing: tilled soil, crop rows, a service path and small field
// equipment around the existing farm artwork, clipped to the plot diamond.
// Static and cached: key = crop group x orientation x level (bounded: 7*4*3).
// Sun fixed upper-left: every shadow falls to +x/+y on screen for all orientations.
import type Phaser from 'phaser';
import { U, makeCanvas, TEX_H } from './art';
import { seeded } from './ground';
import type { Orientation } from './model';
import { rotateLotPoint } from './orientation';
import { YARD_W, YARD_EDGE } from './lot-geometry';
import { addTrimmedTexture } from './trimmed-texture';

type Pt = [number, number];
const TEX_W = YARD_W, ANCHOR = 380, E = YARD_EDGE - .03;
const pr = (lx: number, ly: number): Pt => [TEX_W / 2 + (lx - ly) * U, ANCHOR + (lx + ly) * U / 2];

type Crop = 'grain' | 'orchard' | 'veg' | 'berry' | 'palm' | 'pasture' | 'vine';
const CROPS: Record<string, Crop> = {
  wheat: 'grain', corn: 'grain', citrus: 'orchard', olive: 'orchard', protective_trees: 'orchard', ornamental_trees: 'orchard',
  vegetables: 'veg', strawberry: 'berry', palms: 'palm', cattle: 'pasture', zoo: 'pasture', grapes: 'vine',
};
const PAL: Record<Crop, { soil: string; row: string; plant: string[] }> = {
  grain: { soil: '#9c7f58', row: 'rgba(70,52,30,.38)', plant: ['#c8ab5c', '#b99a4c', '#d6bd70'] },
  orchard: { soil: '#8f7553', row: 'rgba(66,50,32,.3)', plant: ['#6b7a45', '#5c6d3c', '#7d8a50'] },
  veg: { soil: '#7f6446', row: 'rgba(52,38,24,.42)', plant: ['#6f8a48', '#83a052', '#5d7a3c'] },
  berry: { soil: '#7a5f43', row: 'rgba(46,36,26,.45)', plant: ['#5f8240', '#a8433a', '#6e8f48'] },
  palm: { soil: '#a68b62', row: 'rgba(80,62,40,.28)', plant: ['#7b8550', '#8c9058', '#6a7444'] },
  pasture: { soil: '#9a8a5e', row: 'rgba(80,70,44,.22)', plant: ['#8f9459', '#a3a265', '#7f8650'] },
  vine: { soil: '#8d6f4c', row: 'rgba(60,44,28,.36)', plant: ['#5f7a3a', '#6f8c42', '#54703a'] },
};

export function farmLandscape(scene: Phaser.Scene, buildingId: string, orient: Orientation, level: 0 | 1 | 2) {
  const crop = CROPS[buildingId] ?? 'veg', key = `farmyard:${crop}:${orient}:${level}`;
  if (scene.textures.exists(key)) return key;
  const m = (p: Pt) => rotateLotPoint(p, orient), r = seeded(orient * 31 + crop.length * 7 + level);
  const c = makeCanvas(TEX_W, TEX_H), x = c.getContext('2d')!, P = PAL[crop];
  const trace = (poly: Pt[]) => { x.beginPath(); poly.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath(); };
  const plot: Pt[] = [[-E, -E], [E, -E], [E, E], [-E, E]];
  x.save(); trace(plot); x.clip();
  // soil bed with earth texture if available
  x.fillStyle = P.soil; x.globalAlpha = .9; x.fillRect(0, 0, TEX_W, TEX_H);
  const earth = scene.textures.exists('g:earth') ? scene.textures.get('g:earth').getSourceImage() as HTMLImageElement : null;
  if (earth) { x.globalAlpha = .45; const k = U * 3 / 1024; x.setTransform(k, k / 2, -k, k / 2, TEX_W / 2, ANCHOR); x.fillStyle = x.createPattern(earth, 'repeat')!; x.fillRect(-4000, -4000, 8000, 8000); x.setTransform(1, 0, 0, 1, 0, 0); }
  x.globalAlpha = 1;
  // tillage furrows along the rotated lot x axis (orientation-aware)
  const step = crop === 'orchard' || crop === 'palm' || crop === 'vine' ? .2 : .1;
  for (let t = -E + step / 2; t < E; t += step) {
    const [a0, b0] = pr(...m([-E, t])), [a1, b1] = pr(...m([E, t]));
    x.strokeStyle = P.row; x.lineWidth = 1.3; x.beginPath(); x.moveTo(a0, b0 + .8); x.lineTo(a1, b1 + .8); x.stroke();
    x.strokeStyle = 'rgba(220,196,150,.14)'; x.lineWidth = .8; x.beginPath(); x.moveTo(a0, b0 - .4); x.lineTo(a1, b1 - .4); x.stroke();
  }
  // service path from the entrance edge (+y in lot space) to the centre
  const path: Pt[] = [[.08, -.1], [.24, -.1], [.24, E + .05], [.08, E + .05]];
  trace(path.map(m)); x.fillStyle = 'rgba(206,186,148,.85)'; x.fill(); x.strokeStyle = 'rgba(96,78,52,.35)'; x.lineWidth = 1; x.stroke();
  // crops in the outer band only (the farm art covers the centre)
  if (crop === 'vine') vineTrellis(x, m, level, r);
  else if (level >= 1) {
    const spacing = crop === 'orchard' || crop === 'palm' ? .2 : .1;
    const pts: Pt[] = [];
    for (let u = -E + .06; u < E - .04; u += spacing) for (let v = -E + .06; v < E - .04; v += spacing) {
      if (Math.max(Math.abs(u), Math.abs(v)) < .6) continue;
      if (u > .02 && u < .3 && v > -.15) continue; // keep path clear
      pts.push([u, v]);
    }
    const sp = pts.map(m).sort((p, q) => p[0] + p[1] - q[0] - q[1]);
    for (const p of sp) {
      const [px, py] = pr(...p), j = r();
      const grow = level === 2 ? 1 : .5;
      if (crop === 'orchard' || crop === 'palm') {
        x.fillStyle = 'rgba(40,32,20,.24)'; x.beginPath(); x.ellipse(px + 3.5, py + 1.5, 6 * grow, 2.4 * grow, 0, 0, 7); x.fill();
        x.fillStyle = '#5a4630'; x.fillRect(px - .6, py - 6 * grow, 1.2, 6 * grow);
        x.fillStyle = P.plant[Math.floor(j * 3)];
        if (crop === 'palm') { for (let k = 0; k < 5; k++) { const a = k / 5 * Math.PI * 2; x.beginPath(); x.ellipse(px + Math.cos(a) * 3 * grow, py - 7 * grow + Math.sin(a) * 1.4, 3.6 * grow, 1.1, a, 0, 7); x.fill(); } }
        else { x.beginPath(); x.ellipse(px, py - 8 * grow, 5 * grow, 4 * grow, 0, 0, 7); x.fill(); x.fillStyle = 'rgba(255,240,200,.18)'; x.beginPath(); x.ellipse(px - 1.5, py - 9.5 * grow, 2.2 * grow, 1.6 * grow, 0, 0, 7); x.fill(); }
        if (crop === 'orchard' && buildingId === 'citrus' && level === 2 && j > .5) { x.fillStyle = '#e09a2c'; x.fillRect(px + 1, py - 8, 1.4, 1.4); }
      } else if (crop === 'grain') {
        x.strokeStyle = P.plant[Math.floor(j * 3)]; x.lineWidth = 1;
        for (let k = -1; k <= 1; k++) { x.beginPath(); x.moveTo(px + k * 1.6, py); x.lineTo(px + k * 1.6 + .6, py - 4.5 * grow); x.stroke(); }
      } else if (crop === 'pasture') {
        x.fillStyle = P.plant[Math.floor(j * 3)]; x.globalAlpha = .7; x.beginPath(); x.ellipse(px, py - 1, 3.4, 1.6, 0, 0, 7); x.fill(); x.globalAlpha = 1;
      } else {
        x.fillStyle = 'rgba(40,32,20,.2)'; x.beginPath(); x.ellipse(px + 1.5, py + .8, 2.6 * grow, 1.1, 0, 0, 7); x.fill();
        x.fillStyle = P.plant[Math.floor(j * 2)]; x.beginPath(); x.ellipse(px, py - 1.5 * grow, 2.6 * grow, 1.8 * grow, 0, 0, 7); x.fill();
        if (crop === 'berry' && level === 2 && j > .6) { x.fillStyle = P.plant[1]; x.fillRect(px + .8, py - 1.2, 1.2, 1.2); }
      }
    }
  }
  // drip-irrigation main along the back edge and a field kit near the path (level 2)
  if (level === 2) {
    const [a0, b0] = pr(...m([-E + .05, -E + .05])), [a1, b1] = pr(...m([E - .05, -E + .05]));
    x.strokeStyle = 'rgba(30,30,30,.65)'; x.lineWidth = 1.4; x.beginPath(); x.moveTo(a0, b0); x.lineTo(a1, b1); x.stroke();
    const [tx, ty] = pr(...m([-.78, .78]));
    // water tank on a stand
    x.fillStyle = 'rgba(40,32,20,.28)'; x.beginPath(); x.ellipse(tx + 6, ty + 2, 10, 3.5, 0, 0, 7); x.fill();
    x.fillStyle = '#5c5a52'; x.fillRect(tx - 5, ty - 6, 1.2, 6); x.fillRect(tx + 4, ty - 6, 1.2, 6);
    const g = x.createLinearGradient(tx - 6, 0, tx + 6, 0); g.addColorStop(0, '#4d86a8'); g.addColorStop(1, '#2c5670');
    x.fillStyle = g; x.fillRect(tx - 6, ty - 15, 12, 9); x.fillStyle = '#6aa2c2'; x.beginPath(); x.ellipse(tx, ty - 15, 6, 2.4, 0, 0, 7); x.fill();
    // wheelbarrow + crates near the path mouth
    const [wx, wy] = pr(...m([.45, .82]));
    x.fillStyle = 'rgba(40,32,20,.25)'; x.beginPath(); x.ellipse(wx + 4, wy + 1.5, 8, 2.6, 0, 0, 7); x.fill();
    x.fillStyle = '#9a8158'; x.fillRect(wx - 7, wy - 4, 5, 3); x.fillRect(wx - 7, wy - 7, 5, 3);
    x.fillStyle = '#6f7a6a'; x.beginPath(); x.moveTo(wx, wy - 5); x.lineTo(wx + 8, wy - 5); x.lineTo(wx + 6, wy - 1); x.lineTo(wx + 1, wy - 1); x.closePath(); x.fill();
    x.strokeStyle = '#3d3a34'; x.lineWidth = .9; x.beginPath(); x.arc(wx + 2, wy, 1.6, 0, 7); x.stroke();
  }
  x.restore();
  // soft rim so the parcel blends into the block kerb
  trace(plot); x.strokeStyle = 'rgba(80,64,42,.35)'; x.lineWidth = 1.6; x.stroke();
  addTrimmedTexture(scene, key, c);
  return key;
}

/** Grape trellis rows in the outer band: posts, wire, leaf canopy and (ripe) clusters.
 * Rows run along the rotated lot x axis, so they turn with the vineyard artwork. */
function vineTrellis(x: CanvasRenderingContext2D, m: (p: Pt) => Pt, level: 0 | 1 | 2, r: () => number) {
  const rows: number[] = [];
  for (let v = -E + .1; v < E - .05; v += .2) rows.push(v);
  type Seg = { a: Pt; b: Pt; v: number };
  const segs: Seg[] = [];
  for (const v of rows) {
    const inner = Math.abs(v) < .6;
    // outer rows run full length; inner rows only reach the band beside the artwork
    const spans: [number, number][] = inner ? [[-E + .06, -.62], [.62, E - .06]] : [[-E + .06, E - .06]];
    for (const [u0, u1] of spans) {
      if (v > -.15 && u0 < .3 && u1 > .02) { // keep the service path clear
        if (u0 < .02) segs.push({ a: [u0, v], b: [.0, v], v });
        if (u1 > .3) segs.push({ a: [.3, v], b: [u1, v], v });
      } else segs.push({ a: [u0, v], b: [u1, v], v });
    }
  }
  const depth = (s: Seg) => { const a = m(s.a), b = m(s.b); return a[0] + a[1] + b[0] + b[1]; };
  segs.sort((p, q) => depth(p) - depth(q));
  const h = level === 0 ? 3 : 7;
  for (const s of segs) {
    const [a0, b0] = pr(...m(s.a)), [a1, b1] = pr(...m(s.b));
    // cast shadow of the row, sun upper-left -> shadow to +x/+y
    x.strokeStyle = 'rgba(40,30,18,.22)'; x.lineWidth = level === 2 ? 4.5 : 2.5;
    x.beginPath(); x.moveTo(a0 + 3, b0 + 1.6); x.lineTo(a1 + 3, b1 + 1.6); x.stroke();
    const len = Math.hypot(a1 - a0, b1 - b0), posts = Math.max(2, Math.round(len / 16));
    x.fillStyle = '#5d4630';
    for (let k = 0; k <= posts; k++) { const t = k / posts; x.fillRect(a0 + (a1 - a0) * t - .6, b0 + (b1 - b0) * t - h - 1, 1.3, h + 1); }
    x.strokeStyle = 'rgba(70,64,56,.7)'; x.lineWidth = .6;
    x.beginPath(); x.moveTo(a0, b0 - h); x.lineTo(a1, b1 - h); x.stroke();
    if (level === 0) continue;
    const n = Math.max(3, Math.round(len / 3.2));
    for (let k = 0; k <= n; k++) {
      const t = k / n, px = a0 + (a1 - a0) * t, py = b0 + (b1 - b0) * t - h, j = r();
      x.fillStyle = PAL.vine.plant[Math.floor(j * 3)];
      const s2 = level === 2 ? 3 : 1.9;
      x.beginPath(); x.ellipse(px, py + .4, s2, s2 * .72, 0, 0, 7); x.fill();
      if (level === 2 && j > .62) { x.fillStyle = j > .82 ? '#4a2a4a' : '#5e3358'; x.beginPath(); x.ellipse(px + .6, py + 2.6, .9, 1.4, 0, 0, 7); x.fill(); }
    }
    x.fillStyle = 'rgba(255,244,210,.12)';
    x.beginPath(); x.moveTo(a0, b0 - h - 2); x.lineTo(a1, b1 - h - 2); x.lineTo(a1, b1 - h - .8); x.lineTo(a0, b0 - h - .8); x.closePath(); x.fill();
  }
}
