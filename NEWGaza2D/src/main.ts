import Phaser from 'phaser';
import './style.css';
import { BUILDINGS, DISTRICTS, EQUIPMENT, building, projects } from './catalog';
import { GameStore, SAVE_KEY, districtProgress, jobPhase } from './engine';
import type { Category, DistrictState, EquipmentKind, GameState, Plot } from './model';
import { BootScene, CityScene } from './scenes/CityScene';
import { drawVehicle, previewDataURL } from './art';

const app = document.getElementById('app')!;
const stage = document.getElementById('stage')!;
const fmt = (n: number) => Math.floor(n).toLocaleString('ar-EG');
const esc = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!));
function dur(ms: number) { const s = Math.max(0, Math.ceil(ms / 1000)); const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = s % 60; return h ? `${fmt(h)} س ${fmt(m)} د` : m ? `${fmt(m)} د ${fmt(x)} ث` : `${fmt(x)} ث`; }
const secs = (s: number) => dur(s * 1000);
const I = (d: string) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`;
const IC = {
  close: I('<path d="M6 6l12 12M18 6L6 18"/>'), map: I('<path d="M9 4L3 6v14l6-2 6 2 6-2V4l-6 2-6-2zM9 4v14M15 6v14"/>'),
  truck: I('<path d="M3 7h11v9H3zM14 10h4l3 3v3h-7"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/>'),
  box: I('<path d="M3 8l9-5 9 5v8l-9 5-9-5zM3 8l9 5 9-5M12 13v8"/>'), plan: I('<path d="M4 4h16v16H4zM4 10h16M10 10v10"/>'),
  flag: I('<path d="M5 21V4M5 4h11l-2 4 2 4H5"/>'), save: I('<path d="M5 3h11l3 3v15H5zM8 3v6h8M8 21v-7h8v7"/>'),
  plus: I('<path d="M12 5v14M5 12h14"/>'), minus: I('<path d="M5 12h14"/>'), rotate: I('<path d="M4 12a8 8 0 0114-5l2 2M20 4v5h-5M20 12a8 8 0 01-14 5l-2-2M4 20v-5h5"/>'),
};
const CAT: Record<Category, string> = { equipment: 'معدات وورش', industry: 'مصانع', housing: 'سكن', farm: 'زراعة', commerce: 'تجارة', leisure: 'ترفيه ومطاعم' };
const PIN: Record<string, [number, number]> = { karama: [80, 12], 'sheikh-radwan': [70, 22], nasr: [60, 30], tuffah: [80, 34], shujaiya: [88, 48], daraj: [66, 44], 'old-city': [56, 54], rimal: [47, 40], sabra: [44, 62], zeitoun: [60, 72], 'tel-al-hawa': [34, 64], 'sheikh-ijlin': [24, 76], rashid: [12, 82] };
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
const ui = { selDistrict: null as string | null, plot: null as number | null, cat: 'equipment' as Category, pick: null as string | null, orient: 0 as 0 | 1, dialog: null as null | 'fleet' | 'mats' | 'projects' | 'progress' | 'save', objectiveHidden: false };
let toastTimer = 0;
function toast(msg: string, err = false) { document.querySelector('.toast')?.remove(); const t = document.createElement('div'); t.className = 'toast' + (err ? ' err' : ''); t.setAttribute('role', err ? 'alert' : 'status'); t.textContent = msg; document.body.appendChild(t); clearTimeout(toastTimer); toastTimer = window.setTimeout(() => t.remove(), 3200); }
function act(a: Parameters<GameStore['dispatch']>[0], ok?: string) { try { store.dispatch(a); if (ok) toast(ok); return true; } catch (e) { toast(e instanceof Error ? e.message : 'تعذر تنفيذ الإجراء', true); return false; } }

const S = () => store.state;
const cur = (): DistrictState | undefined => S().districts.find(d => d.id === S().currentDistrict);
const dName = (id: string) => DISTRICTS.find(d => d.id === id)?.name ?? id;
const hasRecycler = (st: GameState) => st.districts.some(d => d.id === st.currentDistrict && d.plots.some(p => p.buildingId === 'recycling' && p.status === 'built'));
const recyclerStarted = (st: GameState) => st.districts.some(d => d.id === st.currentDistrict && d.plots.some(p => p.buildingId === 'recycling' && (p.status === 'built' || p.status === 'building')));
const busy = (st: GameState) => new Set(st.jobs.flatMap(j => j.unitIds));

// ---------- focus-preserving region renderer ----------
const regions = new Map<string, string>();
function region(id: string, html: string, cls = '') {
  let el = document.getElementById(id);
  if (!html) { el?.remove(); regions.delete(id); return; }
  if (!el) { el = document.createElement('div'); el.id = id; app.appendChild(el); }
  if (cls) el.className = cls;
  if (regions.get(id) === html) return;
  const f = document.activeElement as HTMLElement | null; const key = f && el.contains(f) ? f.dataset.k : undefined;
  el.innerHTML = html; regions.set(id, html);
  if (key) (el.querySelector(`[data-k="${CSS.escape(key)}"]`) as HTMLElement | null)?.focus();
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
  const status = !ds?.unlocked ? `مقفل. يُفتح بعد استلام حي ${esc(DISTRICTS[idx - 1]?.name ?? '')}.` : ds.claimed ? 'تم استلامه. يمكنك العودة إليه وجمع الدخل.' : 'متاح للإعمار الآن.';
  region('world', `<section class="world" aria-label="خريطة غزة">
    <div class="world-map"><div class="map-frame">
      <img src="${base}art/world-map.jpg" alt="خريطة مرسومة لساحل غزة وأحيائها" onerror="this.style.background='#16747a'" />
      <svg class="route" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"><polyline points="${order.map(([x, y]) => `${x},${y}`).join(' ')}" fill="none" stroke="#f1e6d0" stroke-width=".35" stroke-dasharray="1 1" opacity=".8"/></svg>
      ${pins}</div>
      <div class="map-title"><h1>نيو غزة</h1><p>اختر حيًا على الخريطة. كل حي يُستلم بعد إزالة أنقاضه وإكمال مشاريعه التسعة، فيُفتح الحي التالي حتى كورنيش شارع الرشيد.</p></div>
    </div>
    <aside class="world-side">
      <div class="chip-row"><span class="chip"><span class="coin" style="display:inline-block;vertical-align:middle"></span> <b class="num">${fmt(st.coins)}</b></span><span class="chip">أحياء مستلمة <b class="num">${fmt(st.districts.filter(d => d.claimed).length)}/${fmt(DISTRICTS.length)}</b></span></div>
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
  const inv = st.inventory; const readyProj = d.projects.filter(p => p.status === 'ready' || (p.status === 'complete' && p.id === 'commerce' && p.incomeAt && now >= p.incomeAt + 3600000)).length;
  region('hud', `<header class="hud-top">
    <button class="icon-btn" data-a="map" data-k="tomap" aria-label="العودة إلى خريطة غزة">${IC.map}</button>
    <div class="place"><h1>${esc(def.name)}</h1><small>تقدم الحي <span class="num">${fmt(districtProgress(d))}٪</span></small></div>
    <div class="wallet"><span class="pill"><span class="coin"></span><b class="num">${fmt(st.coins)}</b></span>
      <span class="pill m"><span class="sw" style="background:#9b958a"></span>إسمنت <b class="num">${fmt(inv.concrete)}</b></span>
      <span class="pill m"><span class="sw" style="background:#7a4a34"></span>حديد <b class="num">${fmt(inv.iron)}</b></span></div>
  </header>
  ${objective(st, d)}
  ${store.storageError ? `<div class="banner" role="alert"><span>${esc(store.storageError)}</span><button class="btn gold sm" data-a="export" data-k="exp-b">تصدير</button></div>` : ''}
  <div class="zoom"><button class="icon-btn" data-a="zin" aria-label="تكبير">${IC.plus}</button><button class="icon-btn" data-a="zout" aria-label="تصغير">${IC.minus}</button></div>
  <nav class="dock" aria-label="أدوات الحي">
    <button data-a="dlg" data-id="fleet" data-k="d-fleet" aria-expanded="${ui.dialog === 'fleet'}">${IC.truck}<span>الآليات</span>${st.jobs.length ? `<span class="badge num">${fmt(st.jobs.length)}</span>` : ''}</button>
    <button data-a="dlg" data-id="mats" data-k="d-mats" aria-expanded="${ui.dialog === 'mats'}">${IC.box}<span>المواد</span></button>
    <button data-a="dlg" data-id="projects" data-k="d-proj" aria-expanded="${ui.dialog === 'projects'}">${IC.plan}<span>المشاريع</span>${readyProj ? `<span class="badge num">${fmt(readyProj)}</span>` : ''}</button>
    <button data-a="dlg" data-id="progress" data-k="d-prog" aria-expanded="${ui.dialog === 'progress'}">${IC.flag}<span>الاستلام</span></button>
    <button data-a="dlg" data-id="save" data-k="d-save" aria-expanded="${ui.dialog === 'save'}">${IC.save}<span>الحفظ</span></button>
  </nav>`);
  region('sheet', ui.plot != null ? plotSheet(st, d, now) : '', 'sheet-wrap');
}

