import manifest from './environment-manifest.json';

export const ENVIRONMENT_IDS = Object.keys(manifest).map(Number);
export const environmentUrl = (id: number) => `${import.meta.env.BASE_URL}art/realistic/environment-${id}.webp?v=coastal-painted`;

/** Stamp an illustrated ground patch in world/sprite coordinates, at its ground centre. */
export function groundStamp(c: CanvasRenderingContext2D, image: CanvasImageSource, id: number,
  x: number, y: number, width: number, opacity = 1, mirrored = false) {
  const m = manifest[String(id) as keyof typeof manifest], height = width * m.h / m.w;
  c.save(); c.globalAlpha = opacity; c.translate(x, y); c.scale(mirrored ? -1 : 1, 1);
  c.drawImage(image, -width / 2, -height * m.ay, width, height); c.restore();
}
