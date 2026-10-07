import type Phaser from 'phaser';
import { ART_SC, ruinScale } from './art-scale';
import type { SprMeta } from './realart';
import manifest from './clearing-manifest.json';

export const CLEARING_META = manifest as Record<string, SprMeta>;
export const CLEARING_SIZE = { w: 256, h: 384, ay: .75 };
const VERSION = 'painted-clearing-2';
export function preloadClearing(scene: Phaser.Scene) {
  for (const k of Object.keys(CLEARING_META))
    scene.load.image('clear:' + k, `${import.meta.env.BASE_URL}art/clearing/${k}.webp?v=${VERSION}`);
}

/** Baked immutable artwork, built only at a stage change, not in the animation hot path. */
export function clearingCanvas(_ruin: CanvasImageSource, meta: SprMeta, pile: CanvasImageSource, pileMeta: SprMeta, _remnants: boolean) {
  const canvas = document.createElement('canvas'); canvas.width = CLEARING_SIZE.w; canvas.height = CLEARING_SIZE.h;
  const g = canvas.getContext('2d')!;
  const cx = canvas.width / 2, cy = canvas.height * CLEARING_SIZE.ay;
  // Keep the original ruin's footprint, not a new 220px mound for every lot.
  // No clipped upright wall: a flat clearing surface must not cover the tracks.
  const width = Math.min(154, meta.w * ruinScale(meta.h, ART_SC)) / ART_SC;
  const height = pileMeta.h * width / pileMeta.w;
  g.drawImage(pile, cx - width / 2, cy - height * pileMeta.ay, width, height);
  return canvas;
}
