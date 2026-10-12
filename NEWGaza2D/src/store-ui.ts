import './store-ui.css';
import type { GameState } from './model';
import { COIN_PACKS, GEM_PACKS, commerceProvider } from './rewards';
import { formatNumber as fmt } from './number-format';

/* Coin / gem stores and the gem->coin exchange confirmation.
   Counts are icon-labelled (no word after numbers); prices are USD from cents. */
const B = import.meta.env.BASE_URL;
export const usd = (c: number) => `$${(Math.round(c) / 100).toFixed(2)}`;
export const PAY_REASON = 'الدفع الحقيقي غير متاح حاليًا: لم تُربط بوابة دفع وتحقق خادمي بعد. لن يُخصم أي مبلغ.';
const coin = (cls = '') => `<img class="st-ico coin ${cls}" src="${B}art/wheel/coins.webp" alt="" aria-hidden="true" draggable="false"/>`;
const gem = (cls = '') => `<img class="st-ico gem ${cls}" src="${B}art/wheel/gem.webp" alt="" aria-hidden="true" draggable="false"/>`;
const n = (v: number) => `<bdi class="num" dir="ltr">${fmt(v)}</bdi>`;
const price = (c: number) => `<bdi class="st-price num" dir="ltr">${usd(c)}</bdi>`;
const lock = `<svg class="st-lock" viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 018 0v3" fill="none"/></svg>`;
const payBtn = (c: number, label: string) => `<button class="st-pay" data-a="noop" disabled aria-disabled="true" title="${PAY_REASON}" aria-label="${label} مقابل ${usd(c)} — الدفع غير متاح">${lock}${price(c)}</button>`;
const balances = (st: GameState) => `<div class="st-bal" role="group" aria-label="رصيدك">
  <span class="st-chip coin" aria-label="العملات ${fmt(st.coins)}">${coin()}${n(st.coins)}</span>
  <span class="st-chip gem" aria-label="الجواهر ${fmt(st.gems)}">${gem()}${n(st.gems)}</span></div>`;
const payNote = () => commerceProvider ? '' : `<p class="st-note">${lock}<span>${PAY_REASON}</span></p>`;

export function coinStoreHtml(st: GameState) {
  return `<div class="st st-coins">${balances(st)}
    <p class="st-lead">بدّل جواهرك بعملات فورًا. الشراء بالدولار يُفعّل لاحقًا.</p>
    <div class="list st-list">${COIN_PACKS.map((p, i) => { const ok = st.gems >= p.gems;
      return `<div class="item pack st-card coin t${i}"><div class="st-amt">${coin('big')}<b>${n(p.coins)}</b></div>
      <div class="st-acts"><button class="st-ex ${ok ? '' : 'locked'}" data-a="exch-ask" data-id="${i}" data-k="exch-${i}" ${ok ? '' : 'disabled'} aria-label="تحويل ${fmt(p.gems)} جواهر إلى ${fmt(p.coins)} عملة${ok ? '' : '، الجواهر غير كافية'}">${ok ? '' : lock}${gem()}${n(p.gems)}</button>
      ${payBtn(p.cents, `${fmt(p.coins)} عملة`)}</div></div>`; }).join('')}</div>
    ${payNote()}</div>`;
}
export function gemStoreHtml(st: GameState) {
  return `<div class="st st-gems">${balances(st)}
    <p class="st-lead">تُكسب الجواهر من المشاريع واستلام الأحياء ودولاب الحظ.</p>
    <div class="list st-list">${GEM_PACKS.map((p, i) => `<div class="item pack st-card gem t${i}"><div class="st-amt">${gem('big')}<b>${n(p.gems)}</b></div>
      <div class="st-acts">${payBtn(p.cents, `${fmt(p.gems)} جوهرة`)}</div></div>`).join('')}</div>
    ${payNote()}</div>`;
}
export function exchangeHtml(st: GameState, pack: number) {
  const p = COIN_PACKS[pack]; if (!p) return '<p>الباقة غير موجودة.</p>';
  const ok = st.gems >= p.gems;
  return `<div class="st st-xc">
    <div class="st-swap" aria-label="تحويل ${fmt(p.gems)} جواهر إلى ${fmt(p.coins)} عملة">
      <div class="st-side gem">${gem('big')}<b>${n(p.gems)}</b></div>
      <svg class="st-arrow" viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5M11 6l-6 6 6 6"/></svg>
      <div class="st-side coin">${coin('big')}<b>${n(p.coins)}</b></div></div>
    <table class="st-cmp"><thead><tr><th></th><th>الآن</th><th>بعد التحويل</th></tr></thead><tbody>
      <tr><th scope="row" aria-label="الجواهر">${gem()}</th><td>${n(st.gems)}</td><td class="down">${n(Math.max(0, st.gems - p.gems))}</td></tr>
      <tr><th scope="row" aria-label="العملات">${coin()}</th><td>${n(st.coins)}</td><td class="up">${n(st.coins + (ok ? p.coins : 0))}</td></tr></tbody></table>
    ${ok ? '<p class="st-note">تُنفّذ العملية مرة واحدة حتى عند النقر المتكرر.</p>' : `<p class="st-note bad" role="alert">${lock}<span>رصيد الجواهر غير كافٍ.</span></p>`}
    <div class="row"><button class="btn ghost" data-a="dlg" data-id="coins" data-k="exch-cancel">رجوع</button><button class="btn gold" data-a="exch-confirm" data-k="exch-confirm" ${ok ? '' : 'disabled'}>تأكيد التحويل</button></div></div>`;
}
