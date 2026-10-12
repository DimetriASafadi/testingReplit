import { AUDIO_CLIPS, selectVoices, spatialMix, type AudioClip, type AudioFocus, type AudioSource } from './audio-mix';
import { BUILDINGS } from './catalog';
import type { GameState, Plot } from './model';
import { districtEnvironment } from './district-environment';
import { ThemeMusic, type MusicPhase } from './theme-music';

interface Preferences { enabled: boolean; master: number; effects: number; ambience: number }
interface Voice { source: AudioBufferSourceNode; gain: GainNode; pan: StereoPannerNode; filter: BiquadFilterNode }
export interface FleetSound { id: string; kind: 'truck'|'excavator'|'bulldozer'|'paver'; x: number; y: number; working: boolean }
const PREF_KEY='newgaza2d-audio-v1', DEFS=new Map(BUILDINGS.map(b=>[b.id,b]));
const ground=(p: Plot)=>({x:(p.x-p.y)*64,y:(p.x+p.y+2)*32});
const defaults=():Preferences=>({enabled:true,master:.7,effects:.8,ambience:.5});

export class GameAudio {
  prefs=defaults();
  readonly theme = new ThemeMusic(`${import.meta.env.BASE_URL}audio/victory-theme.mp3?v=796f14edbb29`);
  status='المؤثرات تبدأ بعد أول لمسة';
  private context?: AudioContext;
  private master?: GainNode;
  private compressor?: DynamicsCompressorNode;
  private buffers=new Map<AudioClip,AudioBuffer>();
  private voices=new Map<string,Voice>();
  private loading?: Promise<void>;
  private nextAt=0;
  private district: string|null=null;
  private previous=new Map<string,string>();
  private hidden=false;
  private uiAt=0;
  private completeAt=-10;
  private oneShots=new Set<AudioScheduledSourceNode>();
  constructor(){
    try{
      const p=JSON.parse(localStorage.getItem(PREF_KEY)??'null');
      if(p){this.prefs.enabled=typeof p.enabled==='boolean'?p.enabled:true;
        for(const key of ['master','effects','ambience'] as const)
          if(typeof p[key]==='number'&&Number.isFinite(p[key]))this.prefs[key]=Math.max(0,Math.min(1,p[key]));}
    }catch{this.status='تعذّر قراءة إعدادات الصوت؛ استُخدمت الإعدادات الافتراضية';}
    this.theme.setMaster(this.prefs.master);
    this.theme.setEnabled(this.prefs.enabled);
    if(this.prefs.enabled)this.theme.autoplay();
    document.addEventListener('pointerdown',()=>{void this.unlock();},{capture:true});
    document.addEventListener('visibilitychange',()=>{
      this.hidden=document.hidden;
      if(this.hidden){this.theme.pause();this.stopAll();void this.context?.suspend();}
      else if(this.prefs.enabled)void this.unlock();
    });
  }
  async unlock(){
    if(!this.prefs.enabled||this.hidden)return;
    try{
      if(!this.context){
        this.context=new AudioContext();
        this.master=this.context.createGain();this.compressor=this.context.createDynamicsCompressor();
        this.compressor.threshold.value=-18;this.compressor.ratio.value=3;
        this.compressor.connect(this.master);this.master.connect(this.context.destination);
        this.master.gain.value=this.prefs.master;
      }
      this.theme.start(this.context);
      await this.context.resume();
      if(!this.loading){
        this.status='جاري تحميل المؤثرات…';
        this.loading=this.load();
      }
      await this.loading;
    }catch(e){this.status='تعذّر تشغيل الصوت؛ اضغط إعادة المحاولة';console.warn('2D audio:',e);}
  }
  private async load(){
    const results=await Promise.allSettled(AUDIO_CLIPS.map(async key=>{
      const response=await fetch(`${import.meta.env.BASE_URL}audio/${key}.mp3?v=new-2d-1`);
      if(!response.ok)throw new Error(`Missing audio ${key}: ${response.status}`);
      const buffer=await this.context!.decodeAudioData(await response.arrayBuffer());
      // Level matching once at load time, never in the rendering loop.
      let peak=0;for(let c=0;c<buffer.numberOfChannels;c++){
        const data=buffer.getChannelData(c);for(let i=0;i<data.length;i++)peak=Math.max(peak,Math.abs(data[i]));
      }
      if(peak<.00001)throw new Error(`Silent audio: ${key}`);
      const factor=Math.min(3,.75/peak);
      for(let c=0;c<buffer.numberOfChannels;c++){const data=buffer.getChannelData(c);for(let i=0;i<data.length;i++)data[i]*=factor;}
      if(key==='complete')this.buffers.set(key,buffer);
      else{
        // Join the tail to the head offline; MP3 padding or generated seams
        // must not become a click every six seconds.
        const overlap=Math.min(Math.round(buffer.sampleRate*.06),Math.floor(buffer.length/8));
        const loop=this.context!.createBuffer(buffer.numberOfChannels,buffer.length-overlap,buffer.sampleRate);
        for(let c=0;c<buffer.numberOfChannels;c++){
          const input=buffer.getChannelData(c),output=loop.getChannelData(c);
          output.set(input.subarray(0,loop.length));
          for(let i=0;i<overlap;i++){const t=i/overlap;output[i]=input[i]*t+input[buffer.length-overlap+i]*(1-t);}
        }
        this.buffers.set(key,loop);
      }
    }));
    const failed=results.filter(r=>r.status==='rejected');
    if(failed.length){console.warn('Audio files failed:',failed);this.status=`تعذّر تحميل ${failed.length} مؤثر؛ أعد المحاولة`;}
    else this.status='الصوت جاهز';
  }
  retry(){this.theme.retry();this.loading=undefined;void this.unlock();}
  musicPhase(phase:MusicPhase){this.theme.setPhase(phase);}
  due(){return performance.now()>=this.nextAt;}
  setPreference(key:keyof Preferences,value:number|boolean){
    if(key==='enabled')this.prefs.enabled=Boolean(value);
    else this.prefs[key]=Math.max(0,Math.min(1,Number(value)));
    try{localStorage.setItem(PREF_KEY,JSON.stringify(this.prefs));}catch{this.status='تعذّر حفظ إعدادات الصوت';}
    if(key==='enabled')this.theme.setEnabled(this.prefs.enabled);
    if(key==='master')this.theme.setMaster(this.prefs.master);
    this.master?.gain.setTargetAtTime(this.prefs.enabled?this.prefs.master:0,this.context!.currentTime,.035);
    if(!this.prefs.enabled)this.stopAll();else void this.unlock();
  }
  private available(){return this.prefs.enabled&&!this.hidden&&this.context?.state==='running';}
  private stopAll(){
    for(const id of [...this.voices.keys()])this.stop(id);
    for(const source of this.oneShots){this.oneShots.delete(source);source.stop();}
  }
  private stop(id:string){
    const v=this.voices.get(id);if(!v)return;this.voices.delete(id);
    v.gain.gain.setTargetAtTime(0,this.context!.currentTime,.08);
    v.source.stop(this.context!.currentTime+.4);
  }
  private mix(sources:AudioSource[],focus:AudioFocus){
    if(!this.available()){this.stopAll();return;}
    const ctx=this.context!,selected=selectVoices(sources,focus,this.prefs.ambience,this.prefs.effects);
    const keep=new Set(selected.map(s=>s.id));
    for(const id of this.voices.keys())if(!keep.has(id))this.stop(id);
    for(const s of selected){
      let v=this.voices.get(s.id);
      if(!v){
        const buffer=this.buffers.get(s.clip);if(!buffer)continue;
        const source=ctx.createBufferSource(),gain=ctx.createGain(),pan=ctx.createStereoPanner(),filter=ctx.createBiquadFilter();
        source.buffer=buffer;source.loop=true;gain.gain.value=0;
        filter.type='lowpass';source.connect(gain);gain.connect(pan);pan.connect(filter);filter.connect(this.compressor!);
        source.onended=()=>{source.disconnect();gain.disconnect();pan.disconnect();filter.disconnect();};
        source.start(0,((s.x+s.y)%buffer.duration+buffer.duration)%buffer.duration);
        v={source,gain,pan,filter};this.voices.set(s.id,v);
      }
      v.gain.gain.setTargetAtTime(s.gain,ctx.currentTime,.12);
      v.pan.pan.setTargetAtTime(s.pan,ctx.currentTime,.1);
      v.filter.frequency.setTargetAtTime(s.cutoff,ctx.currentTime,.15);
    }
  }
  mode(state:GameState){
    if(this.district!==state.currentDistrict){
      this.district=state.currentDistrict;this.previous.clear();this.stopAll();this.nextAt=0;
    }
    if(!state.currentDistrict)this.mix([{id:'menu-breeze',clip:'coast',x:0,y:0,volume:.13,ambient:true}],{x:0,y:0,zoom:1.6});
  }
  city(state:GameState,plots:Plot[],fleet:FleetSound[],focus:AudioFocus,now:number){
    const clock=performance.now();if(clock<this.nextAt)return;this.nextAt=clock+100;
    const d=state.districts.find(d=>d.id===state.currentDistrict);if(!d)return;
    const sources:AudioSource[]=[];
    const groups=new Map<AudioClip,AudioSource>();
    for(const p of plots){
      const pos=ground(p),key=`plot:${p.id}`,last=this.previous.get(key);
      if(last==='building'&&p.status==='built')this.complete(pos,focus);
      this.previous.set(key,p.status);
      const def=DEFS.get(p.buildingId);
      const plants=def?.category==='farm'||['ornamental_trees','protective_trees','zoo'].includes(p.buildingId);
      if(p.status==='building')sources.push({id:key,clip:plants?'farm':'construction',...pos,volume:.22,ambient:plants});
      if(p.status==='built'){
        const clip:AudioClip=plants?'farm':def?.category==='industry'||p.buildingId==='recycling'?'industry':'neighborhood';
        const s:AudioSource={id:`built:${clip}`,clip,...pos,volume:clip==='industry'?.14:.1,ambient:true};
        const old=groups.get(clip);if(!old||spatialMix(s,focus).gain>spatialMix(old,focus).gain)groups.set(clip,s);
      }
    }
    sources.push(...groups.values());
    for(const a of fleet){
      sources.push({id:`engine:${a.id}`,clip:a.kind==='paver'?'bulldozer':a.kind,x:a.x,y:a.y,volume:a.working&&a.kind==='truck'?.075:.22});
      if(a.working&&a.kind==='paver')sources.push({id:`paving:${a.id}`,clip:'construction',x:a.x,y:a.y,volume:.13});
      if(a.working&&a.kind!=='truck'&&a.kind!=='paver'){
        const job=state.jobs.find(j=>j.unitIds.includes(a.id));
        const phase=((Math.max(0,now-(job?.arrival??now)-900))%7200)/7200;
        sources.push({id:`rubble:${a.id}`,clip:'rubble',x:a.x,y:a.y,volume:phase<.42||phase>.68?.2:.04});
      }
    }
    for(const j of state.jobs.filter(j=>j.districtId===d.id)){
      const key=`job:${j.id}`,status=j.cleared?'done':'work';
      if(this.previous.get(key)==='work'&&j.cleared){const p=d.plots.find(p=>p.id===j.plotId);if(p)this.complete(ground(p),focus);}
      this.previous.set(key,status);
    }
    for(const p of d.projects){
      const key=`project:${p.id}`,old=this.previous.get(key);
      if(old==='building'&&(p.status==='ready'||p.status==='complete'))this.complete(focus,focus);
      this.previous.set(key,p.status);
    }
    const profile=districtEnvironment(d.id);
    sources.push({id:'district-air',clip:profile.style==='coastal'?'coast':profile.style==='agricultural'?'farm':'neighborhood',
      x:focus.x,y:focus.y,volume:.065,ambient:true});
    this.mix(sources,focus);
  }
  private complete(pos:{x:number;y:number},focus:AudioFocus){
    const gain=spatialMix({id:'complete',clip:'complete',...pos,volume:.35},focus).gain*this.prefs.effects;
    const buffer=this.buffers.get('complete');if(!this.available()||!buffer||gain<.001||this.context!.currentTime-this.completeAt<.25)return;
    this.completeAt=this.context!.currentTime;
    const source=this.context!.createBufferSource(),g=this.context!.createGain(),pan=this.context!.createStereoPanner();
    source.buffer=buffer;g.gain.value=gain;pan.pan.value=Math.max(-.8,Math.min(.8,(pos.x-focus.x)/550));
    source.connect(g);g.connect(pan);pan.connect(this.compressor!);
    this.oneShots.add(source);
    source.onended=()=>{this.oneShots.delete(source);source.disconnect();g.disconnect();pan.disconnect();};source.start();
  }
  click(success=false){
    if(!this.available()||this.prefs.effects<=.001||this.context!.currentTime-this.uiAt<.07)return;
    const ctx=this.context!;this.uiAt=ctx.currentTime;
    const oscillator=ctx.createOscillator(),gain=ctx.createGain();
    oscillator.type='sine';oscillator.frequency.setValueAtTime(success?740:510,ctx.currentTime);
    oscillator.frequency.exponentialRampToValueAtTime(success?1050:380,ctx.currentTime+.08);
    gain.gain.setValueAtTime(0,ctx.currentTime);gain.gain.linearRampToValueAtTime(.055*this.prefs.effects,ctx.currentTime+.008);
    gain.gain.exponentialRampToValueAtTime(.0001,ctx.currentTime+.1);
    oscillator.connect(gain);gain.connect(this.compressor!);oscillator.start();oscillator.stop(ctx.currentTime+.11);
    this.oneShots.add(oscillator);
    oscillator.onended=()=>{this.oneShots.delete(oscillator);oscillator.disconnect();gain.disconnect();};
  }
}
export const gameAudio=new GameAudio();
