import './expansion-ui.css';
import type { DistrictState, GameState } from './model';
import { DIFFICULTIES, PAVER_COST, ROAD_COST, difficultyOf, workDuration } from './economy';
import { roadsFor, roadMid, streetFor } from './roads';
import { formatNumber as fmt } from './number-format';
import { actualWorkDuration, STREET_WORK_MS } from './construction-rules';
import { localUnit, roadUnitBusy, warehousePlots } from './warehouse-capacity';
import { unitInJob } from './fleet-depots';
import { TEST_TIMERS } from './timing-settings';
import { gameAudio } from './game-audio';
import { GEM_ICON_URL, HUD_COIN_ICON_URL } from './gem-icon';

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
/** Visual-only district completion overlay. Credits nothing; the caller owns rewards.
 *  Shows the actual reward values (defaults: 100000 coins, 10 blue gems). */
let celebrationCleanup: (() => void) | null = null;
/** Close and fully tear down any open celebration (timers, rAF, listeners, sound). Safe to call anytime. */
export function closeCelebration() { celebrationCleanup?.(); document.querySelector('.celebration')?.remove(); }
const STAR = '<svg viewBox="0 0 24 24"><path d="M12 1.8l3.1 6.5 7.1.9-5.2 4.9 1.3 7-6.3-3.4-6.3 3.4 1.3-7L1.8 9.2l7.1-.9z"/></svg>';
/** Static celebration card markup (no side effects). Used by celebrate() and usable for screenshot fixtures. */
export function celebrationMarkup(name: string, rewardCoins = 100000, rewardGems = 10, reduced = false) {
  const coins = Math.max(0, Math.round(rewardCoins)), gems = Math.max(0, Math.round(rewardGems));
  const B = import.meta.env.BASE_URL;
  const medal = (cls: string, img: string, label: string, v: number) => v ? `<div class="cel-reward ${cls}" data-testid="reward-${cls}">
      <span class="cel-medallion"><img src="${img}" alt="" width="160" height="160" decoding="async" draggable="false"></span>
      <bdi class="num" data-to="${v}">${fmt(reduced ? v : 0)}</bdi><span class="cel-lbl">${label}</span></div>` : '';
  return `<canvas aria-hidden="true"></canvas>
    <div class="cel-card" style="--cel-scene:url('${B}art/celebration/scene.webp')">
      <button class="cel-x" type="button" aria-label="إغلاق" data-k="celebration-x" data-testid="button-celebration-close-x"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7l10 10M17 7L7 17"/></svg></button>
      <div class="cel-hero">
        <div class="cel-stars" aria-hidden="true"><i>${STAR}</i><i class="big">${STAR}</i><i>${STAR}</i></div>
        <h2 id="cel-title" class="cel-title">تم إكمال الحي بنجاح!</h2>
        <div class="cel-medal" aria-label="نسبة الإنجاز مئة بالمئة"><bdi class="num">100%</bdi></div>
      </div>
      <div class="cel-body">
        <div class="cel-msg"><b class="cel-hey">مبروك لك!</b>
          <p>لقد أعدت بناء <bdi class="cel-name">${esc(name)}</bdi> من جديد، وساهمت في نهضة مدينة غزة.</p></div>
        ${coins || gems ? `<div class="cel-prize" aria-live="polite"><b class="cel-tag">مكافأتك</b><div class="cel-rewards">${medal('gem', GEM_ICON_URL(), gems > 2 ? 'جواهر' : 'جوهرة', gems)}${medal('coin', HUD_COIN_ICON_URL(), 'عملة', coins)}</div></div>` : ''}
      </div>
      <div class="cel-foot">
        <button class="cel-ok" type="button" data-k="celebration-close" data-testid="button-celebration-ok"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4.5 12.5l5 5 10-11"/></svg><span>رائع!</span></button>
        <span class="cel-scroll">معًا.. نعيد بناء غزة أجمل</span></div>
    </div>`;
}
export function celebrate(name: string, rewardCoins = 100000, rewardGems = 10) {
  closeCelebration();
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const box = document.createElement('dialog'); box.className = 'celebration' + (reduce ? ' still' : '');
  box.setAttribute('aria-labelledby', 'cel-title'); box.dataset.testid = 'dialog-celebration';
  box.innerHTML = celebrationMarkup(name, rewardCoins, rewardGems, reduce);
  document.body.appendChild(box);
  // Native modal dialog = browser top layer, above HUD, other dialogs and any z-index.
  try { box.showModal(); } catch { box.setAttribute('open', ''); box.classList.add('fallback'); }
  const timers: number[] = []; let raf = 0, done = false;
  const nums = Array.from(box.querySelectorAll<HTMLElement>('[data-to]'));
  const cv = box.querySelector('canvas')!;
  const stopFx = () => { cancelAnimationFrame(raf); raf = 0; timers.forEach(clearTimeout); timers.length = 0;
    gameAudio.stopFireworks();
    cv.getContext('2d')?.clearRect(0, 0, cv.width, cv.height);
    for (const n of nums) n.textContent = fmt(Number(n.dataset.to)); };
  const vis = () => { if (document.hidden) stopFx(); };
  const close = () => {
    if (done) return; done = true; stopFx();
    document.removeEventListener('keydown', key, true); document.removeEventListener('visibilitychange', vis);
    celebrationCleanup = null; box.classList.add('out');
    setTimeout(() => { try { box.close(); } catch { /* noop */ } box.remove(); }, reduce ? 0 : 320);
  };
  const key = (e: KeyboardEvent) => { if (e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); close(); } };
  celebrationCleanup = close;
  box.addEventListener('cancel', e => { e.preventDefault(); close(); });
  document.addEventListener('keydown', key, true); document.addEventListener('visibilitychange', vis);
  box.addEventListener('click', e => { if (e.target === box || (e.target as HTMLElement).closest('.cel-ok,.cel-x')) { gameAudio.click(true); close(); } });
  (box.querySelector('.cel-ok') as HTMLButtonElement).focus({ preventScroll: true });
  if (reduce) { gameAudio.firework(0); return; }
  // Finite fireworks + confetti: bounded pool, five bursts over ~3 s, then idle forever.
  const ctx = cv.getContext('2d');
  const dpr = Math.min(2, devicePixelRatio || 1), W = innerWidth, H = innerHeight;
  if (ctx) { cv.width = W * dpr; cv.height = H * dpr; ctx.scale(dpr, dpr); }
  const cols = ['#ffd23f', '#ff3b4a', '#3d8bff', '#fff3c2', '#ff9f1c', '#5ec8ff'];
  type P = { x: number; y: number; vx: number; vy: number; life: number; c: string; s: number; conf: boolean; r: number };
  const parts: P[] = [], MAX = 460;
  const burst = (k: number) => {
    const x = W * (.18 + ((k * .37) % .64)), y = H * (.1 + ((k * .19) % .22)), n = W < 520 ? 44 : 64, c0 = cols[k % cols.length], c1 = cols[(k + 2) % cols.length];
    for (let i = 0; i < n && parts.length < MAX; i++) { const a = (i / n) * Math.PI * 2, s = 1.6 + Math.random() * 3;
      parts.push({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: 1, c: i % 3 ? c0 : c1, s: 1.6 + Math.random() * 1.8, conf: false, r: 0 }); }
    for (let i = 0; i < 14 && parts.length < MAX; i++) parts.push({ x: Math.random() * W, y: -10, vx: (Math.random() - .5) * 1.4, vy: 1 + Math.random() * 1.6, life: 1.6, c: cols[i % cols.length], s: 5 + Math.random() * 3, conf: true, r: Math.random() * 6 });
    gameAudio.firework((x / W - .5) * 1.4);
  };
  [0, 600, 1200, 1900, 2600].forEach((ms, k) => timers.push(window.setTimeout(() => { if (!done && !document.hidden) { burst(k); if (!raf) raf = requestAnimationFrame(tick); } }, ms + 250)));
  const t0 = performance.now(), ease = (t: number) => 1 - Math.pow(1 - t, 3);
  const tick = (t: number) => {
    raf = 0; if (done || !box.isConnected) return;
    const k = Math.min(1, Math.max(0, (t - t0 - 600) / 1300));
    for (const n of nums) n.textContent = fmt(Math.round(Number(n.dataset.to) * ease(k)));
    let alive = 0;
    if (ctx) { ctx.clearRect(0, 0, W, H);
      for (let i = parts.length - 1; i >= 0; i--) { const p = parts[i]; p.life -= p.conf ? .008 : .015; if (p.life <= 0 || p.y > H + 20) { parts.splice(i, 1); continue; }
        alive++; p.x += p.vx; p.y += p.vy;
        if (p.conf) { p.r += .12; ctx.globalAlpha = Math.min(1, p.life); ctx.fillStyle = p.c; ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.fillRect(-p.s / 2, -p.s / 4, p.s, p.s / 2 * Math.abs(Math.cos(p.r))+1); ctx.restore(); }
        else { p.vy += .045; p.vx *= .975; p.vy *= .975; ctx.globalAlpha = Math.min(1, p.life * 1.4); ctx.fillStyle = p.c; ctx.fillRect(p.x - p.s / 2, p.y - p.s / 2, p.s, p.s); } }
      ctx.globalAlpha = 1; }
    if (alive || k < 1 || (timers.length && t - t0 < 3400)) raf = requestAnimationFrame(tick);
  };
  raf = requestAnimationFrame(tick);
}
