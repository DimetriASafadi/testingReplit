// Pure parcel geometry (no DOM/Phaser), shared by yard art and tests.
// A plot is 2x2 world units; yard canvases render at ART_SC around anchor (YARD_W/2, 380).
export const LOT_U = 64, LOT_ART_SC = .72, YARD_W = 360, YARD_H = 470, YARD_ANCHOR = 380;
/** world half-size of the dressed land: leaves a .06 seam to the block kerb/road */
export const LAND_WORLD_EDGE = .94;
/** same edge in yard-canvas lot units (pre-ART_SC) */
export const YARD_EDGE = LAND_WORLD_EDGE / LOT_ART_SC;
/** canvas pixel bounds of the ±edge diamond (lot units) */
export function yardBounds(edge = YARD_EDGE, w = YARD_W) {
  return { left: w / 2 - 2 * edge * LOT_U, right: w / 2 + 2 * edge * LOT_U, top: YARD_ANCHOR - edge * LOT_U, bottom: YARD_ANCHOR + edge * LOT_U };
}
/** full plot diamond width in world px vs. dressed land width in world px */
export const PLOT_WORLD_W = 2 * 2 * LOT_U;
export const landWorldWidth = (edge = YARD_EDGE) => 2 * 2 * edge * LOT_U * LOT_ART_SC;
