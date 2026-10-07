export type EquipmentKind = 'excavator' | 'bulldozer' | 'truck';
export type Category = 'equipment' | 'industry' | 'housing' | 'farm' | 'commerce' | 'leisure';
export interface BuildingDef { id: string; name: string; category: Category; floors: number; cost: number; duration: number; income: number }
export interface ProjectDef { id: string; name: string; cost: number; duration: number; prerequisite?: string; income: number; period: number }
export interface Plot { id: number; x: number; y: number; status: 'rubble' | 'empty' | 'building' | 'built'; buildingId: string; orientation: 0 | 1; startedAt: number; endsAt: number; incomeAt: number }
export interface ProjectState { id: string; status: 'idle' | 'building' | 'ready' | 'complete'; startedAt: number; endsAt: number; incomeAt: number }
export interface DistrictState { id: string; unlocked: boolean; claimed: boolean; plots: Plot[]; projects: ProjectState[]; camera: { x: number; y: number; zoom: number } | null }
export interface Unit { id: string; kind: EquipmentKind; level: number; purchasePrice: number }
export interface Job { id: string; districtId: string; plotId: number; unitIds: string[]; start: number; arrival: number; workEnd: number; returnEnd: number; cleared: boolean; value: number; originPlotId: number }
export interface GameState { version: 1; coins: number; inventory: { concrete: number; iron: number; wood: number; other: number; value: number }; units: Unit[]; jobs: Job[]; districts: DistrictState[]; currentDistrict: string | null; lastSeen: number; sequence: number }
export type Action =
 | { type: 'enter'; districtId: string }
 | { type: 'map' }
 | { type: 'place'; plotId: number; buildingId: string; orientation: 0 | 1 }
 | { type: 'buy'; kind: EquipmentKind }
 | { type: 'upgrade'; unitId: string }
 | { type: 'clear'; plotId: number }
 | { type: 'sell' }
 | { type: 'project'; projectId: string }
 | { type: 'collectProject'; projectId: string }
 | { type: 'collectBuilding'; plotId: number }
 | { type: 'claim' }
 | { type: 'camera'; camera: { x: number; y: number; zoom: number } };
export interface GameAPI { state: GameState; storageError: string | null; dispatch(action: Action): void; subscribe(fn: () => void): () => void; tick(now?: number): void; exportSave(): string }
