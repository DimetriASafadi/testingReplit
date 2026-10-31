import './expansion-ui.css';
import type { DistrictState, GameState } from './model';
import { DIFFICULTIES, PAVER_COST, ROAD_COST, difficultyOf, workDuration } from './economy';
import { roadsFor, roadMid, streetFor } from './roads';
import { formatNumber as fmt } from './number-format';
import { actualWorkDuration, STREET_WORK_MS } from './construction-rules';
import { localUnit, roadUnitBusy, warehousePlots } from './warehouse-capacity';
import { unitInJob } from './fleet-depots';
import { TEST_TIMERS } from './timing-settings';

/* Shared expansion surfaces: difficulty, wheel, stores, roads, celebration.
   Everything here is pure HTML for the stable dom-patch renderer, except the
   wheel disc and fireworks which are persistent nodes animated by time. */

const esc = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!));
export const cents = (c: number) => `${fmt(c)} سنت`;
export function clock(ms: number) {
  const s = Math.max(0, Math.ceil(ms / 1000)), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = s % 60;
  const p = (n: number) => String(n).padStart(2, '0');
  return h ? `${h}:${p(m)}:${p(x)}` : `${p(m)}:${p(x)}`;
}

// ---------- difficulty ----------
export function difficultyHtml(d: DistrictState, name: string, picked: string) {
  const rows = (Object.keys(DIFFICULTIES) as (keyof typeof DIFFICULTIES)[]).map(k => {
    const v = DIFFICULTIES[k];
    return `<button class="diff-card ${k}" data-a="diff-pick" data-id="${k}" data-k="diff-${k}" aria-pressed="${picked === k}">
      <b>${v.name}</b><span>الدخل <bdi class="num">${fmt(Math.round(v.income * 100))}%</bdi></span><span>مدة العمل <bdi class="num">×${v.time}</bdi></span></button>`;
  }).join('');
  return `<p>قبل بدء الإعمار في <b>${esc(name)}</b> اختر مستوى الصعوبة. يُحفظ الاختيار لهذا الحي ولا يمكن تغييره لاحقًا.</p>
    <div class="diff-grid" role="group" aria-label="مستوى الصعوبة">${rows}</div>
    <p class="note">الدخل نسبة من عائد المباني والمشاريع الأساسي، ومعامل المدة يطبق على الأعمال الجديدة فقط. يمكن إنجاز كل مستوى دون شراء حقيقي.${d.difficulty ? '' : ''}</p>
    <div class="row"><button class="btn gold" data-a="diff-confirm" data-k="diff-confirm" ${picked ? '' : 'disabled'}>${picked ? `بدء الحي بمستوى ${DIFFICULTIES[picked as keyof typeof DIFFICULTIES].name}` : 'اختر مستوى أولًا'}</button></div>`;
}

// ---------- wheel (art + logic in wheel-art.ts) ----------
export { wheelSpinning, wheelReady, wheelButton, mountWheel, wheelHtml } from './wheel-art';

// ---------- stores (store-ui.ts) ----------
export { coinStoreHtml, gemStoreHtml, exchangeHtml, usd } from './store-ui';

