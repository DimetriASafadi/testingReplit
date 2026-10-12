// Courtyard textures for compact buildings: the plot is land, the house only uses part of it.
// Textures share the skirt anchor (plot centre at TEX_W/2, 380) and are cached by yard/size/orientation/level/variant.
import type Phaser from 'phaser';
import { TEX_W, U, makeCanvas } from './art';
import { roundRect, seeded } from './ground';
import type { Orientation } from './model';
import { rotateLotPoint } from './orientation';
import { dressParcel, drawKerbParking, drawSidewalk, parcelFlavor } from './parcel-dressing';

type Pt = [number, number];
export type YardKind = 'garden' | 'patio' | 'service';
/** 0 graded soil (foundation/early works), 1 + entry path and paved pad, 2 finished garden */
export type YardLevel = 0 | 1 | 2;
export interface LotYard { back: string; front?: string }

// These canvases render at ART_SC (.72), so .98 becomes .706 world units:
// just inside the real lot's ±.72 boundary, independently of the house scale.
const ANCHOR = 380, EDGE = 0.98;
const pr = (lx: number, ly: number): Pt => [TEX_W / 2 + (lx - ly) * U, ANCHOR + (lx + ly) * U / 2];
/** photo canopy clumps cut from the existing SPR tree sprites: [sprite, cx, cy, radius] */
const CLUMPS: [string, number, number, number][] = [['orn_front', 92, 37, 26], ['orn_front', 152, 52, 24], ['citrus_front', 100, 44, 24], ['olive_front', 92, 34, 18]];

export function lotYardKey(yard: YardKind | 'none', factor: number, orient: Orientation, level: YardLevel, n: number, flavor='default') {
  return `yard:${yard}:${Math.round(factor * 100)}:${orient}:${level}:${n % 3}:${flavor}`;
}

