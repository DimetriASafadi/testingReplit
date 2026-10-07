import type { BuildingDef } from './model';
import type { SprMeta } from './realart';

/** Stages 0..7: 0 foundation/excavation, 1-5 rising structure, 6 roof closed, 7 finishing/handover. */
export const CONSTRUCTION_STAGES = 8;
export const constructionStageFor = (prog: number) => Math.min(CONSTRUCTION_STAGES - 1, Math.max(0, Math.floor(prog * CONSTRUCTION_STAGES)));
/** fraction of the real building height that is standing at a stage */
export const stageHeight = (stage: number) => [0, 0.2, 0.4, 0.58, 0.76, 0.9, 1, 1][Math.min(7, Math.max(0, stage))];
/** fraction of height (from the ground up) already finished (plaster/colour) */
export const stageFinish = (stage: number) => [0, 0, 0, 0.1, 0.28, 0.5, 0.78, 1][Math.min(7, Math.max(0, stage))];
export const isFarmDef = (def: BuildingDef) => def.category === 'farm' || def.id === 'zoo';
const hash = (a: number, b: number) => { const s = Math.sin(a * 127.1 + b * 311.7) * 43758.5453; return s - Math.floor(s); };

/** Footprint diamond derived from sprite meta (centre at ground anchor). */
export function footprint(meta: SprMeta) {
  const hw = Math.min(meta.w * 0.3, 102); return { cx: meta.w / 2, cy: meta.h * meta.ay, hw, hh: hw * 0.5 };
}

/** Plot is 1.4 iso units at U=64 and SC=.88: half-diagonal ~102 sprite px. */
export function farmFootprint(meta: SprMeta) { const hw = Math.min(meta.w * 0.47, 102); return { cx: meta.w / 2, cy: meta.h * meta.ay, hw, hh: hw * 0.5 }; }
const TREES = ['citrus', 'olive', 'palms', 'protective_trees', 'ornamental_trees'];
/** plant lattice in plot-local (u,v) in [-1,1]; shared by canvas stages and SiteAnimation */
export function farmLattice(id: string) {
  const tree = TREES.includes(id), herd = id === 'cattle' || id === 'zoo'; const spots: [number, number][] = [];
  const R = tree ? 3 : herd ? 3 : 6, C = tree ? 3 : herd ? 3 : 7;
  for (let r = 0; r < R; r++) for (let k = 0; k < C; k++) spots.push([-0.7 + 1.4 * k / (C - 1) + (tree && r % 2 ? 0.0 : 0), -0.7 + 1.4 * r / (R - 1)]);
  return { tree, herd, rows: R, cols: C, spots, rx: tree ? 26 : herd ? 24 : 15 };
}