function jobFor(st: GameState, d: DistrictState, p: Plot) { return st.jobs.find(j => j.districtId === d.id && j.plotId === p.id); }
function jobHtml(st: GameState, d: DistrictState, p: Plot, now: number) {
  const j = jobFor(st, d, p); if (!j) return '';
  const ph = jobPhase(j, now); const end = ph === 'travel' ? j.arrival : ph === 'work' ? j.workEnd : j.returnEnd;
  const span = ph === 'travel' ? j.arrival - j.start : ph === 'work' ? j.workEnd - j.arrival : j.returnEnd - j.workEnd;
  return `<div class="note"><b>${PHASE[ph]}</b> — متبقٍ <span class="num">${dur(end - now)}</span><div class="meter" style="margin-top:6px"><i style="transform:scaleX(${Math.min(1, 1 - (end - now) / Math.max(1, span))})"></i></div><small>الآليات: ${j.unitIds.map(id => EQUIPMENT[st.units.find(u => u.id === id)?.kind ?? 'truck'].name).join('، ')} — محجوزة حتى العودة. تُضاف المواد عند وصولها للمصنع.</small></div>`;
}

const previews = new Map<string, string>();
const prev = (id: string, o: 0 | 1) => { const k = id + o; if (!previews.has(k)) previews.set(k, previewDataURL(id, o)); return previews.get(k)!; };

