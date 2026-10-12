import './wheel-art.css';
import type { GameState } from './model';
import { WHEEL_PRIZES, WHEEL_SPIN_MS, rewardedAdProvider } from './rewards';
import { formatNumber as fmt } from './number-format';

/* Illustrated, fully vector "Luck Wheel": gold bulb ring, jewel pointer, star hub,
   N dimensional wedges with painted icons. The disc is a persistent node rotated by
   time derived from the persisted spin, so DOM patches never restart or replay it. */

type Prize = { coins: number; gems: number; iron?: number; wood?: number; weight: number };
const P = (i: number) => WHEEL_PRIZES[i] as unknown as Prize;
const SEG = WHEEL_PRIZES.length, STEP = 360 / SEG;
const ART = () => `${import.meta.env.BASE_URL}art/wheel/`;
const AD_REASON = 'مشاهدة إعلان غير متاحة حاليًا: لم يُربط مزود إعلانات يتحقق من المشاهدة الكاملة.';
const COLORS: [string, string][] = [
  ['#2f6fe0', '#173c9c'], ['#3dbb4c', '#1d7a2c'], ['#8a3fe0', '#4f1d9a'], ['#f7b21c', '#c77606'], ['#e8343a', '#9c1220'],
  ['#14b3b0', '#0a6f74'], ['#ec3d9c', '#a3166a'], ['#f47c1c', '#b8480a'], ['#5f7d98', '#33495e'], ['#6a4fd8', '#3a2690'],
];
function clock(ms: number) {
  const s = Math.max(0, Math.ceil(ms / 1000)), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), x = s % 60;
  const p = (n: number) => String(n).padStart(2, '0');
  return h ? `${h}:${p(m)}:${p(x)}` : `${p(m)}:${p(x)}`;
}
function meta(p: Prize) {
  if (p.coins) return { n: p.coins, u: 'عملة', icon: 'coins', name: `${fmt(p.coins)} عملة` };
  if (p.gems) return { n: p.gems, u: p.gems > 2 ? 'جواهر' : 'جوهرة', icon: p.gems > 2 ? 'gems' : 'gem', name: `${fmt(p.gems)} ${p.gems > 2 ? 'جواهر' : 'جوهرة'}` };
  if (p.iron) return { n: p.iron, u: 'وحدة حديد', icon: 'iron', name: `${fmt(p.iron)} حديد` };
  if (p.wood) return { n: p.wood, u: 'وحدة خشب', icon: 'wood', name: `${fmt(p.wood)} خشب` };
  return { n: 0, u: 'حظ أوفر', icon: 'none', name: 'لا جائزة' };
}
const rare = (p: Prize) => p.weight <= 2 && (p.coins >= 1000 || p.gems >= 5);

export const wheelSpinning = (st: GameState, now: number) => !!st.wheel?.startedAt && now < st.wheel.startedAt + WHEEL_SPIN_MS;
export const wheelReady = (st: GameState, now: number) => !wheelSpinning(st, now) && now >= (st.wheel?.nextAt ?? 0);

export function wheelButton(st: GameState, now: number) {
  const ready = wheelReady(st, now), spin = wheelSpinning(st, now);
  const cone = COLORS.slice(0, SEG).map((c, i) => `${c[0]} ${(i * STEP - STEP / 2).toFixed(1)}deg ${(i * STEP + STEP / 2).toFixed(1)}deg`).join(',');
  return `<button class="wheel-fab ${ready ? 'ready' : ''}" data-a="dlg" data-id="wheel" data-k="wheel-fab" aria-label="دولاب الحظ${ready ? '، دوران مجاني متاح' : ''}">
    <i class="wf-wheel" aria-hidden="true"><i class="wf-disc" style="background:conic-gradient(from ${-STEP / 2}deg,${cone})"></i><i class="wf-hub"></i><i class="wf-pin"></i></i>
    <span>${spin ? 'يدور' : ready ? 'مجاني' : `<bdi class="num">${clock((st.wheel?.nextAt ?? 0) - now)}</bdi>`}</span></button>`;
}

