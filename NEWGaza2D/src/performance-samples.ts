import type { DistrictState } from './model';

/** Non-security checksum. Excludes timers, income and camera that advance normally. */
export function progressFingerprint(d: DistrictState) {
  const data = JSON.stringify({
    plots: d.plots.map(p => [p.id, p.status, p.buildingId, p.orientation]),
    projects: d.projects.map(p => [p.id, p.status]),
    paved: [...(d.pavedRoads ?? [])].sort(), claimed: d.claimed,
    celebrated: d.celebrated, rewarded: d.completionRewarded,
  });
  let hash = 2166136261;
  for (let i = 0; i < data.length; i++) hash = Math.imul(hash ^ data.charCodeAt(i), 16777619);
  return (hash >>> 0).toString(16).padStart(8, '0');
}

/** Bounded samples of Phaser frames, not display requestAnimationFrame ticks. */
export class FrameWindow {
  private values: number[] = [];
  dropped = 0;
  add(ms: number) {
    if (!Number.isFinite(ms) || ms <= 0) return;
    if (this.values.length < 1200) this.values.push(ms);
    else this.dropped++;
  }
  report() {
    const values = [...this.values].sort((a, b) => a - b);
    const total = values.reduce((a, b) => a + b, 0);
    const percentile = (p: number) => values.length ? values[Math.min(values.length - 1, Math.floor(values.length * p))] : null;
    return { frames: values.length, dropped: this.dropped, fps: total ? values.length * 1000 / total : null,
      p50Ms: percentile(.5), p95Ms: percentile(.95), p99Ms: percentile(.99),
      maxMs: values.at(-1) ?? null, over50Ms: values.filter(ms => ms > 50).length };
  }
  reset() { this.values = []; this.dropped = 0; }
}