export function constructionCanvas(final: CanvasImageSource, foundation: CanvasImageSource,
  meta: SprMeta, def: BuildingDef, stage: number, _orientation: 0 | 1) {
  const cv = document.createElement('canvas'); cv.width = meta.w; cv.height = meta.h;
  const c = cv.getContext('2d')!;
  const { cx, cy: ground, hw, hh } = footprint(meta); const base = meta.w / 4;
  const farm = isFarmDef(def); stage = Math.min(7, Math.max(0, stage));
  const fh = Math.min(meta.h, farm ? base * 2.1 : base * 2.3);
  c.drawImage(foundation, 0, ground + base - fh, meta.w, fh);
  if (farm) {
    const fp = farmFootprint(meta); const lat = farmLattice(def.id);
    const pt = (u: number, v: number): [number, number] => [fp.cx + (u - v) / 2 * fp.hw, fp.cy + (u + v) / 2 * fp.hh];
    // seedbeds: ploughed rows (or planting pits for trees) follow the plot's own iso axes
    c.lineCap = 'round';
    if (lat.tree || lat.herd) {
      for (const [u, v] of lat.spots) { const [x, y] = pt(u, v); c.fillStyle = 'rgba(58,40,28,.55)'; c.beginPath(); c.ellipse(x, y, 9, 4.2, 0, 0, 7); c.fill(); c.fillStyle = 'rgba(150,118,84,.45)'; c.beginPath(); c.ellipse(x, y - 1, 6, 2.6, 0, 0, 7); c.fill(); }
    } else for (let j = 0; j < lat.rows; j++) {
      const v = -0.82 + 1.64 * j / (lat.rows - 1); const [x0, y0] = pt(-0.86, v), [x1, y1] = pt(0.86, v);
      c.strokeStyle = 'rgba(52,36,24,.5)'; c.lineWidth = 4; c.beginPath(); c.moveTo(x0, y0 + 1.5); c.lineTo(x1, y1 + 1.5); c.stroke();
      c.strokeStyle = 'rgba(176,142,104,.5)'; c.lineWidth = 2.2; c.beginPath(); c.moveTo(x0, y0 - 1); c.lineTo(x1, y1 - 1); c.stroke();
    }
    if (stage === 0 || stage >= 7) { if (stage >= 7) c.drawImage(final, 0, 0); return cv; }
    // Organic reveal: each plant spot grows a soft elliptical patch of the real crop art, rooted at the soil.
    const g = stage / 7; const t = document.createElement('canvas'); t.width = meta.w; t.height = meta.h; const tc = t.getContext('2d')!;
    const full = Math.max(40, fp.cy * 0.95);
    lat.spots.forEach(([u, v], i) => {
      const k = Math.min(1, g * 1.45 - hash(i, stage * 0) * 0.45 - (lat.tree ? 0.1 : 0)); if (k < 0.08) return;
      const [x, y] = pt(u, v); const rx = lat.rx * (0.45 + 0.75 * k), ry = Math.max(8, full * k * (lat.tree ? 1 : 0.8));
      tc.save(); tc.translate(x, y - ry * 0.45); tc.scale(1, ry / rx);
      const gr = tc.createRadialGradient(0, 0, rx * 0.25, 0, 0, rx); gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.7, 'rgba(255,255,255,.9)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
      tc.fillStyle = gr; tc.beginPath(); tc.arc(0, 0, rx, 0, 7); tc.fill(); tc.restore();
    });
    tc.globalCompositeOperation = 'source-in'; tc.drawImage(final, 0, 0); c.drawImage(t, 0, 0);
    return cv;
  }
  if (stage === 0) {
    c.fillStyle = 'rgba(120,112,98,.8)'; c.beginPath(); c.moveTo(cx, ground - hh * .8); c.lineTo(cx + hw * .8, ground); c.lineTo(cx, ground + hh * .8); c.lineTo(cx - hw * .8, ground); c.closePath(); c.fill();
    c.strokeStyle = '#6a4a32'; c.lineWidth = 1.4; for (let i = -3; i <= 3; i++) { c.beginPath(); c.moveTo(cx + i * hw * .2, ground - 2); c.lineTo(cx + i * hw * .2, ground - 12); c.stroke(); }
    return cv;
  }
  const cut = ground * (1 - stageHeight(stage));
  const fin = ground * (1 - stageFinish(stage));
  const yFin = Math.max(cut, fin);
  // raw structure: grey cast concrete with formwork seams, floor slabs and dark openings; the leading floor is an open frame
  const lvH = ground / Math.max(1, def.floors);
  if (yFin > cut) {
    const t = document.createElement('canvas'); t.width = meta.w; t.height = meta.h; const tc = t.getContext('2d')!;
    tc.beginPath(); tc.rect(0, cut, meta.w, yFin - cut); tc.clip();
    tc.filter = 'grayscale(1) contrast(1.35) brightness(.8)'; tc.drawImage(final, 0, 0); tc.filter = 'none';
    tc.globalCompositeOperation = 'source-atop'; tc.fillStyle = 'rgba(150,138,118,.38)'; tc.fillRect(0, 0, meta.w, meta.h);
    tc.fillStyle = 'rgba(30,26,22,.14)'; for (let y = cut + 5; y < yFin; y += 7) tc.fillRect(0, y, meta.w, 1);
    for (let y = ground - lvH; y > cut - 1; y -= lvH) { tc.fillStyle = 'rgba(205,198,182,.75)'; tc.fillRect(0, y - 2, meta.w, 3.5); tc.fillStyle = 'rgba(40,34,28,.3)'; tc.fillRect(0, y + 1.5, meta.w, 1.5); }
    if (stage <= 5) { // open frame on the working floor: columns and slab only, sky/ground visible between
      tc.globalCompositeOperation = 'destination-out'; const band = Math.min(lvH * 0.85, yFin - cut - 4);
      const colW = Math.max(4, meta.w * 0.025); const n = 5;
      for (let i = 0; i < n - 1; i++) { const x0 = cx - hw * .8 + (i / (n - 1)) * hw * 1.6 + colW, x1 = cx - hw * .8 + ((i + 1) / (n - 1)) * hw * 1.6 - colW; tc.fillRect(x0, cut + 4, x1 - x0, band); }
    }
    c.drawImage(t, 0, 0);
  }
  c.save(); c.beginPath(); c.rect(0, yFin, meta.w, meta.h - yFin); c.clip(); c.drawImage(final, 0, 0); c.restore();
  if (stage < 6) { // fresh slab, parapet edge and rebar stubs on the leading floor
    c.fillStyle = 'rgba(176,170,156,.9)'; c.beginPath(); c.moveTo(cx, cut - hh * .6); c.lineTo(cx + hw * .7, cut); c.lineTo(cx, cut + hh * .6); c.lineTo(cx - hw * .7, cut); c.closePath(); c.fill();
    c.strokeStyle = 'rgba(70,52,40,.9)'; c.lineWidth = 1; for (let i = -3; i <= 3; i++) { const x = cx + i * hw * .17; c.beginPath(); c.moveTo(x, cut - Math.abs(i) * 0.5); c.lineTo(x, cut - 9); c.stroke(); }
  }
  if (stage <= 6) { // scaffolding rises on the visible front corners; none left at handover
    const L = [cx - hw * .82, ground + hh * .1], F = [cx, ground + hh * .82], R = [cx + hw * .82, ground + hh * .1];
    const levels = Math.max(1, def.floors); const lv = (ground - cut) / levels;
    c.strokeStyle = 'rgba(88,84,76,.9)'; c.lineWidth = 1.1;
    for (const p of [L, F, R]) { c.beginPath(); c.moveTo(p[0], p[1]); c.lineTo(p[0], Math.max(2, cut - 12)); c.stroke(); }
    for (let k = 1; k * lv <= ground - cut + 1 || k === 1; k++) {
      const dy = k * lv * 0.9; c.strokeStyle = '#a58d62'; c.lineWidth = 2.2; c.beginPath(); c.moveTo(L[0], L[1] - dy); c.lineTo(F[0], F[1] - dy); c.lineTo(R[0], R[1] - dy); c.stroke();
      c.strokeStyle = 'rgba(88,84,76,.8)'; c.lineWidth = .8; c.beginPath(); c.moveTo(L[0], L[1] - dy); c.lineTo(F[0], F[1] - dy + lv * .9); c.moveTo(R[0], R[1] - dy); c.lineTo(F[0], F[1] - dy + lv * .9); c.stroke();
      if (k > 8) break;
    }
    if (stage >= 3) { c.fillStyle = 'rgba(210,205,190,.28)'; c.beginPath(); c.moveTo(L[0], L[1] - lv); c.lineTo(F[0], F[1] - lv); c.lineTo(F[0], F[1] - 3 * lv); c.lineTo(L[0], L[1] - 3 * lv); c.fill(); }
  }
  if (stage <= 4) { // stacked blocks and sand heap beside the front corner
    c.fillStyle = '#a39d90'; for (let i = 0; i < 6; i++) c.fillRect(cx + hw * .5 + (i % 3) * 9, ground + hh * .9 - Math.floor(i / 3) * 5, 8, 4.5);
    c.fillStyle = '#c4a874'; c.beginPath(); c.ellipse(cx - hw * .5, ground + hh * .95, 14, 5, 0, Math.PI, 0); c.fill();
  }
  return cv;
}
