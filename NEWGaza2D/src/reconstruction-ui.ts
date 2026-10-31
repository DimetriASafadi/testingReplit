import type { DistrictState, GameState, EquipmentKind } from './model';
import { EQUIPMENT } from './catalog';
import { warehouseCapacity, localUnit, warehousePlots } from './warehouse-capacity';
import { roadsFor, streetFor } from './roads';
import { formatNumber as fmt } from './number-format';
import { machinePreview } from './fleet-animation';

export function fleetPurchaseHtml(st:GameState,d:DistrictState|null|undefined,hasRecycler:boolean) {
  return `<p class="note">الآليات ملك لك في جميع الأحياء. اشترِ ما تحتاجه، ثم ابنِ مخزن معدات في أي حي لاستخدام جميع الآليات المتاحة فيه. الآليات المشغولة لا تتاح حتى عودتها.</p><div class="list">${
    (Object.keys(EQUIPMENT) as EquipmentKind[]).map(k=>{
      const count=st.units.filter(u=>u.kind===k).length;
      const ok=st.coins>=EQUIPMENT[k].cost;
      return `<div class="item"><img src="${machinePreview(k)}" alt=""/><div><h3>${EQUIPMENT[k].name}</h3><p>السعر <span class="num">${fmt(EQUIPMENT[k].cost)}</span></p><p>المملوك عالميًا <bdi class="num">${fmt(count)}</bdi></p></div><button class="btn sm" data-a="buy" data-id="${k}" data-k="buy-${k}" ${ok?'':'disabled'}>شراء</button></div>`;
    }).join('')}</div>${d?`<p class="note">${warehousePlots(d).map(p=>`مخزن قطعة ${fmt(p.id+1)}: ${fmt(st.units.filter(u=>localUnit(u,d)&&u.home?.warehouseId===p.id).length)} آليات`).join(' · ')||'لاستخدام الأسطول في هذا الحي ابنِ مخزن معدات مكتملًا.'}</p>`:''}`;
}
export function streetsHtml(d:DistrictState) {
  const streets=roadsFor(d.id).filter((r,i,all)=>all.findIndex(q=>q.id.split('-').slice(0,2).join('-')===r.id.split('-').slice(0,2).join('-'))===i);
  return `<p class="note">اختر شارعًا كاملًا لتجهيزه بالجرافة ثم سفلتته. المراحل: رمل، بيسكورس، أسفلت.</p><div class="list">${streets.map((r,i)=>{
    const parts=streetFor(d.id,r.id),ids=new Set(parts.map(p=>p.id));
    const active=d.roadWorks?.some(w=>(w.roadIds??[w.roadId]).some(id=>ids.has(id)));
    const done=parts.every(p=>d.pavedRoads?.includes(p.id));
    return `<div class="item"><span class="st"></span><div><h3>شارع ${fmt(i+1)} · ${r.a[0]===r.b[0]?'طولي':'عرضي'}</h3><p>${active?'قيد التطوير':done?'مسفلت بالكامل':'متاح للتجهيز'}</p></div><button class="btn sm" data-a="road-pick" data-id="${r.id}" data-k="street-${r.id}">عرض</button></div>`;
  }).join('')}</div>`;
}
