// Procedural semi-realistic isometric art. Everything is projected from 3D geometry,
// so FRONT/BACK views and vehicle headings are genuinely different drawings.
import { building } from './catalog';
import type { BuildingDef, EquipmentKind } from './model';

export const U = 64; // pixels per logical unit
export const PLOT = 1.4; // plot footprint in logical units
export const TEX_W = 260, TEX_H = 470, ANCHOR_Y = 380; // texture anchor = plot centre on ground

type Ctx = CanvasRenderingContext2D;
type V3 = [number, number, number];
const P = (x: number, y: number, z: number): [number, number] => [TEX_W / 2 + (x - y) * U, ANCHOR_Y + (x + y) * U / 2 - z * U];

function rng(seed: string) { let h = 2166136261; for (const c of seed) { h ^= c.charCodeAt(0); h = Math.imul(h, 16777619); } return () => { h ^= h << 13; h ^= h >>> 17; h ^= h << 5; return ((h >>> 0) % 10000) / 10000; }; }
function shade(hex: string, f: number) { const n = parseInt(hex.slice(1), 16); const c = [n >> 16, (n >> 8) & 255, n & 255].map(v => Math.max(0, Math.min(255, Math.round(f > 1 ? v + (255 - v) * (f - 1) : v * f)))); return `rgb(${c[0]},${c[1]},${c[2]})`; }
function poly(ctx: Ctx, pts: [number, number][], fill: string, stroke?: string) { ctx.beginPath(); pts.forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.closePath(); ctx.fillStyle = fill; ctx.fill(); if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = 1; ctx.stroke(); } }

interface Box { x0: number; y0: number; x1: number; y1: number; z0: number; z1: number; c: string; top?: string }
function box(ctx: Ctx, b: Box) {
  const { x0, y0, x1, y1, z0, z1, c } = b;
  poly(ctx, [P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1)], shade(c, 0.86), 'rgba(40,28,20,.25)'); // +y left face
  poly(ctx, [P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1)], shade(c, 0.68), 'rgba(40,28,20,.25)'); // +x right face
  poly(ctx, [P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)], b.top ?? shade(c, 1.08), 'rgba(40,28,20,.2)');
}
// quad on a face: face 'L' = +y face, 'R' = +x face. u along face 0..1, z absolute
function faceQuad(ctx: Ctx, b: Box, face: 'L' | 'R', u0: number, u1: number, z0: number, z1: number, fill: string) {
  const pt = (u: number, z: number) => face === 'L' ? P(b.x0 + u * (b.x1 - b.x0), b.y1 + 0.002, z) : P(b.x1 + 0.002, b.y0 + u * (b.y1 - b.y0), z);
  poly(ctx, [pt(u0, z0), pt(u1, z0), pt(u1, z1), pt(u0, z1)], fill);
}
function ground(ctx: Ctx, x0: number, y0: number, x1: number, y1: number, fill: string) { poly(ctx, [P(x0, y0, 0), P(x1, y0, 0), P(x1, y1, 0), P(x0, y1, 0)], fill); }

const FLOOR = 0.2;
const PAL: Record<string, { wall: string[]; accent: string }> = {
  equipment: { wall: ['#c9b48f', '#b7a98d'], accent: '#c58a1c' },
  industry: { wall: ['#a9a59a', '#9aa3a1', '#b3a48a'], accent: '#2f6f73' },
  housing: { wall: ['#eadcc0', '#e3c9a3', '#d9c7a8', '#efe1c9', '#d8b98f'], accent: '#a8322d' },
  farm: { wall: ['#c6a46a'], accent: '#6b7a3a' },
  commerce: { wall: ['#e8d6b4', '#d9c3a0'], accent: '#16747a' },
  leisure: { wall: ['#f0dcc0', '#e7cfae'], accent: '#c0532b' },
};

interface Shape { main: Box; extras: Box[]; def: BuildingDef; r: () => number; wall: string; accent: string }
function shapeOf(id: string): Shape {
  const def = building(id); const r = rng(id); const p = PAL[def.category];
  const wall = p.wall[Math.floor(r() * p.wall.length)];
  const h = def.category === 'farm' ? 0.12 : Math.max(0.32, def.floors * FLOOR + 0.08);
  let w = 0.5 + r() * 0.12, d = 0.42 + r() * 0.12;
  if (def.category === 'industry') { w = 0.6; d = 0.55; }
  if (def.floors >= 6) { w = 0.42; d = 0.38; }
  if (id === 'housing_complex') { w = 0.36; d = 0.5; }
  if (def.category === 'commerce' && def.floors === 3) { w = 0.6; d = 0.5; }
  const main: Box = { x0: -w, y0: -d, x1: w, y1: d * 0.85, z0: 0, z1: h, c: wall };
  const extras: Box[] = [];
  if (id === 'housing_complex') extras.push({ x0: 0.06, y0: -0.6, x1: 0.62, y1: -0.1, z0: 0, z1: h * 0.7, c: shade(wall, 0.95) as string });
  if (id === 'villa' || id === 'tourist_villa') extras.push({ x0: w - 0.05, y0: -d, x1: w + 0.16, y1: 0, z0: 0, z1: h * 0.55, c: wall });
  return { main, extras, def, r, wall, accent: p.accent };
}

function windows(ctx: Ctx, b: Box, face: 'L' | 'R', floors: number, cols: number, glass: string, skipDoor: boolean, r: () => number) {
  for (let f = 0; f < floors; f++) for (let c = 0; c < cols; c++) {
    if (skipDoor && f === 0 && c === Math.floor(cols / 2)) continue;
    const u0 = (c + 0.25) / cols, u1 = (c + 0.75) / cols, z0 = b.z0 + f * FLOOR + 0.06, z1 = z0 + 0.09;
    faceQuad(ctx, b, face, u0 - 0.02, u1 + 0.02, z0 - 0.015, z1 + 0.015, 'rgba(70,50,35,.35)');
    faceQuad(ctx, b, face, u0, u1, z0, z1, r() > 0.75 ? '#f3d58a' : glass);
  }
}

function rooftop(ctx: Ctx, s: Shape, back: boolean) {
  const b = s.main; const z = b.z1;
  box(ctx, { x0: b.x0, y0: b.y0, x1: b.x1, y1: b.y0 + 0.03, z0: z, z1: z + 0.05, c: s.wall }); // parapet
  box(ctx, { x0: b.x0, y0: b.y0, x1: b.x0 + 0.03, y1: b.y1, z0: z, z1: z + 0.05, c: s.wall });
  const tx = back ? b.x0 + 0.12 : b.x1 - 0.2, ty = b.y0 + 0.1;
  box(ctx, { x0: tx, y0: ty, x1: tx + 0.1, y1: ty + 0.1, z0: z, z1: z + 0.13, c: '#2b2b2b' }); // black water tank (typical)
  if (back) box(ctx, { x0: b.x1 - 0.22, y0: b.y1 - 0.2, x1: b.x1 - 0.06, y1: b.y1 - 0.06, z0: z, z1: z + 0.02, c: '#3a6d8c', top: '#6fa3c0' }); // solar panel
  box(ctx, { x0: b.x1 - 0.1 - (back ? 0.3 : 0), y0: b.y1 - 0.12, x1: b.x1 - 0.02 - (back ? 0.3 : 0), y1: b.y1 - 0.04, z0: z, z1: z + 0.07, c: '#d8d2c4' });
}

