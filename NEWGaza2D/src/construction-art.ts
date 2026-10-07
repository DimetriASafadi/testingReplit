import type { BuildingDef } from './model';
import type { SprMeta } from './realart';

/**
 * Build the selected architecture, not a substitute generic completed building.
 * Real render layers supply the masonry/detail; scaffolding is a separate overlay.
 * Both views use their own final render (never mirror a front entrance into a rear).
 */
export function constructionCanvas(final: CanvasImageSource, foundation: CanvasImageSource,
  meta: SprMeta, def: BuildingDef, stage: number, orientation: 0 | 1) {
  const cv = document.createElement('canvas'); cv.width = meta.w; cv.height = meta.h;
  const c = cv.getContext('2d')!;
  const ground = meta.h * meta.ay, base = meta.w / 4;
  const farm = def.category === 'farm' || def.id === 'zoo';
  if (stage === 0) {
    // Excavated ground and foundations retain the chosen footprint/ground anchor.
    const fh = Math.min(meta.h, farm ? base * 2.1 : base * 2.3);
    c.drawImage(foundation, 0, ground + base - fh, meta.w, fh);
    return cv;
  }
  if (farm) {
    // Planting spreads across the actual field/orchard; bare earth remains between batches.
    const fh = base * 2.1;
    c.drawImage(foundation, 0, ground + base - fh, meta.w, fh);
    const fraction = stage === 1 ? 0.48 : 0.84;
    c.save(); c.beginPath();
    const bands = 8, band = meta.w / bands;
    for (let i = 0; i < bands; i++) {
      if ((i * 3 + def.id.length) % bands < fraction * bands)
        c.rect(orientation ? meta.w - (i + 1) * band : i * band, 0, band + 1, meta.h);
    }
    c.clip(); c.drawImage(final, 0, 0); c.restore();
    return cv;
  }
  // Shell grows upward in the selected building's actual silhouette.
  const wallTop = Math.max(0, ground - base);
  const height = wallTop * (stage === 1 ? 0.56 : 0.96);
  const cut = Math.max(0, wallTop - height);
  c.save(); c.beginPath(); c.rect(0, cut, meta.w, meta.h - cut); c.clip();
  c.filter = stage === 1 ? 'grayscale(1) brightness(0.86)' : 'saturate(0.38) brightness(0.95)';
  c.drawImage(final, 0, 0); c.restore();
  // Unpainted formwork edges and slim timber/metal scaffolding at facade planes.
  const left = meta.w * 0.08, right = meta.w * 0.92, middle = meta.w * 0.5;
  const bottom = ground + base * 0.65, level = Math.max(15, (bottom - cut) / Math.max(2, def.floors));
  c.lineWidth = 1.15; c.strokeStyle = 'rgba(75,64,49,.84)';
  for (let x = left; x <= right; x += Math.max(18, meta.w / 7)) {
    const footprint = bottom - Math.abs(x - middle) * 0.44;
    c.beginPath(); c.moveTo(x, Math.max(cut + 3, 5)); c.lineTo(x, footprint + 3); c.stroke();
    for (let y = footprint; y > cut + level; y -= level) {
      c.beginPath(); c.moveTo(x, y); c.lineTo(Math.min(x + 22, right), y - level); c.stroke();
    }
  }
  for (let y = bottom - 6; y > cut + 5; y -= level) {
    c.strokeStyle = '#a89773'; c.lineWidth = 2;
    c.beginPath(); c.moveTo(left, y - base * .35); c.lineTo(middle, y);
    c.lineTo(right, y - base * .35); c.stroke();
    c.strokeStyle = '#5e625f'; c.lineWidth = .8;
    c.beginPath(); c.moveTo(left, y - base * .35 - 6); c.lineTo(middle, y - 6);
    c.lineTo(right, y - base * .35 - 6); c.stroke();
  }
  return cv;
}
