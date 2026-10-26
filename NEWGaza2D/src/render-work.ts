/** Yield between expensive raster jobs; deadlines stay in wall-clock game state. */
export async function renderInBatches<T>(items: readonly T[], visit: (item: T) => void,
  alive: () => boolean, progress: (done: number, total: number) => void,
  clock = () => performance.now(),
  yieldFrame = () => new Promise<void>(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)))) {
  let started = clock();
  for (let i = 0; i < items.length; i++) {
    if (!alive()) return false;
    visit(items[i]);
    if (clock() - started >= 8 || (i + 1) % 4 === 0) {
      progress(i + 1, items.length);
      await yieldFrame();
      started = clock();
    }
  }
  return alive();
}

/** Alpha bounds, preserving every nontransparent pixel and a filtering gutter. */
export function alphaBounds(data: Uint8ClampedArray, width: number, height: number) {
  let left = width, top = height, right = -1, bottom = -1;
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    if (!data[(y * width + x) * 4 + 3]) continue;
    left = Math.min(left, x); right = Math.max(right, x);
    top = Math.min(top, y); bottom = Math.max(bottom, y);
  }
  if (right < 0) return { x: 0, y: 0, width: 1, height: 1 };
  left = Math.max(0, left - 2); top = Math.max(0, top - 2);
  return { x: left, y: top, width: Math.min(width, right + 3) - left, height: Math.min(height, bottom + 3) - top };
}
