import type { DistrictState, Job } from './model';
import { factoryWindows, nextFactorySlot, routeLength } from './fleet-motion';
import { recyclingDepots } from './fleet-depots';
import { recyclerOpeningLead, recyclerVisitWindow, truckDeliveryRoute, truckHomeRoute, UNLOAD_DURATION } from './warehouse-cycle';

/** Reserve a complete visit, including a truck held inside while its home gate
 * is busy. Selection runs once on dispatch, never in the animation loop. */
export function selectRecyclerVisit(d: DistrictState, jobs: readonly Job[], job: Job) {
  const site = d.plots[job.plotId], warehouse = d.plots[job.originPlotId];
  const speed = job.travelSpeed!, toolsReturnEnd = job.toolsReturnEnd!;
  const homeWindows = jobs.filter(j => j.districtId === d.id && j.originPlotId === warehouse.id)
    .flatMap(j => factoryWindows(j, warehouse, d.plots[j.plotId]));
  homeWindows.push(...factoryWindows({ ...job, returnEnd: toolsReturnEnd }, warehouse, site));
  const candidates = recyclingDepots(d).map(depot => {
    const distance = routeLength(truckDeliveryRoute(depot, site));
    const arrival = job.workEnd + 550 + Math.ceil(distance / speed);
    const lead = recyclerOpeningLead(depot, speed);
    const windows = jobs.filter(j => j.districtId === d.id && (j.recyclingPlotId ?? j.originPlotId) === depot.id)
      .flatMap(j => j.recyclingPlotId !== undefined ? [recyclerVisitWindow(j, depot)]
        : factoryWindows(j, depot, d.plots[j.plotId]).slice(1));
    const homeDuration = Math.ceil(routeLength(truckHomeRoute(depot, warehouse)) / speed) + 1000;
    const homeGateDuration = Math.ceil(6.2 / speed) + 950;
    let unloadStartAt = arrival, truckHomeDepartureAt: number;
    // Each retry advances past at least one reservation. Recalculate the home
    // wait too: extending a visit must not overlap another truck's reservation.
    for (;;) {
      const incomingAt = unloadStartAt + UNLOAD_DURATION + homeDuration - homeGateDuration;
      const homeWait = Math.max(0, nextFactorySlot(Math.max(incomingAt, toolsReturnEnd + 850),
        homeGateDuration, homeWindows) - incomingAt);
      truckHomeDepartureAt = unloadStartAt + UNLOAD_DURATION + homeWait;
      const entry = unloadStartAt - lead;
      const next = nextFactorySlot(entry, truckHomeDepartureAt + lead + 700 - entry, windows) + lead;
      if (next === unloadStartAt) break;
      unloadStartAt = next;
    }
    let unloadAt = arrival;
    let unloadQueueDistance: number | undefined;
    if (unloadStartAt > arrival) {
      const queued = windows.filter(w => w.end > arrival).length;
      unloadQueueDistance = Math.min(lead * speed + 1.7 * Math.max(1, queued), distance - .4);
      unloadAt -= Math.ceil(unloadQueueDistance / speed);
    }
    return { depot, distance, waiting: unloadStartAt > arrival,
      timing: { recyclingPlotId: depot.id, unloadAt, unloadStartAt,
        ...(unloadQueueDistance === undefined ? {} : { unloadQueueDistance }),
        unloadEnd: unloadStartAt + UNLOAD_DURATION, truckHomeDepartureAt,
        returnEnd: Math.max(toolsReturnEnd, truckHomeDepartureAt + homeDuration) } };
  });
  // Prefer the nearest street route with no queue on arrival. If every bay is
  // reserved, choose the first usable slot, with distance/ID as stable ties.
  candidates.sort((a, b) => Number(a.waiting) - Number(b.waiting) ||
    (a.waiting ? a.timing.unloadStartAt - b.timing.unloadStartAt : 0) ||
    a.distance - b.distance || a.depot.id - b.depot.id);
  return candidates[0] as (typeof candidates)[number] | undefined;
}