function plotSheet(st: GameState, d: DistrictState, now: number) {
  const p = d.plots.find(x => x.id === ui.plot); if (!p) return '';
  const b = building(p.buildingId);
  let title = '', sub = '', body = '';
  if (p.status === 'rubble') {
    title = `أنقاض ${b.name}`; sub = 'أرض مغطاة بالركام';
    const free = st.units.filter(u => !busy(st).has(u.id));
    const j = jobFor(st, d, p);
    body = j ? jobHtml(st, d, p, now) : `<p>ترسل الآليات المتاحة من مصنع إعادة التدوير عبر الشوارع. السفر حسب المسافة ومستوى الآلية، ثم <b>٦٠ ثانية عمل</b> في الموقع، ثم العودة محمّلة (٣٠–٩٠ ثانية). تصبح الأرض نظيفة عند انتهاء العمل.</p>
      ${!hasRecycler(st) ? '<p class="note">يلزم أولًا بناء مصنع إعادة التدوير على أرض نظيفة.</p>' : !st.units.length ? '<p class="note">لا توجد آليات بعد. اشترِ آلية من لوحة الآليات.</p>' : `<p class="note">آليات متاحة: <b class="num">${fmt(free.length)}</b> من ${fmt(st.units.length)}</p>`}
      <button class="btn gold" data-a="clear" data-k="clear" ${hasRecycler(st) && free.length ? '' : 'disabled'}>إرسال الآليات لإزالة الأنقاض</button>`;
  } else if (p.status === 'empty') {
    title = 'أرض نظيفة'; sub = 'جاهزة للبناء';
    const cats = (Object.keys(CAT) as Category[]);
    const list = BUILDINGS.filter(x => x.category === ui.cat);
    const pick = ui.pick ? building(ui.pick) : null;
    body = `${!recyclerStarted(st) ? '<p class="note">ابدأ بمصنع إعادة التدوير: هو نقطة انطلاق كل الآليات.</p>' : ''}
      <div class="tabs" role="tablist">${cats.map(c => `<button role="tab" aria-selected="${ui.cat === c}" data-a="cat" data-id="${c}" data-k="t-${c}">${CAT[c]}</button>`).join('')}</div>
      <div class="cat-grid">${list.map(x => `<button class="cat-item" aria-pressed="${ui.pick === x.id}" data-a="pickb" data-id="${x.id}" data-k="b-${x.id}"><img src="${prev(x.id, 0)}" alt="" loading="lazy"/><b>${esc(x.name)}</b><small><span class="num">${fmt(x.cost)}</span> · ${secs(x.duration)}${x.income ? ` · دخل <span class="num">${fmt(x.income)}</span>/س` : ''}</small></button>`).join('')}</div>
      ${pick ? `<div class="preview"><span class="ori">${ui.orient === 0 ? 'أمام: الباب نحوك' : 'خلف: الباب في الجهة الخلفية'}</span><img src="${prev(pick.id, ui.orient)}" alt="معاينة ${esc(pick.name)} ${ui.orient === 0 ? 'من الأمام' : 'من الخلف'}"/></div>
      <dl class="kv"><dt>التكلفة</dt><dd class="num">${fmt(pick.cost)}</dd><dt>مدة البناء</dt><dd>${secs(pick.duration)} (وقت حقيقي)</dd><dt>الطوابق</dt><dd class="num">${fmt(pick.floors)}</dd><dt>الدخل</dt><dd>${pick.income ? `<span class="num">${fmt(pick.income)}</span> كل ساعة` : 'لا يوجد'}</dd></dl>
      <div class="row"><button class="btn ghost" data-a="rot" data-k="rot">${IC.rotate} تدوير أمام/خلف</button><button class="btn gold" data-a="place" data-k="place" ${st.coins >= pick.cost ? '' : 'disabled'}>${st.coins >= pick.cost ? 'بناء هنا' : 'الرصيد غير كافٍ'}</button></div>` : '<p class="note">اختر مبنى لمعاينته من الأمام والخلف.</p>'}`;
  } else if (p.status === 'building') {
    title = b.name; sub = `قيد الإنشاء · ${p.orientation === 0 ? 'أمام' : 'خلف'}`;
    const f = Math.min(1, (now - p.startedAt) / Math.max(1, p.endsAt - p.startedAt));
    body = `<div class="meter"><i style="transform:scaleX(${f})"></i></div><dl class="kv"><dt>المرحلة</dt><dd>${f < 0.3 ? 'الأساسات وحديد التسليح' : f < 0.68 ? 'الهيكل والسقالات' : 'الجدران والتشطيب'}</dd><dt>متبقٍ</dt><dd class="num">${dur(p.endsAt - now)}</dd><dt>المدة الكلية</dt><dd>${secs(b.duration)}</dd></dl><p class="note">البناء يستمر بالوقت الحقيقي حتى عند إغلاق اللعبة.</p>`;
  } else {
    title = b.name; sub = `${CAT[b.category]} · ${p.orientation === 0 ? 'أمام' : 'خلف'}`;
    const ready = b.income > 0 && now >= p.incomeAt + 3600000;
    body = `<div class="preview"><img src="${prev(b.id, p.orientation)}" alt=""/></div>
      ${b.income ? `<dl class="kv"><dt>الدخل</dt><dd><span class="num">${fmt(b.income)}</span> كل ساعة</dd><dt>الدفعة التالية</dt><dd>${ready ? 'جاهزة للجمع' : `بعد <span class="num">${dur(p.incomeAt + 3600000 - now)}</span>`}</dd></dl>
      <button class="btn gold" data-a="collectB" data-k="collectB" ${ready ? '' : 'disabled'}>جمع الدخل</button>` : `<p class="note">${b.id === 'recycling' ? 'مقر الآليات: منه تنطلق وإليه تعود المواد.' : 'مبنى خدمي بلا دخل مباشر.'}</p>`}`;
  }
  return `<aside class="sheet" aria-labelledby="sh-t"><div class="sheet-head"><div><h2 id="sh-t">${esc(title)}</h2><p>${esc(sub)} · قطعة <span class="num">${fmt(p.id + 1)}</span></p></div><button class="icon-btn" data-a="close-sheet" data-k="close-sheet" aria-label="إغلاق اللوحة">${IC.close}</button></div><div class="sheet-body">${body}</div></aside>`;
}