function decorate(ctx: Ctx, s: Shape, back: boolean, full = true) {
  const { main: b, def, r } = s; const glass = '#5b8a95';
  const floors = Math.max(1, def.floors);
  const doorFace: 'L' = 'L';
  if (def.category === 'farm') return;
  // windows on both visible faces
  windows(ctx, b, 'L', floors, Math.max(2, Math.round((b.x1 - b.x0) * 5)), glass, !back, r);
  windows(ctx, b, 'R', floors, Math.max(2, Math.round((b.y1 - b.y0) * 5)), glass, false, r);
  if (!back) {
    // FRONT: door + steps + entry canopy facing the player
    faceQuad(ctx, b, doorFace, 0.42, 0.58, 0, 0.16, '#5a3a24');
    faceQuad(ctx, b, doorFace, 0.44, 0.56, 0.02, 0.15, '#7b5032');
    faceQuad(ctx, b, doorFace, 0.38, 0.62, 0.16, 0.19, s.accent);
    const cx = (b.x0 + b.x1) / 2; ground(ctx, cx - 0.09, b.y1, cx + 0.09, 0.7, '#cdbb98');
    if (def.category === 'commerce' || def.category === 'leisure') {
      // striped awning + sign on the street face
      for (let i = 0; i < 6; i++) faceQuad(ctx, b, 'L', i / 6, (i + 1) / 6, 0.2, 0.26, i % 2 ? '#f1e6d2' : s.accent);
      faceQuad(ctx, b, 'L', 0.15, 0.85, 0.28, 0.36, '#21363a');
      faceQuad(ctx, b, 'L', 0.2, 0.8, 0.3, 0.34, shade(s.accent, 1.4));
    }
    // balconies on the front face
    if (floors >= 2 && def.category === 'housing') for (let f = 1; f < floors; f++) faceQuad(ctx, b, 'L', 0.1, 0.9, f * FLOOR + 0.02, f * FLOOR + 0.05, 'rgba(255,248,235,.75)');
  } else {
    // BACK: no door visible. Rear service side: AC units, pipes, laundry, rear stairs; path wraps to hidden door
    for (let f = 0; f < floors; f++) faceQuad(ctx, b, 'L', 0.12, 0.2, f * FLOOR + 0.03, f * FLOOR + 0.08, '#dcd7cc');
    faceQuad(ctx, b, 'R', 0.9, 0.93, 0, b.z1, '#7d7468');
    faceQuad(ctx, b, 'L', 0.85, 0.88, 0, b.z1, '#7d7468');
    if (floors >= 2) for (let f = 1; f < floors; f++) { faceQuad(ctx, b, 'L', 0.55, 0.8, f * FLOOR + 0.1, f * FLOOR + 0.11, '#6f6a62'); faceQuad(ctx, b, 'L', 0.58, 0.62, f * FLOOR + 0.06, f * FLOOR + 0.1, '#c0532b'); faceQuad(ctx, b, 'L', 0.66, 0.7, f * FLOOR + 0.06, f * FLOOR + 0.1, '#2f6f73'); }
    ground(ctx, b.x0 - 0.12, b.y0 - 0.12, b.x0 - 0.02, b.y1 + 0.05, '#cdbb98'); // path leading round to the back door
    ground(ctx, b.x0 - 0.12, b.y0 - 0.12, b.x1, b.y0 - 0.02, '#cdbb98');
  }
  if (full) rooftop(ctx, s, back);
}

function industryProps(ctx: Ctx, s: Shape, back: boolean, prog = 1) {
  const b = s.main; const id = s.def.id; const sx = back ? b.x0 + 0.05 : b.x1 - 0.18;
  const tall = (id === 'cement' || id === 'steel' || id === 'glass' || id === 'asphalt' || id === 'recycling') ? 1 : 0;
  if (tall) box(ctx, { x0: sx, y0: b.y0 + 0.05, x1: sx + 0.1, y1: b.y0 + 0.15, z0: b.z1, z1: b.z1 + 0.7 * prog, c: id === 'steel' ? '#7a5446' : '#b9b2a4' });
  if (id === 'cement' || id === 'asphalt' || id === 'food_factory') for (let i = 0; i < 2; i++) box(ctx, { x0: b.x1 + 0.02, y0: -0.4 + i * 0.25, x1: b.x1 + 0.17, y1: -0.25 + i * 0.25, z0: 0, z1: 0.6 * prog, c: id === 'asphalt' ? '#3c3a38' : '#d6d0c2' });
  if (id === 'water_treatment') { poly(ctx, [P(-0.6, 0.3, 0.02), P(-0.2, 0.3, 0.02), P(-0.2, 0.62, 0.02), P(-0.6, 0.62, 0.02)], '#4aa0a8', '#2f6f73'); }
  if (id === 'glass') box(ctx, { x0: b.x0 + 0.1, y0: b.y0 + 0.1, x1: b.x1 - 0.3, y1: b.y1 - 0.1, z0: b.z1, z1: b.z1 + 0.04, c: '#8fc2c8' });
  if (id === 'recycling' || id === 'equipment_store' || id === 'work') { faceQuad(ctx, b, back ? 'R' : 'L', 0.3, 0.7, 0, Math.min(b.z1 - 0.05, 0.28), back ? '#6a6458' : '#8a7a5c'); if (!back) for (let i = 0; i < 5; i++) faceQuad(ctx, b, 'L', 0.3, 0.7, 0.04 + i * 0.05, 0.05 + i * 0.05, '#6a5c44'); }
  if (id === 'recycling') { for (let i = 0; i < 3; i++) poly(ctx, [P(0.55, -0.5 + i * 0.25, 0), P(0.68, -0.6 + i * 0.25, 0), P(0.65, -0.45 + i * 0.25, 0.15)], ['#8b8b84', '#a39b8a', '#6f6a62'][i]); }
}

