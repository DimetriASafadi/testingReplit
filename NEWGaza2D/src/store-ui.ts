import './store-ui.css';
import type { GameState } from './model';
import { formatNumber as fmt } from './number-format';

const B = import.meta.env.BASE_URL;
export const usd = (c: number) => `$${(Math.round(c) / 100).toFixed(2)}`;
export const PAY_REASON = 'خيارات الشراء غير متاحة.';
const icon = (kind: 'coins'|'gems') => `<img class="st-ico big" src="${B}art/${kind==='coins'?'wheel/coins.webp':'gem.svg'}" alt="${kind==='coins'?'عملات':'جواهر'}"/>`;
function rewardHtml(st: GameState, kind:'coins'|'gems') {
  return `<div class="st"><div class="st-bal">${icon(kind)}<bdi class="num">${fmt(kind==='coins'?st.coins:st.gems)}</bdi></div>
    <p class="st-lead">${kind==='coins'?'اجمع الدخل وبِع المواد المستعادة لكسب العملات.':'تُكسب الجواهر من المشاريع واستلام الأحياء ودولاب الحظ.'}</p>
    <div class="st-card"><h3>مكافأة مشاهدة إعلان</h3><p>خدمة الإعلانات لم تُفعّل بعد. لن تُخصم عملات أو جواهر.</p>
    <button class="btn gold" disabled aria-disabled="true">مشاهدة إعلان · غير متاح حاليًا</button></div></div>`;
}
export const coinStoreHtml = (st:GameState) => rewardHtml(st,'coins');
export const gemStoreHtml = (st:GameState) => rewardHtml(st,'gems');
/** Legacy navigation cannot expose a hidden purchase confirmation. */
export const exchangeHtml = (st:GameState, _pack:number) => coinStoreHtml(st);
