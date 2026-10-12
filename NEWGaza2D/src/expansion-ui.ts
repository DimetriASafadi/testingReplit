import './expansion-ui.css';
import type { DistrictState, GameState } from './model';
import { DIFFICULTIES, PAVER_COST, ROAD_COST, difficultyOf, workDuration } from './economy';
import { COIN_PACKS, GEM_PACKS, commerceProvider } from './rewards';
import { roadsFor, roadMid, streetFor } from './roads';
import { formatNumber as fmt } from './number-format';

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
const PAY_REASON = 'الدفع الحقيقي غير متاح حاليًا: لم تُربط بوابة دفع وتحقق خادمي بعد. لن يُخصم أي مبلغ.';

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

// ---------- stores ----------
export function coinStoreHtml(st: GameState) {
  return `<p>حوّل جواهرك إلى عملات فورًا، أو اشترِ لاحقًا عند تفعيل الدفع.</p>
    <div class="list">${COIN_PACKS.map((p, i) => `<div class="item pack"><span class="pack-amt"><bdi class="num">${fmt(p.coins)}</bdi><small>عملة</small></span>
      <div class="pack-buy"><button class="btn gold sm" data-a="exch-ask" data-id="${i}" data-k="exch-${i}" ${st.gems >= p.gems ? '' : 'disabled'}>${st.gems >= p.gems ? '' : 'غير كافٍ · '}<bdi class="num">${fmt(p.gems)}</bdi> جواهر</button>
      <button class="btn ghost sm" data-a="noop" disabled title="${PAY_REASON}">${cents(p.cents)} · غير متاح حاليًا</button></div></div>`).join('')}</div>
    <p class="note">رصيد الجواهر: <bdi class="num">${fmt(st.gems)}</bdi></p>${commerceProvider ? '' : `<p class="note unavailable">${PAY_REASON}</p>`}`;
}
export function gemStoreHtml(st: GameState) {
  return `<p>الجواهر تُكسب من إنجاز المشاريع واستلام الأحياء ودولاب الحظ.</p>
    <div class="list">${GEM_PACKS.map(p => `<div class="item pack"><span class="pack-amt gem"><bdi class="num">${fmt(p.gems)}</bdi><small>جوهرة</small></span>
      <div class="pack-buy"><button class="btn ghost sm" data-a="noop" disabled title="${PAY_REASON}">${cents(p.cents)} · غير متاح حاليًا</button></div></div>`).join('')}</div>
    <p class="note">رصيدك: <bdi class="num">${fmt(st.gems)}</bdi> جوهرة</p>${commerceProvider ? '' : `<p class="note unavailable">${PAY_REASON}</p>`}`;
}
export function exchangeHtml(st: GameState, pack: number) {
  const p = COIN_PACKS[pack]; if (!p) return '<p>الباقة غير موجودة.</p>';
  const ok = st.gems >= p.gems;
  return `<p>تحويل <b><bdi class="num">${fmt(p.gems)}</bdi> جواهر</b> إلى <b><bdi class="num">${fmt(p.coins)}</bdi> عملة</b>؟</p>
    <dl class="kv"><dt>الجواهر بعد التحويل</dt><dd class="num">${fmt(Math.max(0, st.gems - p.gems))}</dd><dt>العملات بعد التحويل</dt><dd class="num">${fmt(st.coins + (ok ? p.coins : 0))}</dd></dl>
    ${ok ? '<p class="note">تُنفّذ العملية مرة واحدة حتى عند النقر المتكرر.</p>' : '<p class="note" role="alert">رصيد الجواهر غير كافٍ.</p>'}
    <div class="row"><button class="btn ghost" data-a="dlg" data-id="coins" data-k="exch-cancel">رجوع</button><button class="btn gold" data-a="exch-confirm" data-k="exch-confirm" ${ok ? '' : 'disabled'}>تأكيد التحويل</button></div>`;
}

