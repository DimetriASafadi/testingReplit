export type MusicPhase = 'loading' | 'menu' | 'play';

/** One streamed recording across the whole game. Never decode it as an effects buffer. */
export class ThemeMusic {
  readonly element: HTMLAudioElement;
  phase: MusicPhase = 'loading';
  status = 'تبدأ النغمة بعد أول لمسة إذا منع المتصفح التشغيل التلقائي';
  private context?: AudioContext;
  private gain?: GainNode;
  private enabled = true;
  private master = .7;
  private animation = 0;
  private rampFrom = 0;
  private rampTo = 0;
  private rampAt = 0;

  constructor(url: string, element?: HTMLAudioElement) {
    this.element = element ?? new Audio(url);
    this.element.loop = true;
    this.element.preload = 'auto';
    this.element.volume = .4;
    this.element.addEventListener('error', () => { this.status = 'تعذّر تحميل النغمة؛ تحقق من الاتصال ثم اضغط إعادة المحاولة'; });
  }

  get targetLevel() { return this.enabled ? (this.phase === 'play' ? .15 : .4) * (this.master / .7) : 0; }

  /** An allowed autoplay starts at the loading screen; blocked browsers wait for the first touch. */
  autoplay() { if (this.enabled) this.play(); }

  start(context: AudioContext) {
    if (!this.gain) {
      this.context = context;
      const source = context.createMediaElementSource(this.element);
      this.gain = context.createGain();
      this.gain.gain.value = this.element.paused ? 0 : this.targetLevel;
      this.rampFrom = this.rampTo = this.gain.gain.value;
      this.rampAt = context.currentTime;
      source.connect(this.gain);
      this.gain.connect(context.destination);
      this.element.volume = 1; // MediaElementSource now owns the volume, including on iOS.
      if (this.animation) cancelAnimationFrame(this.animation);
    }
    this.play();
    this.fade();
  }

  setPhase(phase: MusicPhase) {
    if (this.phase === phase) return; // Half-second UI updates must not restart a fade.
    const previous = this.targetLevel;
    this.phase = phase;
    if (this.targetLevel !== previous) this.fade();
  }
  setMaster(master: number) {
    const next = Math.max(0, Math.min(1, master));
    if (next === this.master) return;
    this.master = next;
    this.fade();
  }
  setEnabled(enabled: boolean) {
    if (this.enabled === enabled) return;
    this.enabled = enabled;
    if (enabled) this.play();
    this.fade();
    if (!enabled) this.element.pause(); // Resume from the same place when re-enabled.
  }
  pause() { this.element.pause(); }
  resume() { if (this.enabled) this.play(); }
  retry() {
    this.element.load();
    if (this.enabled) this.play();
  }

  private play() {
    if (!this.element.paused) return;
    void this.element.play().then(() => { this.status = 'النغمة تعمل'; }).catch((e: DOMException) => {
      this.status = e.name === 'NotAllowedError'
        ? 'المتصفح ينتظر أول لمسة لتشغيل النغمة'
        : 'تعذّر تشغيل النغمة؛ اضغط إعادة المحاولة';
    });
  }
  private fade() {
    const target = this.targetLevel;
    if (this.gain && this.context) {
      const p = this.gain.gain, now = this.context.currentTime;
      const elapsed = Math.max(0, Math.min(1, (now - this.rampAt) / 1.5));
      const current = this.rampFrom + (this.rampTo - this.rampFrom) * elapsed;
      p.cancelScheduledValues(now);
      p.setValueAtTime(current, now);
      p.linearRampToValueAtTime(target, now + 1.5);
      this.rampFrom = current;
      this.rampTo = target;
      this.rampAt = now;
      return;
    }
    // Browsers that permit autoplay can play before Web Audio is unlocked.
    if (this.animation) cancelAnimationFrame(this.animation);
    const from = this.element.volume, began = performance.now();
    const step = (now: number) => {
      if (this.gain) return;
      const t = Math.min(1, (now - began) / 1500);
      this.element.volume = from + (target - from) * (t * t * (3 - 2 * t));
      if (t < 1) this.animation = requestAnimationFrame(step);
    };
    this.animation = requestAnimationFrame(step);
  }
}
