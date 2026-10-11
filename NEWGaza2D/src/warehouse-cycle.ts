import type { EquipmentKind, Job, Plot } from './model';
import { cityLayout, plotBlock } from './city-layout';
import { convoyMotion, depotPortal, fleetRoute, rounded, routeLength, routePoint, type ConvoyMotion, type DepotPoint } from './fleet-motion';

export const UNLOAD_DURATION = 6000;
const cache = new Map<string, readonly DepotPoint[]>();
const routeKey = (a: Plot, b: Plot, kind: string) =>
  `${kind}:${a.layoutId}:${a.x}:${a.y}:${a.orientation}:${b.x}:${b.y}:${b.orientation}`;
function keep(key: string, points: DepotPoint[]) {
  if (cache.size >= 256) cache.delete(cache.keys().next().value!);
  const saved = Object.freeze(points); cache.set(key, saved); return saved;
}

/** New trips unload behind the doorway; old saved trips keep their original yard dock. */
export function recyclingBay(p: Plot, inside = true): DepotPoint {
  const g = depotPortal(p);
  return inside ? g.inside : [g.outside[0] + g.outward[0] * .6, g.outside[1] + g.outward[1] * .6];
}
export function truckDeliveryRoute(recycler: Plot, site: Plot, inside = true) {
  const key = routeKey(recycler, site, `delivery:${inside}`), saved = cache.get(key); if (saved) return saved;
  return keep(key, inside ? [...fleetRoute(recycler, site, 'truck', true)]
    : rounded([...fleetRoute(recycler, site, 'truck', true).slice(0, -2), recyclingBay(recycler,false)]));
}
function depotAccess(p: Plot, lane: number): DepotPoint[] {
  const g = depotPortal(p), b = plotBlock(p);
  if (!b || !p.layoutId) return [g.inside, g.threshold, g.outside];
  const alongY = !!g.outward[1], sign = alongY ? g.outward[1] : g.outward[0];
  const boundary = alongY ? sign > 0 ? b.y1 : b.y0 : sign > 0 ? b.x1 : b.x0;
  const layout = cityLayout(p.layoutId), roads = alongY ? layout.roadY : layout.roadX;
  const road = roads.filter(r => sign * (r - boundary) >= -1e-8)
    .sort((a,b) => Math.abs(a - boundary) - Math.abs(b - boundary))[0];
  if (road === undefined) throw new Error('Depot exit has no connecting street');
  const direct = alongY ? (sign > 0 ? p.y + 2 : p.y) === boundary : (sign > 0 ? p.x + 2 : p.x) === boundary;
  const corner: DepotPoint = sign > 0 ? [p.x + 2, p.y + 2] : [p.x, p.y];
  const exit: DepotPoint = alongY ? [direct ? g.outside[0] : corner[0], road + sign * lane]
    : [road + sign * lane, direct ? g.outside[1] : corner[1]];
  return [g.inside, g.threshold, g.outside, ...(!direct ? [corner] : []), exit];
}
/** Empty truck goes from the receiving yard to the warehouse, using street junctions. */
export function truckHomeRoute(recycler: Plot, warehouse: Plot, inside = true) {
  const key = routeKey(recycler, warehouse, `home:${inside}`), saved = cache.get(key); if (saved) return saved;
  const a = depotAccess(recycler, .14), b = depotAccess(warehouse, -.14);
  const start = a.at(-1)!, end = b.at(-1)!;
  const ay = !!depotPortal(recycler).outward[1], by = !!depotPortal(warehouse).outward[1];
  const bends: DepotPoint[] = [];
  if (ay !== by) bends.push(ay ? [end[0], start[1]] : [start[0], end[1]]);
  else if (recycler.layoutId) {
    const roads = ay ? cityLayout(recycler.layoutId).roadX : cityLayout(recycler.layoutId).roadY;
    const axis = ay ? 0 : 1;
    const score = (r: number) => Math.abs(start[axis] - r) + Math.abs(end[axis] - r);
    const cross = roads.reduce((best,r) => score(r) < score(best) ? r : best);
    bends.push(...(ay ? [[cross, start[1]], [cross, end[1]]] : [[start[0], cross], [end[0], cross]]) as DepotPoint[]);
  }
  return keep(key, rounded([...(inside ? a : [recyclingBay(recycler,false), ...a.slice(3)]), ...bends, ...b.reverse()]));
}
const portalDistance = (p: Plot) => {
  const g = depotPortal(p); return Math.hypot(g.outside[0]-g.inside[0],g.outside[1]-g.inside[1]);
};
export const recyclerOpeningLead = (p: Plot, speed: number) => Math.ceil((portalDistance(p)+1.2)/speed)+750;
export function recyclerVisitWindow(j: Job, recycler: Plot) {
  const lead = j.insideUnloading ? recyclerOpeningLead(recycler,j.travelSpeed!) : 0;
  return { start: j.unloadStartAt!-lead, end: j.truckHomeDepartureAt!+lead+700 };
}
export function recyclerGateDemand(j: Job, recycler: Plot, now: number) {
  if (!j.insideUnloading) return {open:false,since:now};
  const lead = recyclerOpeningLead(recycler,j.travelSpeed!);
  const entryAt = j.unloadStartAt!-lead;
  if (now >= entryAt && now < j.unloadStartAt!+200) return {open:true,since:entryAt};
  if (now >= j.truckHomeDepartureAt! && now < j.truckHomeDepartureAt!+lead)
    return {open:true,since:j.truckHomeDepartureAt!};
  return {open:false,since:now};
}
export function warehouseMotion(j: Job, warehouse: Plot, site: Plot, recycler: Plot, kind: EquipmentKind, now: number): ConvoyMotion {
  if (now < j.workEnd) return convoyMotion(j, warehouse, site, kind, now);
  if (kind !== 'truck') return convoyMotion({ ...j, returnEnd: j.toolsReturnEnd! }, warehouse, site, kind, now);
  const inside = j.insideUnloading === true;
  const bay = recyclingBay(recycler,inside), recyclerPortal = portalDistance(recycler)+.7;
  const delivery = truckDeliveryRoute(recycler, site,inside), queueDistance = j.unloadQueueDistance ?? 0;
  const deliveryLength = routeLength(delivery);
  if (queueDistance && now >= j.unloadAt! && now < j.unloadStartAt!) {
    const approach = j.unloadStartAt! - queueDistance / j.travelSpeed!;
    const distance = deliveryLength - queueDistance + Math.max(0, now - approach) * j.travelSpeed!;
    return { position: routePoint(delivery,distance), ahead: routePoint(delivery,distance+.04), visibility: 1,
      moving: now > approach, stage: 'waiting', reverse: false, portal: inside && deliveryLength-distance < recyclerPortal, portalPlotId:recycler.id };
  }
  if (now >= j.unloadAt! && now < j.truckHomeDepartureAt!) {
    return { position: bay, ahead: bay, visibility: inside ? 0 : 1, moving: false, stage: now < j.unloadEnd! ? 'unload' : 'waiting', reverse: false, portal: false };
  }
  const home = now >= j.truckHomeDepartureAt!;
  const points = home ? truckHomeRoute(recycler, warehouse,inside) : delivery;
  const start = home ? j.truckHomeDepartureAt! + (inside ? 750 : 0) : j.workEnd + 550;
  const end = home ? j.returnEnd - 250 : j.unloadAt!;
  const length = routeLength(points), travelLength = home ? length : length - queueDistance;
  const distance = Math.max(0, Math.min(travelLength, travelLength * (now - start) / Math.max(1, end - start)));
  const position = routePoint(points, distance), ahead = routePoint(points, distance + .04);
  const g = depotPortal(warehouse);
  const portalLength = Math.hypot(g.outside[0] - g.inside[0], g.outside[1] - g.inside[1]);
  const factoryPortal = inside && (home ? distance < recyclerPortal : length-distance < recyclerPortal);
  return { position, ahead, visibility: home && (now >= end || (inside && distance < .02)) ? 0 : 1, moving: now > start && now < end,
    stage: home && now >= end ? 'inside' : 'return', reverse: false,
    portal: factoryPortal || (home && length-distance < portalLength+.7), portalPlotId: factoryPortal ? recycler.id : warehouse.id };
}
