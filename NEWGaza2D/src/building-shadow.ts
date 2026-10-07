import type Phaser from 'phaser';
import { makeCanvas } from './art';
import { SPR } from './realart';

/** One fixed sun for every pose. Shadows derive from silhouettes, not a
 * directionless oval, and are cached once per final sprite, never per frame. */
export const CITY_SHADOW = { dx: .56, dy: .28 } as const;
export function buildingShadow(scene:Phaser.Scene,raw:string){
  const key=`building-shadow:${raw}`,m=SPR[raw];
  if(!m)throw new Error(`Missing shadow silhouette: ${raw}`);
  const ground=m.h*m.ay,pad=12,ox=pad+m.w/2,oy=pad+(m.ground?.h??m.w/2)*.14;
  const w=Math.ceil(m.w+ground*CITY_SHADOW.dx+pad*2);
  const h=Math.ceil(ground*CITY_SHADOW.dy+oy+pad);
  if(!scene.textures.exists(key)){
    const image=scene.textures.get('s:'+raw).getSourceImage();
    const body=makeCanvas(m.w,Math.ceil(ground)),b=body.getContext('2d')!;
    b.drawImage(image as HTMLImageElement,0,0);
    b.globalCompositeOperation='source-in';b.fillStyle='rgba(48,47,38,.23)';b.fillRect(0,0,body.width,body.height);
    const canvas=makeCanvas(w,h),c=canvas.getContext('2d')!;
    c.save();c.filter='blur(2.5px)';
    c.setTransform(1,0,-CITY_SHADOW.dx,-CITY_SHADOW.dy,
      ox-m.w/2+ground*CITY_SHADOW.dx,oy+ground*CITY_SHADOW.dy);
    c.drawImage(body,0,0);c.restore();
    c.fillStyle='rgba(45,43,35,.13)';c.beginPath();
    c.ellipse(ox,oy,m.w*.29,(m.ground?.h??m.w/2)*.14,0,0,Math.PI*2);c.fill();
    scene.textures.addCanvas(key,canvas);
  }
  return {key,originX:ox/w,originY:oy/h};
}