// ---------- roads & paver ----------
export const paverBusy = (st: GameState, id: string, now: number) => st.districts.some(d => d.roadWorks?.some(w => w.machineId === id && w.returnEnd > now));
export function roadWorkPhase(w: NonNullable<DistrictState['roadWorks']>[number], now: number) {
  const p=w.preparation;
  if(p){
    if(now<w.arrival)return {label:'الجرافة في الطريق من مخزن المعدات',end:w.arrival,span:w.arrival-w.start};
    if(now<p.sandEnd)return {label:'الجرافة تجهز الطريق الرملي',end:p.sandEnd,span:p.sandEnd-w.arrival};
    if(now<p.aggregateEnd)return {label:'تسوية طبقة البيسكورس',end:p.aggregateEnd,span:p.aggregateEnd-p.sandEnd};
    if(now<p.asphaltStart)return {label:'ماكينة السفلتة قادمة من المصنع',end:p.asphaltStart,span:p.asphaltStart-p.aggregateEnd};
    if(now<w.workEnd)return {label:'فرد الأسفلت على كامل الشارع',end:w.workEnd,span:w.workEnd-p.asphaltStart};
    return {label:'عودة الآليات إلى مقراتها',end:w.returnEnd,span:w.returnEnd-w.workEnd};
  }
  return now < w.arrival ? { label: 'آلية السفلتة في الطريق من مصنع الأسفلت', end: w.arrival, span: w.arrival - w.start }
    : now < w.workEnd ? { label: 'تفرد الأسفلت على المقطع', end: w.workEnd, span: w.workEnd - w.arrival }
    : { label: 'عائدة إلى مصنع الأسفلت', end: w.returnEnd, span: w.returnEnd - w.workEnd };
}
export function paverSection(st: GameState, hasAsphalt: boolean, now: number) {
  const list = st.pavers ?? [];
  return `<h3>السفلتة (${fmt(list.length)})</h3>
    <p class="note">آلية السفلتة منفصلة عن فريق إزالة الأنقاض، تنطلق من مصنع الأسفلت المكتمل وتعود إليه.</p>
    <div class="paver-card"><img src="${import.meta.env.BASE_URL}art/expansion/paver_se.webp?v=exp-20261022" alt="آلية سفلتة: قادوس أمامي، جنازير، ومظلة، ومسطرة فرد خلفية" width="126" height="105" loading="lazy"><div><b>آلية فرد الأسفلت</b><p>قادوس أمامي وسير ناقل يغذي مسطرة الفرد الخلفية، تتحرك على جنازير وتعود للتخزين داخل المصنع بعد المهمة.</p></div></div>
    <div class="row"><button class="btn gold" data-a="buy-paver" data-k="buy-paver" ${st.coins >= PAVER_COST ? '' : 'disabled'}>شراء آلية سفلتة · <span class="num">${fmt(PAVER_COST)}</span></button></div>
    ${hasAsphalt ? '' : '<p class="note">لا يوجد مصنع أسفلت مكتمل في هذا الحي؛ ابنِه من فئة المصانع لتبدأ السفلتة.</p>'}
    ${list.length ? `<div class="list">${list.map((p, i) => { const b = paverBusy(st, p.id, now); return `<div class="item"><img class="paver-thumb" src="${import.meta.env.BASE_URL}art/expansion/paver_sw.webp?v=exp-20261022" alt="" width="44" height="37"><span class="st" style="background:${b ? 'var(--ochre)' : 'var(--olive)'}"></span><div><h3>آلية سفلتة <span class="num">${fmt(i + 1)}</span></h3><p>${b ? 'في مهمة سفلتة' : 'متاحة'} · سعر الشراء <span class="num">${fmt(p.cost)}</span></p></div><span></span></div>`; }).join('')}</div>` : '<p class="note">لا توجد آليات سفلتة بعد.</p>'}`;
}
export function roadHtml(st: GameState, d: DistrictState, roadId: string, hasAsphalt: boolean, now: number) {
  const r = roadsFor(d.id).find(x => x.id === roadId);
  if (!r) return '<p>المقطع غير موجود.</p><button class="btn" data-a="close-dlg">إغلاق</button>';
  const street=streetFor(d.id,roadId),ids=new Set(street.map(r=>r.id)),pending=street.filter(r=>!d.pavedRoads?.includes(r.id)),cost=ROAD_COST*pending.length;
  const paved = pending.length===0, work = d.roadWorks?.find(w => (w.roadIds??[w.roadId]).some(id=>ids.has(id)));
  const free = (st.pavers ?? []).filter(p => !paverBusy(st, p.id, now));
  const [mx, my] = roadMid(r), total = roadsFor(d.id).length, done = d.pavedRoads?.length ?? 0;
  const head = `<dl class="kv"><dt>الموقع</dt><dd><bdi class="num">${fmt(Math.round(mx))}, ${fmt(Math.round(my))}</bdi> · ${Math.abs(r.a[0] - r.b[0]) < .001 ? 'شارع طولي كامل' : 'شارع عرضي كامل'}</dd><dt>أجزاء هذا الشارع المنجزة</dt><dd class="num">${fmt(street.length-pending.length)}/${fmt(street.length)}</dd><dt>مقاطع الحي المسفلتة</dt><dd class="num">${fmt(done)}/${fmt(total)}</dd>`;
  if (paved&&!work) return `${head}<dt>الحالة</dt><dd>مسفلت بالكامل</dd></dl><p class="note">اكتملت سفلتة الشارع من بدايته إلى نهايته.</p>`;
  if (work) { const ph = roadWorkPhase(work, now);
    return `${head}<dt>الحالة</dt><dd>${ph.label}</dd><dt>متبقٍ</dt><dd class="num">${clock(ph.end - now)}</dd></dl><div class="meter"><i style="transform:scaleX(${Math.min(1, 1 - (ph.end - now) / Math.max(1, ph.span))})"></i></div><p class="note">لا يُخصم الثمن مرة أخرى ولا يمنع العمل وصول الآليات للأراضي.</p>`; }
  const reqs = [
    { ok: st.units.some(u=>u.kind==='bulldozer'&&localUnit(u,d)&&warehousePlots(d).some(p=>p.id===u.home?.warehouseId)&&!roadUnitBusy(st,u.id)&&!st.jobs.some(j=>unitInJob(j,u,now))), t:'جرافة متاحة في مخزن معدات الحي' },
    { ok: hasAsphalt, t: 'مصنع أسفلت مكتمل في الحي' },
    { ok: free.length > 0, t: `آلية سفلتة متاحة (${fmt(free.length)}/${fmt(st.pavers?.length ?? 0)})` },
    { ok: st.coins >= cost, t: `الرصيد يكفي التكلفة ${fmt(cost)}` },
  ];
  return `${head}<dt>التكلفة</dt><dd class="num">${fmt(cost)}</dd><dt>مدة العمل في الموقع</dt><dd><bdi class="num">${clock(actualWorkDuration(d, STREET_WORK_MS))}</bdi> + سفر الآليات</dd><dt>الصعوبة</dt><dd>${difficultyOf(d).name}</dd></dl><p class="note">رمل ثم بيسكورس ثم أسفلت، من بداية الشارع إلى نهايته. ${TEST_TIMERS ? 'للاختبار: 30 ثانية للمراحل مجتمعة، دون زيادة حسب الصعوبة. المدة النهائية محفوظة.' : 'المدة الأساسية 8 دقائق للمراحل مجتمعة وتزيد حسب الصعوبة.'} التكلفة للأجزاء غير المنجزة فقط: ${fmt(ROAD_COST)} لكل جزء.</p>
    ${reqs.map(q => `<div class="check ${q.ok ? 'ok' : ''}"><i></i>${q.t}</div>`).join('')}
    <div class="row">${(st.pavers?.length ?? 0) === 0 ? `<button class="btn ghost" data-a="dlg" data-id="fleet" data-k="road-fleet">شراء آلية سفلتة</button>` : ''}<button class="btn gold" data-a="pave" data-id="${esc(roadId)}" data-k="pave" ${reqs.every(q => q.ok) ? '' : 'disabled'}>سفلتة الشارع كاملًا · <span class="num">${fmt(cost)}</span></button></div>`;
}

