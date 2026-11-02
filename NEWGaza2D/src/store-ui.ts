import './store-ui.css';
import type { GameState } from './model';
import { formatNumber as fmt } from './number-format';
import { ADS, adRewardLabel } from './ads/config';
import { adUnavailable, adBatchRemaining } from './ads/ledger';
import { COIN_PACKS } from './rewards';
import { COIN_ICON_URL, GEM_ICON_URL } from './gem-icon';

export const usd = (c: number) => `$${(Math.round(c) / 100).toFixed(2)}`;
export const PAY_REASON = 'خيارات الشراء غير متاحة.';
const icon = (kind: 'coins'|'gems') => `<img class="st-ico big" src="${kind==='coins'?COIN_ICON_URL():GEM_ICON_URL()}" alt="${kind==='coins'?'عملات':'جواهر'}"/>`;
function rewardHtml(st: GameState, kind:'coins'|'gems') {
  const reason=adUnavailable(st,kind,Math.max(Date.now(),st.lastSeen));
  return `<div class="st"><div class="st-bal">${icon(kind)}<bdi class="num">${fmt(kind==='coins'?st.coins:st.gems)}</bdi></div>
    <p class="st-lead">${kind==='coins'?'اجمع الدخل وبِع المواد المستعادة لكسب العملات.':'تُكسب الجواهر من المشاريع واستلام الأحياء ودولاب الحظ.'}</p>
    <div class="st-card"><h3>مكافأة مشاهدة إعلان</h3><p>شاهد إعلانًا اختياريًا مقابل <b>${adRewardLabel(kind)}</b>. لن تُخصم عملات أو جواهر.</p>
    <button class="btn gold" data-a="reward-ad" data-id="${kind}" ${reason?'disabled':''}>مشاهدة إعلان · ${adRewardLabel(kind)}</button>
    <p class="ad-status" role="status">المتبقي في الحصة: <bdi class="num">${adBatchRemaining(st,Math.max(Date.now(),st.lastSeen))}/5</bdi> · ${reason||'تُضاف المكافأة عند استحقاقها فقط. الإلغاء لا يمنح مكافأة.'}</p>
    ${ADS.testMode?'<p class="note">إعلانات اختبار Google · لا تحقق إيرادات. توفر الإعلان يعتمد على الشبكة والجهاز.</p>':''}
    <button class="btn ghost ad-privacy" data-a="ad-privacy">خيارات خصوصية الإعلانات</button></div></div>`;
}
export const coinStoreHtml = (st:GameState) => `<div class="st"><h3>تحويل الجواهر إلى عملات</h3><p class="note">اختر باقة لتأكيد التحويل. لا يوجد دفع بمال حقيقي.</p><div class="list">${COIN_PACKS.map((p,i)=>`<div class="item"><div>${icon('coins')} <bdi class="num">${fmt(p.coins)}</bdi></div><button class="btn" data-a="exch-ask" data-id="${i}" data-k="exchange-${i}" ${st.gems<p.gems?'disabled':''}>${icon('gems')} <bdi class="num">${fmt(p.gems)}</bdi> · تحويل</button></div>`).join('')}</div></div>${rewardHtml(st,'coins')}`;
export const gemStoreHtml = (st:GameState) => rewardHtml(st,'gems');
export function exchangeHtml(st:GameState, pack:number) {
  const p=COIN_PACKS[pack];
  if(!p)return '<p>الباقة غير موجودة.</p>';
  return `<div class="st"><p class="st-lead">تأكيد تحويل الجواهر إلى عملات</p><div class="st-bal">${icon('gems')}<bdi class="num">${fmt(p.gems)}</bdi><span aria-hidden="true">←</span>${icon('coins')}<bdi class="num">${fmt(p.coins)}</bdi></div><p class="note">رصيد الجواهر: <bdi class="num">${fmt(st.gems)}</bdi>. التحويل نهائي، دون أي دفع نقدي.</p><button class="btn gold" data-a="exch-confirm" data-k="exch-confirm" ${st.gems<p.gems?'disabled':''}>تأكيد التحويل</button><button class="btn ghost" data-a="dlg" data-id="coins">رجوع دون تحويل</button></div>`;
}
