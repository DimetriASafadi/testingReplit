import test from 'node:test';
import assert from 'node:assert/strict';
import { industrialKit,industrialSites,type IndustrialKit } from '../src/industrial-layout';
import { buildingRenderScale } from '../src/building-footprint';
import manifest from '../src/realistic-manifest.json';

test('water basins use process-equipment scale, not a guessed human door',()=>{
  for(const face of ['front','back','left','right'] as const){
    const water=manifest[`water_${face}`],warehouse=manifest[`equip_${face}`];
    const width=water.ground.w*buildingRenderScale(`water_${face}`,water);
    const depotWidth=warehouse.ground.w*buildingRenderScale(`equip_${face}`,warehouse);
    assert.ok(width>=depotWidth*.8&&width<=depotWidth*1.2,'water facility comparable in width to a depot');
  }
});
test('industrial equipment stays in the free bands with both depot approaches clear',()=>{
  const edge=.84/.72;
  for(const kit of ['water','equipment','recycling','aggregate','metal','glass','lumber','packing','beverage','workers'] as IndustrialKit[]){
    for(const radius of [.45,.72,1.01,1.24]){
      const r=Math.max(.18,radius-.04);
      for(const p of industrialSites(radius,edge,kit)){
        assert.ok(Math.abs(p.u)+p.size/2<edge&&Math.abs(p.v)+p.size/2<edge);
        assert.ok(Math.abs(p.u)-p.size/2>=r||Math.abs(p.v)-p.size/2>=r,'outside calibrated factory core');
        if(kit==='equipment'||kit==='recycling')assert.ok(p.u+p.size/2<0&&p.v+p.size/2<0);
      }
    }
  }
  assert.notEqual(industrialKit('wood_factory'),industrialKit('water_treatment'));
  assert.notEqual(industrialKit('glass'),industrialKit('steel'));
});
