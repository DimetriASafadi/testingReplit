/** Disposable browser smoke fixture. Bundle only for a local verification route,
 * never into public production assets. No GameStore or user save access. */
import Phaser from 'phaser';
import { RoadAnimation } from '../../src/road-animation';
import { newGame } from '../../src/engine';
import { roadsFor, streetFor } from '../../src/roads';
import { paverTexture } from '../../src/expansion-art';

const epoch=1800000000000,d=newGame(epoch).districts[0],all=roadsFor(d.id);
const streets=[0,2,4].map(index=>streetFor(d.id,all.find(r=>r.id.startsWith(`x-${index}-`))!.id));
const unfinished=new Set(streets.flat().map(r=>r.id));
d.pavedRoads=all.filter(r=>!unfinished.has(r.id)).map(r=>r.id);
d.roadWorks=streets.map((street,i)=>({
  roadId:street[0].id,roadIds:street.map(r=>r.id),machineId:`paver-${'ABC'[i]}`,factoryId:0,
  start:i?epoch-10000001000:epoch-11000,arrival:i?epoch-10000000000:epoch-10000,
  workEnd:i?epoch+10000000000:epoch+10000,returnEnd:epoch+20000000000,
  route:[street[0].a,street.at(-1)!.b],
}));

class Review extends Phaser.Scene {
  create(){
    const placeholder=(key:string)=>{
      const c=document.createElement('canvas');c.width=c.height=8;
      const ctx=c.getContext('2d')!;ctx.fillStyle='#f5bb35';ctx.fillRect(0,0,8,8);
      this.textures.addCanvas(key,c);
    };
    for(let i=0;i<4;i++){placeholder(`exp:tent${i}`);placeholder(paverTexture(this,i*Math.PI/2));}
    this.cameras.main.setZoom(.25).centerOn(0,900);
    const updates=new Map<string,number>(),creates=new Map<string,number>();
    let uploadsThisStep=0,maxUploads=0;
    const create=this.textures.createCanvas.bind(this.textures);
    this.textures.createCanvas=(key,width,height)=>{
      const texture=create(key,width,height)!;
      creates.set(key,(creates.get(key)??0)+1);
      const refresh=texture.refresh.bind(texture);
      texture.refresh=()=>{
        uploadsThisStep++;updates.set(key,(updates.get(key)??0)+1);return refresh();
      };
      return texture;
    };
    const road=new RoadAnimation(this);
    const step=(now:number)=>{uploadsThisStep=0;road.update(d,now);maxUploads=Math.max(maxUploads,uploadsThisStep);};
    const count=(map:Map<string,number>,owner:string)=>[...map].filter(([k])=>k.includes(`:paver-${owner}:`)).reduce((sum,[,n])=>sum+n,0);
    for(let i=0;i<200;i++)step(epoch+i*33);
    const beforeA={B:count(updates,'B'),C:count(updates,'C')};
    d.pavedRoads.push(...streets[0].map(r=>r.id));
    for(let i=0;i<80;i++)step(epoch+10001+i*33);
    const duringA={B:count(updates,'B')-beforeA.B,C:count(updates,'C')-beforeA.C};
    const beforeB={A:count(creates,'A'),C:count(updates,'C')};
    d.roadWorks![1].workEnd=epoch+20000;
    d.pavedRoads.push(...streets[1].map(r=>r.id));
    for(let i=0;i<80;i++)step(epoch+20001+i*33);
    const duringB={A:count(creates,'A')-beforeB.A,C:count(updates,'C')-beforeB.C};
    const passed=duringA.B===0&&duringA.C===0&&duringB.A===0&&duringB.C===0&&maxUploads<=1;
    document.querySelector('#stats')!.textContent=
      `${passed?'PASS':'FAIL'} | A completes: B/C unrelated surface uploads ${duringA.B}/${duringA.C}. `+
      `B completes: returning A allocations ${duringB.A}, C unrelated uploads ${duringB.C}. `+
      `Peak shared surface uploads/update: ${maxUploads}. Renderer: ${this.game.renderer.type===Phaser.WEBGL?'WebGL':'Canvas'}. `+
      'Frozen-progress fixture; this checks invalidation, not handset FPS.';
    if(!passed)throw new Error('Concurrent paver surface invalidation regression');
  }
}
new Phaser.Game({type:Phaser.AUTO,width:780,height:520,parent:'render',backgroundColor:'#998b75',
  scene:Review,banner:false,audio:{noAudio:true}});
