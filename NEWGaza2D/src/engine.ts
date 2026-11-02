import { BUILDINGS, DISTRICTS, EQUIPMENT, building, projects, legacyProjects } from './catalog';
import type { Action, DistrictState, GameAPI, GameState, Job, Plot } from './model';
import { validateSave } from './save';
import { factoryWindows, nextFactorySlot, outgoingTravelDuration, returnTravelDuration } from './fleet-motion';
import { cityLayout, INITIAL_EMPTY } from './city-layout';
import { availableDepotTeam, balanceFleetDepots, equipmentWarehouses, unitInJob } from './fleet-depots';
import { truckDeliveryRoute, truckHomeRoute, UNLOAD_DURATION, recyclerOpeningLead, recyclerVisitWindow } from './warehouse-cycle';
import { CONVOY_SPEED, routeLength } from './fleet-motion';
import { isOrientation, nextOrientation } from './orientation';
import { MIN_CAMERA_ZOOM } from './camera-overview';
import { buildingAccruedIncome, buildingSaleValue } from './building-disposal';
import { activityDuration, applyTimingProfile, incomeIntervalLabel, timingProfile } from './game-timing';
import { districtIncome } from './district-income';
import { buildingIncomeDetails } from './building-income';
import { PROJECT_GEMS, DISTRICT_GEMS, HALF_TIME_GEMS } from './gems';
import { difficultyOf, isDifficulty, PAVER_COST, projectIncome, ROAD_COST, STARTING_COINS, workDuration } from './economy';
import { COIN_PACKS, WHEEL_COOLDOWN, WHEEL_PRIZES, wheelResult } from './rewards';
import { roadsFor, pavingRoute, pathLength, streetFor } from './roads';
import { actualWorkDuration, clearingSeconds, STREET_WORK_MS, STREET_STAGE_SHARES } from './construction-rules';
import { roadUnitBusy, localUnit } from './warehouse-capacity';
import { migrateReconstruction } from './reconstruction-migration';
import { wheelCooldown } from './timing-settings';
import { beginAd, cancelAd, markAdEarned, recoverAd, settleEarnedAd } from './ads/ledger';
import type { AdPlacement } from './ads/config';

export const SAVE_KEY = 'newgaza2d-save-v1';
export const UPGRADE_BACKUP_KEY = 'newgaza2d-save-before-upgrade';
export const factoryRotationBlocked = (state: GameState, plotId: number) =>
  state.jobs.some(j => j.districtId === state.currentDistrict && (j.originPlotId === plotId || j.recyclingPlotId === plotId) && j.returnEnd > state.lastSeen) ||
  !!state.districts.find(d=>d.id===state.currentDistrict)?.roadWorks?.some(w=>(w.factoryId===plotId || w.preparation?.warehouseId===plotId) && w.returnEnd>state.lastSeen);