export function lotLandscape(scene: Phaser.Scene, yard: YardKind | 'none', factor: number, orient: Orientation, level: YardLevel, n: number, buildingId=''): LotYard {
  n %= 3; // cache variants must not depend on which plot was built first
  const flavor=parcelFlavor(buildingId,yard), dining=flavor==='dining';
  const dressed=!['garden','retail','dining'].includes(flavor);
  const key = lotYardKey(yard, factor, orient, level, n,flavor), fkey = key + ':f';
  if (scene.textures.exists(key)) return { back: key, front: scene.textures.exists(fkey) ? fkey : undefined };
  const tex = (k: string) => scene.textures.exists(k) ? scene.textures.get(k).getSourceImage() as HTMLImageElement : null;
  const r = seeded(n * 97 + Math.round(factor * 100) + 5);
  const m = (p: Pt): Pt => rotateLotPoint(p, orient);
  const c = makeCanvas(), x = c.getContext('2d')!;
  const trace = (poly: Pt[]) => { x.beginPath(); poly.forEach((p, i) => { const [a, b] = pr(...p); i ? x.lineTo(a, b) : x.moveTo(a, b); }); x.closePath(); };
  const tile = (poly: Pt[], g: string, tile: number, alpha: number, tint: string, blur = 0) => {
    const img = tex(g); x.save(); if (blur) x.filter = `blur(${blur}px)`; x.globalAlpha = alpha; trace(poly); x.clip();
    if (img) { const k = U * tile / 1024; x.setTransform(k, k / 2, -k, k / 2, TEX_W / 2 + n * 29, ANCHOR - 17 * n); x.fillStyle = x.createPattern(img, 'repeat')!; x.fillRect(-4000, -4000, 8000, 8000); x.setTransform(1, 0, 0, 1, 0, 0); }
    x.fillStyle = tint; x.fillRect(0, 0, TEX_W, 470); x.restore();
  };
  const full = yard === 'none', h0 = factor * 0.55, a = h0 - 0.1, patio = yard !== 'garden', service = yard === 'service';
  // soil bed under the whole lot, feathered into the dusty skirt
  tile(roundRect(-EDGE, -EDGE, EDGE, EDGE, 0.22).map(m), 'g:earth', 4, level ? .5 : .42, 'rgba(165,148,120,.2)', 3);
  tile(roundRect(-EDGE + .1, -EDGE + .1, EDGE - .1, EDGE - .1, 0.2).map(m), 'g:sand', 4, .32, 'rgba(190,172,140,.12)', 4);
  const feather = (cx: CanvasRenderingContext2D) => {
    const mk = makeCanvas(), mc = mk.getContext('2d')!; mc.translate(TEX_W / 2, ANCHOR); mc.scale(1, .5);
    const g = mc.createRadialGradient(0, 0, 78, 0, 0, 122); g.addColorStop(0, 'white'); g.addColorStop(.6, 'rgba(255,255,255,.85)'); g.addColorStop(1, 'rgba(255,255,255,0)');
    mc.fillStyle = g; mc.fillRect(-124, -124, 248, 248); cx.globalCompositeOperation = 'destination-in'; cx.drawImage(mk, 0, 0); cx.globalCompositeOperation = 'source-over';
  };
  feather(x);
  // entry: paved pad + path to the lot edge
  const pad: Pt[] = patio ? [[a - .05, a - .05], [a + .5, a - .05], [a + .5, a + .42], [a - .05, a + .42]] : [[a, a], [a + .32, a], [a + .32, a + .3], [a, a + .3]];
  const path: Pt[] = patio ? [] : [[a + .08, a + .28], [a + .22, a + .28], [a + .22, EDGE + .02], [a + .08, EDGE + .02]];
  const gate = (p: Pt) => p;
  if (level >= 1) {
    if(patio){
      // A shop forecourt/civic plaza is functional paving, not an oversized
      // bare house lot. Its outer edge is dusty and translucent, not a slab.
      tile(roundRect(-EDGE+.06,-EDGE+.06,EDGE-.06,EDGE-.06,.2).map(m),'g:gravel',3,.32,'rgba(182,177,159,.16)',2);
      feather(x);
    }
    if (!full) for (const poly of [path, pad]) if (poly.length) {
      const q = poly.map(gate).map(m);
      tile(q, 'g:sand', 2.2, .95, 'rgba(236,226,204,.38)');
      x.save(); trace(q); x.strokeStyle = 'rgba(96,80,60,.38)'; x.lineWidth = 1.4; x.stroke(); x.restore();
    }
    // integrated sidewalk on the parcel boundary: flat, so vehicle/door access is unchanged
    drawSidewalk(x, EDGE - .02, .13, tex('exp:paver'));
  }
  const front: Pt[] = [];
  if (level === 2 && full && flavor === 'housing') drawKerbParking(x, m([.7, .7]), n);
  if (level === 2 && !full) {
    if(dressed) dressParcel(x, m, flavor, EDGE - .12, n);
    else if(patio){
      // Small, shaded furnishings at the free plot corners. The placement
      // rotates with the actual entrance, not with the camera.
      const [px,py]=pr(...m([-.72,.68]));
      x.save();x.fillStyle='rgba(48,43,33,.16)';x.beginPath();x.ellipse(px+6,py+3,15,5,0,0,Math.PI*2);x.fill();
      if(dining){
        x.fillStyle='#706b55';x.fillRect(px-1,py-9,2,10);
        x.fillStyle='#a59472';x.beginPath();x.ellipse(px,py-10,11,5,0,0,Math.PI*2);x.fill();
        x.strokeStyle='#665d49';x.lineWidth=1;x.stroke();
        for(const dx of [-16,16]){x.fillStyle='#817355';x.fillRect(px+dx-3,py-9,6,3);x.fillRect(px+dx-3,py-6,1,6);x.fillRect(px+dx+2,py-6,1,6);}
      }else if(service){
        x.fillStyle='#6c6452';x.fillRect(px-12,py-6,24,4);
        x.strokeStyle='#9c8f6b';x.lineWidth=1;for(let i=0;i<3;i++){x.beginPath();x.moveTo(px-12,py-6+i);x.lineTo(px+12,py-6+i);x.stroke();}
        x.fillStyle='#5b5a50';x.fillRect(px-10,py-2,2,5);x.fillRect(px+8,py-2,2,5);
      }else{
        x.fillStyle='#b3a38b';x.fillRect(px-8,py-10,16,9);
        x.fillStyle='#8a7b62';x.fillRect(px-8,py-2,16,3);
        x.fillStyle='#64734d';x.beginPath();x.ellipse(px,py-11,10,5,0,0,Math.PI*2);x.fill();
      }
      x.restore();
    }
    // modest low wall on the lot boundary, open where the entry path meets the street
    if (!patio) {
      const seg = (p0: Pt, p1: Pt) => { const [x0, y0] = pr(...m(p0)), [x1, y1] = pr(...m(p1));
        x.save(); x.lineCap = 'round'; x.strokeStyle = 'rgba(70,58,44,.3)'; x.lineWidth = 3.2; x.beginPath(); x.moveTo(x0, y0 + 1.5); x.lineTo(x1, y1 + 1.5); x.stroke();
        x.strokeStyle = 'rgba(219,208,184,.85)'; x.lineWidth = 2.2; x.beginPath(); x.moveTo(x0, y0); x.lineTo(x1, y1); x.stroke(); x.restore(); };
      const E = EDGE - .02; seg([-E, -E], [E, -E]); seg([-E, -E], [-E, E]); seg([E, -E], [E, E]); seg([-E, E], [a + .04, E]); seg([a + .26, E], [E, E]);
    }
    // shrubs and small trees only in free land: outside the house footprint, clear of pad and path
    const R = factor * 0.74, spots: Pt[] = [], cap = service ? 4 : patio ? 3 : 8;
    const nearEntry = (px: number, py: number) => px > a - .22 && py > a - .22 && px < a + (patio ? .75 : .5) && (patio || py > a - .22);
    for (let i = 0; i < 70 && spots.length < cap; i++) {
      const px = (r() * 2 - 1) * (EDGE - .1), py = (r() * 2 - 1) * (EDGE - .1);
      if (Math.max(Math.abs(px), Math.abs(py)) < R || nearEntry(px, py)) continue;
      if (spots.some(q => Math.hypot(q[0] - px, q[1] - py) < .3)) continue;
      spots.push([px, py]);
    }
    spots.sort((p, q) => (p[0] + p[1]) - (q[0] + q[1]));
    const clumps = CLUMPS.map(([k, cx, cy, rad]) => {
      const img = tex('s:' + k); if (!img) return null; const cc = makeCanvas(rad * 2, rad * 2), g = cc.getContext('2d')!;
      g.drawImage(img, cx - rad, cy - rad, rad * 2, rad * 2, 0, 0, rad * 2, rad * 2);
      const gr = g.createRadialGradient(rad, rad, rad * .5, rad, rad, rad); gr.addColorStop(0, 'white'); gr.addColorStop(1, 'rgba(255,255,255,0)');
      g.globalCompositeOperation = 'destination-in'; g.fillStyle = gr; g.fillRect(0, 0, rad * 2, rad * 2); return cc;
    }).filter((q): q is HTMLCanvasElement => !!q);
    const fc = makeCanvas(), fx = fc.getContext('2d')!;
    for (const sp of spots) {
      if (!clumps.length) break;
      const mp = m(sp), [px, py] = pr(...mp), cl = clumps[Math.floor(r() * clumps.length)], w = (patio ? 28 : 30) + r() * 14;
      const isFront = (mp[0] + mp[1]) > 0.3 && Math.abs(mp[0] - mp[1]) > .5, ctx = isFront ? fx : x;
      ctx.save(); ctx.fillStyle = 'rgba(40,34,26,.22)'; ctx.beginPath(); ctx.ellipse(px+5, py+2.5, w * .42, w * .16, 0, 0, Math.PI * 2); ctx.fill();
      ctx.drawImage(cl, px - w / 2, py - w * .72, w, w); ctx.restore();
      if (isFront) front.push(mp);
    }
    if (front.length) { scene.textures.addCanvas(fkey, fc); }
  }
  scene.textures.addCanvas(key, c);
  return { back: key, front: front.length ? fkey : undefined };
}
