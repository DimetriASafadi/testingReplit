/** Four non-overlapping, function-specific bands outside the actual building.
 * Pure geometry; no per-frame work and no plot-specific texture variants. */
export type CourtyardZone={kind:'garden'|'terrace'|'parking';x0:number;y0:number;x1:number;y1:number};
export function courtyardZones(radius:number,edge:number):CourtyardZone[]{
  const e=edge-.08,r=radius+.08;
  if(e-r<.24)return [];
  return [
    {kind:'garden',x0:-e,y0:-e,x1:e,y1:-r},
    {kind:'garden',x0:-e,y0:-r+.04,x1:-r,y1:e},
    {kind:'terrace',x0:r,y0:-r+.04,x1:e,y1:r-.04},
    {kind:'parking',x0:-r+.04,y0:r,x1:e,y1:e},
  ];
}
