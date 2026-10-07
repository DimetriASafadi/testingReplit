import Phaser from 'phaser';
import metadata from './machinery-manifest.json';
import type { EquipmentKind, GameState, Job, Plot, Unit } from './model';
import { U } from './art';

type Pt = [number, number];
type Pose = { tip: number[]; worldTip: number[] };
type Manifest = { size: number; ortho: number; foot: number[]; revision: string;
  clips: Record<string, string[]>; frames: Record<string, Pose> };
const META: Manifest = metadata;
const SCALE = .66, CYCLE = 7200;
const iso = (p: Pt): Pt => [(p[0] - p[1]) * U, (p[0] + p[1]) * U / 2];
const mix = (a: Pt, b: Pt, t: number): Pt => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
const clamp = (n: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, n));
const turnDelta = (a: number, b: number) => Math.atan2(Math.sin(b - a), Math.cos(b - a));
export const machinePreview = (kind: EquipmentKind) => `${import.meta.env.BASE_URL}art/machinery/${kind}-preview.webp?v=${META.revision}`;
export function preloadMachinery(scene: Phaser.Scene) {
  for (const kind of ['excavator', 'truck', 'bulldozer'])
    scene.load.atlas('machine:' + kind, `${import.meta.env.BASE_URL}art/machinery/${kind}.webp?v=${META.revision}`,
      `${import.meta.env.BASE_URL}art/machinery/${kind}.json?v=${META.revision}`);
}
interface Actor { a: Phaser.GameObjects.Image; b: Phaser.GameObjects.Image; heading: number; pos?: Pt;
  distance: number; dustAt: number; stoneAt: number; tip?: Pt }
interface Dump { position: Pt; start: number; ends: number }
const dock = (plot: Plot, kind: EquipmentKind): Pt => kind === 'excavator'
  ? [plot.x + 1.38, plot.y + 1.92]
  // Matching bed separation after the modest machine-scale increase keeps loading physical.
  : kind === 'truck' ? [plot.x + 2.095, plot.y + 1.92] : [plot.x + .72, plot.y + 1.92];
function along(points: Pt[], f: number): Pt {
  const lengths = points.slice(1).map((p, i) => Math.hypot(p[0] - points[i][0], p[1] - points[i][1]));
  let d = clamp(f, 0, 1) * lengths.reduce((a, b) => a + b, 0);
  for (let i = 0; i < lengths.length; i++) {
    if (d <= lengths[i] || i === lengths.length - 1) return mix(points[i], points[i + 1], lengths[i] ? d / lengths[i] : 0);
    d -= lengths[i];
  }
  return points[0];
}