const pt = (r: number, deg: number) => { const a = (deg - 90) * Math.PI / 180; return `${(r * Math.cos(a)).toFixed(2)} ${(r * Math.sin(a)).toFixed(2)}`; };
function discSvg() {
  const r = 100, base = ART();
  const defs = COLORS.slice(0, SEG).map((c, i) => `<radialGradient id="wg${i}" cx="0" cy="0" r="100" gradientUnits="userSpaceOnUse"><stop offset=".25" stop-color="${c[1]}"/><stop offset=".62" stop-color="${c[0]}"/><stop offset="1" stop-color="${c[1]}"/></radialGradient>`).join('');
  const segs = Array.from({ length: SEG }, (_, i) => {
    const p = P(i), m = meta(p), a0 = i * STEP - STEP / 2, a1 = i * STEP + STEP / 2;
    const d = `M0 0L${pt(r, a0)}A${r} ${r} 0 0 1 ${pt(r, a1)}Z`;
    const badge = rare(p) ? `<g class="rb"><rect x="-11" y="-47" width="22" height="6.4" rx="3.2"/><text y="-42.4" text-anchor="middle">نسبة قليلة</text></g>` : '';
    return `<path d="${d}" fill="url(#wg${i})"/><path d="M${pt(r - 3, a0 + 1)}A${r - 3} ${r - 3} 0 0 1 ${pt(r - 3, a1 - 1)}" class="gl"/>
      <g transform="rotate(${i * STEP})"><image href="${base}${m.icon}.webp" x="-13" y="-92" width="26" height="26" preserveAspectRatio="xMidYMid meet"/>
      <text y="-57" text-anchor="middle" class="wl">${fmt(m.n)}</text><text y="-50.5" text-anchor="middle" class="wu">${m.u}</text>${badge}</g>`;
  }).join('');
  const lines = Array.from({ length: SEG }, (_, i) => `<line x1="0" y1="0" x2="${pt(r, i * STEP + STEP / 2).replace(' ', '" y2="')}"/>`).join('');
  return `<svg viewBox="-101 -101 202 202" aria-hidden="true"><defs>${defs}</defs>${segs}<g class="sep">${lines}</g><circle r="100" class="inner-shade"/></svg>`;
}
function ringSvg() {
  const bulbs = Array.from({ length: 24 }, (_, i) => { const [x, y] = pt(111, i * 15).split(' '); return `<circle cx="${x}" cy="${y}" r="3.6" class="bulb ${i % 2 ? 'b' : 'a'}"/>`; }).join('');
  return `<svg viewBox="-122 -122 244 244" aria-hidden="true"><defs>
    <linearGradient id="wr-gold" x1="0" y1="-1" x2="0" y2="1"><stop offset="0" stop-color="#fff2a8"/><stop offset=".35" stop-color="#f5c033"/><stop offset=".7" stop-color="#b9700e"/><stop offset="1" stop-color="#ffd85a"/></linearGradient>
    <radialGradient id="wr-bulb"><stop offset="0" stop-color="#fffbe6"/><stop offset=".55" stop-color="#ffe27a"/><stop offset="1" stop-color="#e59a12"/></radialGradient></defs>
    <circle r="118" fill="#4a2a08" opacity=".45" cy="4"/><circle r="117" fill="url(#wr-gold)"/><circle r="105" fill="#6b3d0b"/><circle r="103" fill="#2a1604"/>${bulbs}</svg>`;
}
function hubSvg() {
  return `<svg viewBox="-30 -30 60 60" aria-hidden="true"><defs><radialGradient id="wh-b" cx="-.2" cy="-.3" r="1.1"><stop offset="0" stop-color="#d98a3a"/><stop offset=".7" stop-color="#7a3c10"/><stop offset="1" stop-color="#4a2006"/></radialGradient>
    <linearGradient id="wh-s" x1="0" y1="-1" x2="0" y2="1"><stop offset="0" stop-color="#fff6a0"/><stop offset=".6" stop-color="#f7bd22"/><stop offset="1" stop-color="#d98706"/></linearGradient></defs>
    <circle r="29" fill="url(#wr-gold)"/><circle r="24" fill="url(#wh-b)"/>
    <path d="M0-15L4.4-5.3 15-4.6 7-1.8 9.4 12 0 6 -9.4 12-7-1.8-15-4.6-4.4-5.3Z" transform="translate(0 1)" fill="url(#wh-s)" stroke="#9a5a06" stroke-width="1" stroke-linejoin="round"/>
    <path d="M-17 6q3 9 11 12M17 6q-3 9-11 12" fill="none" stroke="#f5c033" stroke-width="1.6" stroke-linecap="round"/></svg>`;
}
function pointerSvg() {
  return `<svg viewBox="0 0 40 52" aria-hidden="true"><defs><linearGradient id="wp-r" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff6a5a"/><stop offset="1" stop-color="#a8101c"/></linearGradient></defs>
    <path d="M20 50L5 24A16 16 0 1 1 35 24Z" fill="url(#wr-gold)" stroke="#8a5208" stroke-width="1.2"/><path d="M20 44L9 23A12 12 0 1 1 31 23Z" fill="url(#wp-r)"/>
    <circle cx="20" cy="17" r="6.5" fill="#ffe27a"/><circle cx="18" cy="15" r="2.4" fill="#fffbe6"/></svg>`;
}