function farm(ctx: Ctx, s: Shape, back: boolean, prog: number) {
  const id = s.def.id; const r = rng(id + back);
  const soil = id === 'cattle' ? '#b9a26a' : '#8a6a44';
  ground(ctx, -0.66, -0.66, 0.66, 0.66, soil);
  // furrows run perpendicular depending on orientation
  for (let i = 0; i < 9; i++) { const t = -0.6 + i * 0.15; if (!back) ground(ctx, t, -0.62, t + 0.05, 0.62, shade(soil, 0.8)); else ground(ctx, -0.62, t, 0.62, t + 0.05, shade(soil, 0.8)); }
  const plant: Record<string, [string, number, number]> = { wheat: ['#d9b44a', 0.08, 0], citrus: ['#3f7a3a', 0.2, 1], olive: ['#7c8b5a', 0.18, 1], vegetables: ['#4f8f3a', 0.06, 0], strawberry: ['#3d7a3a', 0.05, 2], palms: ['#4f7a3a', 0.4, 3], corn: ['#a8b443', 0.16, 0], protective_trees: ['#2f5f36', 0.34, 1], ornamental_trees: ['#6a9a4a', 0.22, 4], cattle: ['#f2ead8', 0.08, 5] };
  const [col, hgt, kind] = plant[id] ?? ['#4f8f3a', 0.1, 0];
  const n = kind === 0 ? 9 : 5;
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    if (r() > prog + 0.05) continue;
    const x = -0.55 + (i + 0.5) * (1.1 / n), y = -0.55 + (j + 0.5) * (1.1 / n); const [px, py] = P(x, y, 0);
    const hh = hgt * U * Math.max(0.2, prog);
    ctx.fillStyle = 'rgba(40,30,15,.25)'; ctx.beginPath(); ctx.ellipse(px + 3, py + 1, kind === 0 ? 3 : 8, 3, 0, 0, Math.PI * 2); ctx.fill();
    if (kind === 0) { ctx.strokeStyle = col; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px - 2, py - hh); ctx.moveTo(px, py); ctx.lineTo(px + 2, py - hh); ctx.stroke(); }
    else if (kind === 3) { ctx.strokeStyle = '#7a5a36'; ctx.lineWidth = 3; ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + 2, py - hh); ctx.stroke(); ctx.fillStyle = col; for (let k = 0; k < 6; k++) { ctx.beginPath(); ctx.ellipse(px + 2 + Math.cos(k) * 7, py - hh + Math.sin(k) * 3, 9, 3, k, 0, Math.PI * 2); ctx.fill(); } }
    else if (kind === 5) { if ((i + j) % 2) continue; ctx.fillStyle = r() > 0.5 ? '#f2ead8' : '#5a3f2c'; ctx.beginPath(); ctx.ellipse(px, py - 5, 9, 5, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#2b2320'; ctx.fillRect(px + 6, py - 9, 4, 4); }
    else { ctx.fillStyle = '#6a4a2a'; ctx.fillRect(px - 1, py - hh * 0.5, 2, hh * 0.5); ctx.fillStyle = shade(col, 0.8); ctx.beginPath(); ctx.arc(px, py - hh * 0.6, kind === 2 ? 4 : 9, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = col; ctx.beginPath(); ctx.arc(px - 2, py - hh * 0.65, kind === 2 ? 3 : 7, 0, Math.PI * 2); ctx.fill();
      if (prog >= 1 && (id === 'citrus' || id === 'strawberry' || id === 'ornamental_trees')) { ctx.fillStyle = id === 'citrus' ? '#f2a23a' : id === 'strawberry' ? '#c8322d' : '#e889a8'; ctx.fillRect(px + 2, py - hh * 0.7, 2, 2); ctx.fillRect(px - 4, py - hh * 0.55, 2, 2); } }
  }
  // farm shed — door faces player in front, faces away in back
  const shed: Box = { x0: 0.38, y0: -0.62, x1: 0.62, y1: -0.38, z0: 0, z1: 0.18 * Math.min(1, prog * 1.5), c: id === 'cattle' ? '#a8322d' : '#c6a46a' };
  box(ctx, shed); if (!back && prog >= 1) faceQuad(ctx, shed, 'L', 0.35, 0.65, 0, 0.12, '#4a3020');
  // fence
  ctx.strokeStyle = '#7a5a36'; ctx.lineWidth = 1.5; ctx.beginPath(); [P(-0.68, 0.68, 0.06), P(0.68, 0.68, 0.06), P(0.68, -0.68, 0.06)].forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.stroke();
}

function leisure(ctx: Ctx, s: Shape, back: boolean) {
  if (s.def.id === 'zoo') {
    ground(ctx, -0.66, -0.66, 0.66, 0.66, '#9fb46a');
    poly(ctx, [P(-0.5, 0.1, 0.01), P(-0.1, 0.1, 0.01), P(-0.1, 0.5, 0.01), P(-0.5, 0.5, 0.01)], '#4aa0a8');
    const animals: [number, number, string, number][] = [[0.3, 0.3, '#d9b44a', 10], [0.4, -0.1, '#8a8a84', 14], [-0.3, -0.35, '#c58a1c', 8]];
    animals.forEach(([x, y, c, sz]) => { const [px, py] = P(x, y, 0); ctx.fillStyle = c; ctx.beginPath(); ctx.ellipse(px, py - sz * 0.6, sz, sz * 0.55, 0, 0, Math.PI * 2); ctx.fill(); ctx.fillRect(px + sz * 0.6, py - sz * 1.4, 4, sz); });
    const gate: Box = { x0: -0.15, y0: 0.55, x1: 0.15, y1: 0.62, z0: 0, z1: 0.25, c: '#c0532b' }; box(ctx, gate);
    if (!back) faceQuad(ctx, gate, 'L', 0.25, 0.75, 0, 0.18, '#3a2a1c');
    return true;
  }
  return false;
}

function umbrellas(ctx: Ctx, s: Shape, back: boolean) {
  if (s.def.category !== 'leisure') return;
  const y = back ? -0.6 : 0.5; const cols = ['#c0532b', '#16747a', '#e7c46a'];
  for (let i = 0; i < 3; i++) { const [px, py] = P(-0.4 + i * 0.3, y, 0); ctx.fillStyle = '#5a4030'; ctx.fillRect(px - 1, py - 20, 2, 20); ctx.fillStyle = cols[i]; ctx.beginPath(); ctx.ellipse(px, py - 20, 12, 5, 0, Math.PI, 0); ctx.fill(); ctx.fillStyle = '#e8dcc4'; ctx.fillRect(px - 5, py - 6, 10, 3); }
}

function lot(ctx: Ctx, tone: string) { ground(ctx, -PLOT / 2, -PLOT / 2, PLOT / 2, PLOT / 2, tone); }

export function drawBuilt(ctx: Ctx, id: string, orient: 0 | 1) {
  const s = shapeOf(id); const back = orient === 1;
  lot(ctx, s.def.category === 'farm' ? '#8a6a44' : '#d6c4a0');
  if (s.def.category === 'farm') { farm(ctx, s, back, 1); return; }
  if (leisure(ctx, s, back)) return;
  // shadow
  poly(ctx, [P(s.main.x0, s.main.y0, 0), P(s.main.x1 + 0.25, s.main.y0, 0), P(s.main.x1 + 0.25, s.main.y1 + 0.1, 0), P(s.main.x0, s.main.y1 + 0.1, 0)], 'rgba(60,40,20,.22)');
  if (id === 'tourist_villa' || id === 'villa') poly(ctx, [P(-0.6, 0.45, 0.01), P(-0.25, 0.45, 0.01), P(-0.25, 0.62, 0.01), P(-0.6, 0.62, 0.01)], '#5fb3bb');
  for (const e of s.extras) { box(ctx, e); windows(ctx, e, 'L', Math.max(1, Math.round((e.z1) / FLOOR)), 2, '#5b8a95', false, s.r); }
  box(ctx, s.main);
  decorate(ctx, s, back);
  if (s.def.category === 'industry' || s.def.category === 'equipment') industryProps(ctx, s, back);
  if (id === 'municipality') { const b = s.main; const [px, py] = P((b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2, b.z1); ctx.fillStyle = '#2f6f73'; ctx.beginPath(); ctx.ellipse(px, py - 4, 18, 14, 0, Math.PI, 0); ctx.fill(); ctx.fillStyle = '#a8322d'; ctx.fillRect(px - 1, py - 34, 2, 16); ctx.fillRect(px, py - 34, 10, 6); }
  umbrellas(ctx, s, back);
}

/** Construction: individualized, derived from the same geometry & orientation. stage 0..2 */
export function drawConstruction(ctx: Ctx, id: string, orient: 0 | 1, stage: number) {
  const s = shapeOf(id); const back = orient === 1; const b = s.main;
  lot(ctx, '#bfa77c');
  // site fence on the street side
  for (let i = 0; i < 12; i++) { const t = -0.68 + i * 0.124; box(ctx, { x0: t, y0: 0.64, x1: t + 0.1, y1: 0.67, z0: 0, z1: 0.09, c: i % 2 ? '#e8dcc4' : '#c58a1c' }); }
  if (s.def.category === 'farm') { farm(ctx, s, back, [0.15, 0.45, 0.75][stage]); return; }
  // foundation slab
  box(ctx, { x0: b.x0 - 0.04, y0: b.y0 - 0.04, x1: b.x1 + 0.04, y1: b.y1 + 0.04, z0: 0, z1: 0.04, c: '#a9a59a' });
  const frac = [0.08, 0.5, 0.82][stage]; const hz = Math.max(0.06, b.z1 * frac);
  if (stage >= 1) {
    // shell walls (lower part filled), upper part bare frame
    const shell = { ...b, z1: stage === 2 ? hz : hz * 0.45, c: stage === 2 ? s.wall : '#b9b2a4' };
    box(ctx, shell);
    if (stage === 2) { windows(ctx, shell, 'L', Math.max(1, Math.floor(shell.z1 / FLOOR)), Math.max(2, Math.round((b.x1 - b.x0) * 5)), '#3a3530', !back, s.r); if (!back) faceQuad(ctx, shell, 'L', 0.42, 0.58, 0, Math.min(0.16, shell.z1), '#3a2a1c'); else faceQuad(ctx, shell, 'L', 0.12, 0.2, 0.03, 0.08, '#dcd7cc'); }
  }
  // columns (rebar frame) rising to hz
  const cols: [number, number][] = [[b.x0, b.y0], [b.x1, b.y0], [b.x0, b.y1], [b.x1, b.y1], [(b.x0 + b.x1) / 2, b.y1], [b.x1, (b.y0 + b.y1) / 2]];
  for (const [x, y] of cols) box(ctx, { x0: x - 0.02, y0: y - 0.02, x1: x + 0.02, y1: y + 0.02, z0: 0, z1: hz + 0.06, c: '#9b958a' });
  for (let f = 1; f * FLOOR <= hz; f++) { box(ctx, { x0: b.x0, y0: b.y1 - 0.015, x1: b.x1, y1: b.y1 + 0.015, z0: f * FLOOR - 0.02, z1: f * FLOOR, c: '#9b958a' }); box(ctx, { x0: b.x1 - 0.015, y0: b.y0, x1: b.x1 + 0.015, y1: b.y1, z0: f * FLOOR - 0.02, z1: f * FLOOR, c: '#9b958a' }); }
  // rebar spikes
  ctx.strokeStyle = '#7a4a34'; ctx.lineWidth = 1;
  for (const [x, y] of cols) { const [px, py] = P(x, y, hz + 0.06); ctx.beginPath(); ctx.moveTo(px - 2, py); ctx.lineTo(px - 3, py - 8); ctx.moveTo(px + 2, py); ctx.lineTo(px + 3, py - 8); ctx.stroke(); }
  // scaffolding on the visible face matching the chosen orientation (front face side has scaffold + hoist)
  if (stage >= 1) {
    ctx.strokeStyle = 'rgba(197,138,28,.95)'; ctx.lineWidth = 1.4;
    const fy = b.y1 + 0.07; const n = 5;
    for (let i = 0; i <= n; i++) { const x = b.x0 + (b.x1 - b.x0) * i / n; const [a1, a2] = P(x, fy, 0); const [c1, c2] = P(x, fy, hz + 0.05); ctx.beginPath(); ctx.moveTo(a1, a2); ctx.lineTo(c1, c2); ctx.stroke(); }
    for (let z = 0.1; z < hz + 0.05; z += 0.12) { const [a1, a2] = P(b.x0, fy, z); const [c1, c2] = P(b.x1, fy, z); ctx.beginPath(); ctx.moveTo(a1, a2); ctx.lineTo(c1, c2); ctx.stroke(); }
    if (!back) { const [a1, a2] = P((b.x0 + b.x1) / 2, fy + 0.02, 0); ctx.fillStyle = 'rgba(22,116,122,.55)'; ctx.fillRect(a1 - 6, a2 - hz * U - 6, 12, hz * U); } // green safety netting at entry
    else { const [a1, a2] = P(b.x0 - 0.06, (b.y0 + b.y1) / 2, 0); ctx.fillStyle = 'rgba(168,50,45,.5)'; ctx.fillRect(a1 - 4, a2 - hz * U, 8, hz * U); } // rear chute
  }
  // material stacks: blocks + rebar bundle
  box(ctx, { x0: 0.45, y0: 0.35, x1: 0.62, y1: 0.55, z0: 0, z1: 0.08, c: '#cfc7b4' });
  box(ctx, { x0: -0.62, y0: 0.4, x1: -0.35, y1: 0.48, z0: 0, z1: 0.03, c: '#7a4a34' });
  if (s.def.category === 'industry' || s.def.category === 'equipment') industryProps(ctx, { ...s, main: { ...b, z1: hz } }, back, frac);
}

/** Ruined structure — silhouette derived from the plot's buildingId and its floor count */
export function drawRubble(ctx: Ctx, id: string, seed: number) {
  const s = shapeOf(id); const r = rng(id + seed); const b = s.main; const floors = s.def.floors;
  lot(ctx, ['#b9a888', '#c2ae8a', '#ad9c80'][seed % 3]);
  // dust stain / scorch on the lot
  poly(ctx, [P(b.x0 - 0.1, b.y0 - 0.1, 0), P(b.x1 + 0.2, b.y0 - 0.05, 0), P(b.x1 + 0.15, b.y1 + 0.2, 0), P(b.x0 - 0.05, b.y1 + 0.15, 0)], 'rgba(90,78,66,.28)');
  if (s.def.category !== 'farm') {
    // grid of structural fragments: each keeps a different number of floors
    const nx = floors >= 6 ? 3 : 2, ny = 2; const pieces: Box[] = [];
    const wall = shade(s.wall, 0.92) as string;
    for (let i = 0; i < nx; i++) for (let j = 0; j < ny; j++) {
      const x0 = b.x0 + (b.x1 - b.x0) * i / nx, x1 = b.x0 + (b.x1 - b.x0) * (i + 1) / nx;
      const y0 = b.y0 + (b.y1 - b.y0) * j / ny, y1 = b.y0 + (b.y1 - b.y0) * (j + 1) / ny;
      const back = (i === 0 || j === 0) ? 1 : 0.55; // rear fragments stand taller, front collapsed for readability
      const kept = Math.max(1, Math.round(floors * back * (0.25 + r() * 0.7)));
      if (r() < 0.18 && !(i === 0 && j === 0)) continue; // fully collapsed bay
      pieces.push({ x0: x0 + 0.01, y0: y0 + 0.01, x1: x1 - 0.01, y1: y1 - 0.01, z0: 0, z1: Math.min(b.z1, kept * FLOOR + 0.04), c: r() > 0.5 ? wall : shade(s.wall, 0.82) as string });
    }
    pieces.sort((a, z) => (a.x0 + a.y0) - (z.x0 + z.y0));
    for (const h of pieces) {
      box(ctx, { ...h, top: '#a59a88' });
      const fl = Math.max(1, Math.floor(h.z1 / FLOOR));
      // window holes: warm dark, not black; some blown open into larger gaps
      for (const face of ['L', 'R'] as const) for (let f = 0; f < fl; f++) for (let c = 0; c < 2; c++) {
        const z0 = f * FLOOR + 0.06; const big = r() > 0.8;
        faceQuad(ctx, h, face, 0.18 + c * 0.42, 0.42 + c * 0.42 + (big ? 0.1 : 0), z0, z0 + (big ? 0.13 : 0.09), big ? '#5a4c40' : '#6b5a4a');
      }
      // floor slab edges (exposed storeys)
      for (let f = 1; f <= fl; f++) faceQuad(ctx, h, 'L', 0, 1, f * FLOOR - 0.012, f * FLOOR + 0.004, '#cfc6b2');
      // jagged notches on top
      for (let i = 0; i < 3; i++) { const u = r() * 0.8; faceQuad(ctx, h, r() > 0.5 ? 'L' : 'R', u, u + 0.2, h.z1 - 0.04 - r() * 0.08, h.z1 + 0.01, '#b5a68a'); }
      faceQuad(ctx, h, 'R', 0.3, 0.7, h.z1 * 0.5, h.z1, 'rgba(60,48,40,.22)');
      // hanging slab: pancaked floor tilted off the top
      if (r() > 0.55 && fl >= 2) poly(ctx, [P(h.x1, h.y0, h.z1 - 0.02), P(h.x1 + 0.14, h.y0 + 0.05, h.z1 - 0.18), P(h.x1 + 0.14, h.y1, h.z1 - 0.2), P(h.x1, h.y1, h.z1 - 0.02)], '#b9b0a0', 'rgba(40,30,20,.45)');
      ctx.strokeStyle = '#7a4a34'; ctx.lineWidth = 1.2;
      for (let i = 0; i < 3; i++) { const [px, py] = P(h.x0 + r() * (h.x1 - h.x0), h.y0 + r() * (h.y1 - h.y0), h.z1); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + (r() - 0.5) * 12, py - 5 - r() * 9); ctx.stroke(); }
    }
  }
  // debris heaps — dense, sized by the building's mass
  const heaps = 8 + Math.min(10, floors * 1.5) + Math.floor(r() * 4);
  for (let i = 0; i < heaps; i++) {
    const x = (r() - 0.5) * 1.25, y = (r() - 0.5) * 1.25; const [px, py] = P(x, y, 0); const w = 10 + r() * (16 + floors * 2), h = 6 + r() * (10 + floors * 1.5);
    const c = ['#9b958a', '#b8ae9a', '#8a8378', '#c9bfa8', '#a7957c'][Math.floor(r() * 5)];
    ctx.fillStyle = 'rgba(50,40,30,.22)'; ctx.beginPath(); ctx.ellipse(px + 4, py + 2, w, w * 0.45, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = c; ctx.beginPath(); ctx.moveTo(px - w, py); ctx.lineTo(px - w * 0.4, py - h); ctx.lineTo(px + w * 0.1, py - h * 0.7); ctx.lineTo(px + w * 0.5, py - h * 1.05); ctx.lineTo(px + w, py); ctx.closePath(); ctx.fill();
    ctx.fillStyle = shade(c, 0.78); ctx.beginPath(); ctx.moveTo(px + w * 0.1, py - h * 0.7); ctx.lineTo(px + w * 0.5, py - h * 1.05); ctx.lineTo(px + w, py); ctx.lineTo(px + w * 0.2, py); ctx.closePath(); ctx.fill();
    for (let k = 0; k < 4; k++) { ctx.fillStyle = r() > 0.5 ? '#e1d6bf' : '#6f665c'; ctx.fillRect(px - w + r() * w * 2, py - r() * h * 0.8, 3 + r() * 4, 2 + r() * 3); }
  }
  for (let i = 0; i < 3; i++) { const x = (r() - 0.5), y = (r() - 0.5); poly(ctx, [P(x, y, 0.02), P(x + 0.25, y + 0.05, 0.1 * r()), P(x + 0.22, y + 0.22, 0.12), P(x - 0.02, y + 0.18, 0.02)], '#b0a898', 'rgba(40,30,20,.4)'); }
}

export function drawEmpty(ctx: Ctx, seed: number) {
  const r = rng('e' + seed);
  lot(ctx, ['#dcc9a2', '#d4bf95', '#cdb88e'][seed % 3]);
  for (let i = 0; i < 40; i++) { const [px, py] = P((r() - 0.5) * 1.3, (r() - 0.5) * 1.3, 0); ctx.fillStyle = r() > 0.6 ? 'rgba(120,96,64,.35)' : 'rgba(255,245,220,.4)'; ctx.fillRect(px, py, 2 + r() * 3, 1 + r() * 2); }
  for (let i = 0; i < 5; i++) { const [px, py] = P((r() - 0.5) * 1.2, (r() - 0.5) * 1.2, 0); ctx.strokeStyle = '#8a9a4a'; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px - 2, py - 5); ctx.moveTo(px, py); ctx.lineTo(px + 2, py - 6); ctx.stroke(); }
  // survey stakes: ready for construction
  for (const [x, y] of [[-0.6, -0.6], [0.6, -0.6], [-0.6, 0.6], [0.6, 0.6]]) { const [px, py] = P(x, y, 0); ctx.fillStyle = '#a8322d'; ctx.fillRect(px - 1, py - 10, 2, 10); }
}

// ---------- vehicles: true 3D boxes rotated by heading, then projected ----------
interface VBox { c: V3; s: V3; col: string; rot?: number }
const VEH: Record<EquipmentKind, VBox[]> = {
  excavator: [
    { c: [0, 0.1, 0.03], s: [0.34, 0.07, 0.06], col: '#3a3532' }, { c: [0, -0.1, 0.03], s: [0.34, 0.07, 0.06], col: '#3a3532' },
    { c: [-0.02, 0, 0.1], s: [0.28, 0.24, 0.08], col: '#d9a227' }, { c: [0.04, 0.05, 0.19], s: [0.12, 0.1, 0.1], col: '#e0b03a' },
    { c: [0.16, -0.04, 0.2], s: [0.16, 0.04, 0.04], col: '#d9a227', rot: 0 }, { c: [0.27, -0.04, 0.12], s: [0.05, 0.04, 0.16], col: '#c99520' }, { c: [0.3, -0.04, 0.04], s: [0.07, 0.08, 0.05], col: '#4a4440' },
  ],
  bulldozer: [
    { c: [0, 0.1, 0.03], s: [0.3, 0.06, 0.06], col: '#3a3532' }, { c: [0, -0.1, 0.03], s: [0.3, 0.06, 0.06], col: '#3a3532' },
    { c: [-0.02, 0, 0.1], s: [0.24, 0.2, 0.09], col: '#c0532b' }, { c: [-0.06, 0, 0.2], s: [0.11, 0.14, 0.1], col: '#d06a3e' },
    { c: [0.2, 0, 0.06], s: [0.03, 0.3, 0.1], col: '#6f6a62' },
  ],
  truck: [
    { c: [0.15, 0, 0.1], s: [0.1, 0.18, 0.13], col: '#e8e2d4' }, { c: [-0.07, 0, 0.09], s: [0.26, 0.2, 0.1], col: '#16747a' },
    { c: [-0.07, 0, 0.16], s: [0.22, 0.16, 0.03], col: '#9b958a' }, { c: [0.15, 0, 0.18], s: [0.09, 0.17, 0.04], col: '#5b8a95' },
    { c: [0.12, 0.1, 0.03], s: [0.05, 0.02, 0.03], col: '#222' }, { c: [-0.12, 0.1, 0.03], s: [0.05, 0.02, 0.03], col: '#222' },
  ],
};
export const DIRS = 16; export const VEH_SIZE = 96;
export function drawVehicle(ctx: Ctx, kind: EquipmentKind, dir: number, level: number) {
  const th = (dir / DIRS) * Math.PI * 2; const cs = Math.cos(th), sn = Math.sin(th);
  const S = VEH_SIZE * 1.2; const ox = VEH_SIZE / 2, oy = VEH_SIZE * 0.62;
  const pr = (p: V3): [number, number] => [ox + (p[0] - p[1]) * S, oy + (p[0] + p[1]) * S / 2 - p[2] * S];
  const rot = (p: V3): V3 => [p[0] * cs - p[1] * sn, p[0] * sn + p[1] * cs, p[2]];
  ctx.fillStyle = 'rgba(40,30,20,.3)'; ctx.beginPath(); ctx.ellipse(ox, oy, S * 0.3, S * 0.15, 0, 0, Math.PI * 2); ctx.fill();
  const boxes = VEH[kind].map(b => ({ b, d: rot(b.c)[0] + rot(b.c)[1] + b.c[2] * 0.5 })).sort((a, z) => a.d - z.d);
  for (const { b } of boxes) {
    const [cx, cy, cz] = b.c, [sx, sy, sz] = b.s;
    const corner = (i: number, j: number, k: number) => rot([cx + (i ? sx : -sx) / 2, cy + (j ? sy : -sy) / 2, cz + (k ? sz : -sz) / 2]);
    const faces: { pts: V3[]; n: V3 }[] = [
      { pts: [corner(0, 0, 1), corner(1, 0, 1), corner(1, 1, 1), corner(0, 1, 1)], n: [0, 0, 1] },
      { pts: [corner(1, 0, 0), corner(1, 1, 0), corner(1, 1, 1), corner(1, 0, 1)], n: rot([1, 0, 0]) },
      { pts: [corner(0, 0, 0), corner(0, 1, 0), corner(0, 1, 1), corner(0, 0, 1)], n: rot([-1, 0, 0]) },
      { pts: [corner(0, 1, 0), corner(1, 1, 0), corner(1, 1, 1), corner(0, 1, 1)], n: rot([0, 1, 0]) },
      { pts: [corner(0, 0, 0), corner(1, 0, 0), corner(1, 0, 1), corner(0, 0, 1)], n: rot([0, -1, 0]) },
    ];
    for (const f of faces) { const vis = f.n[2] > 0 || f.n[0] + f.n[1] > 0.01; if (!vis) continue;
      const light = f.n[2] > 0 ? 1.1 : 0.62 + 0.3 * Math.max(0, (f.n[1] * 0.8 - f.n[0] * 0.2));
      poly(ctx, f.pts.map(pr), shade(b.col, light), 'rgba(30,20,10,.35)'); }
  }
  // upgrade chevrons on roof
  const top = pr([0, 0, 0.3]); for (let i = 0; i < level; i++) { ctx.fillStyle = '#f2d06b'; ctx.beginPath(); ctx.moveTo(top[0] - 6 + i * 6, top[1] - 6); ctx.lineTo(top[0] - 3 + i * 6, top[1] - 10); ctx.lineTo(top[0] + i * 6, top[1] - 6); ctx.fill(); }
}

export function makeCanvas(w = TEX_W, h = TEX_H) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }

