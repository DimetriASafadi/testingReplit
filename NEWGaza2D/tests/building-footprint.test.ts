import test from 'node:test';
import assert from 'node:assert/strict';
import { buildingFootprint, buildingLotDimensions, buildingMaxHeight, buildingRenderScale,buildingYardRadius } from '../src/building-footprint';
import { architecturalReference,architecturalScale,STOREY_WORLD_PX } from '../src/architectural-scale';
import { courtyardZones } from '../src/courtyard-layout';
import { LAND_WORLD_EDGE, PLOT_WORLD_W } from '../src/lot-geometry';
import manifest from '../src/realistic-manifest.json';

test('compact houses leave usable land while apartments and factories keep their full footprint', () => {
  assert.equal(buildingFootprint('smallhouse_front').factor, .62);
  assert.ok(buildingFootprint('house_front').factor > buildingFootprint('smallhouse_front').factor);
  assert.ok(buildingFootprint('villa_front').factor > buildingFootprint('house_front').factor);
  for (const key of ['apt4_front','h6_front','complex_front','modern_front','tower_front','recycling_front','steel_front','olive_front']) {
    assert.deepEqual(buildingFootprint(key), { factor: 1, yard: 'none' }, key);
  }
  assert.equal(buildingFootprint('smallhouse_front').yard, 'garden');
  assert.equal(buildingFootprint('kiosk_front').yard, 'patio');
});
test('published four-face sprites share parcel/scale across every family', () => {
  const sprites = manifest as Record<string, { w: number; h: number; ay: number; ground?: { w: number; h: number } }>;
  const fronts = Object.keys(sprites).filter(k => k.endsWith('_front') && !['ind_front', 'shop_front'].includes(k));
  fronts.push('farm_cattle');
  assert.equal(fronts.length, 56); // Six new campaign building families.
  const pending: string[] = [];
  for (const front of fronts) {
    const stem = front === 'farm_cattle' ? 'cattle' : front.replace(/_front$/, '');
    const names = [front, ...['right', 'back', 'left'].map(face => `${stem}_${face}`)];
    if (!sprites[names[1]] || !sprites[names[3]]) {
      pending.push(stem);
      for (const key of [front, `${stem}_back`]) {
        assert.ok(sprites[key], key);
        assert.ok(sprites[key].w <= 512 && sprites[key].h <= 512, key);
      }
      continue;
    }
    for (const key of names) {
      assert.ok(sprites[key], key);
      assert.ok(sprites[key].w > 0 && sprites[key].h > 0 && sprites[key].w <= 512 && sprites[key].h <= 512, key);
      assert.ok(sprites[key].ay > 0 && sprites[key].ay <= 1, key);
      assert.deepEqual(buildingFootprint(key), buildingFootprint(front), key);
      assert.equal(buildingMaxHeight(key), buildingMaxHeight(front), key);
      assert.equal(sprites[key].ground?.w, sprites[front].ground?.w, key);
      assert.equal(sprites[key].ground?.h, sprites[front].ground?.h, key);
      assert.equal(buildingRenderScale(key, sprites[key]), buildingRenderScale(front, sprites[front]), key);
    }
  }
  assert.deepEqual(pending, []);
});
test('single-floor retail has modest roof height and furnished land in both orientations',()=>{
  for(const stem of ['foodshop','clothes','shoe','falafel','shawarma','western','cafe','kiosk']){
    assert.ok(buildingMaxHeight(stem+'_front')<=86);
    assert.equal(buildingMaxHeight(stem+'_front'),buildingMaxHeight(stem+'_back'));
    assert.equal(buildingFootprint(stem+'_front').yard,'patio');
    assert.deepEqual(buildingFootprint(stem+'_front'),buildingFootprint(stem+'_back'));
    assert.ok(buildingMaxHeight(stem+'_front')<buildingMaxHeight('apt4_front'));
  }
  assert.equal(buildingFootprint('muni_back').yard,'service');
});
test('rotation preserves house scale and landscape category', () => {
  for (const stem of ['smallhouse','house','villa','tourist','kiosk','apt4','h6']) {
    assert.deepEqual(buildingFootprint(`${stem}_front`), buildingFootprint(`${stem}_back`));
    const f = buildingFootprint(`${stem}_front`).factor;
    assert.ok(f > 0 && f <= 1);
  }
});
test('every finished face fits the dressed parcel on both ground axes and respects structural height',()=>{
  const sprites=manifest as Record<string,{w:number;h:number;ay:number;ground?:{w:number;h:number}}>;
  for(const [key,m] of Object.entries(sprites)){
    if(!/_(front|back|right|left)$/.test(key)&&key!=='farm_cattle')continue;
    const scale=buildingRenderScale(key,m),target=buildingLotDimensions(key);
    const gw=m.ground?.w??m.w,gh=m.ground?.h??gw/2;
    assert.ok(gw*scale<=target.width+.001,key+' width');
    assert.ok(gh*scale<=target.height+.001,key+' depth');
    assert.ok(gw*scale<=PLOT_WORLD_W*LAND_WORLD_EDGE+.001,key+' parcel width');
    assert.ok(gh*scale<=PLOT_WORLD_W*LAND_WORLD_EDGE/2+.001,key+' parcel depth');
    assert.ok(Math.max(1,m.h*m.ay-gh/2)*scale<=buildingMaxHeight(key)+.001,key+' roof');
  }
});
test('parks, sports fields and farms occupy most of their plot rather than a small central stamp',()=>{
  const sprites=manifest as Record<string,{w:number;h:number;ay:number;ground?:{w:number;h:number}}>;
  for(const stem of ['rpark','club','zoo','wheat','citrus','olive','veg','straw','cattle','palms','corn','trees','orn','grapes']){
    for(const face of ['front','right','back','left']){
      const key=stem==='cattle'&&face==='front'?'farm_cattle':`${stem}_${face}`,m=sprites[key];
      const scale=buildingRenderScale(key,m);
      const farm=!['rpark','club','zoo'].includes(stem);
      assert.ok((m.ground?.w??m.w)*scale>=PLOT_WORLD_W*(farm?.80:.85),key+' coverage');
      if(farm){
        const halfEdge=(m.ground?.w??m.w)*scale/PLOT_WORLD_W;
        assert.ok(halfEdge<=.82+.00001,key+' inside rounded kerb');
        // Production pavement: .12 inset, .2 corner radius. Checking just
        // the rectangular sides missed the soil overhang at the round corners.
        const cornerCentre=1-.12-.2;
        assert.ok(Math.hypot(halfEdge-cornerCentre,halfEdge-cornerCentre)<=.2,
          key+' soil corner remains within the curved pavement');
      }
    }
  }
});
test('restaurant sizes reflect their use while small-house gardens keep their accepted proportions',()=>{
  assert.ok(buildingLotDimensions('western_front').width>buildingLotDimensions('falafel_front').width);
  assert.ok(buildingLotDimensions('luxury_front').width>buildingLotDimensions('western_front').width);
  const m=manifest.western_front,scale=buildingRenderScale('western_front',m);
  assert.ok(m.ground.w*scale>=PLOT_WORLD_W*.30);
  assert.ok(buildingLotDimensions('smallhouse_front').width<PLOT_WORLD_W*.35);
  assert.ok(buildingLotDimensions('villa_front').width>buildingLotDimensions('smallhouse_front').width);
});
test('facade units, not equal PNG width, establish a shared human/architectural scale',()=>{
  const sprites=manifest as Record<string,{w:number;h:number;ay:number;ground?:{w:number;h:number}}>;
  const rises:Record<string,number>={};
  for(const stem of ['smallhouse','house','villa','apt4','h6','complex','modern','trad','tower','tourist']){
    const m=sprites[`${stem}_front`],scale=buildingRenderScale(`${stem}_front`,m),r=architecturalReference(stem)!;
    const floorPitch=r.pixels*m.w/r.width*scale;
    assert.ok(floorPitch>=STOREY_WORLD_PX*.85&&floorPitch<=STOREY_WORLD_PX*1.001,stem+' actual floor pitch');
    rises[stem]=(m.h*m.ay-(m.ground?.h??m.w/2)/2)*scale;
    assert.equal(architecturalScale(stem,m.w),architecturalScale(stem,m.w*2)*2);
  }
  assert.ok(rises.trad<rises.apt4,'three-floor facade cannot dominate the four-floor block');
  assert.ok(rises.apt4<rises.h6&&rises.h6<rises.modern&&rises.modern<rises.tower);
  assert.ok(rises.tower>rises.trad*3,'high-rise must visibly read as much taller than three floors');
});
test('courtyard furniture bands stay inside the parcel and outside the actual reserved building',()=>{
  for(const key of ['trad_front','apt4_front','smallhouse_front','house_front','h6_front','tower_front','western_front']){
    const radius=buildingYardRadius(key),zones=courtyardZones(radius,1.30555556);
    if(key==='trad_front'||key==='smallhouse_front')assert.equal(zones.length,4,key+' uses all four free bands');
    for(const z of zones){
      assert.ok(z.x0>=-1.30556&&z.x1<=1.30556&&z.y0>=-1.30556&&z.y1<=1.30556);
      assert.ok(z.x0<z.x1&&z.y0<z.y1);
      assert.ok(z.x1<=-radius||z.x0>=radius||z.y1<=-radius||z.y0>=radius,key+' core clearance');
    }
    assert.equal(radius,buildingYardRadius(key.replace('_front','_right')));
  }
});
test('packing resolution does not change world dimensions of a sprite family',()=>{
  // An unknown family lets the supplied metadata be the sole face. Doubling
  // pixels and the ground diamond must halve scale, not enlarge the building.
  const m={w:196,h:168,ay:119/168,ground:{w:196,h:98}};
  const doubled={w:m.w*2,h:m.h*2,ay:m.ay,ground:{w:m.ground.w*2,h:m.ground.h*2}};
  assert.equal(buildingRenderScale('sample_front',m),buildingRenderScale('sample_front',doubled)*2);
});