// ---------- roads & paver ----------
export const paverBusy = (st: GameState, id: string, now: number) => st.districts.some(d => d.roadWorks?.some(w => w.machineId === id && w.returnEnd > now));
export function roadWorkPhase(w: NonNullable<DistrictState['roadWorks']>[number], now: number) {
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
    { ok: hasAsphalt, t: 'مصنع أسفلت مكتمل في الحي' },
    { ok: free.length > 0, t: `آلية سفلتة متاحة (${fmt(free.length)}/${fmt(st.pavers?.length ?? 0)})` },
    { ok: st.coins >= cost, t: `الرصيد يكفي التكلفة ${fmt(cost)}` },
  ];
  return `${head}<dt>التكلفة</dt><dd class="num">${fmt(cost)}</dd><dt>مدة العمل في الموقع</dt><dd><bdi class="num">${clock(workDuration(d, 60_000*pending.length))}</bdi> + الذهاب والعودة</dd><dt>الصعوبة</dt><dd>${difficultyOf(d).name}</dd></dl><p class="note">سفلتة الشارع كاملًا من البداية للنهاية. التكلفة للأجزاء غير المنجزة فقط: ${fmt(ROAD_COST)} لكل جزء.</p>
    ${reqs.map(q => `<div class="check ${q.ok ? 'ok' : ''}"><i></i>${q.t}</div>`).join('')}
    <div class="row">${(st.pavers?.length ?? 0) === 0 ? `<button class="btn ghost" data-a="dlg" data-id="fleet" data-k="road-fleet">شراء آلية سفلتة</button>` : ''}<button class="btn gold" data-a="pave" data-id="${esc(roadId)}" data-k="pave" ${reqs.every(q => q.ok) ? '' : 'disabled'}>سفلتة الشارع كاملًا · <span class="num">${fmt(cost)}</span></button></div>`;
}

// ---------- celebration ----------
export function celebrate(name: string) {
  document.querySelector('.celebration')?.remove();
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const box = document.createElement('div'); box.className = 'celebration' + (reduce ? ' still' : '');
  box.setAttribute('role', 'status');
  box.innerHTML = `<canvas aria-hidden="true"></canvas><div class="celebration-card"><small>اكتمل الإعمار</small><b>${esc(name)}</b><span>الحي جاهز للاستلام</span></div>`;
  document.body.appendChild(box);
  const life = reduce ? 3200 : 4600;
  setTimeout(() => box.classList.add('out'), life - 500); setTimeout(() => box.remove(), life);
  box.onclick = () => box.remove();
  if (reduce) return;
  const cv = box.querySelector('canvas')!, ctx = cv.getContext('2d'); if (!ctx) return;
  const dpr = Math.min(2, devicePixelRatio || 1);
  cv.width = innerWidth * dpr; cv.height = innerHeight * dpr; ctx.scale(dpr, dpr);
  const cols = ['#f2b632', '#f4f7fc', '#4ea0e0', '#e86a5a', '#8fd18a'];
  type P = { x: number; y: number; vx: number; vy: number; life: number; c: string };
  let parts: P[] = []; const t0 = performance.now(); let bursts = 0;
  const burst = () => { const x = innerWidth * (.2 + Math.random() * .6), y = innerHeight * (.15 + Math.random() * .3), c = cols[bursts++ % cols.length];
    for (let i = 0; i < 46; i++) { const a = i / 46 * Math.PI * 2, s = 1.6 + Math.random() * 2.4; parts.push({ x, y, vx: Math.cos(a) * s, vy: Math.sin(a) * s, life: 1, c }); } };
  const tick = (t: number) => {
    if (!box.isConnected) return;
    if (bursts < 7 && t - t0 > bursts * 480) burst();
    ctx.clearRect(0, 0, innerWidth, innerHeight);
    parts = parts.filter(p => (p.life -= .014) > 0);
    for (const p of parts) { p.x += p.vx; p.y += p.vy; p.vy += .035; p.vx *= .985; ctx.globalAlpha = p.life; ctx.fillStyle = p.c; ctx.fillRect(p.x, p.y, 3, 3); }
    if (t - t0 < life) requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}
