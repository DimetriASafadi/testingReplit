import { BUILDINGS, DISTRICTS, EQUIPMENT, building, projects } from './catalog';
import type { Action, DistrictState, GameAPI, GameState, Job, Plot } from './model';
import { validateSave } from './save';
import { factoryWindows, nextFactorySlot, outgoingTravelDuration, returnTravelDuration } from './fleet-motion';
import { cityLayout, INITIAL_EMPTY } from './city-layout';
import { availableDepotTeam, balanceFleetDepots } from './fleet-depots';
import { isOrientation, nextOrientation } from './orientation';
import { MIN_CAMERA_ZOOM } from './camera-overview';
import { buildingAccruedIncome, buildingSaleValue } from './building-disposal';
import { activityDuration, applyTimingProfile, incomeIntervalLabel, timingProfile } from './game-timing';
import { districtIncome } from './district-income';
import { buildingIncomeDetails } from './building-income';
import { PROJECT_GEMS, DISTRICT_GEMS, HALF_TIME_GEMS } from './gems';

export const SAVE_KEY = 'newgaza2d-save-v1';
export const factoryRotationBlocked = (state: GameState, plotId: number) =>
  state.jobs.some(j => j.districtId === state.currentDistrict && j.originPlotId === plotId && j.returnEnd > state.lastSeen);
