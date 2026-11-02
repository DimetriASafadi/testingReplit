import { LAND_WORLD_EDGE, PLOT_WORLD_W } from './lot-geometry';
import { architecturalScale } from './architectural-scale';
import manifest from './realistic-manifest.json';
/** A plot is land: smaller buildings leave usable space rather than filling it. */
export interface BuildingFootprint { factor: number; yard: 'garden' | 'patio' | 'service' | 'none' }
const FULL_LOT: Readonly<BuildingFootprint> = { factor: 1, yard: 'none' };
const COMPACT: Record<string, Readonly<BuildingFootprint>> = {
  smallhouse: { factor: .62, yard: 'garden' },
  house: { factor: .78, yard: 'garden' },
  villa: { factor: .84, yard: 'garden' },
  tourist: { factor: .84, yard: 'garden' },
  kiosk: { factor: .62, yard: 'patio' },
  wood: { factor: .78, yard: 'service' }, hshop: { factor: .64, yard: 'patio' },
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
  school: { factor: .9, yard: 'service' },
  hospital: { factor: .9, yard: 'service' },
  mosque: { factor: .86, yard: 'service' },
  church: { factor: .84, yard: 'service' },
  smarket: { factor: .84, yard: 'service' },
  toys: { factor: .62, yard: 'patio' },
  juiceshop: { factor: .62, yard: 'patio' },
};
/** Ground footprint and roof height are independent constraints. A one-storey
 * shop must not have an apartment tower's height merely because its PNG is tall. */
const HEIGHTS: Record<string, number> = {
  smallhouse: 82, house: 124, villa: 137, tourist: 118,
  wood: 92, hshop: 84, foodshop: 78, clothes: 76, shoe: 76, falafel: 76, shawarma: 78,
  western: 84, kiosk: 78, cafe: 86, luxury: 127,
  muni: 145, tmall: 164, mmall: 164,
  school: 118, hospital: 176, mosque: 172, church: 136, club: 112, rpark: 124,
  smarket: 112, toys: 80, juiceshop: 80, drinks: 142, juicef: 134,
  work: 124, food_factory: 112, water: 114,
  glass: 149, cement: 167, steel: 151, asphalt: 163,
  apt4: 174, h6: 187, complex: 181, modern: 187, trad: 164, tower: 187,
};
export function buildingMaxHeight(finalSprite: string) {
  return HEIGHTS[finalSprite.replace(/_(front|back|right|left)$/, '')] ?? 187;
}
/** All four variants use the same footprint; construction inherits its final sprite. */
export function buildingFootprint(finalSprite: string): Readonly<BuildingFootprint> {
  return COMPACT[finalSprite.replace(/_(front|back|right|left)$/, '')] ?? FULL_LOT;
}
/** Fraction of the actual 2x2 parcel's projected width/depth. Open-land uses
 * nearly the whole lot; these are world dimensions, not PNG scale factors. */
const LAND_COVERAGE: Record<string,number> = {
  rpark:.90, club:.88, zoo:.92,
  wheat:.92, citrus:.92, olive:.92, veg:.92, straw:.92, cattle:.92,
  palms:.92, corn:.92, trees:.92, orn:.92, grapes:.92,
  western:.66, luxury:.74, cafe:.56,
  tower:.92,
};
const stemOf = (key:string) => key === 'farm_cattle' ? 'cattle' : key.replace(/_(front|back|right|left)$/, '');
/** Public/pure dimensions also used to test every catalog family's parcel fit.
 * Keep the accepted small-house/villa garden proportions, not a giant house. */
export function buildingLotDimensions(finalKey: string) {
  const stem=stemOf(finalKey), house=['smallhouse','house','villa','tourist'].includes(stem);
  const coverage=LAND_COVERAGE[stem] ?? (house ? .55 : .70)*buildingFootprint(finalKey).factor;
  return {width:PLOT_WORLD_W*coverage,height:PLOT_WORLD_W*coverage/2};
}
/** Pure render geometry shared by sprites, construction, shadows and entrances.
 * Height means structure ABOVE the back ground edge, not the whole image:
 * counting the land diamond as building height disproportionately shrinks farms. */
export function buildingRenderScale(finalKey: string, m: { w: number; h: number; ay?:number; ground?: { w: number; h: number } }, base = .72) {
  const metrics = manifest as Record<string, typeof m>;
  const stem = stemOf(finalKey), target=buildingLotDimensions(finalKey);
  const faces = ['front', 'right', 'back', 'left'].map(face =>
    metrics[stem === 'cattle' && face === 'front' ? 'farm_cattle' : `${stem}_${face}`]).filter(Boolean);
  // One world scale for all views: a different silhouette/yard cannot make the
  // building suddenly grow or shrink when the player rotates it.
  const one = (v: typeof m) => {
    const gw = v.ground?.w ?? v.w, gh = v.ground?.h ?? gw / 2;
    const elevation=Math.max(1,v.h*(v.ay ?? 1-gh/(2*v.h))-gh/2);
    return Math.min(architecturalScale(stem,v.w)*base/.72,
      target.width/gw*base/.72,target.height/gh*base/.72,
      buildingMaxHeight(finalKey)/elevation,
      PLOT_WORLD_W*LAND_WORLD_EDGE/gw,PLOT_WORLD_W*LAND_WORLD_EDGE/2/gh);
  };
  return Math.min(one(m), ...faces.map(one));
}

const yardRadii=new Map<string,number>();
/** Reserved central square in yard-canvas coordinates, shared by all faces.
 * Quantized upward so no accessory intrudes and the texture cache stays bounded. */
export function buildingYardRadius(finalKey:string):number{
  const stem=stemOf(finalKey),cached=yardRadii.get(stem);
  if(cached!==undefined)return cached;
  const metrics=manifest as Record<string,{w:number;h:number;ay:number;ground?:{w:number;h:number}}>;
  const m=metrics[finalKey];if(!m)return .65;
  const radius=Math.min(1.24,Math.ceil(((m.ground?.w??m.w)*buildingRenderScale(finalKey,m)/(PLOT_WORLD_W*.72)+.10)*25)/25);
  yardRadii.set(stem,radius);return radius;
}
