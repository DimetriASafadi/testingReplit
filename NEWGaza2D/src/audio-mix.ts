export const AUDIO_CLIPS = ['excavator','bulldozer','truck','rubble','construction','industry','farm','neighborhood','coast','complete'] as const;
export type AudioClip = typeof AUDIO_CLIPS[number];
export interface AudioSource { id: string; clip: AudioClip; x: number; y: number; volume: number; ambient?: boolean }
export interface AudioFocus { x: number; y: number; zoom: number }
const clamp = (n: number, a: number, b: number) => Math.max(a, Math.min(b, n));

/** Screen-isometric ground focus, not the apparent top of a tall building. */
export function spatialMix(source: AudioSource, focus: AudioFocus) {
  const z = clamp((focus.zoom - .65) / .95, 0, 1), zoom = z*z*(3-2*z);
  const distance = Math.hypot(source.x-focus.x, (source.y-focus.y)*2);
  const near = clamp(1-distance/850,0,1);
  return { gain: source.volume*zoom*near*near, pan: clamp((source.x-focus.x)/550,-.8,.8),
    cutoff: 1300 + 6700*zoom*near };
}
export function selectVoices(sources: AudioSource[], focus: AudioFocus, ambience: number, effects: number) {
  return sources.map(source => ({ ...source, ...spatialMix(source,focus),
    level: source.ambient ? ambience : effects }))
    .map(s=>({...s,gain:s.gain*s.level}))
    .filter(s=>s.gain>.001).sort((a,b)=>b.gain-a.gain).slice(0,8);
}