let disc: HTMLDivElement | null = null, raf = 0;
const ease = (t: number) => 1 - Math.pow(1 - t, 4);
/** Lands the centre of wedge `result` exactly under the pointer at the top. */
function discAngle(st: GameState, now: number) {
  const w = st.wheel; if (!w?.startedAt) return 0;
  const final = 360 * 7 - w.result * STEP;
  const t = Math.min(1, Math.max(0, (now - w.startedAt) / WHEEL_SPIN_MS));
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  return reduce ? (t >= 1 ? final : final - 90 * (1 - t)) : final * ease(t);
}
export function mountWheel(root: ParentNode, getState: () => GameState) {
  const host = root.querySelector<HTMLElement>('[data-wheel-host]');
  if (!host) { cancelAnimationFrame(raf); raf = 0; return; }
  if (!disc) { disc = document.createElement('div'); disc.className = 'wheel-disc'; disc.innerHTML = discSvg(); }
  if (disc.parentElement !== host) host.appendChild(disc);
  const frame = () => {
    const st = getState(), now = Date.now();
    disc!.style.transform = `rotate(${discAngle(st, now)}deg)`;
    raf = wheelSpinning(st, now) && disc!.isConnected ? requestAnimationFrame(frame) : 0;
  };
  if (!raf) frame();
}

export function wheelHtml(st: GameState, now: number) {
  const w = st.wheel, spin = wheelSpinning(st, now), ready = wheelReady(st, now);
  const shown = w?.startedAt && !spin ? P(w.result) : null;
  const sm = shown ? meta(shown) : null;
  const odds = Array.from({ length: SEG }, (_, i) => { const p = P(i); return `<li><i style="background:${COLORS[i][0]}"></i><span>${meta(p).name}</span><bdi class="num">${fmt(p.weight)}%</bdi></li>`; }).join('');
  return `<div class="wz${spin ? ' spinning' : ''}${ready ? ' is-ready' : ''}" style="background-image:linear-gradient(rgba(10,24,64,.35),rgba(10,24,64,.1) 45%,rgba(30,20,8,.45)),url('${ART()}bg.webp')">
    <header class="wz-head"><div class="wz-plank"><b>دولاب الحظ</b></div><p class="wz-ribbon">حظك اليوم… فرصة رائعة!</p>
      <button class="wz-close" data-a="close-dlg" data-k="wz-close" aria-label="إغلاق دولاب الحظ"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7l10 10M17 7L7 17"/></svg></button></header>
    <div class="wz-main"><div class="wheel-stage">
      <div class="wz-ring">${ringSvg()}</div><div class="wheel-host" data-wheel-host></div>
      <div class="wz-hub">${hubSvg()}</div><div class="wheel-pointer">${pointerSvg()}</div></div>
    <div class="wz-side">
      <p class="wheel-status" role="status">${spin ? 'الدولاب يدور… الجائزة محددة ومحفوظة مسبقًا.' : sm && shown ? `آخر نتيجة: <b>${sm.name}</b>${sm.n ? ' · أُضيفت إلى رصيدك' : ''}` : 'دوران مجاني كل ساعتين.'}</p>
      <p class="wheel-timer">${ready ? 'الدوران المجاني متاح الآن' : spin ? '&nbsp;' : `الدوران التالي بعد <bdi class="num">${clock((w?.nextAt ?? 0) - now)}</bdi>`}</p>
      <button class="wz-spin" data-a="spin" data-k="spin" ${ready ? '' : 'disabled'}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 12a8 8 0 01-14 5M4 12a8 8 0 0114-5M18 3v4h-4M6 21v-4h4"/></svg><span>${spin ? 'يدور…' : ready ? 'أدر الآن' : 'انتظر العداد'}</span></button>
      <details class="odds"><summary>الاحتمالات</summary><ol>${odds}</ol></details>
      ${rewardedAdProvider ? '' : `<p class="note unavailable">${AD_REASON}</p>`}
    </div></div></div>`;
}
