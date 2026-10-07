import type Phaser from 'phaser';
import { makeCanvas } from './art';
import { GK, type GroundFrame } from './ground';

/** WebGL 1 guarantees 2048, not the 4105+ pixels of an expanded district. */
export function groundTileRects(width: number, height: number, limit = 2048) {
  if (![width, height, limit].every(n => Number.isSafeInteger(n) && n > 0))
    throw new Error('Invalid ground texture dimensions');
  const tiles: { x: number; y: number; width: number; height: number }[] = [];
  for (let y = 0; y < height; y += limit) for (let x = 0; x < width; x += limit)
    tiles.push({ x, y, width: Math.min(limit, width - x), height: Math.min(limit, height - y) });
  return tiles;
}

/** Split only the rendered texture: world coordinates and art resolution stay unchanged. */
export function installGroundTiles(scene: Phaser.Scene, source: HTMLCanvasElement,
  frame: GroundFrame, prefix: string, depth: number,
  keys: string[], images: Phaser.GameObjects.Image[]) {
  for (const tile of groundTileRects(source.width, source.height)) {
    const canvas = makeCanvas(tile.width, tile.height);
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('تعذر تخصيص ذاكرة لرسوم الأرض');
    ctx.drawImage(source, tile.x, tile.y, tile.width, tile.height, 0, 0, tile.width, tile.height);
    if (source.dataset.terrain) canvas.dataset.terrain = source.dataset.terrain;
    const key = `${prefix}:${tile.x}:${tile.y}`;
    // Record ownership before allocation, including a partially failed build.
    keys.push(key);
    scene.textures.addCanvas(key, canvas);
    images.push(scene.add.image(frame.ox + tile.x / GK, frame.oy + tile.y / GK, key)
      .setOrigin(0, 0).setScale(1 / GK).setDepth(depth));
  }
  // Do not retain the oversized staging buffer as well as the finished tiles.
  source.width = source.height = 0;
}
