export interface SpriteOpacity { width: number; height: number; alpha: Uint8Array }
type Source = HTMLImageElement | HTMLCanvasElement;
type Reader = (source: Source) => SpriteOpacity;

/** Immutable sprite sources are shared. Never read canvas/GPU pixels in the animation loop. */
export class SpriteOpacityCache {
  private readonly masks = new WeakMap<object, SpriteOpacity>();
  private canvas?: HTMLCanvasElement;
  constructor(private readonly reader?: Reader) {}
  prepare(source: Source): SpriteOpacity {
    const hit = this.masks.get(source); if (hit) return hit;
    const mask = this.reader ? this.reader(source) : this.read(source);
    this.masks.set(source, mask); return mask;
  }
  private read(source: Source): SpriteOpacity {
    const width = 'naturalWidth' in source ? source.naturalWidth : source.width;
    const height = 'naturalHeight' in source ? source.naturalHeight : source.height;
    if (!width || !height) throw new Error('Cannot prepare opacity for an unloaded sprite');
    const canvas = this.canvas ?? (this.canvas = document.createElement('canvas'));
    canvas.width = width; canvas.height = height;
    const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
    ctx.drawImage(source, 0, 0);
    const rgba = ctx.getImageData(0, 0, width, height).data, alpha = new Uint8Array(width * height);
    for (let i = 0; i < alpha.length; i++) alpha[i] = rgba[i * 4 + 3];
    return { width, height, alpha };
  }
}

export function spriteAlphaAt(mask: SpriteOpacity, x: number, y: number): number {
  x = Math.floor(x); y = Math.floor(y);
  return x >= 0 && y >= 0 && x < mask.width && y < mask.height ? mask.alpha[y * mask.width + x] : 0;
}
