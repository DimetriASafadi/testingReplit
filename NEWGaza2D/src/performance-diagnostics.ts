import { FrameWindow } from './performance-samples';

const KEY = 'newgaza2d-performance-v1';
const ENABLED = 'newgaza2d-performance-enabled';
const LIMIT = 6, SAMPLE_LIMIT = 60;
type Session = { startedAt: string; label: string; environment: object; samples: object[]; events: object[] };
type Probe = () => object;
let enabled = false, sessions: Session[] = [], current: Session | undefined;
let probe: Probe = () => ({ stage: 'module-loading' });
let detach: (() => void) | undefined, attach: (() => () => void) | undefined;
let timer = 0, last = 0, phase = 'opening', storageError = '';
const frames = new FrameWindow();
try { enabled = localStorage.getItem(ENABLED) === 'true'; } catch { /* Explicit error shown when recording starts. */ }
function persist() {
  try { localStorage.setItem(KEY, JSON.stringify(sessions)); storageError = ''; }
  catch { storageError = 'تعذر حفظ تقرير التشخيص محليًا؛ انسخ التقرير قبل الإغلاق.'; }
}
function sample() {
  if (!current) return;
  const memory = (performance as Performance & { memory?: { usedJSHeapSize: number; jsHeapSizeLimit: number } }).memory;
  let state: object;
  try { state = probe(); } catch { state = { probeFailed: true }; }
  current.samples.push({ elapsedMs: Math.round(performance.now()), phase, hidden: document.hidden,
    frameWindow: frames.report(), heapUsedBytes: memory?.usedJSHeapSize ?? null,
    heapLimitBytes: memory?.jsHeapSizeLimit ?? null, ...state });
  current.samples = current.samples.slice(-SAMPLE_LIMIT);
  frames.reset(); persist();
}
function resetFrames() { last = 0; frames.reset(); }
export function recordGameFrame() {
  if (!enabled || document.hidden) { last = 0; return; }
  const now = performance.now();
  if (last) frames.add(now - last);
  last = now;
}
export function diagnosticEvent(event: string, flush = false) {
  if (!current || !enabled) return;
  current.events.push({ event: event.slice(0, 100), elapsedMs: Math.round(performance.now()) });
  current.events = current.events.slice(-60);
  // Parcel preparation can emit hundreds of updates. Do not write storage for
  // each one; the five-second sampler flushes the bounded event buffer.
  if (flush) persist();
}
function start() {
  if (current) return;
  try {
    const old = JSON.parse(localStorage.getItem(KEY) ?? '[]');
    if (Array.isArray(old)) sessions = old.filter(s => s && Array.isArray(s.samples) && Array.isArray(s.events))
      .slice(-(LIMIT - 1)).map(s => ({ ...s, samples: s.samples.slice(-SAMPLE_LIMIT), events: s.events.slice(-60) }));
  } catch { storageError = 'تعذر قراءة التقارير السابقة؛ التقرير الجديد متاح للنسخ.'; }
  const nav = navigator as Navigator & { deviceMemory?: number };
  current = { startedAt: new Date().toISOString(), label: sessions.at(-1)?.label ?? 'before', environment: {
    userAgent: navigator.userAgent, deviceMemoryGb: nav.deviceMemory ?? null,
    cores: navigator.hardwareConcurrency ?? null, dpr: devicePixelRatio,
    viewport: [innerWidth, innerHeight], timeOrigin: performance.timeOrigin,
  }, samples: [], events: [] };
  sessions.push(current); sessions = sessions.slice(-LIMIT);
  resetFrames(); detach = attach?.();
  diagnosticEvent('recording-started', true);
  timer = window.setInterval(sample, 5000);
}
export function connectDiagnostics(getState: Probe, subscribe: (frame: () => void) => () => void) {
  probe = getState;
  attach = () => subscribe(recordGameFrame);
  if (enabled) { detach?.(); detach = attach(); diagnosticEvent('game-connected'); }
}
function stop() {
  sample(); clearInterval(timer); detach?.(); detach = undefined;
  current = undefined; resetFrames();
}
if (enabled) start();
document.addEventListener('visibilitychange', () => {
  if (enabled) { sample(); diagnosticEvent(document.hidden ? 'background' : 'foreground', true); resetFrames(); }
});
window.addEventListener('pagehide', () => { if (enabled) { sample(); diagnosticEvent('pagehide', true); } });
window.addEventListener('error', () => diagnosticEvent('runtime-error'));
window.addEventListener('unhandledrejection', () => diagnosticEvent('unhandled-rejection'));