// ---------- celebration ----------
/** Visual-only district completion overlay. Credits nothing; the caller owns rewards. */
let celebrationCleanup: (() => void) | null = null;
export function celebrate(name: string, rewardCoins?: number, rewardGems?: number) {
  celebrationCleanup?.(); document.querySelector('.celebration')?.remove();
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const coins = Math.max(0, Math.round(rewardCoins ?? 0)), gems = Math.max(0, Math.round(rewardGems ?? 0));
  const box = document.createElement('div'); box.className = 'celebration' + (reduce ? ' still' : '');
  box.setAttribute('role', 'dialog'); box.setAttribute('aria-modal', 'true'); box.setAttribute('aria-labelledby', 'cel-title');
  const reward = (cls: string, label: string, v: number) => v ? `<div class="cel-reward ${cls}"><i aria-hidden="true"></i><bdi class="num" data-to="${v}">${fmt(reduce ? v : 0)}</bdi><span>${label}</span></div>` : '';
  box.innerHTML = `<canvas aria-hidden="true"></canvas><div class="celebration-card">
    <div class="cel-seal" aria-hidden="true"><svg viewBox="0 0 64 64"><circle cx="32" cy="32" r="28"/><path d="M19 33l9 9 18-20"/></svg></div>
    <h2 id="cel-title">تم إنجاز الحي!</h2><b class="cel-name">${esc(name)}</b>
    ${coins || gems ? `<div class="cel-rewards" aria-live="polite">${reward('coin', 'عملة', coins)}${reward('gem', 'جوهرة', gems)}</div>` : '<span class="cel-sub">الحي جاهز للحياة من جديد</span>'}
    <button class="btn gold cel-close" type="button" data-k="celebration-close">متابعة</button></div>`;
  document.body.appendChild(box);
  const timers: number[] = []; let raf = 0, done = false;
  const close = () => {
    if (done) return; done = true; cancelAnimationFrame(raf); timers.forEach(clearTimeout);
    document.removeEventListener('keydown', key, true); celebrationCleanup = null;
    box.classList.add('out'); timers.length = 0;
    setTimeout(() => box.remove(), reduce ? 0 : 320);
  };
  const key = (e: KeyboardEvent) => { if (e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); close(); } };
  celebrationCleanup = close;
  document.addEventListener('keydown', key, true);
  box.addEventListener('click', e => { if (e.target === box || (e.target as HTMLElement).closest('.cel-close')) close(); });
  (box.querySelector('.cel-close') as HTMLButtonElement).focus({ preventScroll: true });
  timers.push(window.setTimeout(close, reduce ? 9000 : 7000));
  if (reduce) return;
  // count-up of the reward amounts (display only), bounded 1.3 s
  const nums = Array.from(box.querySelectorAll<HTMLElement>('[data-to]'));
  const cv = box.querySelector('canvas')!, ctx = cv.getContext('2d');
  const dpr = Math.min(2, devicePixelRatio || 1), W = innerWidth, H = innerHeight;
  if (ctx) { cv.width = W * dpr; cv.height = H * dpr; ctx.scale(dpr, dpr); }
  const cols = ['#e2b23a', '#f4efe2', '#2f8a57', '#c8343c', '#1d1f22'];
  type P = { x: number; y: number; vx: number; vy: number; r: number; vr: number; life: number; c: string };
  const parts: P[] = [];
  for (let i = 0; i < 90; i++) { const a = -Math.PI / 2 + (Math.random() - .5) * 1.6, s = 5 + Math.random() * 6;
    parts.push({ x: W / 2, y: H * .56, vx: Math.cos(a) * s, vy: Math.sin(a) * s, r: Math.random() * 6, vr: (Math.random() - .5) * .3, life: 1, c: cols[i % cols.length] }); }
  const t0 = performance.now(), ease = (t: number) => 1 - Math.pow(1 - t, 3);
  const tick = (t: number) => {
    if (done || !box.isConnected) return;
    const k = Math.min(1, Math.max(0, (t - t0 - 450) / 1300));
    for (const n of nums) n.textContent = fmt(Math.round(Number(n.dataset.to) * ease(k)));
    if (ctx) { ctx.clearRect(0, 0, W, H);
      for (const p of parts) { if (p.life <= 0) continue; p.life -= .009; p.x += p.vx; p.y += p.vy; p.vy += .16; p.vx *= .985; p.r += p.vr;
        ctx.save(); ctx.globalAlpha = Math.max(0, p.life); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.fillStyle = p.c; ctx.fillRect(-3, -1.5, 6, 3); ctx.restore(); } }
    if (t - t0 < 2400) raf = requestAnimationFrame(tick); else { for (const n of nums) n.textContent = fmt(Number(n.dataset.to)); ctx?.clearRect(0, 0, W, H); }
  };
  raf = requestAnimationFrame(tick);
}
