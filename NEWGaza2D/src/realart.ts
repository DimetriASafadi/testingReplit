// Mapping from catalog buildings / projects to the pre-rendered photoreal sprites (see tools/pack_art.py).
import manifest from './realistic-manifest.json';
import { building } from './catalog';
import type { Orientation } from './model';
import { ORIENTATION_FACES } from './orientation';
import { availableBuildingFace } from './artwork-face';

export interface SprMeta { w: number; h: number; ay: number; ground?: { w: number; h: number } }
export const SPR = manifest as Record<string, SprMeta>;
export const SPRITE_KEYS = Object.keys(SPR);
export const GROUND_KEYS = ['earth', 'sand', 'asphalt', 'gravel', 'water'] as const;
const ART_VERSION = 'four-sides-20261026';
export const sprUrl = (k: string) => `${import.meta.env.BASE_URL}art/realistic/s/${k}.webp?v=${ART_VERSION}`;
export const groundUrl = (k: string) => `${import.meta.env.BASE_URL}art/realistic/ground-${k}.jpg?v=${ART_VERSION}`;

const KEY: Record<string, string> = {
  work: 'work', equipment_store: 'equip', recycling: 'recycling', glass: 'glass', cement: 'cement', steel: 'steel', asphalt: 'asphalt', food_factory: 'food_factory', water_treatment: 'water', wood_factory: 'wood', household_shop: 'hshop',
  small_house: 'smallhouse', medium_house: 'house', villa: 'villa', housing_4: 'apt4', housing_6: 'h6', housing_complex: 'complex', modern_housing: 'modern', traditional_housing: 'trad', housing_tower: 'tower', tourist_villa: 'tourist',
  food_shop: 'foodshop', clothes_shop: 'clothes', shoe_shop: 'shoe', traditional_mall: 'tmall', modern_mall: 'mmall', municipality: 'muni',
  zoo: 'zoo', falafel: 'falafel', shawarma: 'shawarma', western_food: 'western', luxury_restaurant: 'luxury', cafe: 'cafe', snack_kiosk: 'kiosk',
  school: 'school', hospital: 'hospital', sports_club: 'club', recreation_park: 'rpark', mosque: 'mosque', church: 'church',
  wheat: 'wheat', citrus: 'citrus', olive: 'olive', vegetables: 'veg', strawberry: 'straw', cattle: 'cattle', palms: 'palms', corn: 'corn', protective_trees: 'trees', ornamental_trees: 'orn',
};
export const isFarm = (id: string) => building(id).category === 'farm' || id === 'zoo';
const reportedFaces = new Set<string>();
export function builtKey(id: string, orient: Orientation) {
  const k = KEY[id]; if (!k) throw new Error(`Unknown building artwork: ${id}`);
  const face = availableBuildingFace(k, orient, key => !!SPR[key]);
  const requested = `${k}_${ORIENTATION_FACES[orient]}`;
  if (face.missing && !reportedFaces.has(requested)) {
    reportedFaces.add(requested);
    console.warn(`Incomplete building quartet: ${requested}; displaying same building as ${face.key}`);
    if (typeof window !== 'undefined') window.dispatchEvent(new Event('city-art-incomplete'));
  }
  return face.key;
}
/** construction archetype for the civic/leisure additions (independent of catalog category) */
const CIVIC_CONSTRUCT: Record<string, string> = { school: 'shop_c', hospital: 'apt_c', mosque: 'shop_c', church: 'house_c', sports_club: 'shop_c', recreation_park: 'house_c' };
export function constructKey(id: string, stage: number) {
  if (CIVIC_CONSTRUCT[id]) return `${CIVIC_CONSTRUCT[id]}${stage}`;
  const d = building(id); let base = 'house_c';
  if (id === 'housing_tower') base = 'tower_c'; else if (d.category === 'industry' || d.category === 'equipment') base = 'ind_c';
  else if (d.category === 'commerce' || d.category === 'leisure') base = 'shop_c'; else if (d.floors >= 4) base = 'apt_c';
  return `${base}${stage}`;
}
export function ruinKey(id: string, variant: number) {
  // Former floor count does not imply a standing tower after collapse.
  // Mostly low piles/slabs, some medium frames, occasional tall remnants.
  const hash = ((variant + 1) * 73 + id.length * 19) % 100;
  const f = building(id).floors;
  const tier = f <= 2 || hash < 58 ? 0 : f <= 4 || hash < 88 ? 1 : 2;
  return `ruin${tier * 4 + ((variant * 3 + id.length) % 4)}`;
}
/** project sites: [archetype used for construction/finished sprite] */
const SITE: Record<string, string> = { water: 'water', power: 'ind', housing: 'apt4', park: 'orn', services: 'muni', farm: 'wheat', commerce: 'mmall', industry: 'steel' };
export function siteKey(projectId: string, stage: 0 | 1 | 2, q: number): string | null {
  const a = SITE[projectId]; if (stage === 0 || !a) return null;
  if (stage === 2) return `${a}_front`;
  return a === 'water' || a === 'ind' || a === 'steel' ? `ind_c${q}` : a === 'apt4' ? `apt_c${q}` : a === 'mmall' || a === 'muni' ? `shop_c${q}` : `house_c${q}`;
}
/** DOM preview uses the exact scene sprite */
export function previewUrl(id: string, orient: Orientation) { return sprUrl(builtKey(id, orient)); }