// ---------- dialogs ----------
const dlg = document.createElement('dialog'); dlg.setAttribute('aria-labelledby', 'dlg-t'); document.body.appendChild(dlg);
dlg.addEventListener('close', () => { ui.dialog = null; regions.delete('dlg'); render(); });
dlg.addEventListener('click', e => { if (e.target === dlg) dlg.close(); });
function dialogHtml(st: GameState, now: number): [string, string] {
  const d = cur();
  if (ui.dialog === 'fleet') {
    const b = busy(st);
    return ['الآليات', `${!hasRecycler(st) ? '<p class="note">شراء الآليات يتطلب مصنع إعادة تدوير مكتملًا.</p>' : ''}
      <h3>شراء</h3><div class="list">${(Object.keys(EQUIPMENT) as EquipmentKind[]).map(k => `<div class="item"><img src="" data-veh="${k}" alt=""/><div><h3>${EQUIPMENT[k].name}</h3><p>السعر <span class="num">${fmt(EQUIPMENT[k].cost)}</span></p></div><button class="btn sm" data-a="buy" data-id="${k}" data-k="buy-${k}" ${hasRecycler(st) && st.coins >= EQUIPMENT[k].cost ? '' : 'disabled'}>شراء</button></div>`).join('')}</div>
      <h3>أسطولك (${fmt(st.units.length)})</h3>${st.units.length ? `<div class="list">${st.units.map(u => { const cost = Math.floor(u.purchasePrice / 2); return `<div class="item"><span class="st" style="background:${b.has(u.id) ? 'var(--ochre)' : 'var(--olive)'}"></span><div><h3>${EQUIPMENT[u.kind].name} · مستوى <span class="num">${fmt(u.level)}</span>/٣</h3><p>${b.has(u.id) ? 'في مهمة' : 'متاحة'} · الترقية تسرّع السفر وتزيد الحمولة، العمل يبقى ٦٠ ثانية</p></div><button class="btn ghost sm" data-a="upg" data-id="${u.id}" data-k="u-${u.id}" ${u.level >= 3 || st.coins < cost ? 'disabled' : ''}>${u.level >= 3 ? 'أقصى مستوى' : `ترقية <span class="num">${fmt(cost)}</span>`}</button></div>`; }).join('')}</div>` : '<p class="note">لا توجد آليات بعد.</p>'}
      <h3>المهام الجارية</h3>${st.jobs.length ? `<div class="list">${st.jobs.map(j => { const ph = jobPhase(j, now); const end = ph === 'travel' ? j.arrival : ph === 'work' ? j.workEnd : j.returnEnd; return `<div class="item building"><span class="st"></span><div><h3>${esc(dName(j.districtId))} · قطعة <span class="num">${fmt(j.plotId + 1)}</span></h3><p>${PHASE[ph]} · متبقٍ <span class="num">${dur(end - now)}</span></p></div><span></span></div>`; }).join('')}</div>` : '<p class="note">لا توجد مهام الآن.</p>'}`];
  }
  if (ui.dialog === 'mats') {
    const i = st.inventory;
    return ['مخزن المواد', `<div class="mats"><div class="mat" style="--c:#9b958a">إسمنت وخرسانة<b class="num">${fmt(i.concrete)}</b></div><div class="mat" style="--c:#7a4a34">حديد<b class="num">${fmt(i.iron)}</b></div><div class="mat" style="--c:#a87a44">خشب<b class="num">${fmt(i.wood)}</b></div><div class="mat" style="--c:#5f6d33">مواد أخرى<b class="num">${fmt(i.other)}</b></div></div>
      <p>القيمة الفعلية المخزنة</p><div class="big-num num">${fmt(i.value)}</div><p class="note">تُضاف المواد فقط عند عودة الآليات إلى المصنع. البيع يحوّل كامل المخزون بقيمته الدقيقة.</p>
      <button class="btn gold" data-a="sell" data-k="sell" ${i.value > 0 ? '' : 'disabled'}>بيع كل المواد مقابل <span class="num">${fmt(i.value)}</span></button>`];
  }
  if (ui.dialog === 'projects' && d) {
    const defs = projects(d.id === 'rashid');
    return ['مشاريع الحي', `<p class="note">تسعة مشاريع بالوقت الحقيقي. الزراعة والورشة تُكرَّر بعد الجمع، والسوق يدرّ دخلًا كل ساعة.</p><div class="list">${defs.map(def => { const ps = d.projects.find(x => x.id === def.id)!; const pre = def.prerequisite ? d.projects.find(x => x.id === def.prerequisite) : null; const locked = pre && pre.status !== 'complete';
      const repeat = def.id === 'farm' || def.id === 'industry'; let btn = '', info = '';
      if (ps.status === 'idle') { info = locked ? `يتطلب: ${esc(defs.find(x => x.id === def.prerequisite)!.name)}` : `<span class="num">${fmt(def.cost)}</span> · ${secs(def.duration)}${def.income ? ` · عائد <span class="num">${fmt(def.income)}</span>` : ''}`; btn = `<button class="btn sm" data-a="proj" data-id="${def.id}" data-k="p-${def.id}" ${locked || st.coins < def.cost ? 'disabled' : ''}>بدء</button>`; }
      else if (ps.status === 'building') { info = `قيد التنفيذ · متبقٍ <span class="num">${dur(ps.endsAt - now)}</span>`; }
      else if (ps.status === 'ready') { info = `جاهز · عائد <span class="num">${fmt(def.income)}</span>`; btn = `<button class="btn gold sm" data-a="cproj" data-id="${def.id}" data-k="c-${def.id}">جمع</button>`; }
      else { if (def.period && ps.incomeAt) { const next = ps.incomeAt + def.period * 1000; const r = now >= next; info = r ? 'دخل الساعة جاهز' : `الدخل التالي بعد <span class="num">${dur(next - now)}</span>`; btn = `<button class="btn gold sm" data-a="cproj" data-id="${def.id}" data-k="c-${def.id}" ${r ? '' : 'disabled'}>جمع</button>`; } else if (repeat) { info = 'مكتمل · يمكن تكرار الدورة'; btn = `<button class="btn ghost sm" data-a="proj" data-id="${def.id}" data-k="p-${def.id}" ${st.coins < def.cost ? 'disabled' : ''}>دورة جديدة</button>`; } else info = 'مكتمل'; }
      return `<div class="item ${ps.status}"><span class="st"></span><div><h3>${esc(def.name)}</h3><p>${info}</p></div>${btn || '<span></span>'}</div>`; }).join('')}</div>`];
  }
  if (ui.dialog === 'progress' && d) {
    const rub = d.plots.filter(p => p.status === 'rubble').length; const done = d.projects.filter(p => p.status === 'complete').length; const jobs = st.jobs.filter(j => j.districtId === d.id).length;
    const ok = !rub && !jobs && done === 9 && !d.claimed; const def = DISTRICTS.find(x => x.id === d.id)!;
    return ['استلام الحي', `<div class="big-num num">${fmt(districtProgress(d))}٪</div><div class="meter"><i style="transform:scaleX(${districtProgress(d) / 100})"></i></div>
      <div class="check ${!rub ? 'ok' : ''}"><i></i>إزالة الأنقاض: <span class="num">${fmt(53 - rub)}/٥٣</span></div>
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
  if (!ui.dialog) return;
  const [t, body] = dialogHtml(S(), Date.now()); if (!t) { dlg.close(); return; }
  const html = `<div class="dlg-head"><h2 id="dlg-t">${t}</h2><button class="icon-btn" data-a="close-dlg" data-k="close-dlg" aria-label="إغلاق">${IC.close}</button></div><div class="dlg-body">${body}</div>`;
  if (regions.get('dlg') !== html) { const f = document.activeElement as HTMLElement | null; const k = f && dlg.contains(f) ? f.dataset.k : undefined; dlg.innerHTML = html; regions.set('dlg', html); vehImgs(); if (k) (dlg.querySelector(`[data-k="${CSS.escape(k)}"]`) as HTMLElement | null)?.focus(); }
  if (!dlg.open) dlg.showModal();
}
const vehCache: Record<string, string> = {};
function vehImgs() { dlg.querySelectorAll<HTMLImageElement>('img[data-veh]').forEach(img => { const k = img.dataset.veh as EquipmentKind; if (!vehCache[k]) { const c = document.createElement('canvas'); c.width = c.height = 96; drawVehicle(c.getContext('2d')!, k, 3, 0); vehCache[k] = c.toDataURL(); } img.src = vehCache[k]; }); }

// ---------- master render ----------
let scene: CityScene | null = null;
function render() {
  const st = S();
  if (st.currentDistrict) { stage.classList.remove('hidden'); region('world', ''); renderCity(); scene?.sync(); }
  else { stage.classList.add('hidden'); region('hud', ''); region('sheet', ''); ui.plot = null; renderWorld(); }
  renderDialog();
}

// ---------- events ----------
document.addEventListener('click', e => {
  const t = (e.target as HTMLElement).closest<HTMLElement>('[data-a]'); if (!t || (t as HTMLButtonElement).disabled) return;
  const id = t.dataset.id ?? ''; const st = S();
  switch (t.dataset.a) {
    case 'pick-d': ui.selDistrict = id; break;
    case 'enter': ui.objectiveHidden = false; act({ type: 'enter', districtId: id }); break;
    case 'map': ui.plot = null; scene?.select(null); act({ type: 'map' }); break;
    case 'close-sheet': ui.plot = null; ui.pick = null; scene?.select(null); break;
    case 'cat': ui.cat = id as Category; break;
    case 'pickb': ui.pick = id; ui.orient = 0; break;
    case 'rot': ui.orient = ui.orient ? 0 : 1; break;
    case 'place': if (ui.plot != null && ui.pick && act({ type: 'place', plotId: ui.plot, buildingId: ui.pick, orientation: ui.orient }, `بدأ إنشاء ${building(ui.pick).name}`)) ui.pick = null; break;
    case 'clear': if (ui.plot != null) act({ type: 'clear', plotId: ui.plot }, 'انطلقت الآليات من المصنع'); break;
    case 'collectB': if (ui.plot != null) act({ type: 'collectBuilding', plotId: ui.plot }, 'تم جمع الدخل'); break;
    case 'buy': act({ type: 'buy', kind: id as EquipmentKind }, `تم شراء ${EQUIPMENT[id as EquipmentKind].name}`); break;
    case 'upg': act({ type: 'upgrade', unitId: id }, 'تمت الترقية'); break;
    case 'sell': { const v = st.inventory.value; act({ type: 'sell' }, `تم البيع مقابل ${fmt(v)}`); break; }
    case 'proj': act({ type: 'project', projectId: id }, 'بدأ المشروع'); break;
    case 'cproj': act({ type: 'collectProject', projectId: id }, 'تم الجمع'); break;
    case 'claim': act({ type: 'claim' }, 'تم استلام الحي'); break;
    case 'dlg': ui.dialog = id as typeof ui.dialog; break;
    case 'close-dlg': dlg.close(); return;
    case 'hide-obj': ui.objectiveHidden = true; break;
    case 'zin': scene?.zoomBy(1.2); scene?.saveCam(); break;
    case 'zout': scene?.zoomBy(1 / 1.2); scene?.saveCam(); break;
    case 'export': download(store.exportSave(), `newgaza2d-save-${new Date().toISOString().slice(0, 10)}.json`); break;
    case 'reset': if (confirm('حذف كل التقدم من هذا الجهاز نهائيًا؟') && confirm('تأكيد أخير: إعادة الضبط لا يمكن التراجع عنها.')) { localStorage.removeItem(SAVE_KEY); location.reload(); } return;
  }
  render();
});
document.addEventListener('keydown', e => { if (e.key === 'Escape' && !dlg.open && ui.plot != null) { ui.plot = null; scene?.select(null); render(); } });

// ---------- Phaser ----------
const game = new Phaser.Game({
  type: Phaser.AUTO, parent: stage, backgroundColor: '#d9c39a', scale: { mode: Phaser.Scale.RESIZE, width: window.innerWidth, height: window.innerHeight },
  scene: [BootScene, CityScene], render: { antialias: true }, input: { activePointers: 3 },
});
game.registry.set('hooks', {
  getState: () => store.state,
  onSelect: (id: number | null) => { ui.plot = id; ui.pick = null; if (id != null) scene?.focusPlot(id); render(); },
  onCamera: (c: { x: number; y: number; zoom: number }) => { try { store.dispatch({ type: 'camera', camera: c }); } catch { /* camera save is best-effort */ } },
});
game.events.once('booted', () => {
  scene = game.scene.getScene('city') as CityScene;
  document.getElementById('boot')?.classList.add('done');
  render();
});
store.subscribe(() => render());
setInterval(() => { store.tick(Date.now()); render(); }, 500);
render();