export function newGame(now = Date.now()): GameState {
  const homes = BUILDINGS.filter(b => b.category === 'housing');
  return {
    version: 1, campaignVersion:1, reconstructionVersion:1, expansionVersion:1, pavers:[], exchanges:[], timerProfile: timingProfile(), coins: STARTING_COINS, gems: 0, inventory: { concrete: 0, iron: 0, wood: 0, other: 0, value: 0 },
    units: [], jobs: [], currentDistrict: null, lastSeen: now, sequence: 0,
    districts: DISTRICTS.map((d, index) => ({
      id: d.id, unlocked: index === 0, claimed: false, camera: null,
      difficulty:'medium',difficultyChosen:false,pavedRoads:[],roadWorks:[],celebrated:false,
      projects: projects(d.id === 'rashid').map(p => ({ id: p.id, status: 'idle', startedAt: 0, endsAt: 0, incomeAt: 0 })),
      plots: cityLayout(d.id).cells.map(({ id: i, x, y }) => ({
        id: i, x, y, layoutId: d.id,
        status: INITIAL_EMPTY.includes(i) ? 'empty' : 'rubble',
        buildingId: homes[(i * 7 + index * 3) % homes.length].id,
        orientation: (i % 2) as 0 | 1, startedAt: 0, endsAt: 0, incomeAt: 0,
      })),
    })),
  };
}
/** Add the new chapter without replacing any existing district, depot or job. */
export function expandDistrictSave(state: GameState): void {
  if (state.campaignVersion===1 || state.districts.some(d => d.id === 'shati')) return;
  const added = newGame(state.lastSeen).districts.find(d => d.id === 'shati')!;
  if(state.reconstructionVersion!==1) added.projects=legacyProjects().map(p=>({id:p.id,status:'idle',startedAt:0,endsAt:0,incomeAt:0}));
  added.unlocked = state.districts.find(d => d.id === 'sheikh-ijlin')!.claimed;
   state.districts.splice(state.districts.findIndex(d => d.id === 'rashid'), 0, added);
}
function ensure(condition: unknown, message: string): asserts condition { if (!condition) throw new Error(message) }
export function jobPhase(job: Job, now = Date.now()): 'travel' | 'work' | 'return' {
  return now < job.arrival ? 'travel' : now < job.workEnd ? 'work' : 'return';
}
export function districtProgress(d: DistrictState): number {
   if(d.claimed || districtComplete(d))return 100;
   const plots=d.plots.reduce((s,p)=>s+(p.status==='built'?1:p.status==='building'?.55:p.status==='empty'?.2:0),0)/d.plots.length;
   const roads=(d.pavedRoads?.length??0)/roadsFor(d.id).length;
   const projects=d.projects.filter(p=>p.status==='complete').length/d.projects.length;
   return Math.max(0,Math.min(99,Math.floor(plots*70+roads*15+projects*15)));
}
export function districtComplete(d:DistrictState) {
 const paved=new Set(d.pavedRoads);
 return d.plots.every(p=>p.status==='built') && d.projects.every(p=>p.status==='complete') &&
   roadsFor(d.id).every(r=>paved.has(r.id)) && !d.roadWorks?.length;
}
export function migrateExpansion(state:GameState){
  for (const d of state.districts) if (!d.projects.some(p => p.id === 'flag')) {
    d.projects.push({ id:'flag', status:d.claimed?'complete':'idle', startedAt:0, endsAt:0, incomeAt:0, gemRewarded:d.claimed });
  }
 if(state.expansionVersion===1)return;
 state.pavers??=[];state.exchanges??=[];
 for(const d of state.districts){
  d.difficulty??='medium';
  d.difficultyChosen = d.claimed || d.id===state.currentDistrict || d.plots.some(p=>p.status==='built'||p.status==='building') || d.projects.some(p=>p.status!=='idle');
  d.pavedRoads??=d.claimed?roadsFor(d.id).map(r=>r.id):[];
  d.roadWorks??=[];d.celebrated??=d.claimed;
  // Already earned building income retains its old amount until collection.
 }
 state.expansionVersion=1;
}
/** Expand valid legacy saves by plot identity, retaining all earned access and jobs. */
export function expandCitySave(state: GameState): void {
  const needsExpansion = (d: DistrictState) => d.plots.length === 64 ||
    d.plots.some((p,i) => { const cell = cityLayout(d.id).cells[i]; return p.x !== cell.x || p.y !== cell.y; });
  if (!state.districts.some(needsExpansion)) return;
  const fresh = newGame(state.lastSeen);
  for (const [index, d] of state.districts.entries()) {
    if (!needsExpansion(d)) continue;
    const template = fresh.districts.find(v=>v.id===d.id)?.plots ?? cityLayout(d.id).cells.map(c=>({...d.plots[c.id%d.plots.length],...c,layoutId:d.id}));
    d.plots = template.map(p => p.id < d.plots.length
      ? { ...d.plots[p.id], x: p.x, y: p.y, layoutId: d.id } : p);
    d.camera = null;
  }
}
export function rubbleYield(price: number) {
  const budget = Math.floor(Math.floor(price / 3) / 10);
  const concrete = Math.floor(budget / 5 / 2), iron = Math.floor(budget / 5 / 6), wood = Math.floor(budget / 5 / 7) * 2;
  const other = budget - concrete * 2 - iron * 6 - wood * 7 / 2;
  return { concrete, iron, wood, other, value: budget * 10 };
}
export class GameStore implements GameAPI {
  state: GameState;
  storageError: string | null = null;
  migrationNotice: string | null = null;
  private listeners = new Set<() => void>();
  private savedAt = 0;
  constructor(private storage: Pick<Storage, 'getItem' | 'setItem'> = localStorage, now = Date.now()) {
    let raw: string | null;
    try { raw = storage.getItem(SAVE_KEY) } catch { throw new Error('تعذر قراءة الحفظ المحلي. اسمح بتخزين بيانات الموقع ثم أعد المحاولة.') }
    if (raw !== null) {
      try { const parsed: unknown = JSON.parse(raw); validateSave(parsed); expandDistrictSave(parsed); expandCitySave(parsed); validateSave(parsed); this.state = parsed }
      catch { throw new Error('تعذر التحقق من ملف الحفظ. لم نحذفه أو نستبدله؛ نزّل نسخة منه قبل اختيار بداية جديدة.') }
    } else this.state = newGame(now);
    if(raw!==null && (this.state.campaignVersion!==1 || this.state.reconstructionVersion!==1 || this.state.timerProfile!==timingProfile())) {
      try { this.storage.setItem(UPGRADE_BACKUP_KEY,raw); }
      catch { throw new Error('تعذر حفظ نسخة احتياطية قبل تحديث التقدم. لم نغيّر الحفظ الأصلي؛ تحقق من مساحة التخزين.'); }
    }
    this.state.gems ??= 0;
    migrateExpansion(this.state);
    migrateReconstruction(this.state, Math.max(now,this.state.lastSeen));
    applyTimingProfile(this.state, Math.max(now,this.state.lastSeen));
    if(this.state.campaignVersion!==1) {
      // Archived districts remain in the save for their jobs, equipment and
      // paid construction; only the active chapter order changes.
      const active=DISTRICTS.map(def=>this.state.districts.find(d=>d.id===def.id)!);
      const archived=this.state.districts.filter(d=>!DISTRICTS.some(def=>def.id===d.id));
      active[0].unlocked=true;
      for(let i=1;i<active.length;i++)if(active[i-1].claimed)active[i].unlocked=true;
      this.state.districts=[...active,...archived];
      if(archived.some(d=>d.id===this.state.currentDistrict)) {
        this.state.currentDistrict=null;
        this.migrationNotice='تم تحديث ترتيب الأحياء. حُفظ تقدم الحي السابق وآلياته بأمان؛ اختر حيًا متاحًا من الخريطة.';
      }
      this.state.campaignVersion=1;
    }
    balanceFleetDepots(this.state);
    this.tick(now);
    recoverAd(this.state, this.state.lastSeen);
    this.persist();
  }
  subscribe(fn: () => void) { this.listeners.add(fn); return () => { this.listeners.delete(fn) } }
  exportSave() { return JSON.stringify(this.state, null, 2) }
  reset(now = Date.now()) {
    ensure(Number.isSafeInteger(now) && now >= 0, 'وقت الجهاز غير صالح');
    const fresh = newGame(now);
    validateSave(fresh);
    // Commit before changing live state; old timers then also see the fresh save.
    try { this.storage.setItem(SAVE_KEY, JSON.stringify(fresh)); }
    catch {
      this.storageError = 'تعذرت إعادة ضبط الحفظ. لم نغيّر تقدمك؛ تحقق من مساحة التخزين.';
      this.notify(); return false;
    }
    this.state = fresh; this.storageError = null; this.savedAt = now;
    this.notify(); return true;
  }
  private notify() { for (const fn of this.listeners) fn() }
  /** Atomic local transitions. Only ad adapters call earned; this is not a server receipt. */
  private adTransition<T>(change: () => T): T {
    this.tick();
    const previous = structuredClone(this.state);
    try {
      const result = change();
      if (!this.persist()) throw Error(this.storageError!);
      this.notify(); return result;
    } catch (e) { this.state = previous; this.notify(); throw e; }
  }
  beginRewardedAd(p: AdPlacement) { return this.adTransition(() => beginAd(this.state, p, this.state.lastSeen)); }
  earnedRewardedAd(id: string) {
    // Durable grant marker first; a reload after this write can finish the credit.
    this.adTransition(() => markAdEarned(this.state, id));
    return this.adTransition(() => settleEarnedAd(this.state, this.state.lastSeen));
  }
  cancelRewardedAd(id: string) { this.adTransition(() => cancelAd(this.state, id)); }
  private persist() {
    try {
      this.storage.setItem(SAVE_KEY, JSON.stringify(this.state));
      this.storageError = null; this.savedAt = this.state.lastSeen; return true;
    } catch { this.storageError = 'لم يُحفظ آخر تقدم: مساحة التخزين ممتلئة أو محظورة. صدّر الحفظ قبل إغلاق الصفحة.'; return false; }
  }
  tick(now = Date.now()) {
    ensure(Number.isSafeInteger(now) && now >= 0, 'وقت الجهاز غير صالح');
    now = Math.max(now, this.state.lastSeen);
    let changed = false;
    for (const d of this.state.districts) {
      for(const w of d.roadWorks??[])if(now>=w.workEnd)for(const id of w.roadIds??[w.roadId])if(!d.pavedRoads!.includes(id)){d.pavedRoads!.push(id);changed=true;}
      if(d.roadWorks?.some(w=>now>=w.returnEnd)){d.roadWorks=d.roadWorks.filter(w=>now<w.returnEnd);changed=true;}
      for (const p of d.plots) if (p.status === 'building' && now >= p.endsAt) {
        p.status = 'built'; p.incomeAt = p.endsAt; changed = true;
      }
      const definitions = projects(d.id === 'rashid');
      for (const p of d.projects) if (p.status === 'building' && now >= p.endsAt) {
        const def = definitions.find(v => v.id === p.id)!;
        p.status = def.income && !def.period ? 'ready' : 'complete';
        p.incomeAt = p.endsAt; changed = true;
      }
      for (const p of d.projects) {
        if ((p.status === 'complete' || p.status === 'ready') && !p.gemRewarded) {
          this.state.gems += PROJECT_GEMS;
          p.gemRewarded = true; changed = true;
        }
      }
    }
    for (const j of this.state.jobs) {
      const d = this.state.districts.find(v => v.id === j.districtId)!;
      if (!j.cleared && now >= j.workEnd) {
        d.plots[j.plotId].status = 'empty'; j.cleared = true; changed = true;
      }
      if (now >= (j.unloadEnd ?? j.returnEnd) && !j.delivered) {
        const yieldStock = rubbleYield(j.value);
        for (const key of ['concrete','iron','wood','other','value'] as const) this.state.inventory[key] += yieldStock[key];
        j.delivered = true;
        changed = true;
      }
    }
    this.state.jobs = this.state.jobs.filter(j => now < j.returnEnd);
    for(const d of this.state.districts) if(DISTRICTS.some(def=>def.id===d.id) && !d.completionRewarded && !d.claimed && districtComplete(d)) {
      this.state.coins+=DISTRICTS.find(def=>def.id===d.id)!.reward;
      this.state.gems+=DISTRICT_GEMS;d.completionRewarded=true;changed=true;
    }
    this.state.lastSeen = now;
    if (changed) balanceFleetDepots(this.state);
    if (changed || now - this.savedAt >= 10000) this.persist();
    this.notify();
  }
  private district(): DistrictState {
    const d = this.state.districts.find(d => d.id === this.state.currentDistrict);
    ensure(d?.unlocked, 'اختر حيًا متاحًا أولًا'); return d;
  }
  private plot(id: number): Plot {
    const p = this.district().plots.find(p => p.id === id);
    ensure(p, 'الموقع غير موجود'); return p;
  }
  private pay(amount: number) { ensure(this.state.coins >= amount, 'الرصيد غير كافٍ'); this.state.coins -= amount }
  private busy(id: string) { const u = this.state.units.find(u => u.id === id); return roadUnitBusy(this.state,id) || (!!u && this.state.jobs.some(j => unitInJob(j, u, this.state.lastSeen))) }
  private recycler(d: DistrictState) { return d.plots.find(p => p.buildingId === 'recycling' && p.status === 'built') }
  dispatch(action: Action) {
    this.tick();
    const snapshot = structuredClone(this.state);
    try {
      const now = this.state.lastSeen;
      switch (action.type) {
        case 'map': this.state.currentDistrict = null; break;
        case 'difficulty': {
          const d=this.district();ensure(!d.difficultyChosen,'تم تحديد صعوبة هذا الحي بالفعل');
          ensure(isDifficulty(action.difficulty),'مستوى الصعوبة غير صالح');
          d.difficulty=action.difficulty;d.difficultyChosen=true;break;
        }
        case 'buyPaver': {
          ensure(this.district().plots.some(p=>p.status==='built'&&p.buildingId==='asphalt'),'ابنِ مصنع أسفلت مكتملًا أولًا');
          this.pay(PAVER_COST);(this.state.pavers??=[]).push({id:`paver-${++this.state.sequence}`,cost:PAVER_COST});break;
        }
        case 'pave': {
          const d=this.district(),street=streetFor(d.id,action.roadId);
          ensure(street.length,'الشارع غير موجود');
          const ids=new Set(street.map(r=>r.id)),pending=street.filter(r=>!d.pavedRoads?.includes(r.id));
          ensure(pending.length&&!d.roadWorks?.some(w=>(w.roadIds??[w.roadId]).some(id=>ids.has(id))),'الشارع مسفلت أو قيد العمل بالفعل');
          const road={id:action.roadId,a:street[0].a,b:street[street.length-1].b};
          const factory=d.plots.filter(p=>p.status==='built'&&p.buildingId==='asphalt').sort((a,b)=>pathLength(pavingRoute(d,a,road))-pathLength(pavingRoute(d,b,road)))[0];
          ensure(factory,'يلزم مصنع أسفلت مكتمل');
          const machine=this.state.pavers?.find(m=>!this.state.districts.some(d=>d.roadWorks?.some(w=>w.machineId===m.id)));
          ensure(machine,'اشترِ آلية سفلتة أو انتظر عودتها');
          balanceFleetDepots(this.state);
          const dozer=this.state.units.find(u=>u.kind==='bulldozer'&&localUnit(u,d)&&!this.busy(u.id)&&equipmentWarehouses(d).some(p=>p.id===u.home?.warehouseId));
          ensure(dozer,'يلزم جرافة متاحة في مخزن معدات هذا الحي لتجهيز الطريق');
          const warehouse=d.plots[dozer.home!.warehouseId];
          this.pay(ROAD_COST*pending.length);
          const route=pavingRoute(d,factory,road),travel=Math.max(1500,Math.ceil(pathLength(route)/CONVOY_SPEED*1000));
          const dozerRoute=pavingRoute(d,warehouse,road),dozerTravel=Math.max(1500,Math.ceil(pathLength(dozerRoute)/CONVOY_SPEED*1000));
          const total=actualWorkDuration(d,STREET_WORK_MS),arrival=now+dozerTravel;
          const sandEnd=arrival+Math.round(total*STREET_STAGE_SHARES.sand),aggregateEnd=sandEnd+Math.round(total*STREET_STAGE_SHARES.aggregate);
          const asphaltStart=aggregateEnd+travel,workEnd=asphaltStart+(total-(aggregateEnd-arrival)),dozerReturnEnd=aggregateEnd+dozerTravel;
          const returnEnd=Math.max(dozerReturnEnd,workEnd+travel+Math.ceil(pathLength([road.a,road.b])/CONVOY_SPEED*1000));
          (d.roadWorks??=[]).push({roadId:road.id,roadIds:pending.map(r=>r.id),factoryId:factory.id,machineId:machine.id,start:now,arrival,workEnd,returnEnd,route,
            preparation:{unitId:dozer.id,warehouseId:warehouse.id,route:dozerRoute,sandEnd,aggregateEnd,asphaltStart,dozerReturnEnd}});break;
        }
        case 'spin': {
          ensure(!this.state.wheel || now>=this.state.wheel.startedAt+4500,'انتظر انتهاء دوران الدولاب');
          const bonus=!!this.state.wheel && now<this.state.wheel.nextAt;
          ensure(!bonus || (this.state.ads?.spinCredits??0)>0,'انتظر انتهاء وقت دولاب الحظ');
          if(bonus)this.state.ads!.spinCredits--;
          const result=wheelResult(Math.random()),prize=WHEEL_PRIZES[result];
          const nextAt=bonus?this.state.wheel!.nextAt:now+wheelCooldown();
          this.state.wheel={nextAt,cooldownMs:nextAt-now,result,startedAt:now,...(bonus?{bonus:true}:{})};
          this.state.coins+=prize.coins;this.state.gems+=prize.gems;
          this.state.inventory.iron+=prize.iron;this.state.inventory.wood+=prize.wood;
          this.state.inventory.value+=prize.iron*60+prize.wood*35;break;
        }
        case 'exchange': {
          ensure(Number.isInteger(action.pack)&&COIN_PACKS[action.pack],'الباقة غير موجودة');
          ensure(typeof action.requestId==='string'&&action.requestId.length>=8&&action.requestId.length<=100,'معرّف العملية غير صالح');
          ensure(!this.state.exchanges?.includes(action.requestId),'تم تنفيذ هذه العملية بالفعل');
          const pack=COIN_PACKS[action.pack];ensure(this.state.gems>=pack.gems,'الجواهر غير كافية');
          this.state.gems-=pack.gems;this.state.coins+=pack.coins;(this.state.exchanges??=[]).push(action.requestId);break;
        }
        case 'celebrated': {
          const d=this.district();ensure(districtComplete(d),'الحي غير مكتمل');d.celebrated=true;break;
        }
        case 'enter': {
          const d = this.state.districts.find(d => d.id === action.districtId);
          ensure(d?.unlocked && DISTRICTS.some(def=>def.id===d.id), 'أكمل الحي السابق واستلم مكافأته أولًا');
          this.state.currentDistrict = d.id; break;
        }
        case 'camera': {
          const { x,y,zoom } = action.camera;
          ensure(Number.isFinite(x) && Number.isFinite(y) && zoom >= MIN_CAMERA_ZOOM && zoom <= 4, 'إعداد الكاميرا غير صالح');
          this.district().camera = { x,y,zoom }; break;
        }
        case 'place': {
          const p = this.plot(action.plotId), def = building(action.buildingId);
          ensure(p.status === 'empty', 'البناء متاح على الأرض النظيفة فقط');
          ensure(isOrientation(action.orientation), 'اختر إحدى واجهات المبنى الأربع');
          ensure(action.accelerate === undefined || typeof action.accelerate === 'boolean', 'خيار التسريع غير صالح');
          if (action.accelerate) {
            ensure(this.state.gems >= HALF_TIME_GEMS, 'تحتاج إلى جوهرتين لتقليل وقت البناء للنصف');
            this.state.gems -= HALF_TIME_GEMS;
          }
          this.pay(def.cost);
          Object.assign(p, { status: 'building', buildingId: def.id, orientation: action.orientation, startedAt: now, endsAt: now + Math.round(actualWorkDuration(this.district(),def.duration * 1000) / (action.accelerate ? 2 : 1)), incomeAt: 0, paidCost: def.cost, accelerated: !!action.accelerate, incomeFactor:difficultyOf(this.district()).income });
          break;
        }
        case 'accelerateBuilding': {
          const p=this.plot(action.plotId);
          ensure(p.status==='building' && p.startedAt===action.expectedStartedAt,'تغيّر المبنى أو اكتمل؛ افتح تفاصيله مجددًا');
          ensure(!p.accelerated,'تم استخدام تسريع الجواهر لهذا البناء بالفعل');
          ensure(this.state.gems>=HALF_TIME_GEMS,'تحتاج إلى جوهرتين لتسريع البناء');
          this.state.gems-=HALF_TIME_GEMS;p.accelerated=true;
          // Preserve the visible fraction at the moment of acceleration.
          const remaining=Math.max(1,Math.ceil((p.endsAt-now)/2));
          p.startedAt=now-Math.floor((now-p.startedAt)/2);p.endsAt=now+remaining;
          break;
        }
        case 'sellBuilding':
        case 'removeBuilding': {
          const p = this.plot(action.plotId);
          ensure(p.status === 'built' || p.status === 'building', 'البيع والإزالة متاحان للمباني فقط');
          ensure(!action.expected || (p.buildingId === action.expected.buildingId && p.startedAt === action.expected.startedAt),
            'تغيّر المبنى؛ افتح تفاصيله وأكّد العملية مجددًا');
          ensure(!factoryRotationBlocked(this.state, p.id), 'انتظر عودة جميع آليات هذا المقر قبل بيعه أو إزالته');
          ensure(buildingAccruedIncome(p, now) === 0, 'اجمع دخل المبنى أولًا قبل بيعه أو إزالته');
          this.state.coins += action.type === 'sellBuilding' ? buildingSaleValue(p) : 0;
          Object.assign(p, { status: 'empty', orientation: 0, startedAt: 0, endsAt: 0, incomeAt: 0 });
          delete p.paidCost;
          delete p.accelerated;
          balanceFleetDepots(this.state);
          break;
        }
        case 'rotateBuilding': {
          const p = this.plot(action.plotId);
          ensure(p.status === 'building' || p.status === 'built', 'التدوير متاح للمباني فقط');
          ensure(!factoryRotationBlocked(this.state, p.id), 'انتظر عودة آليات المهمة قبل تدوير هذا المبنى حتى لا يتغير مدخلها');
          const orientation = action.orientation ?? nextOrientation(p.orientation);
          ensure(isOrientation(orientation), 'اتجاه المبنى غير صالح');
          p.orientation = orientation;
          break;
        }
        case 'buy': {
          const def = EQUIPMENT[action.kind]; ensure(def, 'نوع الآلية غير صالح'); this.pay(def.cost);
          this.state.units.push({ id: `unit-${++this.state.sequence}`, kind: action.kind, level: 1, purchasePrice: def.cost }); break;
        }
        case 'upgrade': {
          const u = this.state.units.find(v => v.id === action.unitId);
          ensure(u, 'الآلية غير موجودة'); ensure(u.level < 4, 'اكتملت الترقيات الثلاث');
          ensure(!this.busy(u.id), 'انتظر عودة الآلية قبل ترقيتها');
          this.pay(u.purchasePrice / 2); u.level++; break;
        }
        case 'clear': {
          const d = this.district(), p = this.plot(action.plotId), recycler = this.recycler(d);
          ensure(recycler, 'يلزم مصنع إعادة تدوير مكتمل في هذا الحي');
          ensure(equipmentWarehouses(d).length, 'ابنِ مخزن معدات مكتملًا لتنطلق الآليات منه وتعود إليه');
          ensure(p.status === 'rubble', 'هذا الموقع ليس ركامًا');
          ensure(!this.state.jobs.some(j => j.districtId === d.id && j.plotId === p.id), 'يوجد فريق يعمل هنا بالفعل');
           balanceFleetDepots(this.state);
           const assigned = availableDepotTeam(this.state, d, p);
           ensure(assigned, 'يلزم حفار وجرافة وشاحنة متاحة؛ اشترِ المعدات أو انتظر عودة الفريق');
           const { team, warehouse: source, depot: processing } = assigned;
          const level = Math.min(...team.map(u => u.level));
          const travel = outgoingTravelDuration(source, p, level);
           const arrival = now + travel, workEnd = arrival + actualWorkDuration(d,clearingSeconds(building(p.buildingId).floors)*1000);
          const returning = returnTravelDuration(source, p, travel);
          const job: Job = { id: `job-${++this.state.sequence}`, districtId: d.id, plotId: p.id,
            unitIds: team.map(u => u.id), start: now, arrival, workEnd, returnEnd: workEnd + returning,
             cleared: false, value: building(p.buildingId).cost, originPlotId: source.id };
           const windows = this.state.jobs.filter(j => j.districtId === d.id && j.originPlotId === source.id)
             .flatMap(j => factoryWindows(j, source, d.plots[j.plotId]));
           const outgoing = factoryWindows(job, source, p)[0];
          job.departureAt = nextFactorySlot(now, outgoing.end - outgoing.start, windows);
          const dispatchWait = job.departureAt - now;
          job.arrival += dispatchWait; job.workEnd += dispatchWait; job.returnEnd += dispatchWait;
           const incoming = factoryWindows(job, source, p)[1];
          const returnWait = nextFactorySlot(incoming.start, incoming.end - incoming.start, windows) - Math.ceil(incoming.start);
          job.returnStartAt = job.workEnd + Math.max(0, returnWait);
          job.returnEnd += Math.max(0, returnWait);
          job.recyclingPlotId = processing.id;
          job.toolsReturnEnd = job.returnEnd;
          job.insideUnloading = true;
          job.travelSpeed = CONVOY_SPEED * (1 + Math.max(0, level - 1) * .35) / 1000;
          job.unloadAt = job.workEnd + 550 + Math.ceil(routeLength(truckDeliveryRoute(processing, p)) / job.travelSpeed);
          const unloadWindows = this.state.jobs.filter(j => j.districtId === d.id && j.recyclingPlotId === processing.id)
            .map(j => recyclerVisitWindow(j,processing));
          const lead = recyclerOpeningLead(processing,job.travelSpeed);
          job.unloadStartAt = nextFactorySlot(job.unloadAt-lead, lead*2+UNLOAD_DURATION+700, unloadWindows)+lead;
          if (job.unloadStartAt > job.unloadAt) {
            const queued = unloadWindows.filter(w => w.end > job.unloadAt!).length;
            job.unloadQueueDistance = Math.min(lead*job.travelSpeed + 1.7 * Math.max(1,queued), routeLength(truckDeliveryRoute(processing,p))-.4);
            job.unloadAt -= Math.ceil(job.unloadQueueDistance / job.travelSpeed);
          }
          job.unloadEnd = job.unloadStartAt + UNLOAD_DURATION;
          const homeDuration = Math.ceil(routeLength(truckHomeRoute(processing, source)) / job.travelSpeed) + 1000;
          const homeGateDuration = Math.ceil(6.2 / job.travelSpeed) + 950;
          const incomingAt = job.unloadEnd + homeDuration - homeGateDuration;
          const ownWindows = factoryWindows({ ...job, returnEnd: job.toolsReturnEnd }, source, p);
          const homeWait = Math.max(0, nextFactorySlot(Math.max(incomingAt, job.toolsReturnEnd + 850), homeGateDuration, [...windows, ...ownWindows]) - incomingAt);
          job.truckHomeDepartureAt = job.unloadEnd + homeWait;
          job.returnEnd = Math.max(job.toolsReturnEnd, job.truckHomeDepartureAt + homeDuration);
          job.delivered = false;
          this.state.jobs.push(job);
          break;
        }
        case 'sell': {
          ensure(this.state.inventory.value > 0, 'لا توجد مواد للبيع بعد');
          this.state.coins += this.state.inventory.value;
          this.state.inventory = { concrete: 0, iron: 0, wood: 0, other: 0, value: 0 }; break;
        }
        case 'project': {
          const d = this.district(), defs = projects(d.id === 'rashid'), def = defs.find(p => p.id === action.projectId);
          ensure(def, 'المشروع غير معروف'); const p = d.projects.find(p => p.id === def.id)!;
          ensure(p.status === 'idle' || (p.status === 'complete' && def.income > 0 && def.period === 0), 'المشروع بدأ بالفعل أو بانتظار التحصيل');
          ensure(!def.prerequisite || d.projects.some(p => p.id === def.prerequisite && p.status === 'complete'), 'أكمل المشروع المطلوب أولًا');
          this.pay(def.cost); p.status = 'building'; p.startedAt = now; p.endsAt = now + workDuration(d,def.duration * 1000); break;
        }
        case 'collectProject': {
          const d = this.district(), def = projects(d.id === 'rashid').find(p => p.id === action.projectId);
          ensure(def && def.income > 0, 'ليس لهذا المشروع دخل');
          const p = d.projects.find(p => p.id === def.id)!;
          if (def.period) {
            ensure(p.status === 'complete', 'لم يكتمل المشروع');
            const period = activityDuration(def.period * 1000);
            ensure(now >= p.incomeAt + period, `يجهز الدخل بعد ${incomeIntervalLabel()}`);
            this.state.coins += projectIncome(d,def);
            p.incomeAt = now;
          } else {
            ensure(p.status === 'ready', 'الدخل غير جاهز للتحصيل');
            this.state.coins += projectIncome(d,def); p.status = 'complete';
          }
          break;
        }
        case 'collectBuilding': {
          const p = this.plot(action.plotId), def = building(p.buildingId);
          ensure(p.status === 'built' && def.income > 0, 'المبنى لا ينتج دخلًا حاليًا');
          const income = buildingIncomeDetails(p, now);
          ensure(income.periods > 0, `يجهز الدخل بعد ${incomeIntervalLabel()}`);
          this.state.coins += income.amount; p.incomeAt = now; p.incomeFactor=difficultyOf(this.district()).income; break;
        }
        case 'collectAllIncome': {
          const income = districtIncome(this.district(), now);
          ensure(income.total > 0, 'لا يوجد دخل جاهز للجمع في هذا الحي');
          this.state.coins += income.total;
          for (const entry of income.plots){entry.plot.incomeAt = now;entry.plot.incomeFactor=difficultyOf(this.district()).income;}
          for (const entry of income.investments) {
            if (entry.period) entry.project.incomeAt = now;
            else entry.project.status = 'complete';
          }
          break;
        }
        case 'claim': {
          const d = this.district(), index = this.state.districts.indexOf(d);
          ensure(!d.claimed, 'استلمت مكافأة هذا الحي بالفعل');
          ensure(districtComplete(d) && !this.state.jobs.some(j => j.districtId === d.id), 'ابنِ جميع الأراضي وأكمل سفلتة الشوارع والمشاريع وانتظر عودة الآليات أولًا');
          if(!d.completionRewarded) {
            this.state.coins+=DISTRICTS[index].reward;this.state.gems+=DISTRICT_GEMS;d.completionRewarded=true;
          }
          d.claimed = true;
          if (index + 1 < DISTRICTS.length) {this.state.districts[index + 1].unlocked = true;this.state.currentDistrict=this.state.districts[index + 1].id;}
          else this.state.currentDistrict=null;
          break;
        }
        default: throw new Error('إجراء غير معروف');
      }
       if (action.type === 'buy' || action.type === 'enter') balanceFleetDepots(this.state);
       ensure(Number.isSafeInteger(this.state.coins), 'تجاوز الرصيد الحد المسموح');
       ensure(Number.isSafeInteger(this.state.gems) && this.state.gems >= 0, 'رصيد الجواهر غير صالح');
    } catch (error) { this.state = snapshot; throw error }
    if(!this.persist()){this.state=snapshot;this.notify();throw new Error(this.storageError!);}
    this.notify();
  }
}
