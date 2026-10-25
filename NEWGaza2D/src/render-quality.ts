/** Cap framebuffer cost, not artwork resolution. Coordinates/saves remain world units. */
export function renderDensity(dpr: number, width: number, height: number) {
  return Math.max(1, Math.min(2, dpr || 1, Math.sqrt(2500000 / Math.max(1, width * height))));
}
export const PIXEL_DENSITY = typeof window === 'undefined' ? 1
  : renderDensity(window.devicePixelRatio, window.innerWidth, window.innerHeight);
