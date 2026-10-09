import Phaser from 'phaser';
import './style.css';
import './navigation-icons.css';
import { activityDuration, buildingIncomePeriod, incomeIntervalLabel, MINUTE_TEST_TIMERS } from './game-timing';
import { BUILDINGS, DISTRICTS, EQUIPMENT, building, projects } from './catalog';
import { GameStore, SAVE_KEY, districtProgress, jobPhase, factoryRotationBlocked } from './engine';
import type { Category, DistrictState, EquipmentKind, GameState, Plot, Orientation } from './model';
import { BootScene, CityScene } from './scenes/CityScene';
import { machinePreview } from './fleet-animation';
import { previewUrl, SPR, builtKey } from './realart';
import { ORIENTATION_CYCLE, ORIENTATION_LABELS, nextOrientation } from './orientation';
import { patchContent } from './dom-patch';
import { dialogLayout, dialogPageSize, pageControls, pageSize, splitActions } from './ui-layout';
import { gameAudio } from './game-audio';
import { buildingAccruedIncome, buildingSaleValue } from './building-disposal';
import { districtIncome } from './district-income';
import { buildingIncomeDetails } from './building-income';

const app = document.getElementById('app')!;
const stage = document.getElementById('stage')!;
const cityStatus = document.createElement('section');
let bootLoading = true, cityLoading = false;
function syncMusic() { gameAudio.musicPhase(bootLoading || cityLoading ? 'loading' : S().currentDistrict ? 'play' : 'menu'); }
cityStatus.id = 'city-status'; cityStatus.hidden = true;
cityStatus.setAttribute('role', 'status'); cityStatus.setAttribute('aria-live', 'polite');
document.body.append(cityStatus);
function showCityStatus(status: 'loading' | 'ready' | 'error', message = '') {
  cityLoading = status === 'loading';
  syncMusic();
  cityStatus.hidden = status === 'ready' || !S().currentDistrict;
  if (status === 'ready') return;
  cityStatus.innerHTML = status === 'loading'
    ? '<div class="city-status-card"><h2>تجهيز الحي…</h2><p>جارٍ رسم الأرض والمباني. انتظر قليلًا.</p></div>'
    : `<div class="city-status-card"><h2>تعذر عرض الحي</h2><p>إعادة المحاولة لا تحذف تقدمك.</p><p class="note">${esc(message)}</p><div class="row"><button class="btn gold" data-a="retry-city">إعادة المحاولة</button><button class="btn" data-a="map">خريطة الأحياء</button></div></div>`;
}
const fmt = (n: number) => Math.floor(n).toLocaleString('ar-EG');
const esc = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!));
function dur(ms: number) { const s = Math.max(0, Math.ceil(ms / 1000)); const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = s % 60; return h ? `${fmt(h)} س ${fmt(m)} د` : m ? `${fmt(m)} د ${fmt(x)} ث` : `${fmt(x)} ث`; }
const secs = (s: number) => dur(activityDuration(s * 1000));
const resourceIcon = (name: 'iron' | 'concrete' | 'wood' | 'coins') =>
  `<img class="resource-icon resource-${name}" src="${import.meta.env.BASE_URL}art/ui-resources/${name}.webp" width="160" height="160" alt="" aria-hidden="true" draggable="false"/>`;
const navigationIcon = (name: 'fleet' | 'mats' | 'projects' | 'progress' | 'save') =>
  `<img class="nav-art" src="${import.meta.env.BASE_URL}art/ui-navigation/${name}.webp" width="256" height="256" alt="" aria-hidden="true" draggable="false"/>`;