/** Visual-only crew coordination. Saved deadlines, route selection and money stay in the engine. */
export class FleetAnimation {
  private actors = new Map<string, Actor>();
  private lastJobs = new Map<string, Job>();
  private dumps = new Map<string, Dump>();
  private idleHeadings = new Map<string, number>();
  private activeIds = new Set<string>();
  private previousTime = 0;
  private dust?: Phaser.GameObjects.Particles.ParticleEmitter;
  private stones?: Phaser.GameObjects.Particles.ParticleEmitter;
  constructor(private scene: Phaser.Scene) {}
  private particles() {
    if (this.dust) return;
    if (!this.scene.textures.exists('fleet:dust')) {
      const c = document.createElement('canvas'); c.width = c.height = 16; const g = c.getContext('2d')!;
      const fade = g.createRadialGradient(8, 8, 0, 8, 8, 8);
      fade.addColorStop(0, 'rgba(190,178,155,.65)'); fade.addColorStop(1, 'rgba(190,178,155,0)');
      g.fillStyle = fade; g.fillRect(0, 0, 16, 16); this.scene.textures.addCanvas('fleet:dust', c);
      const r = document.createElement('canvas'); r.width = r.height = 7; const q = r.getContext('2d')!;
      q.fillStyle = '#a49e90'; q.beginPath(); q.moveTo(1, 1); q.lineTo(5, 0); q.lineTo(7, 4); q.lineTo(3, 7); q.lineTo(0, 4); q.fill();
      this.scene.textures.addCanvas('fleet:stone', r);
    }
    this.dust = this.scene.add.particles(0, 0, 'fleet:dust', { emitting: false, lifespan: 650,
      speedX: { min: -10, max: 10 }, speedY: { min: -12, max: -3 }, scale: { start: .28, end: .82 },
      alpha: { start: .27, end: 0 }, maxParticles: 90 }).setDepth(1e5);
    this.stones = this.scene.add.particles(0, 0, 'fleet:stone', { emitting: false, lifespan: 290,
      speedX: { min: -7, max: 7 }, speedY: { min: 6, max: 11 }, gravityY: 145,
      scale: { start: .4, end: .32 }, rotate: { min: 0, max: 180 }, maxParticles: 90 }).setDepth(1e5 + 1);
  }
  private actor(unit: Unit) {
    let a = this.actors.get(unit.id);
    if (!a) {
      const key = 'machine:' + unit.kind, frame = META.clips[unit.kind + ':drive'][0];
      const make = () => this.scene.add.image(0, 0, key, frame)
        .setOrigin(META.foot[0] / META.size, META.foot[1] / META.size).setScale(SCALE);
      a = { a: make(), b: make(), heading: 0, distance: 0, dustAt: 0, stoneAt: 0 };
      this.actors.set(unit.id, a);
    }
    return a;
  }
  private pose(a: Actor, unit: Unit, first: string, second: string, blend: number, pos: Pt) {
    const [x, y] = iso(pos);
    a.a.setFrame(first).setAlpha(1 - blend).setPosition(x, y).setDepth(y + .4);
    a.b.setFrame(second).setAlpha(blend).setPosition(x, y).setDepth(y + .401);
    const p = META.frames[first].tip, q = META.frames[second].tip;
    a.tip = [x + ((p[0] + (q[0] - p[0]) * blend) - META.foot[0]) * SCALE,
      y + ((p[1] + (q[1] - p[1]) * blend) - META.foot[1]) * SCALE];
  }
  private clip(a: Actor, unit: Unit, clip: string, progress: number, pos: Pt, loop = true) {
    const frames = META.clips[unit.kind + ':' + clip];
    const t = clamp(progress, 0, .999999) * (loop ? frames.length : frames.length - 1);
    const n = Math.floor(t), next = loop ? (n + 1) % frames.length : Math.min(n + 1, frames.length - 1);
    this.pose(a, unit, frames[n], frames[next], t - n, pos);
  }
  private drive(a: Actor, unit: Unit, heading: number, pos: Pt, dt: number, loaded: boolean) {
    a.heading += clamp(turnDelta(a.heading, heading), -dt * 2.8, dt * 2.8);
    if (a.pos) a.distance += Math.hypot(pos[0] - a.pos[0], pos[1] - a.pos[1]);
    const d = (a.heading / (Math.PI * 2) * 16 % 16 + 16) % 16, n = Math.floor(d);
    const roll = Math.floor(a.distance / .13) % 2;
    const frames = META.clips[unit.kind + ':' + (loaded && unit.kind === 'truck' ? 'loaded' : 'drive')];
    this.pose(a, unit, frames[n * 2 + roll], frames[((n + 1) % 16) * 2 + roll], d - n, pos);
  }
  private route(job: Job, plots: Plot[], unit: Unit, index: number) {
    const source = plots.find(p => p.id === job.originPlotId) ?? plots[0];
    const target = plots.find(p => p.id === job.plotId)!;
    const park = dock(target, unit.kind);
    const bay: Pt = [source.x + .40 + (index % 4) * .83, source.y + 2 + Math.floor(index / 4) * 2];
    return [bay, [source.x + 4, bay[1]], [source.x + 4, target.y + 2], park] as Pt[];
  }
  update(state: GameState, now: number, roadStage: number) {
    this.particles();
    const district = state.districts.find(d => d.id === state.currentDistrict);
    if (!district) return;
    const dt = clamp((now - this.previousTime) / 1000, 0, .1); this.previousTime = now;
    const jobs = state.jobs.filter(j => j.districtId === district.id);
    const active = new Set(jobs.flatMap(j => j.unitIds)), seen = new Set<string>();
    this.activeIds = active;
    for (const [id, old] of this.lastJobs) {
      if (!jobs.some(j => j.id === id) && now >= old.returnEnd) {
        for (const uid of old.unitIds) {
          const u = state.units.find(v => v.id === uid);
          if (u?.kind === 'truck' && !active.has(uid)) {
            const points = this.route(old, district.plots, u, state.units.indexOf(u));
            this.dumps.set(uid, { position: points[0], start: now, ends: now + 3800 });
            this.idleHeadings.set(uid, Math.PI);
          }
        }
      }
    }
    this.lastJobs = new Map(jobs.map(j => [j.id, j]));
    for (const job of jobs) for (const uid of job.unitIds) {
      const unit = state.units.find(u => u.id === uid); if (!unit) continue;
      this.dumps.delete(uid); seen.add(uid);
      const a = this.actor(unit), points = this.route(job, district.plots, unit, state.units.indexOf(unit));
      let position: Pt, moving = false;
      if (now < job.arrival) {
        const f = clamp((now - job.start) / Math.max(1, job.arrival - job.start), 0, 1);
        position = along(points, f);
        const ahead = along(points, f + .012);
        // A tipper backs into the loading position rather than spinning its whole
        // body through 180 degrees beside the excavator.
        const reverse = unit.kind === 'truck' && Math.abs(ahead[1] - position[1]) < .00001 && ahead[0] < position[0];
        const heading = Math.atan2(ahead[1] - position[1], ahead[0] - position[0]) + (reverse ? Math.PI : 0);
        this.drive(a, unit, heading, position, dt, false); moving = f > 0 && f < 1;
      } else if (now < job.workEnd) {
        const plot = district.plots.find(p => p.id === job.plotId)!;
        position = dock(plot, unit.kind);
        const heading = unit.kind === 'truck' ? 0 : -Math.PI / 2;
        const elapsed = now - job.arrival, phase = ((Math.max(0, elapsed - 900)) % CYCLE) / CYCLE;
        if (elapsed < 900 || Math.abs(turnDelta(a.heading, heading)) > .06) {
          this.drive(a, unit, heading, position, dt, false);
        } else if (unit.kind === 'truck') {
          const amount = clamp((elapsed - CYCLE * .65) / Math.max(1, job.workEnd - job.arrival - CYCLE * .65), 0, 1);
          const frame = META.clips['truck:fill'][Math.min(3, Math.floor(amount * 4))];
          this.pose(a, unit, frame, frame, 0, position);
        } else {
          if (unit.kind === 'bulldozer') position[1] -= .19 * Math.sin(phase * Math.PI) ** 2;
          this.clip(a, unit, 'work', phase, position);
          if (a.tip && now > a.dustAt && (unit.kind === 'bulldozer' ? phase < .52 : phase < .17)) {
            this.dust!.emitParticleAt(...a.tip, 2); a.dustAt = now + 180;
          }
          if (unit.kind === 'excavator' && a.tip && phase > .60 && phase < .70 && now > a.stoneAt) {
            this.stones!.emitParticleAt(...a.tip, 3); a.stoneAt = now + 65;
          }
        }
      } else {
        const f = clamp((now - job.workEnd) / Math.max(1, job.returnEnd - job.workEnd), 0, 1);
        const back = [...points].reverse(); position = along(back, f);
        const ahead = along(back, f + .012);
        this.drive(a, unit, Math.atan2(ahead[1] - position[1], ahead[0] - position[0]), position, dt, true);
        moving = f < 1;
      }
      if (moving && roadStage < 3 && now > a.dustAt) {
        const q = iso(position); this.dust!.emitParticleAt(q[0], q[1], 1); a.dustAt = now + 210;
      }
      a.pos = position;
    }
    const depot = district.plots.find(p => p.buildingId === 'recycling' && p.status === 'built');
    for (const [slot, unit] of state.units.entries()) if (!seen.has(unit.id) && !active.has(unit.id) && depot) {
      seen.add(unit.id); const a = this.actor(unit), dump = this.dumps.get(unit.id);
      if (dump && now < dump.ends) {
        this.activeIds.add(unit.id);
        this.clip(a, unit, 'work', (now - dump.start) / (dump.ends - dump.start), dump.position, false);
        const phase = (now - dump.start) / (dump.ends - dump.start);
        if (a.tip && phase > .4 && phase < .7 && now > a.stoneAt) {
          this.stones!.emitParticleAt(...a.tip, 4); this.dust!.emitParticleAt(a.tip[0], a.tip[1] + 12, 1); a.stoneAt = now + 90;
        }
        a.pos = dump.position;
      } else {
        this.dumps.delete(unit.id);
        const position: Pt = [depot.x + .40 + (slot % 4) * .83, depot.y + 2 + Math.floor(slot / 4) * 2];
        this.drive(a, unit, this.idleHeadings.get(unit.id) ?? 0, position, dt, false); a.pos = position;
      }
    }
    for (const [id, actor] of this.actors) if (!seen.has(id)) {
      actor.a.destroy(); actor.b.destroy(); this.actors.delete(id); this.dumps.delete(id);
    }
  }
  resetDistrict() {
    for (const a of this.actors.values()) { a.a.destroy(); a.b.destroy(); }
    this.actors.clear(); this.dumps.clear(); this.lastJobs.clear(); this.idleHeadings.clear(); this.previousTime = 0;
    this.dust?.destroy(); this.stones?.destroy(); this.dust = this.stones = undefined;
    this.activeIds.clear();
  }
  /** Sample actual body/tool positions for foreground occlusion, never move units onto roofs. */
  focusPoints() {
    const points: { x: number; y: number; depth: number }[] = [];
    for (const [id, a] of this.actors) if (this.activeIds.has(id)) {
      for (const dx of [-10, 0, 10]) points.push({ x: a.a.x + dx, y: a.a.y - 17, depth: a.a.depth });
      if (a.tip) points.push({ x: a.tip[0], y: a.tip[1], depth: a.a.depth });
    }
    return points;
  }
  destroy() { this.resetDistrict(); }
}
