import type Phaser from 'phaser';
import { U } from './art';
import type { RoadSegment } from './roads';

type XY = { x: number; y: number };
const iso = (x: number, y: number): XY => ({ x: (x - y) * U, y: (x + y) * U / 2 });
/** Rasterize each short road section, never a full-street/mobile-oversized texture. */
export function surfaceBounds(r: RoadSegment) {
  const a=iso(...r.a),b=iso(...r.b);
  const x=Math.floor(Math.min(a.x,b.x)-.8*U)-4, y=Math.floor(Math.min(a.y,b.y)-.4*U)-4;
  return {x,y,width:Math.ceil(Math.max(a.x,b.x)+.8*U)-x+4,
    height:Math.ceil(Math.max(a.y,b.y)+.4*U)-y+4};
}

/** Small Canvas painter matching only the ground operations the road art uses.
 * Thousands of grains become ONE ordinary sprite, not thousands of Graphics
 * commands replayed and tessellated on every Canvas/WebGL render. */
export class SurfaceBrush {
  constructor(private c: CanvasRenderingContext2D) {}
  fillStyle(color: number, alpha = 1) {
    this.c.fillStyle = `rgba(${color>>16&255},${color>>8&255},${color&255},${alpha})`; return this;
  }
  lineStyle(width: number, color: number, alpha = 1) {
    this.c.lineWidth = width; this.c.strokeStyle = `rgba(${color>>16&255},${color>>8&255},${color&255},${alpha})`; return this;
  }
  private path(points: XY[], close: boolean) {
    this.c.beginPath();
    points.forEach((p,i) => i ? this.c.lineTo(p.x,p.y) : this.c.moveTo(p.x,p.y));
    if(close)this.c.closePath();
  }
  fillPoints(points: XY[], close: boolean) { this.path(points,close);this.c.fill();return this; }
  strokePoints(points: XY[], close: boolean) { this.path(points,close);this.c.stroke();return this; }
  fillRect(x:number,y:number,w:number,h:number) { this.c.fillRect(x,y,w,h);return this; }
  fillEllipse(x:number,y:number,w:number,h:number) {
    this.c.beginPath();this.c.ellipse(x,y,w/2,h/2,0,0,Math.PI*2);this.c.fill();return this;
  }
  lineBetween(x:number,y:number,x2:number,y2:number) {
    this.c.beginPath();this.c.moveTo(x,y);this.c.lineTo(x2,y2);this.c.stroke();return this;
  }
}

type Tile = { key:string; image:Phaser.GameObjects.Image; texture:Phaser.Textures.CanvasTexture;
  stamp:string; at:number; x:number; y:number; width:number; height:number };
let nextCache = 0;
export class RoadSurfaceCache {
  private tiles = new Map<string,Tile>();
  private live = new Set<string>();
  private prefix = `road-surface:${nextCache++}:`;
  constructor(private scene:Phaser.Scene,private depth:number) {}
  begin() { this.live.clear(); }
  /** Hold existing mat during the incremental finished-coat handover. */
  retain(){for(const id of this.tiles.keys())this.live.add(id);}
  paint(id:string,r:RoadSegment,stamp:string,now:number,draw:(brush:SurfaceBrush)=>void) {
    this.live.add(id);
    let t = this.tiles.get(id);
    const b = t ?? surfaceBounds(r), v = this.scene.cameras.main.worldView;
    const visible = b.x+b.width>=v.x && b.x<=v.right && b.y+b.height>=v.y && b.y<=v.bottom;
    if(t)t.image.setVisible(visible);
    if(!visible)return;
    if(!t) {
      if(b.width>1024||b.height>1024)throw new Error('Road surface must be split into short sections');
      const key = this.prefix+id, texture = this.scene.textures.createCanvas(key,b.width,b.height)!;
      t = { key,texture,image:this.scene.add.image(b.x,b.y,key).setOrigin(0,0).setDepth(this.depth),
        stamp:'',at:-Infinity,x:b.x,y:b.y,width:b.width,height:b.height };
      this.tiles.set(id,t);
    }
    if(t.stamp===stamp || (now>=t.at && now-t.at<200))return;
    const c = t.texture.getContext();
    c.setTransform(1,0,0,1,0,0);c.clearRect(0,0,t.texture.width,t.texture.height);
    c.setTransform(1,0,0,1,-t.x,-t.y);draw(new SurfaceBrush(c));
    t.texture.refresh();t.stamp=stamp;t.at=now;
  }
  end() {
    for(const [id,t] of this.tiles)if(!this.live.has(id)) {
      t.image.destroy();this.scene.textures.remove(t.key);this.tiles.delete(id);
    }
  }
  reset() { this.live.clear();this.end(); }
}
