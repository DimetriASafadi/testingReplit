/** Artistic surroundings, not surveyed district boundaries or official land-use data. */
export interface DistrictEnvironment {
  style: 'agricultural' | 'urban' | 'coastal';
  vegetation: 'olive' | 'citrus' | 'mixed' | 'palms';
  farmland: number;
  historic: boolean;
  seed: number;
}
const PROFILES: Record<string, DistrictEnvironment> = {
  shujaiya: { style: 'agricultural', vegetation: 'mixed', farmland: .8, historic: false, seed: 101 },
  tuffah: { style: 'agricultural', vegetation: 'citrus', farmland: .85, historic: false, seed: 211 },
  'sheikh-radwan': { style: 'urban', vegetation: 'mixed', farmland: .15, historic: false, seed: 307 },
  daraj: { style: 'urban', vegetation: 'olive', farmland: .05, historic: true, seed: 401 },
  karama: { style: 'agricultural', vegetation: 'mixed', farmland: .65, historic: false, seed: 503 },
  'old-city': { style: 'urban', vegetation: 'olive', farmland: .05, historic: true, seed: 601 },
  nasr: { style: 'urban', vegetation: 'mixed', farmland: .2, historic: false, seed: 701 },
  sabra: { style: 'urban', vegetation: 'olive', farmland: .25, historic: false, seed: 809 },
  zeitoun: { style: 'agricultural', vegetation: 'olive', farmland: .8, historic: false, seed: 907 },
  rimal: { style: 'coastal', vegetation: 'palms', farmland: .1, historic: false, seed: 1009 },
  'tel-al-hawa': { style: 'urban', vegetation: 'mixed', farmland: .15, historic: false, seed: 1103 },
  'sheikh-ijlin': { style: 'coastal', vegetation: 'palms', farmland: .35, historic: false, seed: 1201 },
  rashid: { style: 'coastal', vegetation: 'palms', farmland: .1, historic: false, seed: 1301 },
};
export function districtEnvironment(id: string): Readonly<DistrictEnvironment> {
  const profile = PROFILES[id];
  if (!profile) throw new Error(`Unknown district environment: ${id}`);
  return profile;
}