/** No save access, uploads, art generation or frame listeners while disabled. */
export function openDiagnostics() {
  let dialog = document.querySelector<HTMLDialogElement>('#performance-diagnostics');
  if (dialog) { dialog.showModal(); return; }
  dialog = document.createElement('dialog'); dialog.id = 'performance-diagnostics'; dialog.dir = 'rtl';
  dialog.style.cssText = 'max-width:540px;width:90vw;max-height:85dvh;overflow:auto;background:#102b36;color:white;border:1px solid #a9bbc1;border-radius:14px;padding:20px;z-index:10000';
  dialog.innerHTML = `<h2>تشخيص الأداء الاختياري</h2>
    <p>لا يغيّر تقدمك ولا يرسل بيانات. يحتفظ بآخر 6 جلسات و60 عينة لكل جلسة (عينة كل 5 ثوانٍ). أوقفه بعد التجربة.</p>
    <p>الذاكرة المقاسة JavaScript فقط إن أتاحها الجهاز؛ حجم القوام تقدير RGBA وليس ذاكرة GPU الفعلية. النتائج ليست ضمانًا لسلاسة الهاتف.</p>
    <label>المقارنة <select id="perf-label"><option value="before">قبل التحديث</option><option value="after">بعد التحديث</option></select></label>
    <label>المرحلة <select id="perf-phase"><option value="opening">فتح الحي</option><option value="idle">حي ممتلئ ساكن</option><option value="construction">البناء</option><option value="paving">السفلتة</option><option value="celebration">الاحتفال</option><option value="switching">تبديل الأحياء</option></select></label>
    <p id="perf-status" role="status"></p>
    <button class="btn" id="perf-toggle"></button>
    <button class="btn" id="perf-export">عرض التقرير للنسخ</button>
    <button class="btn" id="perf-close">إغلاق</button>
    <textarea id="perf-report" aria-label="تقرير الأداء" readonly hidden style="width:100%;height:200px;direction:ltr"></textarea>`;
  document.body.append(dialog);
  const root = dialog;
  const status = () => {
    root.querySelector('#perf-status')!.textContent = (enabled ? 'التسجيل يعمل؛ أغلق هذه النافذة أثناء القياس.' : 'التسجيل متوقف.') + ' ' + storageError;
    root.querySelector('#perf-toggle')!.textContent = enabled ? 'إيقاف التسجيل' : 'بدء التسجيل';
  };
  const label = root.querySelector<HTMLSelectElement>('#perf-label')!;
  label.value = current?.label === 'after' ? 'after' : 'before';
  label.onchange = () => { if (current) { sample(); current.label = label.value; persist(); } };
  const phaseSelect = root.querySelector<HTMLSelectElement>('#perf-phase')!;
  phaseSelect.value = phase;
  phaseSelect.onchange = () => { sample(); phase = phaseSelect.value; resetFrames(); diagnosticEvent(`phase:${phase}`); };
  root.querySelector<HTMLButtonElement>('#perf-toggle')!.onclick = () => {
    if (enabled) { stop(); enabled = false; }
    else { enabled = true; start(); if (current) current.label = label.value; persist(); }
    try { localStorage.setItem(ENABLED, String(enabled)); }
    catch { storageError = 'تعذر حفظ اختيار التسجيل؛ لن يُستأنف تلقائيًا بعد الإغلاق.'; }
    status();
  };
  root.querySelector<HTMLButtonElement>('#perf-export')!.onclick = () => {
    if (enabled) sample();
    else {
      try { sessions = JSON.parse(localStorage.getItem(KEY) ?? '[]'); }
      catch { storageError = 'تعذر قراءة التقرير المحلي.'; }
    }
    const text = root.querySelector<HTMLTextAreaElement>('#perf-report')!;
    text.hidden = false; text.value = JSON.stringify({ schema: 1,
      limitations: ['JS heap is not total process memory', 'RGBA estimate excludes driver/mip/compression overhead',
        'Software GPU is not a physical Android test', 'Frame windows exclude hidden time; dialogs perturb timing'],
      sessions }, null, 2);
    text.focus(); text.select(); status();
  };
  root.querySelector<HTMLButtonElement>('#perf-close')!.onclick = () => root.close();
  root.addEventListener('close', () => { diagnosticEvent('diagnostic-dialog-closed'); resetFrames(); root.remove(); });
  diagnosticEvent('diagnostic-dialog-opened'); status(); root.showModal();
}
if (new URLSearchParams(location.search).get('diagnostics') === '1') openDiagnostics();
