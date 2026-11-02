/** Physical contents stay outside the calibrated plant and away from the full
 * front/right approach. Warehouses and recyclers retain both vehicle lanes. */
export type IndustrialKit='equipment'|'recycling'|'water'|'aggregate'|'metal'|'glass'|'lumber'|'packing'|'beverage'|'workers';
export function industrialKit(id:string):IndustrialKit{
  if(id==='equipment_store')return 'equipment';
  if(id==='recycling')return 'recycling';
  if(id==='water_treatment')return 'water';
  if(id==='cement'||id==='asphalt')return 'aggregate';
  if(id==='steel')return 'metal';
  if(id==='glass')return 'glass';
  if(id==='wood_factory')return 'lumber';
  if(id==='drinks_factory'||id==='juice_factory')return 'beverage';
  if(id==='work')return 'workers';
  return 'packing';
}
export interface IndustrialSite{u:number;v:number;size:number}
export function industrialSites(radius:number,edge:number,kit:IndustrialKit):IndustrialSite[]{
  const r=Math.max(.18,radius-.04),gap=edge-r;
  if(gap<.16)return [];
  const size=Math.min(.30,gap*.70),b=(r+edge)/2;
  const sites:IndustrialSite[]=[];
  const access=kit==='equipment'||kit==='recycling';
  for(const t of access?[-.66,-.28]:[-.66,-.14,.38]){
    sites.push({u:-b,v:t,size},{u:t,v:-b,size});
  }
  if(!access){
    // Loading/storage corners may use the front bands, but leave a broad
    // central pedestrian/receiving lane. Depots never receive these objects.
    for(const t of [-.64,.64])sites.push({u:b,v:t,size},{u:t,v:b,size});
  }
  return sites;
}
