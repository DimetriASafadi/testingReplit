import Phaser from 'phaser';
import metadata from './machinery-manifest.json';
import type { EquipmentKind, GameState, Unit } from './model';
import { U } from './art';
import { convoyMotion, gateDemand, workDock } from './fleet-motion';
import { DepotDoors } from './depot-door';

type Pt = [number, number];
type Pose = { tip: number[]; worldTip: number[] };
type Manifest = { size: number; ortho: number; foot: number[]; revision: string;
  clips: Record<string, string[]>; frames: Record<string, Pose> };
const META: Manifest = metadata;
const SCALE = .66, CYCLE = 7200;
const iso = (p: Pt): Pt => [(p[0] - p[1]) * U, (p[0] + p[1]) * U / 2];
const clamp = (n: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, n));
const turnDelta = (a: number, b: number) => Math.atan2(Math.sin(b - a), Math.cos(b - a));
export const machinePreview = (kind: EquipmentKind) => `${import.meta.env.BASE_URL}art/machinery/${kind}-preview.webp?v=${META.revision}`;
export function preloadMachinery(scene: Phaser.Scene) {
  for (const kind of ['excavator', 'truck', 'bulldozer'])
    scene.load.atlas('machine:' + kind, `${import.meta.env.BASE_URL}art/machinery/${kind}.webp?v=${META.revision}`,
      `${import.meta.env.BASE_URL}art/machinery/${kind}.json?v=${META.revision}`);
}
interface Actor { a: Phaser.GameObjects.Image; b: Phaser.GameObjects.Image; heading: number; pos?: Pt;
  distance: number; dustAt: number; stoneAt: number; tip?: Pt; revealing: boolean }

/** Visual-only crew coordination. Saved deadlines, route selection and money stay in the engine. */
export class FleetAnimation {
  private actors = new Map<string, Actor>();
  private doors: DepotDoors;
  private activeIds = new Set<string>();
  private previousTime = 0;
  private dust?: Phaser.GameObjects.Particles.ParticleEmitter;
  private stones?: Phaser.GameObjects.Particles.ParticleEmitter;
  constructor(private scene: Phaser.Scene) { this.doors = new DepotDoors(scene); }
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
      a = { a: make(), b: make(), heading: 0, distance: 0, dustAt: 0, stoneAt: 0, revealing: false };
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
  update(state: GameState, now: number, roadStage: number) {
    this.particles();
    const district = state.districts.find(d => d.id === state.currentDistrict);
    if (!district) return;
    const dt = clamp((now - this.previousTime) / 1000, 0, .1); this.previousTime = now;
    const jobs = state.jobs.filter(j => j.districtId === district.id);
    const seen = new Set<string>(); this.activeIds = new Set();
    const depots = district.plots.filter(p => p.buildingId === 'recycling' && p.status === 'built');
    for (const depot of depots) {
      const demands = jobs.filter(j => j.originPlotId === depot.id)
        .map(j => gateDemand(j, depot, district.plots[j.plotId], now)).filter(d => d.open);
      this.doors.update(depot, demands.length > 0, now, demands.length ? Math.min(...demands.map(d => d.since)) : now);
    }
    this.doors.retain(new Set(depots.map(p => p.id)));
    for (const job of jobs) for (const uid of job.unitIds) {
      const unit = state.units.find(u => u.id === uid); if (!unit) continue;
      const source = district.plots[job.originPlotId], plot = district.plots[job.plotId];
      const motion = convoyMotion(job, source, plot, unit.kind, now);
      if (motion.visibility <= 0) continue;
      seen.add(uid); this.activeIds.add(uid);
      const a = this.actor(unit);
      // Travelling units pass behind buildings. Only actual loading needs
      // selective foreground reveal; never make an entire street-front house
      // transparent just because a truck is driving behind it.
      a.revealing = motion.stage === 'work';
      let position = motion.position;
      if (motion.stage === 'work') {
        position = workDock(plot, unit.kind);
        const heading = unit.kind === 'truck' ? 0 : -Math.PI / 2;
        if (!a.pos) a.heading = heading;
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
        const heading = Math.atan2(motion.ahead[1] - position[1], motion.ahead[0] - position[0])
          + (motion.reverse ? Math.PI : 0);
        if (!a.pos) a.heading = heading;
        this.drive(a, unit, heading, position, dt, motion.stage === 'return');
      }
      a.a.setAlpha(a.a.alpha * motion.visibility); a.b.setAlpha(a.b.alpha * motion.visibility);
      if (motion.moving && roadStage < 3 && now > a.dustAt) {
        const q = iso(position); this.dust!.emitParticleAt(q[0], q[1], 1); a.dustAt = now + 210;
      }
      a.pos = position;
    }
    for (const [id, actor] of this.actors) if (!seen.has(id)) {
      actor.a.destroy(); actor.b.destroy(); this.actors.delete(id);
    }
  }
  resetDistrict() {
    for (const a of this.actors.values()) { a.a.destroy(); a.b.destroy(); }
    this.actors.clear(); this.doors.reset(); this.previousTime = 0;
    this.dust?.destroy(); this.stones?.destroy(); this.dust = this.stones = undefined;
    this.activeIds.clear();
  }
  /** Sample actual body/tool positions for foreground occlusion, never move units onto roofs. */
  focusPoints() {
    const points: { x: number; y: number; depth: number }[] = [];
    for (const [id, a] of this.actors) if (this.activeIds.has(id) && a.revealing) {
      for (const dx of [-10, 0, 10]) points.push({ x: a.a.x + dx, y: a.a.y - 17, depth: a.a.depth });
      if (a.tip) points.push({ x: a.tip[0], y: a.tip[1], depth: a.a.depth });
    }
    return points;
  }
  destroy() { this.resetDistrict(); }
}
