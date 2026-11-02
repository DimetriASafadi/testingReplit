/** Physical image calibration, measured from repeated facade floors / doors.
 * PNG bounds include plinths, roof tanks, planting and transparent margins; they
 * are NOT a unit of architectural size. Values refer to the published images.
 * A packing resize must scale both the reference width and facade pitch.
 */
export const STOREY_WORLD_PX = 12;
export const DOOR_WORLD_PX = 8;
type Reference = { width:number; pixels:number; unit:'storey'|'door' };
const floor=(width:number,pixels:number):Reference=>({width,pixels,unit:'storey'});
const door=(width:number,pixels:number):Reference=>({width,pixels,unit:'door'});
const REFERENCE:Record<string,Reference>={
  smallhouse:floor(196,44), house:floor(196,28), villa:floor(196,30),
  apt4:floor(190,23), h6:floor(196,18), complex:floor(196,23),
  modern:floor(196,13), trad:floor(196,45), tower:floor(150,7.3),
  tourist:floor(196,30),
  wood:door(196,22), hshop:door(196,20), foodshop:door(196,21),
  clothes:door(196,21), shoe:door(196,21), falafel:door(196,22),
  shawarma:door(196,22), western:door(211,18), cafe:door(196,22),
  kiosk:door(196,22), luxury:floor(207,28),
  school:floor(196,25), hospital:floor(196,24),
  muni:floor(196,28), tmall:floor(196,25), mmall:floor(196,25),
  mosque:door(196,15), church:door(196,18),
  smarket:door(200,22), toys:door(196,21), juiceshop:door(196,21),
  work:floor(196,26), food_factory:door(196,20), water:door(196,19),
  drinks:door(196,19), juicef:door(196,19),
};
export function architecturalScale(stem:string,imageWidth:number):number{
  const r=REFERENCE[stem];
  return r?(r.unit==='storey'?STOREY_WORLD_PX:DOOR_WORLD_PX)/(r.pixels*imageWidth/r.width):Infinity;
}
export function architecturalReference(stem:string):Readonly<Reference>|undefined{return REFERENCE[stem];}