export function newGame(now = Date.now()): GameState {
  const homes = BUILDINGS.filter(b => b.category === 'housing');
  return {
    version: 1, timerProfile: timingProfile(), coins: 1000000, gems: 0, inventory: { concrete: 0, iron: 0, wood: 0, other: 0, value: 0 },
    units: [], jobs: [], currentDistrict: null, lastSeen: now, sequence: 0,
    districts: DISTRICTS.map((d, index) => ({
      id: d.id, unlocked: index === 0, claimed: false, camera: null,
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
  if (state.districts.some(d => d.id === 'shati')) return;
  const added = newGame(state.lastSeen).districts.find(d => d.id === 'shati')!;
  added.unlocked = state.districts.find(d => d.id === 'sheikh-ijlin')!.claimed;
  state.districts.splice(DISTRICTS.findIndex(d => d.id === 'shati'), 0, added);
}
function ensure(condition: unknown, message: string): asserts condition { if (!condition) throw new Error(message) }
export function jobPhase(job: Job, now = Date.now()): 'travel' | 'work' | 'return' {
  return now < job.arrival ? 'travel' : now < job.workEnd ? 'work' : 'return';
}
export function districtProgress(d: DistrictState): number {
  const rubbleLeft = d.plots.filter(p => p.status === 'rubble').length;
  const initialRubble = d.plots.length - INITIAL_EMPTY.length;
  return Math.max(0, Math.min(100, Math.round(((initialRubble - rubbleLeft) + d.projects.filter(p => p.status === 'complete').length) / (initialRubble + d.projects.length) * 100)));
}
/** Expand valid legacy saves by plot identity, retaining all earned access and jobs. */
export function expandCitySave(state: GameState): void {
  const needsExpansion = (d: DistrictState) => d.plots.length === 64 ||
    d.plots.some((p,i) => { const cell = cityLayout(d.id).cells[i]; return p.x !== cell.x || p.y !== cell.y; });
  if (!state.districts.some(needsExpansion)) return;
  const fresh = newGame(state.lastSeen);
  for (const [index, d] of state.districts.entries()) {
    if (!needsExpansion(d)) continue;
    d.plots = fresh.districts[index].plots.map(p => p.id < d.plots.length
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
  private listeners = new Set<() => void>();
  private savedAt = 0;
  constructor(private storage: Pick<Storage, 'getItem' | 'setItem'> = localStorage, now = Date.now()) {
    let raw: string | null;
    try { raw = storage.getItem(SAVE_KEY) } catch { throw new Error('تعذر قراءة الحفظ المحلي. اسمح بتخزين بيانات الموقع ثم أعد المحاولة.') }
    if (raw !== null) {
      try { const parsed: unknown = JSON.parse(raw); validateSave(parsed); expandDistrictSave(parsed); expandCitySave(parsed); validateSave(parsed); this.state = parsed }
      catch { throw new Error('تعذر التحقق من ملف الحفظ. لم نحذفه أو نستبدله؛ نزّل نسخة منه قبل اختيار بداية جديدة.') }
    } else this.state = newGame(now);
    this.state.gems ??= 0;
    balanceFleetDepots(this.state);
    this.tick(now);
    applyTimingProfile(this.state, this.state.lastSeen);
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
  private persist() {
    try {
      this.storage.setItem(SAVE_KEY, JSON.stringify(this.state));
      this.storageError = null; this.savedAt = this.state.lastSeen;
    } catch { this.storageError = 'لم يُحفظ آخر تقدم: مساحة التخزين ممتلئة أو محظورة. صدّر الحفظ قبل إغلاق الصفحة.' }
  }
  tick(now = Date.now()) {
    ensure(Number.isSafeInteger(now) && now >= 0, 'وقت الجهاز غير صالح');
    now = Math.max(now, this.state.lastSeen);
    let changed = false;
    for (const d of this.state.districts) {
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
      if (now >= j.returnEnd) {
        const yieldStock = rubbleYield(j.value);
        for (const key of ['concrete','iron','wood','other','value'] as const) this.state.inventory[key] += yieldStock[key];
        changed = true;
      }
    }
    this.state.jobs = this.state.jobs.filter(j => now < j.returnEnd);
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
  private busy(id: string) { return this.state.jobs.some(j => j.unitIds.includes(id)) }
  private recycler(d: DistrictState) { return d.plots.find(p => p.buildingId === 'recycling' && p.status === 'built') }
  dispatch(action: Action) {
    this.tick();
    const snapshot = structuredClone(this.state);
    try {
      const now = this.state.lastSeen;
      switch (action.type) {
        case 'map': this.state.currentDistrict = null; break;
        case 'enter': {
          const d = this.state.districts.find(d => d.id === action.districtId);
          ensure(d?.unlocked, 'أكمل الحي السابق واستلم مكافأته أولًا');
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
          Object.assign(p, { status: 'building', buildingId: def.id, orientation: action.orientation, startedAt: now, endsAt: now + activityDuration(def.duration * 1000) / (action.accelerate ? 2 : 1), incomeAt: 0, paidCost: def.cost, accelerated: !!action.accelerate });
          break;
        }
        case 'sellBuilding':
        case 'removeBuilding': {
          const p = this.plot(action.plotId);
          ensure(p.status === 'built' || p.status === 'building', 'البيع والإزالة متاحان للمباني فقط');
          ensure(!action.expected || (p.buildingId === action.expected.buildingId && p.startedAt === action.expected.startedAt),
            'تغيّر المبنى؛ افتح تفاصيله وأكّد العملية مجددًا');
          ensure(!factoryRotationBlocked(this.state, p.id), 'انتظر عودة جميع آليات المصنع قبل بيعه أو إزالته');
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
          ensure(!factoryRotationBlocked(this.state, p.id), 'انتظر عودة آليات المصنع قبل تدويره حتى لا يتغير مدخلها أثناء المهمة');
          const orientation = action.orientation ?? nextOrientation(p.orientation);
          ensure(isOrientation(orientation), 'اتجاه المبنى غير صالح');
          p.orientation = orientation;
          break;
        }
        case 'buy': {
          ensure(this.recycler(this.district()), 'ابنِ مصنع إعادة التدوير وانتظر اكتماله أولًا');
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
          ensure(p.status === 'rubble', 'هذا الموقع ليس ركامًا');
          ensure(!this.state.jobs.some(j => j.districtId === d.id && j.plotId === p.id), 'يوجد فريق يعمل هنا بالفعل');
           balanceFleetDepots(this.state);
           const assigned = availableDepotTeam(this.state, d, p);
           ensure(assigned, 'يلزم حفار وجرافة وشاحنة متاحة؛ اشترِ المعدات أو انتظر عودة الفريق');
           const { team, depot: source } = assigned;
          const level = Math.min(...team.map(u => u.level));
          const travel = outgoingTravelDuration(source, p, level);
          const arrival = now + travel, workEnd = arrival + 60000;
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
          this.pay(def.cost); p.status = 'building'; p.startedAt = now; p.endsAt = now + activityDuration(def.duration * 1000); break;
        }
        case 'collectProject': {
          const d = this.district(), def = projects(d.id === 'rashid').find(p => p.id === action.projectId);
          ensure(def && def.income > 0, 'ليس لهذا المشروع دخل');
          const p = d.projects.find(p => p.id === def.id)!;
          if (def.period) {
            ensure(p.status === 'complete', 'لم يكتمل المشروع');
            const period = activityDuration(def.period * 1000);
            ensure(now >= p.incomeAt + period, `يجهز الدخل بعد ${incomeIntervalLabel()}`);
            this.state.coins += def.income;
            p.incomeAt = now;
          } else {
            ensure(p.status === 'ready', 'الدخل غير جاهز للتحصيل');
            this.state.coins += def.income; p.status = 'complete';
          }
          break;
        }
        case 'collectBuilding': {
          const p = this.plot(action.plotId), def = building(p.buildingId);
          ensure(p.status === 'built' && def.income > 0, 'المبنى لا ينتج دخلًا حاليًا');
          const income = buildingIncomeDetails(p, now);
          ensure(income.periods > 0, `يجهز الدخل بعد ${incomeIntervalLabel()}`);
          this.state.coins += income.amount; p.incomeAt = now; break;
        }
        case 'collectAllIncome': {
          const income = districtIncome(this.district(), now);
          ensure(income.total > 0, 'لا يوجد دخل جاهز للجمع في هذا الحي');
          this.state.coins += income.total;
          for (const entry of income.plots) entry.plot.incomeAt = now;
          for (const entry of income.investments) {
            if (entry.period) entry.project.incomeAt = now;
            else entry.project.status = 'complete';
          }
          break;
        }
        case 'claim': {
          const d = this.district(), index = this.state.districts.indexOf(d);
          ensure(!d.claimed, 'استلمت مكافأة هذا الحي بالفعل');
          ensure(d.projects.every(p => p.status === 'complete') && !d.plots.some(p => p.status === 'rubble') && !this.state.jobs.some(j => j.districtId === d.id), 'أزل كل الركام وأكمل مشاريع الحي وحصّل إنتاجها أولًا');
          d.claimed = true; this.state.coins += DISTRICTS[index].reward;
          this.state.gems += DISTRICT_GEMS;
          if (index + 1 < this.state.districts.length) this.state.districts[index + 1].unlocked = true;
          break;
        }
        default: throw new Error('إجراء غير معروف');
      }
       if (action.type === 'buy' || action.type === 'enter') balanceFleetDepots(this.state);
       ensure(Number.isSafeInteger(this.state.coins), 'تجاوز الرصيد الحد المسموح');
       ensure(Number.isSafeInteger(this.state.gems) && this.state.gems >= 0, 'رصيد الجواهر غير صالح');
    } catch (error) { this.state = snapshot; throw error }
    this.persist(); this.notify();
  }
}
