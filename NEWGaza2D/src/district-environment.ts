/** Artistic surroundings, not surveyed district boundaries or official land-use data. */
export interface DistrictEnvironment {
  style: 'agricultural' | 'urban' | 'coastal';
  vegetation: 'olive' | 'citrus' | 'mixed' | 'palms';
  farmland: number;
  historic: boolean;
  seed: number;
  /** Coastal profiles: the sea lies on geographic west = low iso lx (screen upper-left). */
  rashidRoad?: boolean;
  /** Outer-edge landmark dressing drawn by district-life. */
  fringe?: 'greenhouses' | 'orchard-walls' | 'low-rise' | 'boats';
}
const PROFILES: Record<string, DistrictEnvironment> = {
  shujaiya: { style: 'agricultural', vegetation: 'mixed', farmland: .8, historic: false, seed: 101, fringe: 'greenhouses' },
  tuffah: { style: 'agricultural', vegetation: 'citrus', farmland: .85, historic: false, seed: 211, fringe: 'orchard-walls' },
  'sheikh-radwan': { style: 'urban', vegetation: 'mixed', farmland: .15, historic: false, seed: 307, fringe: 'low-rise' },
  daraj: { style: 'urban', vegetation: 'olive', farmland: .05, historic: true, seed: 401, fringe: 'low-rise' },
  karama: { style: 'agricultural', vegetation: 'mixed', farmland: .65, historic: false, seed: 503, fringe: 'greenhouses' },
  'old-city': { style: 'urban', vegetation: 'olive', farmland: .05, historic: true, seed: 601, fringe: 'low-rise' },
  nasr: { style: 'urban', vegetation: 'mixed', farmland: .2, historic: false, seed: 701, fringe: 'low-rise' },
  sabra: { style: 'urban', vegetation: 'olive', farmland: .25, historic: false, seed: 809, fringe: 'low-rise' },
  zeitoun: { style: 'agricultural', vegetation: 'olive', farmland: .8, historic: false, seed: 907, fringe: 'orchard-walls' },
  rimal: { style: 'coastal', vegetation: 'palms', farmland: .1, historic: false, seed: 1009, fringe: 'boats', rashidRoad: true },
  'tel-al-hawa': { style: 'urban', vegetation: 'mixed', farmland: .15, historic: false, seed: 1103, fringe: 'low-rise' },
  'sheikh-ijlin': { style: 'coastal', vegetation: 'palms', farmland: .35, historic: false, seed: 1201, fringe: 'boats', rashidRoad: true },
  shati: { style: 'coastal', vegetation: 'palms', farmland: .05, historic: false, seed: 1409, fringe: 'boats', rashidRoad: true },
  rashid: { style: 'coastal', vegetation: 'palms', farmland: .1, historic: false, seed: 1301, fringe: 'boats', rashidRoad: true },
};
export function districtEnvironment(id: string): Readonly<DistrictEnvironment> {
  const profile = PROFILES[id];
  if (!profile) throw new Error(`Unknown district environment: ${id}`);
  return profile;
}
