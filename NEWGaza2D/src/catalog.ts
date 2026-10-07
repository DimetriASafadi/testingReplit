import type { BuildingDef, EquipmentKind, ProjectDef } from './model';
export const EQUIPMENT: Record<EquipmentKind, { name: string; cost: number }> = {
  excavator: { name: 'حفار', cost: 8000 }, bulldozer: { name: 'جرافة', cost: 5000 }, truck: { name: 'شاحنة نقل', cost: 6000 },
};
export const DISTRICTS = [
  ['shujaiya','الشجاعية'],['tuffah','التفاح'],['sheikh-radwan','الشيخ رضوان'],['daraj','الدرج'],
  ['karama','الكرامة'],['old-city','البلدة القديمة'],['nasr','النصر'],['sabra','الصبرة'],
  ['zeitoun','الزيتون'],['rimal','الرمال'],['tel-al-hawa','تل الهوا'],['sheikh-ijlin','الشيخ عجلين'],['rashid','شارع الرشيد'],
].map(([id,name],i)=>({id,name,reward:i===12?1000000:100000+([0,1,2,3,3,4,5,5,6,7,8,9,10][i])*25000}));
const rows: Array<[BuildingDef['category'], string[], string[], number[], number[]]> = [
 ['equipment',['work','equipment_store','recycling'],['مبنى عمل','مخزن معدات','مصنع إعادة التدوير'],[2,1,2],[2000,4000,15000]],
 ['industry',['glass','cement','steel','asphalt','food_factory','water_treatment'],['مصنع زجاج','مصنع أسمنت','مصنع الحديد والصلب','مصنع أسفلت','مصنع الأغذية','محطة معالجة المياه'],[2,2,2,2,2,2],[9000,10000,11000,12000,13000,14000]],
 ['housing',['small_house','medium_house','villa','housing_4','housing_6','housing_complex','modern_housing','traditional_housing','housing_tower','tourist_villa'],['منزل صغير','منزل متوسط','فيلا سكنية','عمارة ٤ طوابق','عمارة ٦ طوابق','مجمع سكني','عمارة حديثة','عمارة تقليدية','برج سكني','فيلا سياحية'],[1,2,2,4,6,6,8,3,14,3],[2500,5000,7500,10000,12500,15000,17500,20000,22500,25000]],
 ['farm',['wheat','citrus','olive','vegetables','strawberry','cattle','palms','corn','protective_trees','ornamental_trees'],['أرض قمح','أرض حمضيات','أرض زيتون','أرض خضروات','أرض فراولة','مزرعة أبقار','أرض نخيل','أرض ذرة','أشجار حماية','أشجار زينة'],[1,1,1,1,1,1,1,1,1,1],[500,700,900,1100,1300,1500,1700,1900,2100,2300]],
 ['commerce',['food_shop','clothes_shop','shoe_shop','traditional_mall','modern_mall','municipality'],['محل أغذية','محل ملابس','محل أحذية','مجمع تجاري تقليدي','مجمع تجاري حديث','بلدية المنطقة'],[1,1,1,3,3,3],[3000,6000,9000,12000,15000,18000]],
 ['leisure',['zoo','falafel','shawarma','western_food','luxury_restaurant','cafe','snack_kiosk'],['حديقة حيوان','مطعم فلافل','مطعم شاورما','مطعم وجبات غربية','مطعم فاخر','كافيه','كشك مسليات'],[1,1,1,1,1,1,1],[20000,2000,3000,4000,5000,6000,7000]],
];
export const BUILDINGS: BuildingDef[] = rows.flatMap(([category,ids,names,floors,costs]) => ids.map((id,i)=>({
 id,name:names[i],category,floors:floors[i],cost:costs[i],duration:60+floors[i]*30,income:category==='equipment'?0:Math.max(100,Math.floor(costs[i]/12)),
})));
export function building(id:string): BuildingDef { const def=BUILDINGS.find(b=>b.id===id); if(!def)throw new Error('نوع المبنى غير معروف'); return def }
export function projects(finale=false): ProjectDef[] {
 return [
  {id:'water',name:'شبكة المياه',cost:18000,duration:3600,income:0,period:0},
  {id:'power',name:'شبكة الكهرباء',cost:22000,duration:5400,income:0,period:0},
  {id:'housing',name:'مشروع الإسكان',cost:25000,duration:7200,prerequisite:'water',income:0,period:0},
  {id:'road',name:finale?'طريق الكورنيش':'الطريق الرئيسي',cost:150000,duration:64800,prerequisite:'water',income:0,period:0},
  {id:'park',name:finale?'منتزه الكورنيش':'حديقة الحي',cost:12000,duration:3600,prerequisite:'road',income:0,period:0},
  {id:'services',name:'مركز الخدمات',cost:30000,duration:10800,prerequisite:'power',income:0,period:0},
  {id:'farm',name:'زراعة موسمية',cost:500,duration:300,income:1500,period:0},
  {id:'commerce',name:finale?'فندق الضيافة الساحلي':'سوق الحي',cost:finale?60000:10000,duration:finale?14400:3600,prerequisite:'services',income:finale?8000:3000,period:3600},
  {id:'industry',name:'دفعة ورشة التدوير',cost:3000,duration:900,prerequisite:'power',income:4500,period:0},
 ];
}
