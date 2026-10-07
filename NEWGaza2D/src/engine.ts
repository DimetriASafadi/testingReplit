import { BUILDINGS, DISTRICTS, EQUIPMENT, building, projects } from './catalog';
import type { Action, DistrictState, EquipmentKind, GameAPI, GameState, Job, Plot } from './model';
import { validateSave } from './save';

export const SAVE_KEY = 'newgaza2d-save-v1';
export function newGame(now = Date.now()): GameState {
  const homes = BUILDINGS.filter(b => b.category === 'housing');
  return {
    version: 1, coins: 1000000, inventory: { concrete: 0, iron: 0, wood: 0, other: 0, value: 0 },
    units: [], jobs: [], currentDistrict: null, lastSeen: now, sequence: 0,
    districts: DISTRICTS.map((d, index) => ({
      id: d.id, unlocked: index === 0, claimed: false, camera: null,
      projects: projects(index === 12).map(p => ({ id: p.id, status: 'idle', startedAt: 0, endsAt: 0, incomeAt: 0 })),
      plots: Array.from({ length: 64 }, (_, i) => ({
        id: i, x: (i % 8) * 2, y: Math.floor(i / 8) * 2,
        status: [0, 1, 8, 9, 15, 48, 55, 56, 57, 62, 63].includes(i) ? 'empty' : 'rubble',
        buildingId: homes[(i * 7 + index * 3) % homes.length].id,
        orientation: (i % 2) as 0 | 1, startedAt: 0, endsAt: 0, incomeAt: 0,
      })),
    })),
  };
}
function ensure(condition: unknown, message: string): asserts condition { if (!condition) throw new Error(message) }
export function jobPhase(job: Job, now = Date.now()): 'travel' | 'work' | 'return' {
  return now < job.arrival ? 'travel' : now < job.workEnd ? 'work' : 'return';
}
export function districtProgress(d: DistrictState): number {
  const rubbleLeft = d.plots.filter(p => p.status === 'rubble').length;
  return Math.round(((53 - rubbleLeft) + d.projects.filter(p => p.status === 'complete').length) / 62 * 100);
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
      try { const parsed: unknown = JSON.parse(raw); validateSave(parsed); this.state = parsed }
      catch { throw new Error('تعذر التحقق من ملف الحفظ. لم نحذفه أو نستبدله؛ نزّل نسخة منه قبل اختيار بداية جديدة.') }
    } else this.state = newGame(now);
    this.tick(now);
    this.persist();
  }
  subscribe(fn: () => void) { this.listeners.add(fn); return () => { this.listeners.delete(fn) } }
  exportSave() { return JSON.stringify(this.state, null, 2) }
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
          ensure(Number.isFinite(x) && Number.isFinite(y) && zoom >= 0.15 && zoom <= 4, 'إعداد الكاميرا غير صالح');
          this.district().camera = { x,y,zoom }; break;
        }
        case 'place': {
          const p = this.plot(action.plotId), def = building(action.buildingId);
          ensure(p.status === 'empty', 'البناء متاح على الأرض النظيفة فقط');
          ensure(action.orientation === 0 || action.orientation === 1, 'اختر الأمام أو الخلف');
          this.pay(def.cost);
          Object.assign(p, { status: 'building', buildingId: def.id, orientation: action.orientation, startedAt: now, endsAt: now + def.duration * 1000, incomeAt: 0 });
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
          const kinds: EquipmentKind[] = ['excavator','bulldozer','truck'];
          const units = kinds.map(k => this.state.units.find(u => u.kind === k && !this.busy(u.id)));
          ensure(units.every(Boolean), 'يلزم حفار وجرافة وشاحنة متاحة؛ اشترِ المعدات أو انتظر عودة الفريق');
          const team = units.map(u => u!);
          const level = Math.min(...team.map(u => u.level));
          const distance = Math.abs(p.x - recycler.x) + Math.abs(p.y - recycler.y) + 2;
          const travel = Math.ceil((8000 + distance * 850) / (1 + (level - 1) * .35));
          const arrival = now + travel, workEnd = arrival + 60000;
          const truck = team.find(u => u.kind === 'truck')!;
          const returning = Math.max(30000, Math.ceil(90000 / (1 + (truck.level - 1) * .35)), travel);
          this.state.jobs.push({ id: `job-${++this.state.sequence}`, districtId: d.id, plotId: p.id,
            unitIds: team.map(u => u.id), start: now, arrival, workEnd, returnEnd: workEnd + returning,
            cleared: false, value: building(p.buildingId).cost, originPlotId: recycler.id });
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
          this.pay(def.cost); p.status = 'building'; p.startedAt = now; p.endsAt = now + def.duration * 1000; break;
        }
        case 'collectProject': {
          const d = this.district(), def = projects(d.id === 'rashid').find(p => p.id === action.projectId);
          ensure(def && def.income > 0, 'ليس لهذا المشروع دخل');
          const p = d.projects.find(p => p.id === def.id)!;
          if (def.period) {
            ensure(p.status === 'complete', 'لم يكتمل المشروع');
            const periods = Math.floor((now - p.incomeAt) / (def.period * 1000));
            ensure(periods > 0, 'يتراكم الدخل كل ساعة'); this.state.coins += periods * def.income;
            p.incomeAt += periods * def.period * 1000;
          } else {
            ensure(p.status === 'ready', 'الدخل غير جاهز للتحصيل');
            this.state.coins += def.income; p.status = 'complete';
          }
          break;
        }
        case 'collectBuilding': {
          const p = this.plot(action.plotId), def = building(p.buildingId);
          ensure(p.status === 'built' && def.income > 0, 'المبنى لا ينتج دخلًا حاليًا');
          const periods = Math.floor((now - p.incomeAt) / 3600000);
          ensure(periods > 0, 'يتراكم الدخل كل ساعة');
          this.state.coins += periods * def.income; p.incomeAt += periods * 3600000; break;
        }
        case 'claim': {
          const d = this.district(), index = this.state.districts.indexOf(d);
          ensure(!d.claimed, 'استلمت مكافأة هذا الحي بالفعل');
          ensure(d.projects.every(p => p.status === 'complete') && !d.plots.some(p => p.status === 'rubble') && !this.state.jobs.some(j => j.districtId === d.id), 'أزل كل الركام وأكمل مشاريع الحي وحصّل إنتاجها أولًا');
          d.claimed = true; this.state.coins += DISTRICTS[index].reward;
          if (index + 1 < this.state.districts.length) this.state.districts[index + 1].unlocked = true;
          break;
        }
        default: throw new Error('إجراء غير معروف');
      }
      ensure(Number.isSafeInteger(this.state.coins), 'تجاوز الرصيد الحد المسموح');
    } catch (error) { this.state = snapshot; throw error }
    this.persist(); this.notify();
  }
}
