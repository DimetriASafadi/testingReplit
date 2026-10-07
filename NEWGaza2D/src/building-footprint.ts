/** A plot is land: smaller buildings leave usable space rather than filling it. */
export interface BuildingFootprint { factor: number; yard: 'garden' | 'patio' | 'service' | 'none' }
const FULL_LOT: Readonly<BuildingFootprint> = { factor: 1, yard: 'none' };
const COMPACT: Record<string, Readonly<BuildingFootprint>> = {
  smallhouse: { factor: .62, yard: 'garden' },
  house: { factor: .78, yard: 'garden' },
  villa: { factor: .84, yard: 'garden' },
  tourist: { factor: .84, yard: 'garden' },
  kiosk: { factor: .62, yard: 'patio' },
  foodshop: { factor: .64, yard: 'patio' },
  clothes: { factor: .60, yard: 'patio' },
  shoe: { factor: .58, yard: 'patio' },
  falafel: { factor: .60, yard: 'patio' },
  shawarma: { factor: .62, yard: 'patio' },
  western: { factor: .72, yard: 'patio' },
  cafe: { factor: .65, yard: 'patio' },
  luxury: { factor: .88, yard: 'patio' },
  muni: { factor: .88, yard: 'service' },
  tmall: { factor: .92, yard: 'service' },
  mmall: { factor: .92, yard: 'service' },
};
/** Ground footprint and roof height are independent constraints. A one-storey
 * shop must not have an apartment tower's height merely because its PNG is tall. */
const HEIGHTS: Record<string, number> = {
  smallhouse: 82, house: 124, villa: 137, tourist: 118,
  foodshop: 78, clothes: 76, shoe: 76, falafel: 76, shawarma: 78,
  western: 84, kiosk: 78, cafe: 86, luxury: 127,
  muni: 145, tmall: 164, mmall: 164,
  work: 124, food_factory: 112, water: 114,
  glass: 149, cement: 167, steel: 151, asphalt: 163,
  apt4: 174, h6: 187, complex: 181, modern: 187, trad: 164, tower: 187,
};
export function buildingMaxHeight(finalSprite: string) {
  return HEIGHTS[finalSprite.replace(/_(front|back)$/, '')] ?? 187;
}
/** Front/back variants use the same footprint; construction inherits its final sprite. */
export function buildingFootprint(finalSprite: string): Readonly<BuildingFootprint> {
  return COMPACT[finalSprite.replace(/_(front|back)$/, '')] ?? FULL_LOT;
}
