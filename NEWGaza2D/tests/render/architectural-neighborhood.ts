/** Actual CityScene plot renderer, isolated state, no storage or gameplay writes.
 * Review adjacent catalog types in a real district, not separate thumbnails. */
import Phaser from 'phaser';
import { CityScene } from '../../src/scenes/CityScene';
import { builtKey,sprUrl,GROUND_KEYS,groundUrl } from '../../src/realart';
import type { Plot } from '../../src/model';
import { preloadFarmCanopies } from '../../src/farm-canopies';
import { newGame } from '../../src/engine';
import { roadsFor } from '../../src/roads';
const state=newGame(Date.now()),district=state.districts[0];
state.currentDistrict=district.id;
district.difficultyChosen=true;
const xs=[...new Set(district.plots.map(p=>p.x))].sort((a,b)=>a-b).slice(0,4);
const ys=[...new Set(district.plots.map(p=>p.y))].sort((a,b)=>a-b).slice(0,4);
const ids=new URLSearchParams(location.search).has('farms')?[
  'housing_tower','housing_6','medium_house','palms',
  'olive','citrus','protective_trees','ornamental_trees',
  'wheat','corn','vegetables','strawberry',
  'cattle','grapes','villa','small_house',
]:[
  'housing_tower','modern_housing','housing_6','housing_4',
  'traditional_housing','medium_house','villa','housing_complex',
  'school','hospital','western_food','cafe',
  'small_house','tourist_villa','mosque','recreation_park',
];
district.plots=district.plots.filter(p=>xs.includes(p.x)&&ys.includes(p.y));
district.plots.sort((a,b)=>a.y-b.y||a.x-b.x).forEach((p,i)=>{
  p.status='built';p.buildingId=ids[i%ids.length];p.orientation=0;
});
district.pavedRoads=roadsFor(district.id).map(r=>r.id);
const width=window.innerWidth,height=window.innerHeight;
const cx=(xs[0]+xs.at(-1)!)/2+1,cy=(ys[0]+ys.at(-1)!)/2+1;
const camera=()=>{
  const scene=game.scene.getScene('city') as CityScene;
  const span=(xs.at(-1)!-xs[0]+ys.at(-1)!-ys[0]+4)*64;
  scene.cameras.main.centerOn((cx-cy)*64,(cx+cy)*32-35)
    .setZoom(width<600?.7:Math.min(.8,(width-30)/span));
  document.documentElement.dataset.ready='true';
  document.getElementById('loading')?.remove();
};
class ReviewCity extends CityScene{
  preload(){
    const keys=new Set([...district.plots.map(p=>builtKey(p.buildingId,p.orientation)),'orn_front','citrus_front','olive_front']);
    for(const key of keys)this.load.image('s:'+key,sprUrl(key));
    for(const key of GROUND_KEYS)this.load.image('g:'+key,groundUrl(key));
    preloadFarmCanopies(this);
  }
  create(){
    // Render only the measured neighborhood. Use the real plot/yard/shadow
    // implementation without unrelated terrain generation or life simulation.
    this.hooks=this.registry.get('hooks');
    const graphics=this.add.graphics().setDepth(-300000);
    const iso=(a:number,b:number)=>new Phaser.Math.Vector2((a-b)*64,(a+b)*32);
    const quad=(a:number,b:number,c:number,d:number)=>{
      graphics.fillStyle(0x39433f).fillPoints([iso(a,b),iso(c,b),iso(c,d),iso(a,d)],true);
      graphics.lineStyle(1.5,0xc8beab).strokePoints([iso(a,b),iso(c,b),iso(c,d),iso(a,d)],true);
    };
    for(let i=0;i<3;i++){
      quad(xs[i]+2,ys[0]-.35,xs[i+1],ys.at(-1)!+2.35);
      quad(xs[0]-.35,ys[i]+2,xs.at(-1)!+2.35,ys[i+1]);
    }
    const actual=this as unknown as {syncPlot(p:Plot,now:number):void};
    for(const p of district.plots)actual.syncPlot(p,Date.now());
    camera();
  }
}
const game=new Phaser.Game({
  type:Phaser.CANVAS,width,height,parent:'render',backgroundColor:'#998b75',
  scene:[ReviewCity],banner:false,audio:{noAudio:true},
  callbacks:{preBoot:g=>g.registry.set('hooks',{
    getState:()=>state,onSelect:()=>{},onCamera:()=>{},
    onWorldState:(phase:string)=>{if(phase==='ready')camera();},
  })},
});
