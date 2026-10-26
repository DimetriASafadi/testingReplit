import type Phaser from 'phaser';
import { alphaBounds } from './render-work';

/** Upload only visible pixels. Phaser trim retains the original logical anchor/scale. */
export function addTrimmedTexture(scene: Phaser.Scene, key: string, source: HTMLCanvasElement) {
  const width = source.width, height = source.height;
  const ctx = source.getContext('2d', { willReadFrequently: true })!;
  const b = alphaBounds(ctx.getImageData(0, 0, width, height).data, width, height);
  const cv = document.createElement('canvas');
  cv.width = b.width; cv.height = b.height;
  cv.getContext('2d')!.drawImage(source, b.x, b.y, b.width, b.height, 0, 0, b.width, b.height);
  const texture = scene.textures.addCanvas(key, cv);
  if (!texture) throw new Error(`Cannot allocate yard texture: ${key}`);
  texture.get().setTrim(width, height, b.x, b.y, b.width, b.height);
  source.width = source.height = 0;
}