const I = (d: string) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`;
const IC = {
  close: I('<path d="M6 6l12 12M18 6L6 18"/>'), map: I('<path d="M9 4L3 6v14l6-2 6 2 6-2V4l-6 2-6-2zM9 4v14M15 6v14"/>'),
  plus: I('<path d="M12 5v14M5 12h14"/>'), minus: I('<path d="M5 12h14"/>'), rotate: I('<path d="M4 12a8 8 0 0114-5l2 2M20 4v5h-5M20 12a8 8 0 01-14 5l-2-2M4 20v-5h5"/>'),
};
const CAT: Record<Category, string> = { equipment: 'معدات وورش', industry: 'مصانع', housing: 'سكن', farm: 'زراعة', commerce: 'تجارة', leisure: 'ترفيه ومطاعم' };
const PIN: Record<string, [number, number]> = { karama: [80, 12], 'sheikh-radwan': [70, 22], shati: [64, 21], nasr: [60, 30], tuffah: [80, 34], shujaiya: [88, 48], daraj: [66, 44], 'old-city': [56, 54], rimal: [47, 40], sabra: [44, 62], zeitoun: [60, 72], 'tel-al-hawa': [34, 64], 'sheikh-ijlin': [24, 76], rashid: [12, 82] };
const PHASE = { travel: 'في الطريق عبر الشوارع', work: 'تعمل في الموقع', return: 'عائدة إلى المصنع محمّلة' };

// ---------- store (with explicit recovery) ----------
let store: GameStore;
try { store = new GameStore(); }
catch (e) { showRecovery(e instanceof Error ? e.message : String(e)); throw e; }

function showRecovery(msg: string) {
  document.getElementById('boot')?.classList.add('done');
  const raw = (() => { try { return localStorage.getItem(SAVE_KEY) ?? ''; } catch { return ''; } })();
  app.innerHTML = `<div class="recovery" role="alertdialog" aria-labelledby="rh"><div class="card">
    <h2 id="rh">تعذر قراءة الحفظ</h2><p>${esc(msg)}</p>
    <p class="note">لم نحذف أي شيء. الحفظ مخزن محليًا على هذا الجهاز فقط تحت المفتاح <bdi class="num">${SAVE_KEY}</bdi>. نزّل النسخة الأصلية قبل أي قرار.</p>
    <textarea readonly aria-label="نص الحفظ الأصلي">${esc(raw)}</textarea>
    <div class="row"><button class="btn" id="rx">تنزيل الحفظ الأصلي</button><button class="btn sumac" id="rr">بدء لعبة جديدة (يحذف الحفظ)</button></div></div></div>`;
  document.getElementById('rx')!.onclick = () => download(raw, 'newgaza2d-corrupt-save.json');
  document.getElementById('rr')!.onclick = () => { if (confirm('سيحذف هذا الحفظ التالف نهائيًا من هذا الجهاز. هل أنت متأكد؟')) { localStorage.removeItem(SAVE_KEY); location.reload(); } };
}
function download(text: string, name: string) { const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' })); a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 2000); }

// ---------- UI state ----------
const ui = { selDistrict: null as string | null, plot: null as number | null, cat: 'all' as Category | 'all', catPage: 0, dialogTab: 0, dialogPage: 0, pick: null as string | null, orient: 0 as Orientation, dialog: null as null | 'fleet' | 'mats' | 'projects' | 'progress' | 'save' | 'audio' | 'building-disposal', objectiveHidden: false };
let disposal: { districtId: string; plotId: number; buildingId: string; startedAt: number; mode: 'sellBuilding' | 'removeBuilding' } | null = null;
let toastTimer = 0;
function toast(msg: string, err = false) { document.querySelector('.toast')?.remove(); const t = document.createElement('div'); t.className = 'toast' + (err ? ' err' : ''); t.setAttribute('role', err ? 'alert' : 'status'); t.textContent = msg; document.body.appendChild(t); clearTimeout(toastTimer); toastTimer = window.setTimeout(() => t.remove(), 3200); }
function act(a: Parameters<GameStore['dispatch']>[0], ok?: string) { try { store.dispatch(a); gameAudio.click(true); if (ok) toast(ok); return true; } catch (e) { gameAudio.click(); toast(e instanceof Error ? e.message : 'تعذر تنفيذ الإجراء', true); return false; } }

const S = () => store.state;
const cur = (): DistrictState | undefined => S().districts.find(d => d.id === S().currentDistrict);
const dName = (id: string) => DISTRICTS.find(d => d.id === id)?.name ?? id;
const hasRecycler = (st: GameState) => st.districts.some(d => d.id === st.currentDistrict && d.plots.some(p => p.buildingId === 'recycling' && p.status === 'built'));
const recyclerStarted = (st: GameState) => st.districts.some(d => d.id === st.currentDistrict && d.plots.some(p => p.buildingId === 'recycling' && (p.status === 'built' || p.status === 'building')));
const busy = (st: GameState) => new Set(st.jobs.flatMap(j => j.unitIds));

// ---------- focus-preserving region renderer ----------
const regions = new Map<string, string>();
const resetKeys = new Set<string>();
const snap = (el: HTMLElement) => { const m = new Map<string, number>(); el.querySelectorAll<HTMLElement>('[data-sk]').forEach(n => m.set(n.dataset.sk!, n.scrollTop)); return m; };
const restore = (el: HTMLElement, m: Map<string, number>) => el.querySelectorAll<HTMLElement>('[data-sk]').forEach(n => { const k = n.dataset.sk!; const v = m.get(k); if (resetKeys.has(k)) n.scrollTop = 0; else if (v !== undefined && n.scrollTop !== v) n.scrollTop = v; });
const resetScroll = (...k: string[]) => k.forEach(x => resetKeys.add(x));
function region(id: string, html: string, cls = '') {
  let el = document.getElementById(id);
  if (!html) { el?.remove(); regions.delete(id); return; }
  if (!el) { el = document.createElement('div'); el.id = id; app.appendChild(el); }
  if (cls) el.className = cls;
  if (regions.get(id) === html) return;
  const f = document.activeElement as HTMLElement | null; const key = f && el.contains(f) ? f.dataset.k : undefined;
  const pos = snap(el);
  patchContent(el, html);
  regions.set(id, html); restore(el, pos);
  if (key) (el.querySelector(`[data-k="${CSS.escape(key)}"]`) as HTMLElement | null)?.focus({ preventScroll: true });
}

// ---------- WORLD SCREEN ----------
function renderWorld() {
  const st = S(); const base = import.meta.env.BASE_URL;
  const sel = ui.selDistrict ?? st.districts.find(d => d.unlocked && !d.claimed)?.id ?? DISTRICTS[0].id;
  ui.selDistrict = sel;
  const order = DISTRICTS.map(d => PIN[d.id]).filter(Boolean);
  const pins = DISTRICTS.map((d, i) => { const ds = st.districts.find(x => x.id === d.id); const s = !ds ? 'locked' : ds.claimed ? 'claimed' : ds.unlocked ? 'open' : 'locked'; const [x, y] = PIN[d.id] ?? [50, 50];
    return `<button class="pin ${s} ${sel === d.id ? 'sel' : ''}" style="left:${x}%;top:${y}%;animation-delay:${i * 60}ms" data-a="pick-d" data-id="${d.id}" data-k="pin-${d.id}" aria-label="${esc(d.name)}، ${s === 'claimed' ? 'مكتمل' : s === 'open' ? 'متاح' : 'مقفل'}"><span class="flag"><span class="n">${fmt(i + 1)}</span>${esc(d.name)}</span><span class="stem"></span><span class="dot"></span></button>`; }).join('');
  const ds = st.districts.find(x => x.id === sel); const def = DISTRICTS.find(d => d.id === sel)!; const idx = DISTRICTS.indexOf(def);
  const status = (!ds?.unlocked ? `مقفل. يُفتح بعد استلام حي ${esc(DISTRICTS[idx - 1]?.name ?? '')}.` : ds.claimed ? 'تم استلامه. يمكنك العودة إليه وجمع الدخل.' : 'متاح للإعمار الآن.') + (def.id === 'shati' ? '<br>حي ساحلي شمال غرب مدينة غزة.' : '');
  region('world', `<section class="world" aria-label="خريطة غزة">
    <div class="world-map"><div class="map-frame">
      <img src="${base}art/world-map.jpg" alt="خريطة مرسومة لساحل غزة وأحيائها" onerror="this.style.background='#16747a'" />
      <svg class="route" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"><polyline points="${order.map(([x, y]) => `${x},${y}`).join(' ')}" fill="none" stroke="#f1e6d0" stroke-width=".35" stroke-dasharray="1 1" opacity=".8"/></svg>
      ${pins}</div>
      <div class="map-title"><h1>نيو غزة</h1><p>اختر حيًا على الخريطة. كل حي يُستلم بعد إزالة أنقاضه وإكمال مشاريعه التسعة، فيُفتح الحي التالي حتى كورنيش شارع الرشيد.</p></div>
    </div>
    <aside class="world-side" data-sk="world">
      <div class="chip-row"><span class="chip" aria-label="الرصيد: ${fmt(st.coins)}">${resourceIcon('coins')} <b class="num">${fmt(st.coins)}</b></span><span class="chip">أحياء مستلمة <b class="num">${fmt(st.districts.filter(d => d.claimed).length)}/${fmt(DISTRICTS.length)}</b></span></div>
      <div class="district-card"><small>المرحلة ${fmt(idx + 1)} من ${fmt(DISTRICTS.length)}${idx === DISTRICTS.length - 1 ? ' — الختام' : ''}</small><h2>${esc(def.name)}</h2><p>${status}</p>
        ${ds ? `<div class="meter" aria-label="تقدم الحي"><i style="transform:scaleX(${districtProgress(ds) / 100})"></i></div><p class="num" style="font-size:.85rem">${fmt(districtProgress(ds))}٪</p>` : ''}
        <p>مكافأة الاستلام: <b class="num">${fmt(def.reward)}</b></p>
        <button class="btn gold" style="width:100%" data-a="enter" data-id="${sel}" data-k="enter" ${ds?.unlocked ? '' : 'disabled'}>${ds?.unlocked ? 'دخول الحي' : 'مقفل'}</button></div>
      <h3>مسار الإعمار</h3>
      <ol class="ladder">${DISTRICTS.map((d, i) => { const x = st.districts.find(y => y.id === d.id); const s = x?.claimed ? 'claimed' : x?.unlocked ? 'open' : 'locked'; return `<li class="${s}"><button data-a="pick-d" data-id="${d.id}" data-k="l-${d.id}"><span>${fmt(i + 1)}. ${esc(d.name)}${i === DISTRICTS.length - 1 ? ' (الختام)' : ''}</span><span class="tag ${s === 'claimed' ? 'olive' : s === 'open' ? 'gold' : ''}">${s === 'claimed' ? 'مستلم' : s === 'open' ? 'متاح' : 'مقفل'}</span></button></li>`; }).join('')}</ol>
      <p class="save-note">التقدم محفوظ على هذا الجهاز وهذا المتصفح فقط. <button class="btn ghost sm" data-a="dlg" data-id="save" data-k="savebtn">الحفظ والتصدير</button></p>
    </aside></section>`);
}

// ---------- CITY SCREEN ----------
function objective(st: GameState, d: DistrictState) {
  if (ui.objectiveHidden) return '';
  const steps = [
    { t: 'اختر أرضًا نظيفة وابنِ مصنع إعادة التدوير (١٥٬٠٠٠، يستغرق دقيقتين)', ok: recyclerStarted(st) },
    { t: 'انتظر اكتمال المصنع', ok: hasRecycler(st) },
    { t: 'اشترِ فريقًا كاملًا: حفار وجرافة وشاحنة نقل', ok: (['excavator', 'bulldozer', 'truck'] as const).every(k => st.units.some(u => u.kind === k)) },
    { t: 'اختر أرض أنقاض وأرسل الآليات: سفر ثم دقيقة عمل ثم عودة', ok: st.jobs.length > 0 || d.plots.filter(p => p.status === 'rubble').length < 53 },
    { t: 'بِع المواد المستعادة وابدأ مشاريع الحي التسعة', ok: d.projects.some(p => p.status !== 'idle') },
  ];
  const next = steps.find(s => !s.ok); if (!next) return '';
  return `<div class="objective" role="region" aria-label="الهدف الحالي"><div style="display:flex;gap:8px"><h3>الهدف: ${esc(next.t)}</h3><button class="icon-btn" style="width:32px;height:32px;box-shadow:none;margin-inline-start:auto;flex:none" data-a="hide-obj" aria-label="إخفاء الهدف">${IC.close}</button></div><ol>${steps.map(s => `<li class="${s.ok ? 'done' : ''}">${esc(s.t)}</li>`).join('')}</ol></div>`;
}

function renderCity() {
  const st = S(); const d = cur()!; const now = Date.now(); const def = DISTRICTS.find(x => x.id === d.id)!;
  const inv = st.inventory; const readyProj = d.projects.filter(p => p.status === 'ready' || (p.status === 'complete' && p.id === 'commerce' && p.incomeAt && now >= p.incomeAt + buildingIncomePeriod())).length;
  const income = districtIncome(d, Math.max(now, st.lastSeen));
  const compactIncome = new Intl.NumberFormat('ar-EG', { notation: 'compact', maximumFractionDigits: 1 }).format(income.total);
  region('hud', `<header class="hud-top">
    <button class="icon-btn" data-a="map" data-k="tomap" aria-label="العودة إلى خريطة غزة">${IC.map}</button>
    <div class="place"><h1>${esc(def.name)}</h1><small>تقدم الحي <span class="num">${fmt(districtProgress(d))}٪</span></small></div>
    <div class="wallet"><span class="pill" aria-label="الرصيد: ${fmt(st.coins)}">${resourceIcon('coins')}<b class="num">${fmt(st.coins)}</b></span>
      <span class="pill m">${resourceIcon('concrete')}إسمنت <b class="num">${fmt(inv.concrete)}</b></span>
      <span class="pill m">${resourceIcon('iron')}حديد <b class="num">${fmt(inv.iron)}</b></span></div>
  </header>
  ${objective(st, d)}
  ${store.storageError ? `<div class="banner" role="alert"><span>${esc(store.storageError)}</span><button class="btn gold sm" data-a="export" data-k="exp-b">تصدير</button></div>` : ''}
  <div class="zoom"><button class="icon-btn" data-a="zin" aria-label="تكبير">${IC.plus}</button><button class="icon-btn" data-a="zout" aria-label="تصغير">${IC.minus}</button></div>
  <nav class="dock" aria-label="أدوات الحي">
    <button class="collect-all" data-a="collect-all" data-k="collect-all" aria-label="جمع الدخل المتاح في الحي: ${fmt(income.total)} عملة من ${fmt(income.count)} مصادر" title="الدخل المتاح: ${fmt(income.total)} عملة" ${income.total > 0 ? '' : 'disabled'}>${resourceIcon('coins')}<span>جمع الدخل</span>${income.total > 0 ? `<span class="badge num">${compactIncome}</span>` : ''}</button>
    <button data-a="dlg" data-id="fleet" data-k="d-fleet" aria-expanded="${ui.dialog === 'fleet'}">${navigationIcon('fleet')}<span>الآليات</span>${st.jobs.length ? `<span class="badge num">${fmt(st.jobs.length)}</span>` : ''}</button>
    <button data-a="dlg" data-id="mats" data-k="d-mats" aria-expanded="${ui.dialog === 'mats'}">${navigationIcon('mats')}<span>المواد</span></button>
    <button data-a="dlg" data-id="projects" data-k="d-proj" aria-expanded="${ui.dialog === 'projects'}">${navigationIcon('projects')}<span>المشاريع</span>${readyProj ? `<span class="badge num">${fmt(readyProj)}</span>` : ''}</button>
    <button data-a="dlg" data-id="progress" data-k="d-prog" aria-expanded="${ui.dialog === 'progress'}">${navigationIcon('progress')}<span>الاستلام</span></button>
    <button data-a="dlg" data-id="save" data-k="d-save" aria-expanded="${ui.dialog === 'save'}">${navigationIcon('save')}<span>الحفظ</span></button>
  </nav>`);
  region('sheet', ui.plot != null ? plotSheet(st, d, now) : '', 'sheet-wrap');
}

function jobFor(st: GameState, d: DistrictState, p: Plot) { return st.jobs.find(j => j.districtId === d.id && j.plotId === p.id); }
function jobDisplay(j: Parameters<typeof jobPhase>[0], now: number) {
  const ph = jobPhase(j, now);
  if (ph === 'travel' && now < (j.departureAt ?? j.start))
    return { label: 'انتظار دور الخروج من المصنع', end: j.departureAt!, span: j.departureAt! - j.start };
  if (ph === 'return' && now < (j.returnStartAt ?? j.workEnd))
    return { label: 'انتظار دور العودة إلى المصنع', end: j.returnStartAt!, span: j.returnStartAt! - j.workEnd };
  return { label: PHASE[ph], end: ph === 'travel' ? j.arrival : ph === 'work' ? j.workEnd : j.returnEnd,
    span: ph === 'travel' ? j.arrival - (j.departureAt ?? j.start) : ph === 'work' ? j.workEnd - j.arrival : j.returnEnd - (j.returnStartAt ?? j.workEnd) };
}
function jobHtml(st: GameState, d: DistrictState, p: Plot, now: number) {
  const j = jobFor(st, d, p); if (!j) return '';
  const { label, end, span } = jobDisplay(j, now);
  return `<div class="note"><b>${label}</b> — متبقٍ <span class="num">${dur(end - now)}</span><div class="meter" style="margin-top:6px"><i style="transform:scaleX(${Math.min(1, 1 - (end - now) / Math.max(1, span))})"></i></div><small>الآليات: ${j.unitIds.map(id => EQUIPMENT[st.units.find(u => u.id === id)?.kind ?? 'truck'].name).join('، ')} — محجوزة حتى العودة. تُضاف المواد عند وصولها للمصنع.</small></div>`;
}

const previews = new Map<string, string>();
const prev = (id: string, o: Orientation) => { const k = id + o; if (!previews.has(k)) previews.set(k, previewUrl(id, o)); return previews.get(k)!; };
const fourFacesReady = (id: string) => ORIENTATION_CYCLE.every(o => !!SPR[builtKey(id, o)]);
const nextVisibleFace = (id: string, o: Orientation): Orientation => fourFacesReady(id) ? nextOrientation(o) : o === 0 ? 1 : 0;
const rotateLabel = (id: string) => fourFacesReady(id) ? 'تدوير ربع دورة' : 'تبديل أمام / خلف';
const pendingFaceNote = (id: string) => fourFacesReady(id) ? '' : '<p class="note">الأمام والخلف متاحان. رسومات اليمين واليسار لهذا النوع لم تُعتمد بعد، حتى لا يظهر بزاوية أو شكل غير صحيح.</p>';

function plotSheet(st: GameState, d: DistrictState, now: number) {
  const p = d.plots.find(x => x.id === ui.plot); if (!p) return '';
  const b = building(p.buildingId);
  let title = '', sub = '', body = '', bodyCls = '';
  if (p.status === 'rubble') {
    title = `أنقاض ${b.name}`; sub = 'أرض مغطاة بالركام';
    const free = st.units.filter(u => !busy(st).has(u.id));
    const j = jobFor(st, d, p);
    body = j ? jobHtml(st, d, p, now) : `<p>ترسل الآليات المتاحة من مصنع إعادة التدوير عبر الشوارع. السفر حسب المسافة ومستوى الآلية، ثم <b>٦٠ ثانية عمل</b> في الموقع، ثم العودة محمّلة (٣٠–٩٠ ثانية). تصبح الأرض نظيفة عند انتهاء العمل.</p>
      ${!hasRecycler(st) ? '<p class="note">يلزم أولًا بناء مصنع إعادة التدوير على أرض نظيفة.</p>' : !st.units.length ? '<p class="note">لا توجد آليات بعد. اشترِ آلية من لوحة الآليات.</p>' : `<p class="note">آليات متاحة: <b class="num">${fmt(free.length)}</b> من ${fmt(st.units.length)}</p>`}
      <button class="btn gold" data-a="clear" data-k="clear" ${hasRecycler(st) && free.length ? '' : 'disabled'}>إرسال الآليات لإزالة الأنقاض</button>`;
  } else if (p.status === 'empty') {
    title = 'أرض نظيفة'; sub = 'جاهزة للبناء';
    const cats = ['all', ...Object.keys(CAT)] as Array<Category | 'all'>;
    const cnt = (c: Category | 'all') => c === 'all' ? BUILDINGS.length : BUILDINGS.filter(x => x.category === c).length;
    const list = ui.cat === 'all' ? BUILDINGS : BUILDINGS.filter(x => x.category === ui.cat);
    const pages = Math.ceil(list.length / pageSize());
    const page = Math.max(0, Math.min(ui.catPage, pages - 1));
    const visible = list.slice(page * pageSize(), (page + 1) * pageSize());
    const pick = ui.pick ? building(ui.pick) : null;
    bodyCls = 'build' + (pick ? ' has-pick' : '');
    const detail = pick ? `<button class="btn ghost catalog-back" data-a="back-catalog" data-k="back-catalog">رجوع إلى المباني والتصنيفات</button><div class="bd-top" data-sk="detail-top"><div class="preview"><span class="ori">${ORIENTATION_LABELS[ui.orient]}</span><img src="${prev(pick.id, ui.orient)}" alt="معاينة ${esc(pick.name)}: ${ORIENTATION_LABELS[ui.orient]}"/></div>
        <div class="bd-info"><h3>${esc(pick.name)}</h3><small>${CAT[pick.category]}</small><dl class="kv"><dt>التكلفة</dt><dd class="num">${fmt(pick.cost)}</dd><dt>المدة</dt><dd>${secs(pick.duration)}</dd><dt>الطوابق</dt><dd class="num">${fmt(pick.floors)}</dd><dt>الدخل</dt><dd>${pick.income ? `<span class="num">${fmt(pick.income)}</span> كل ${incomeIntervalLabel()}` : 'لا يوجد'}</dd></dl></div></div>
        <div class="row bd-actions"><button class="btn ghost" data-a="rot" data-k="rot" aria-label="${rotateLabel(pick.id)}">${IC.rotate} ${rotateLabel(pick.id)}</button><button class="btn gold" data-a="place" data-k="place" ${st.coins >= pick.cost ? '' : 'disabled'}>${st.coins >= pick.cost ? 'بناء هنا' : 'الرصيد غير كافٍ'}</button></div>${pendingFaceNote(pick.id)}`
      : '<p class="note">اختر مبنى لمعاينة جهاته المتوفرة ثم ابنِه.</p>';
    body = `<div class="build-detail" data-sk="detail">${!recyclerStarted(st) ? '<p class="note">ابدأ بمصنع إعادة التدوير: هو نقطة انطلاق كل الآليات.</p>' : ''}${detail}</div>
      <div class="build-list"><div class="cat-filters" role="tablist" aria-label="فئات المباني">${cats.map(c => `<button role="tab" aria-selected="${ui.cat === c}" data-a="cat" data-id="${c}" data-k="t-${c}">${c === 'all' ? 'كل المباني' : CAT[c]} <span class="cnt num">${fmt(cnt(c))}</span></button>`).join('')}</div>
      <div class="cat-scroll" data-sk="cat"><div class="cat-grid" aria-label="عدد المباني ${fmt(list.length)}">${visible.map(x => `<button class="cat-item" aria-pressed="${ui.pick === x.id}" title="${esc(x.name)}" data-a="pickb" data-id="${x.id}" data-k="b-${x.id}"><img src="${prev(x.id, 0)}" alt="" loading="lazy"/><b>${esc(x.name)}</b><small><span class="num">${fmt(x.cost)}</span> · ${secs(x.duration)}</small></button>`).join('')}</div></div>${pageControls(page,pages,'catalog-page',list.length)}</div>`;
  } else if (p.status === 'building') {
    title = b.name; sub = `قيد الإنشاء · ${ORIENTATION_LABELS[p.orientation]}`;
    const f = Math.min(1, (now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt));
    body = `<div class="meter"><i style="transform:scaleX(${f})"></i></div><dl class="kv"><dt>المرحلة</dt><dd>${f < 0.3 ? 'الأساسات وحديد التسليح' : f < 0.68 ? 'الهيكل والسقالات' : 'الجدران والتشطيب'}</dd><dt>متبقٍ</dt><dd class="num">${dur(p.endsAt - now)}</dd><dt>المدة الكلية</dt><dd>${secs(b.duration)}</dd></dl><p class="note">البناء يستمر بالوقت الحقيقي حتى عند إغلاق اللعبة.</p>`;
  } else {
    title = b.name; sub = `${CAT[b.category]} · ${ORIENTATION_LABELS[p.orientation]}`;
    const incomeNow = Math.max(now, st.lastSeen);
    const income = buildingIncomeDetails(p, incomeNow);
    const ready = income.amount > 0;
    body = `<div class="preview"><img src="${prev(b.id, p.orientation)}" alt=""/></div>
      ${b.income ? `<dl class="kv" data-sk="income-summary"><dt>دخل الدفعة</dt><dd><span class="num">${fmt(income.perPeriod)}</span> كل ${incomeIntervalLabel()}</dd><dt>الدفعات المتراكمة</dt><dd class="num">${fmt(income.periods)}</dd><dt>المتاح للجمع</dt><dd data-income-total="${income.amount}"><span class="num">${fmt(income.amount)}</span> عملة</dd><dt>الدفعة التالية</dt><dd>بعد <span class="num">${dur(income.nextAt - incomeNow)}</span></dd></dl>
        ` : `<p class="note">${b.id === 'recycling' ? `مقر الآليات: منه تنطلق وإليه تعود المواد. الآليات المخصصة لهذا المصنع: <b class="num">${fmt(st.units.filter(u => u.depotIds?.[d.id] === p.id).length)}</b>. تتوزع الفرق تلقائيًا عند اكتمال مصنع جديد.` : b.category === 'housing' ? 'مبنى سكني؛ لا ينتج دخلًا.' : 'هذا المبنى لا ينتج دخلًا.'}</p>`}`;
    body += `<div class="row">${b.income ? `<button class="btn gold" data-a="collectB" data-k="collectB" data-income-amount="${income.amount}" ${ready ? '' : 'disabled'}>جمع الدخل · <span class="num">${fmt(income.amount)}</span></button>` : ''}<button class="btn ghost" data-a="rotate-building" data-k="rotate-building" ${factoryRotationBlocked(st, p.id) ? 'disabled' : ''}>${IC.rotate} ${rotateLabel(b.id)}</button></div>${pendingFaceNote(b.id)}`;
    if (factoryRotationBlocked(st, p.id)) body += '<p class="note">يمكن تدوير المصنع بعد عودة آلياته؛ يبقى مدخل الفريق ثابتًا أثناء المهمة.</p>';
  }
  if (p.status === 'building') body += `<button class="btn ghost" data-a="rotate-building" data-k="rotate-building">${IC.rotate} ${rotateLabel(b.id)}</button>${pendingFaceNote(b.id)}`;
  if (p.status === 'built' || p.status === 'building') {
    const blocked = factoryRotationBlocked(st, p.id);
    body += `<div class="row"><button class="btn" data-a="dispose-building" data-id="sellBuilding" data-k="sell-building" ${blocked ? 'disabled' : ''}>بيع · <span class="num">${fmt(buildingSaleValue(p))}</span></button><button class="btn sumac" data-a="dispose-building" data-id="removeBuilding" data-k="remove-building" ${blocked ? 'disabled' : ''}>إزالة المبنى</button></div><p class="note">البيع يعيد ٥٠٪ من سعر البناء؛ الإزالة دون تعويض. ${blocked ? 'انتظر عودة جميع آليات المصنع أولًا.' : 'تعود القطعة نظيفة ومتاحة للبناء.'}</p>`;
  }
  const layout = splitActions(body);
  return `<aside class="sheet" aria-labelledby="sh-t"><div class="sheet-head"><div><h2 id="sh-t">${esc(title)}</h2><p>${esc(sub)} · قطعة <span class="num">${fmt(p.id + 1)}</span></p></div><button class="icon-btn" data-a="close-sheet" data-k="close-sheet" aria-label="إغلاق اللوحة">${IC.close}</button></div><div class="sheet-body ${bodyCls}" data-sk="body">${layout.body}</div>${layout.footer ? `<footer class="sheet-footer">${layout.footer}</footer>` : ''}</aside>`;
}

// ---------- dialogs ----------
const dlg = document.createElement('dialog'); dlg.setAttribute('aria-labelledby', 'dlg-t'); document.body.appendChild(dlg);
const soundControl=document.createElement('button');
soundControl.className='icon-btn sound-toggle';soundControl.dataset.a='dlg';soundControl.dataset.id='audio';
soundControl.setAttribute('aria-label','إعدادات الصوت');soundControl.setAttribute('aria-haspopup','dialog');
soundControl.innerHTML=I('<path d="M4 9h4l5-4v14l-5-4H4zM17 8c3 2 3 6 0 8M20 5c5 4 5 10 0 14"/>');
document.body.appendChild(soundControl);
dlg.addEventListener('close', () => { ui.dialog = null; resetScroll('dlg'); regions.delete('dlg'); render(); });
function dialogHtml(st: GameState, now: number): [string, string] {
  if (ui.dialog === 'building-disposal') {
    const choice = disposal, d = st.districts.find(d => d.id === choice?.districtId);
    const p = d?.plots.find(p => p.id === choice?.plotId);
    if (!choice || !p || st.currentDistrict !== choice.districtId || p.buildingId !== choice.buildingId ||
        p.startedAt !== choice.startedAt || (p.status !== 'building' && p.status !== 'built')) {
      return ['المبنى تغيّر', '<p>لم ننفّذ أي بيع أو إزالة. أغلق النافذة وافتح تفاصيل المبنى مجددًا.</p><button class="btn" data-a="close-dlg">إغلاق</button>'];
    }
    const selling = choice.mode === 'sellBuilding', blocked = factoryRotationBlocked(st, p.id);
    const income = buildingAccruedIncome(p, now);
    return [selling ? 'تأكيد بيع المبنى' : 'تأكيد إزالة المبنى',
      `<p>هل تريد ${selling ? 'بيع' : 'إزالة'} <b>${esc(building(p.buildingId).name)}</b> من القطعة <span class="num">${fmt(p.id + 1)}</span>؟</p>
      <dl class="kv"><dt>${selling ? 'مبلغ البيع · ٥٠٪' : 'التعويض عن المبنى'}</dt><dd class="num">${fmt(selling ? buildingSaleValue(p) : 0)}</dd>${income ? `<dt>دخل مكتمل يُجمع تلقائيًا</dt><dd class="num">${fmt(income)}</dd>` : ''}</dl>
      <p class="note">سيختفي المبنى وتصبح القطعة نظيفة ومتاحة للبناء. ${p.status === 'building' ? 'سيُلغى الإنشاء الحالي نهائيًا. ' : ''}لا يمكن التراجع عن هذه العملية.</p>
      ${p.buildingId === 'recycling' ? `<p class="note">لن تُحذف آلياتك؛ تُوزّع على المصانع المتبقية، أو تبقى محفوظة إلى أن تبني مصنعًا جديدًا.</p>` : ''}
      ${blocked ? '<p role="alert">انتظر عودة جميع آليات هذا المصنع أولًا.</p>' : ''}
      <div class="row"><button class="btn ghost" data-a="close-dlg" data-k="cancel-disposal">إلغاء</button><button class="btn ${selling ? 'gold' : 'sumac'}" data-a="confirm-disposal" data-k="confirm-disposal" ${blocked ? 'disabled' : ''}>${selling ? 'تأكيد البيع' : 'تأكيد الإزالة'}</button></div>`];
  }
  if(ui.dialog==='audio'){
    const p=gameAudio.prefs;
    const rows=([['master','الصوت العام'],['effects','الآليات والعمل والأزرار'],['ambience','أجواء المدينة والطبيعة']] as const)
      .map(([key,label])=>`<label class="audio-row" for="audio-${key}"><span>${label}<b class="num">${fmt(p[key]*100)}٪</b></span><input id="audio-${key}" data-pref="${key}" data-k="audio-${key}" type="range" min="0" max="100" step="1" value="${Math.round(p[key]*100)}"/></label>`).join('');
    return ['الصوتيات',`<p class="note" role="status">${esc(gameAudio.status)}</p>${rows}
      <p class="note audio-help">يعلو صوت المواقع القريبة عند التكبير، ويتلاشى عند إبعاد الكاميرا. يبدأ بعد أول لمسة ويتوقف عند مغادرة الصفحة.</p>
      <p class="note audio-music">النغمة المرفقة: ٤٠٪ في التحميل والرئيسية، و١٥٪ داخل الحي، مع انتقال تدريجي. ${esc(gameAudio.theme.status)}</p>
      <div class="row"><button class="btn gold" data-a="audio-toggle" data-k="audio-toggle" aria-pressed="${p.enabled}">${p.enabled?'كتم جميع الأصوات':'تشغيل الصوت'}</button><button class="btn" data-a="audio-test">تجربة الصوت</button>${gameAudio.status.includes('تعذّر')||gameAudio.theme.status.includes('تعذّر')?'<button class="btn" data-a="audio-retry">إعادة المحاولة</button>':''}</div>`];
  }
  const d = cur();
  if (ui.dialog === 'fleet') {
    const b = busy(st);
    return ['الآليات', `${!hasRecycler(st) ? '<p class="note">شراء الآليات يتطلب مصنع إعادة تدوير مكتملًا.</p>' : ''}
      <h3>شراء</h3><div class="list">${(Object.keys(EQUIPMENT) as EquipmentKind[]).map(k => `<div class="item"><img src="${machinePreview(k)}" data-veh="${k}" alt=""/><div><h3>${EQUIPMENT[k].name}</h3><p>السعر <span class="num">${fmt(EQUIPMENT[k].cost)}</span></p></div><button class="btn sm" data-a="buy" data-id="${k}" data-k="buy-${k}" ${hasRecycler(st) && st.coins >= EQUIPMENT[k].cost ? '' : 'disabled'}>شراء</button></div>`).join('')}</div>
      <h3>أسطولك (${fmt(st.units.length)})</h3>${d ? `<p class="note">الآليات حسب المصنع: ${d.plots.filter(p => p.buildingId === 'recycling' && p.status === 'built').map(p => `قطعة ${fmt(p.id + 1)}: ${fmt(st.units.filter(u => u.depotIds?.[d.id] === p.id).length)}`).join(' · ') || 'لا يوجد مصنع مكتمل'}</p>` : ''}${st.units.length ? `<div class="list">${st.units.map(u => { const cost = Math.floor(u.purchasePrice / 2); return `<div class="item"><span class="st" style="background:${b.has(u.id) ? 'var(--ochre)' : 'var(--olive)'}"></span><div><h3>${EQUIPMENT[u.kind].name} · مستوى <span class="num">${fmt(u.level)}</span>/٣</h3><p>${b.has(u.id) ? 'في مهمة' : 'متاحة'} · الترقية تسرّع السفر وتزيد الحمولة، العمل يبقى ٦٠ ثانية</p></div><button class="btn ghost sm" data-a="upg" data-id="${u.id}" data-k="u-${u.id}" ${u.level >= 3 || st.coins < cost ? 'disabled' : ''}>${u.level >= 3 ? 'أقصى مستوى' : `ترقية <span class="num">${fmt(cost)}</span>`}</button></div>`; }).join('')}</div>` : '<p class="note">لا توجد آليات بعد.</p>'}
      <h3>المهام الجارية</h3>${st.jobs.length ? `<div class="list">${st.jobs.map(j => { const { label, end } = jobDisplay(j, now); return `<div class="item building"><span class="st"></span><div><h3>${esc(dName(j.districtId))} · قطعة <span class="num">${fmt(j.plotId + 1)}</span></h3><p>${label} · متبقٍ <span class="num">${dur(end - now)}</span></p></div><span></span></div>`; }).join('')}</div>` : '<p class="note">لا توجد مهام الآن.</p>'}`];
  }
  if (ui.dialog === 'mats') {
    const i = st.inventory;
    return ['مخزن المواد', `<div class="mats"><div class="mat" style="--c:#e99c24"><span class="mat-title">${resourceIcon('concrete')}إسمنت وخرسانة</span><b class="num">${fmt(i.concrete)}</b></div><div class="mat" style="--c:#258ecc"><span class="mat-title">${resourceIcon('iron')}حديد</span><b class="num">${fmt(i.iron)}</b></div><div class="mat" style="--c:#68a62c"><span class="mat-title">${resourceIcon('wood')}خشب</span><b class="num">${fmt(i.wood)}</b></div><div class="mat" style="--c:#5f6d33">مواد أخرى<b class="num">${fmt(i.other)}</b></div></div>
      <p>القيمة الفعلية المخزنة</p><div class="big-num num">${fmt(i.value)}</div><p class="note">تُضاف المواد فقط عند عودة الآليات إلى المصنع. البيع يحوّل كامل المخزون بقيمته الدقيقة.</p>
      <button class="btn gold" data-a="sell" data-k="sell" ${i.value > 0 ? '' : 'disabled'}>بيع كل المواد مقابل <span class="num">${fmt(i.value)}</span></button>`];
  }
  if (ui.dialog === 'projects' && d) {
    const defs = projects(d.id === 'rashid');
    return ['مشاريع الحي', `<p class="note">${MINUTE_TEST_TIMERS ? 'وضع الاختبار مؤقتًا: مدد الأنشطة دقيقة واحدة.' : 'تسعة مشاريع بالوقت الحقيقي.'} الزراعة والورشة تُكرَّر بعد الجمع، والسوق يدرّ دخلًا كل ${incomeIntervalLabel()}.</p><div class="list">${defs.map(def => { const ps = d.projects.find(x => x.id === def.id)!; const pre = def.prerequisite ? d.projects.find(x => x.id === def.prerequisite) : null; const locked = pre && pre.status !== 'complete';
      const repeat = def.id === 'farm' || def.id === 'industry'; let btn = '', info = '';
      if (ps.status === 'idle') { info = locked ? `يتطلب: ${esc(defs.find(x => x.id === def.prerequisite)!.name)}` : `<span class="num">${fmt(def.cost)}</span> · ${secs(def.duration)}${def.income ? ` · عائد <span class="num">${fmt(def.income)}</span>` : ''}`; btn = `<button class="btn sm" data-a="proj" data-id="${def.id}" data-k="p-${def.id}" ${locked || st.coins < def.cost ? 'disabled' : ''}>بدء</button>`; }
      else if (ps.status === 'building') { info = `قيد التنفيذ · متبقٍ <span class="num">${dur(ps.endsAt - now)}</span>`; }
      else if (ps.status === 'ready') { info = `جاهز · عائد <span class="num">${fmt(def.income)}</span>`; btn = `<button class="btn gold sm" data-a="cproj" data-id="${def.id}" data-k="c-${def.id}">جمع</button>`; }
      else { if (def.period && ps.incomeAt) { const next = ps.incomeAt + activityDuration(def.period * 1000); const r = now >= next; info = r ? 'الدخل جاهز للجمع' : `الدخل التالي بعد <span class="num">${dur(next - now)}</span>`; btn = `<button class="btn gold sm" data-a="cproj" data-id="${def.id}" data-k="c-${def.id}" ${r ? '' : 'disabled'}>جمع</button>`; } else if (repeat) { info = 'مكتمل · يمكن تكرار الدورة'; btn = `<button class="btn ghost sm" data-a="proj" data-id="${def.id}" data-k="p-${def.id}" ${st.coins < def.cost ? 'disabled' : ''}>دورة جديدة</button>`; } else info = 'مكتمل'; }
      return `<div class="item ${ps.status}"><span class="st"></span><div><h3>${esc(def.name)}</h3><p>${info}</p></div>${btn || '<span></span>'}</div>`; }).join('')}</div>`];
  }
  if (ui.dialog === 'progress' && d) {
    const rub = d.plots.filter(p => p.status === 'rubble').length; const done = d.projects.filter(p => p.status === 'complete').length; const jobs = st.jobs.filter(j => j.districtId === d.id).length;
    const ok = !rub && !jobs && done === 9 && !d.claimed; const def = DISTRICTS.find(x => x.id === d.id)!;
    return ['استلام الحي', `<div class="big-num num">${fmt(districtProgress(d))}٪</div><div class="meter"><i style="transform:scaleX(${districtProgress(d) / 100})"></i></div>
      <div class="check ${!rub ? 'ok' : ''}"><i></i>إزالة الأنقاض: <span class="num">${fmt(d.plots.length - 11 - rub)}/${fmt(d.plots.length - 11)}</span></div>
      <div class="check ${done === 9 ? 'ok' : ''}"><i></i>المشاريع المكتملة: <span class="num">${fmt(done)}/٩</span></div>
      <div class="check ${!jobs ? 'ok' : ''}"><i></i>لا مهام جارية في الحي</div>
      <p>المكافأة: <b class="num">${fmt(def.reward)}</b></p>
      <button class="btn gold" data-a="claim" data-k="claim" ${ok ? '' : 'disabled'}>${d.claimed ? 'تم الاستلام' : 'استلام الحي وفتح التالي'}</button>`];
  }
  if (ui.dialog === 'save') {
    return ['الحفظ', `<p>يُحفظ التقدم تلقائيًا <b>على هذا الجهاز وهذا المتصفح فقط</b>. لا توجد مزامنة سحابية.</p>${store.storageError ? `<p class="note" role="alert">${esc(store.storageError)}</p>` : ''}
      <div class="row"><button class="btn" data-a="export" data-k="exp">تنزيل نسخة الحفظ</button></div>
      <p class="note">إعادة الضبط تحذف كل التقدم من هذا الجهاز ولا يمكن التراجع عنها.</p><button class="btn sumac" data-a="reset" data-k="reset">إعادة ضبط اللعبة</button>`];
  }
  return ['', ''];
}
function renderDialog() {
  if (!ui.dialog) { if (dlg.open) dlg.close(); return; }
  const [t, body] = dialogHtml(S(), Date.now()); if (!t) { dlg.close(); return; }
  const layout = dialogLayout(body, ui.dialogTab, ui.dialogPage);
  const html = `<div class="dlg-head"><h2 id="dlg-t">${t}</h2><button class="icon-btn" data-a="close-dlg" data-k="close-dlg" aria-label="إغلاق">${IC.close}</button></div>${layout.tabs}<div class="dlg-body" data-sk="dlg">${layout.body}</div>${layout.footer ? `<footer class="dlg-footer">${layout.footer}</footer>` : ''}`;
  if (regions.get('dlg') !== html) { const f = document.activeElement as HTMLElement | null; const k = f && dlg.contains(f) ? f.dataset.k : undefined; const pos = snap(dlg); patchContent(dlg, html); regions.set('dlg', html); restore(dlg, pos); if (k) (dlg.querySelector(`[data-k="${CSS.escape(k)}"]`) as HTMLElement | null)?.focus({ preventScroll: true }); }
  if (!dlg.open) dlg.showModal();
}

// ---------- master render ----------
let scene: CityScene | null = null;
function render() {
  const st = S();
  if(!st.currentDistrict)cityLoading=false;
  syncMusic();
  gameAudio.mode(st);
  soundControl.style.bottom=st.currentDistrict?'var(--sound-city-bottom,112px)':'18px';
  soundControl.hidden=ui.plot!==null||ui.dialog!==null;
  soundControl.dataset.muted=String(!gameAudio.prefs.enabled);
  soundControl.setAttribute('aria-expanded',String(ui.dialog==='audio'));
  if (st.currentDistrict) { stage.classList.remove('hidden'); region('world', ''); renderCity(); scene?.sync(); }
  else { cityStatus.hidden = true; stage.classList.add('hidden'); region('hud', ''); region('sheet', ''); ui.plot = null; renderWorld(); }
  renderDialog();
  resetKeys.clear();
}

// ---------- events ----------
document.addEventListener('click', e => {
  const t = (e.target as HTMLElement).closest<HTMLElement>('[data-a]'); if (!t || (t as HTMLButtonElement).disabled) return;
  if(!['enter','map','place','clear','buy','upg','sell','proj','cproj','collectB','collect-all','claim'].includes(t.dataset.a??''))gameAudio.click();
  const id = t.dataset.id ?? ''; const st = S();
  switch (t.dataset.a) {
    case 'audio-toggle': gameAudio.setPreference('enabled',!gameAudio.prefs.enabled); break;
    case 'audio-test': void gameAudio.unlock().then(()=>gameAudio.click(true)); break;
    case 'audio-retry': gameAudio.retry(); break;
    case 'pick-d': ui.selDistrict = id; resetScroll('world'); break;
    case 'enter': ui.objectiveHidden = false; act({ type: 'enter', districtId: id }); break;
    case 'map': resetScroll('body', 'cat', 'detail', 'world'); ui.plot = null; scene?.select(null); act({ type: 'map' }); break;
    case 'close-sheet': resetScroll('body', 'cat', 'detail'); ui.plot = null; ui.pick = null; scene?.select(null); break;
    case 'cat': ui.cat = id as Category | 'all'; ui.catPage = 0; ui.pick = null; resetScroll('cat', 'detail', 'detail-top'); break;
    case 'catalog-page': ui.catPage = Math.max(0, Number(id)); resetScroll('cat'); break;
    case 'dialog-tab': ui.dialogTab = Number(id); ui.dialogPage = 0; resetScroll('dlg'); break;
    case 'dialog-page': ui.dialogPage = Math.max(0, Number(id)); resetScroll('dlg'); break;
    case 'pickb': ui.pick = id; ui.orient = 0; resetScroll('detail', 'detail-top'); break;
    case 'back-catalog': ui.pick = null; resetScroll('detail', 'detail-top'); break;
    case 'rot': if (ui.pick) ui.orient = nextVisibleFace(ui.pick, ui.orient); break;
    case 'rotate-building': {
      const p = store.state.districts.find(d => d.id === store.state.currentDistrict)?.plots.find(p => p.id === ui.plot);
      if (p) act({ type: 'rotateBuilding', plotId: p.id, orientation: nextVisibleFace(p.buildingId, p.orientation) });
      break;
    }
    case 'dispose-building': {
      const p = st.districts.find(d => d.id === st.currentDistrict)?.plots.find(p => p.id === ui.plot);
      if (p && st.currentDistrict && (id === 'sellBuilding' || id === 'removeBuilding')) {
        disposal = { districtId: st.currentDistrict, plotId: p.id, buildingId: p.buildingId, startedAt: p.startedAt, mode: id };
        ui.dialog = 'building-disposal'; ui.dialogTab = 0; ui.dialogPage = 0; resetScroll('dlg');
      }
      break;
    }
    case 'confirm-disposal': {
      const choice = disposal;
      if (choice && choice.districtId === st.currentDistrict &&
          act({ type: choice.mode, plotId: choice.plotId, expected: { buildingId: choice.buildingId, startedAt: choice.startedAt } },
            choice.mode === 'sellBuilding' ? 'تم بيع المبنى وأصبحت القطعة متاحة للبناء' : 'تمت إزالة المبنى وأصبحت القطعة متاحة للبناء')) {
        disposal = null; ui.dialog = null; ui.pick = null;
      }
      break;
    }
    case 'place': if (ui.plot != null && ui.pick && act({ type: 'place', plotId: ui.plot, buildingId: ui.pick, orientation: ui.orient }, `بدأ إنشاء ${building(ui.pick).name}`)) ui.pick = null; break;
    case 'clear': if (ui.plot != null) act({ type: 'clear', plotId: ui.plot }, 'انطلقت الآليات من المصنع'); break;
    case 'collectB': {
      if (ui.plot != null) {
        const previousCoins = S().coins;
        if (act({ type: 'collectBuilding', plotId: ui.plot })) toast(`تم جمع ${fmt(S().coins - previousCoins)} عملة من دخل المبنى`);
      }
      break;
    }
    case 'collect-all': {
      const previousCoins = S().coins;
      if (act({ type: 'collectAllIncome' })) toast(`تم جمع ${fmt(S().coins - previousCoins)} عملة من دخل الحي`);
      break;
    }
    case 'buy': act({ type: 'buy', kind: id as EquipmentKind }, `تم شراء ${EQUIPMENT[id as EquipmentKind].name}`); break;
    case 'upg': act({ type: 'upgrade', unitId: id }, 'تمت الترقية'); break;
    case 'sell': { const v = st.inventory.value; act({ type: 'sell' }, `تم البيع مقابل ${fmt(v)}`); break; }
    case 'proj': act({ type: 'project', projectId: id }, 'بدأ المشروع'); break;
    case 'cproj': act({ type: 'collectProject', projectId: id }, 'تم الجمع'); break;
    case 'claim': act({ type: 'claim' }, 'تم استلام الحي'); break;
    case 'dlg': resetScroll('dlg'); ui.dialogTab = 0; ui.dialogPage = 0; ui.dialog = id as typeof ui.dialog; break;
    case 'close-dlg': ui.dialog = null; dlg.close(); return;
    case 'hide-obj': ui.objectiveHidden = true; break;
    case 'zin': scene?.zoomBy(1.2); scene?.saveCam(); break;
    case 'zout': scene?.zoomBy(1 / 1.2); scene?.saveCam(); break;
    case 'export': download(store.exportSave(), `newgaza2d-save-${new Date().toISOString().slice(0, 10)}.json`); break;
    case 'reset': if (confirm('حذف كل التقدم من هذا الجهاز نهائيًا؟') && confirm('تأكيد أخير: إعادة الضبط لا يمكن التراجع عنها.') && store.reset()) location.reload(); return;
    case 'retry-city': scene?.retryWorld(); break;
  }
  render();
});
document.addEventListener('keydown', e => { if (e.key === 'Escape' && !dlg.open && ui.plot != null) { ui.plot = null; scene?.select(null); render(); } });
document.addEventListener('input',e=>{
  const input=e.target as HTMLInputElement;
  if(['master','effects','ambience'].includes(input.dataset.pref??'')){
    gameAudio.setPreference(input.dataset.pref as 'master'|'effects'|'ambience',Number(input.value)/100);
    renderDialog();
  }
});

// ---------- Phaser ----------
const game = new Phaser.Game({
  type: Phaser.AUTO, parent: stage, backgroundColor: '#d9c39a', scale: { mode: Phaser.Scale.RESIZE, width: window.innerWidth, height: window.innerHeight },
  scene: [BootScene, CityScene], render: { antialias: true }, input: { activePointers: 3 },
});
// Development-only inspection for the isolated art regression browser.
if (import.meta.env.DEV) Object.defineProperty(window, '__newgazaArtScene', {
  configurable: true, get: () => game.scene.getScene('city'),
});
game.registry.set('hooks', {
  getState: () => store.state,
  onWorldState: showCityStatus,
  onSelect: (id: number | null) => { if (id === null && ui.plot !== null) return; resetScroll('body', 'cat', 'detail'); ui.catPage = 0; ui.plot = id; ui.pick = null; if (id != null) scene?.focusPlot(id); render(); },
  onCamera: (c: { x: number; y: number; zoom: number }) => { try { store.dispatch({ type: 'camera', camera: c }); } catch { /* camera save is best-effort */ } },
});
game.events.once('booted', () => {
  bootLoading = false;
  scene = game.scene.getScene('city') as CityScene;
  document.getElementById('boot')?.classList.add('done');
  render();
});
store.subscribe(() => render());
// DOM text swaps automatically; already-rasterized canvas labels need a refresh
// if the locally hosted font arrives after the scene has started.
void document.fonts.load('500 16px "NewGaza Elgharib"', 'نيو غزة إعادة إعمار الأحياء ١٢٣٬٤٥٦').then(() => {
  for (const current of game.scene.scenes) {
    if (!current.sys.isActive()) continue;
    for (const child of current.children.list) {
      if (child instanceof Phaser.GameObjects.Text) child.updateText();
    }
  }
}).catch(() => toast('تعذّر تحميل الخط المرفق؛ تحقق من الاتصال وأعد فتح اللعبة.', true));
let viewportFrame = 0, previousPageSize = pageSize(), previousDialogPageSize = dialogPageSize();
const refreshViewport = () => {
  cancelAnimationFrame(viewportFrame);
  viewportFrame = requestAnimationFrame(() => {
    const size = pageSize();
    if (size !== previousPageSize) {
      // Keep the same catalog/list position when the number of visible rows
      // changes. Never dismiss a dialog or clear a selected building.
      ui.catPage = Math.floor(ui.catPage * previousPageSize / size);
      previousPageSize = size;
    }
    const dialogSize = dialogPageSize();
    if (dialogSize !== previousDialogPageSize) {
      ui.dialogPage = Math.floor(ui.dialogPage * previousDialogPageSize / dialogSize);
      previousDialogPageSize = dialogSize;
    }
    render();
  });
};
window.addEventListener('resize', refreshViewport);
window.visualViewport?.addEventListener('resize', refreshViewport);
setInterval(() => { store.tick(Date.now()); render(); }, 500);
render();
