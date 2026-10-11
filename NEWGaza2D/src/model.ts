export type EquipmentKind = 'excavator' | 'bulldozer' | 'truck';
export type Category = 'equipment' | 'industry' | 'housing' | 'farm' | 'commerce' | 'leisure';
export type Orientation = 0 | 1 | 2 | 3;
export interface BuildingDef { id: string; name: string; category: Category; floors: number; cost: number; duration: number; income: number }
export interface ProjectDef { id: string; name: string; cost: number; duration: number; prerequisite?: string; income: number; period: number }
export interface Plot { id: number; x: number; y: number; layoutId?: string; status: 'rubble' | 'empty' | 'building' | 'built'; buildingId: string; orientation: Orientation; startedAt: number; endsAt: number; incomeAt: number; paidCost?: number; accelerated?: boolean }
export interface ProjectState { id: string; status: 'idle' | 'building' | 'ready' | 'complete'; startedAt: number; endsAt: number; incomeAt: number; gemRewarded?: boolean }
export interface DistrictState { id: string; unlocked: boolean; claimed: boolean; plots: Plot[]; projects: ProjectState[]; camera: { x: number; y: number; zoom: number } | null }
export interface Unit { id: string; kind: EquipmentKind; level: number; purchasePrice: number; depotIds?: Record<string, number> }
export interface Job { id: string; districtId: string; plotId: number; unitIds: string[]; start: number; arrival: number; workEnd: number; returnEnd: number; cleared: boolean; value: number; originPlotId: number; departureAt?: number; returnStartAt?: number; recyclingPlotId?: number; toolsReturnEnd?: number; unloadAt?: number; unloadStartAt?: number; unloadEnd?: number; unloadQueueDistance?: number; truckHomeDepartureAt?: number; travelSpeed?: number; delivered?: boolean; insideUnloading?: boolean }
export interface GameState { version: 1; timerProfile?: 'normal' | 'minute-test'; coins: number; gems: number; inventory: { concrete: number; iron: number; wood: number; other: number; value: number }; units: Unit[]; jobs: Job[]; districts: DistrictState[]; currentDistrict: string | null; lastSeen: number; sequence: number }
export type Action =
 | { type: 'enter'; districtId: string }
 | { type: 'map' }
 | { type: 'place'; plotId: number; buildingId: string; orientation: Orientation; accelerate?: boolean }
 | { type: 'rotateBuilding'; plotId: number; orientation?: Orientation }
 | { type: 'sellBuilding' | 'removeBuilding'; plotId: number; expected?: { buildingId: string; startedAt: number } }
 | { type: 'buy'; kind: EquipmentKind }
 | { type: 'upgrade'; unitId: string }
 | { type: 'clear'; plotId: number }
 | { type: 'sell' }
 | { type: 'project'; projectId: string }
 | { type: 'collectProject'; projectId: string }
 | { type: 'collectBuilding'; plotId: number }
 | { type: 'collectAllIncome' }
 | { type: 'claim' }
 | { type: 'camera'; camera: { x: number; y: number; zoom: number } };
export interface GameAPI { state: GameState; storageError: string | null; dispatch(action: Action): void; subscribe(fn: () => void): () => void; tick(now?: number): void; exportSave(): string }
