/** Stable random city blocks: adjacent lots share a block, not an asphalt grid. */
export const LOT_COUNT = 144;
export const INITIAL_EMPTY = [0, 1, 8, 9, 15, 48, 55, 56, 57, 62, 63];
export interface CityBlock { x0: number; y0: number; x1: number; y1: number; roadX: number; roadY: number; plotIds: number[] }
export interface LayoutCell { id: number; x: number; y: number; block: number }
const cache = new Map<string, ReturnType<typeof generate>>();
function random(id: string) {
  let seed = 2166136261;
  for (const c of id) seed = Math.imul(seed ^ c.charCodeAt(0), 16777619);
  return () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; };
}
function generate(id: string, revision = 2) {
  const r = random(id);
  const axis = () => {
    const positions: number[] = [], groups: { first: number; last: number; start: number; end: number; road: number }[] = [];
    const roads = [-.5]; let cursor = 0;
    while (positions.length < 12) {
      const choice = revision === 2 && positions.length === 0 ? 1 : revision === 2 && positions.length === 1 ? 2 : r() < .66 ? 2 : 1;
      const count = Math.min(12 - positions.length, choice);
      const first = positions.length, start = cursor;
      for (let j = 0; j < count; j++) { positions.push(cursor); cursor += 2; }
      const gap = Math.round((.95 + r() * .25) * 100) / 100;
      const road = cursor + gap / 2;
      groups.push({ first, last: positions.length - 1, start, end: cursor, road });
      roads.push(road); cursor += gap;
    }
    return { positions, groups, roads, end: groups.at(-1)!.end + .65 };
  };
  const x = axis(), y = axis();
  // Preserve the identities/relative arrangement of the original 8×8 plots.
  const cells: LayoutCell[] = [];
  const coords: [number, number][] = Array.from({ length: 64 }, (_, i) => [i % 8, Math.floor(i / 8)]);
  for (let row = 0; row < 12; row++) for (let col = 0; col < 12; col++)
    if (col >= 8 || row >= 8) coords.push([col, row]);
  const blocks: CityBlock[] = [];
  for (const gy of y.groups) for (const gx of x.groups)
    blocks.push({ x0: gx.start, y0: gy.start, x1: gx.end, y1: gy.end, roadX: gx.road, roadY: gy.road, plotIds: [] });
  coords.forEach(([col, row], plotId) => {
    const gx = x.groups.findIndex(g => col >= g.first && col <= g.last);
    const gy = y.groups.findIndex(g => row >= g.first && row <= g.last);
    const block = gy * x.groups.length + gx;
    cells.push({ id: plotId, x: x.positions[col], y: y.positions[row], block });
    blocks[block].plotIds.push(plotId);
  });
  return { cells, blocks, roadX: x.roads, roadY: y.roads, extent: Math.max(x.end, y.end) };
}
export function cityLayout(id: string, revision = 2) {
  const key = `${id}:${revision}`;
  let layout = cache.get(key);
  if (!layout) { layout = generate(id, revision); cache.set(key, layout); }
  return layout;
}
export function plotBlock(p: { id: number; layoutId?: string }) {
  if (!p.layoutId) return undefined;
  const l = cityLayout(p.layoutId); return l.blocks[l.cells[p.id].block];
}