/** small DOM preview used in the build dialog */
export function previewDataURL(id: string, orient: 0 | 1) { const c = makeCanvas(); drawBuilt(c.getContext('2d')!, id, orient); return c.toDataURL(); }

// ---------- infrastructure project sites (perimeter, not on user plots) ----------
/** stage: 0 = surveyed/idle, 1 = under construction (prog 0..1), 2 = complete */
export function drawProjectSite(ctx: Ctx, id: string, stage: 0 | 1 | 2, prog = 0) {
  const done = stage === 2; const k = stage === 0 ? 0 : stage === 1 ? 0.25 + prog * 0.6 : 1;
  const grey = '#b3ab9c';
  const col = (c: string) => done ? c : grey;
  lot(ctx, done ? (id === 'park' ? '#9fb46a' : id === 'farm' ? '#8a6a44' : '#d2c19c') : '#c9b48c');
  if (stage === 0) {
    // survey: stakes, string line, site board
    for (const [x, y] of [[-0.6, -0.6], [0.6, -0.6], [-0.6, 0.6], [0.6, 0.6]]) { const [px, py] = P(x, y, 0); ctx.fillStyle = '#a8322d'; ctx.fillRect(px - 1, py - 12, 2, 12); }
    ctx.strokeStyle = 'rgba(168,50,45,.6)'; ctx.setLineDash([4, 3]); ctx.beginPath(); [P(-0.6, -0.6, 0.1), P(0.6, -0.6, 0.1), P(0.6, 0.6, 0.1), P(-0.6, 0.6, 0.1), P(-0.6, -0.6, 0.1)].forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.stroke(); ctx.setLineDash([]);
    const sb: Box = { x0: -0.1, y0: 0.5, x1: 0.2, y1: 0.53, z0: 0.08, z1: 0.24, c: '#2f6f73' }; box(ctx, sb);
    return;
  }
  if (stage === 1) for (let i = 0; i < 12; i++) { const t = -0.68 + i * 0.124; box(ctx, { x0: t, y0: 0.64, x1: t + 0.1, y1: 0.67, z0: 0, z1: 0.09, c: i % 2 ? '#e8dcc4' : '#c58a1c' }); }
  const scaffold = (b: Box) => { if (done) return; ctx.strokeStyle = 'rgba(197,138,28,.95)'; ctx.lineWidth = 1.3; for (let i = 0; i <= 4; i++) { const x = b.x0 + (b.x1 - b.x0) * i / 4; const [a1, a2] = P(x, b.y1 + 0.05, 0); const [c1, c2] = P(x, b.y1 + 0.05, b.z1 + 0.04); ctx.beginPath(); ctx.moveTo(a1, a2); ctx.lineTo(c1, c2); ctx.stroke(); } for (let z = 0.08; z < b.z1; z += 0.1) { const [a1, a2] = P(b.x0, b.y1 + 0.05, z); const [c1, c2] = P(b.x1, b.y1 + 0.05, z); ctx.beginPath(); ctx.moveTo(a1, a2); ctx.lineTo(c1, c2); ctx.stroke(); } };
  const disc = (x: number, y: number, rad: number, h: number, c: string, top: string) => { const [px, py] = P(x, y, 0); const rx = rad * U * 1.4, ry = rx / 2; ctx.fillStyle = shade(c, 0.75); ctx.fillRect(px - rx, py - h * U, rx * 2, h * U); ctx.beginPath(); ctx.ellipse(px, py, rx, ry, 0, 0, Math.PI); ctx.fill(); ctx.fillStyle = top; ctx.beginPath(); ctx.ellipse(px, py - h * U, rx, ry, 0, 0, Math.PI * 2); ctx.fill(); ctx.strokeStyle = 'rgba(40,30,20,.35)'; ctx.stroke(); };
  if (id === 'water') {
    disc(-0.3, 0.2, 0.28, 0.08 * k, col('#d6d0c2'), done ? '#4aa0a8' : '#8a8378');
    disc(0.25, 0.25, 0.22, 0.08 * k, col('#d6d0c2'), done ? '#5fb3bb' : '#8a8378');
    const t: Box = { x0: 0.2, y0: -0.5, x1: 0.42, y1: -0.28, z0: 0.55 * k, z1: 0.75 * k, c: col('#e8e2d4') };
    for (const [x, y] of [[0.22, -0.48], [0.38, -0.48], [0.22, -0.32], [0.38, -0.32]]) box(ctx, { x0: x, y0: y, x1: x + 0.03, y1: y + 0.03, z0: 0, z1: 0.55 * k, c: '#8a8378' });
    box(ctx, t); if (done) faceQuad(ctx, t, 'L', 0.1, 0.9, t.z0 + 0.08, t.z0 + 0.12, '#16747a'); scaffold(t);
  } else if (id === 'power') {
    // substation: transformers + lattice pylon + insulators
    for (let i = 0; i < 3; i++) { const b: Box = { x0: -0.55 + i * 0.3, y0: 0.1, x1: -0.35 + i * 0.3, y1: 0.35, z0: 0, z1: 0.18 * k, c: col('#7d8b84') }; box(ctx, b); if (done) faceQuad(ctx, b, 'L', 0.2, 0.8, 0.05, 0.08, '#f2d06b'); }
    const [bx, by] = P(0.2, -0.3, 0); const h = 1.0 * k * U; ctx.strokeStyle = done ? '#5a5f66' : '#9b958a'; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(bx - 16, by); ctx.lineTo(bx, by - h); ctx.lineTo(bx + 16, by); ctx.stroke();
    for (let y = 10; y < h; y += 12) { const w = 16 * (1 - y / h); ctx.beginPath(); ctx.moveTo(bx - w, by - y); ctx.lineTo(bx + w, by - y); ctx.stroke(); }
    if (k > 0.7) { ctx.beginPath(); ctx.moveTo(bx - 22, by - h * 0.8); ctx.lineTo(bx + 22, by - h * 0.8); ctx.stroke(); }
    if (done) { ctx.strokeStyle = 'rgba(40,40,40,.6)'; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(bx - 22, by - h * 0.8); ctx.quadraticCurveTo(bx - 70, by - h * 0.5, bx - 110, by - h * 0.65); ctx.moveTo(bx + 22, by - h * 0.8); ctx.quadraticCurveTo(bx + 60, by - h * 0.55, bx + 110, by - h * 0.7); ctx.stroke(); }
    ctx.strokeStyle = '#8a8378'; ctx.lineWidth = 1; ctx.beginPath(); [P(-0.66, -0.66, 0.08), P(0.66, -0.66, 0.08), P(0.66, 0.66, 0.08)].forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.stroke();
  } else if (id === 'housing') {
    for (let i = 0; i < 3; i++) { const b: Box = { x0: -0.6 + i * 0.42, y0: -0.3 + (i % 2) * 0.1, x1: -0.26 + i * 0.42, y1: 0.35, z0: 0, z1: (0.5 + i * 0.12) * k, c: col(['#eadcc0', '#e3c9a3', '#d9c7a8'][i]) }; box(ctx, b); const fl = Math.floor(b.z1 / FLOOR); windows(ctx, b, 'L', fl, 2, done ? '#5b8a95' : '#3a3530', false, rng('h' + i)); windows(ctx, b, 'R', fl, 2, done ? '#5b8a95' : '#3a3530', false, rng('hr' + i)); if (done) box(ctx, { x0: b.x1 - 0.12, y0: b.y0 + 0.05, x1: b.x1 - 0.04, y1: b.y0 + 0.13, z0: b.z1, z1: b.z1 + 0.1, c: '#2b2b2b' }); scaffold(b); }
  } else if (id === 'park') {
    if (done) { ground(ctx, -0.66, -0.06, 0.66, 0.06, '#e2d3b2'); ground(ctx, -0.06, -0.66, 0.06, 0.66, '#e2d3b2'); disc(0, 0, 0.14, 0.05, '#d6d0c2', '#5fb3bb'); }
    else ground(ctx, -0.6, -0.6, 0.6, 0.6, prog > 0.5 ? '#9a8a5a' : '#8a6a44');
    const r = rng('park'); for (let i = 0; i < 9; i++) { if (r() > k) continue; const x = (r() - 0.5) * 1.1, y = (r() - 0.5) * 1.1; if (Math.abs(x) < 0.15 || Math.abs(y) < 0.15) continue; const [px, py] = P(x, y, 0); const hh = 26 * k; ctx.fillStyle = '#6a4a2a'; ctx.fillRect(px - 1, py - hh * 0.5, 2, hh * 0.5); ctx.fillStyle = '#3f7a3a'; ctx.beginPath(); ctx.arc(px, py - hh * 0.6, 4 + 8 * k, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = '#5f9a4a'; ctx.beginPath(); ctx.arc(px - 2, py - hh * 0.7, 3 + 6 * k, 0, Math.PI * 2); ctx.fill(); }
    if (done) for (const [x, y] of [[0.3, 0.12], [-0.3, -0.12]]) box(ctx, { x0: x, y0: y, x1: x + 0.15, y1: y + 0.04, z0: 0.03, z1: 0.05, c: '#7a5a36' });
  } else if (id === 'services') {
    const b: Box = { x0: -0.5, y0: -0.4, x1: 0.5, y1: 0.3, z0: 0, z1: 0.48 * k, c: col('#e8dccb') }; box(ctx, b);
    windows(ctx, b, 'L', Math.floor(b.z1 / FLOOR), 6, done ? '#5b8a95' : '#3a3530', true, rng('svc')); windows(ctx, b, 'R', Math.floor(b.z1 / FLOOR), 4, done ? '#5b8a95' : '#3a3530', false, rng('svc2'));
    if (done) { faceQuad(ctx, b, 'L', 0.4, 0.6, 0, 0.18, '#16747a'); faceQuad(ctx, b, 'L', 0.3, 0.7, 0.36, 0.43, '#16747a'); const [px, py] = P(0.35, -0.3, b.z1); ctx.fillStyle = '#5a4030'; ctx.fillRect(px, py - 34, 2, 34); ctx.fillStyle = '#a8322d'; ctx.fillRect(px + 2, py - 34, 14, 8); }
    scaffold(b);
  } else if (id === 'farm') {
    ground(ctx, -0.62, -0.62, 0.62, 0.62, '#8a6a44');
    for (let i = 0; i < 8; i++) { const t = -0.58 + i * 0.15; ground(ctx, t, -0.6, t + 0.06, 0.6, '#74583a'); if (stage >= 1) { const r = rng('f' + i); for (let j = 0; j < 7; j++) { if (r() > k) continue; const [px, py] = P(t + 0.03, -0.55 + j * 0.17, 0); ctx.fillStyle = done ? '#5f9a3a' : '#7f9a4a'; ctx.beginPath(); ctx.ellipse(px, py - 3 * k, 3 + 4 * k, 2 + 3 * k, 0, 0, Math.PI * 2); ctx.fill(); } } }
    box(ctx, { x0: 0.42, y0: -0.62, x1: 0.62, y1: -0.42, z0: 0, z1: 0.3, c: '#6fa3c0', top: '#4aa0a8' });
  } else if (id === 'commerce') {
    const cols = ['#c0532b', '#16747a', '#c58a1c', '#a8322d', '#5f6d33'];
    for (let i = 0; i < 5; i++) { if (i / 5 > k + 0.05) continue; const x = -0.6 + (i % 3) * 0.42, y = -0.4 + Math.floor(i / 3) * 0.55; const b: Box = { x0: x, y0: y, x1: x + 0.3, y1: y + 0.25, z0: 0, z1: 0.14, c: col('#e8d6b4') }; box(ctx, b);
      if (done) { poly(ctx, [P(x - 0.03, y + 0.28, 0.17), P(x + 0.33, y + 0.28, 0.17), P(x + 0.33, y - 0.02, 0.24), P(x - 0.03, y - 0.02, 0.24)], cols[i]); for (let n = 0; n < 4; n++) { const [px, py] = P(x + 0.05 + n * 0.07, y + 0.3, 0.02); ctx.fillStyle = ['#f2a23a', '#c8322d', '#5f9a3a', '#e7c46a'][n]; ctx.beginPath(); ctx.arc(px, py - 3, 3, 0, Math.PI * 2); ctx.fill(); } } else scaffold(b); }
  } else if (id === 'industry') {
    const b: Box = { x0: -0.55, y0: -0.45, x1: 0.35, y1: 0.25, z0: 0, z1: 0.32 * k, c: col('#a9a59a') }; box(ctx, b);
    for (let i = 0; i < 4; i++) poly(ctx, [P(b.x0 + i * 0.225, b.y0, b.z1), P(b.x0 + i * 0.225 + 0.112, b.y0, b.z1 + 0.08 * k), P(b.x0 + i * 0.225 + 0.112, b.y1, b.z1 + 0.08 * k), P(b.x0 + i * 0.225, b.y1, b.z1)], done ? '#8fa6a8' : '#9b958a');
    if (done) faceQuad(ctx, b, 'L', 0.3, 0.6, 0, 0.22, '#6a5c44');
    box(ctx, { x0: 0.42, y0: -0.4, x1: 0.52, y1: -0.3, z0: 0, z1: 0.75 * k, c: col('#7a5446') });
    if (done) for (let i = 0; i < 3; i++) poly(ctx, [P(0.4, 0.35 + i * 0.08, 0), P(0.6, 0.3 + i * 0.08, 0), P(0.52, 0.4 + i * 0.08, 0.1)], ['#8b8b84', '#a39b8a', '#7a4a34'][i]);
    scaffold(b);
  }
}
