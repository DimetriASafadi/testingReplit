export const WHEEL_COOLDOWN = 2*60*60*1000;
export const WHEEL_SPIN_MS = 4500;
export const WHEEL_PRIZES = [
 {coins:0,gems:0,iron:0,wood:0,weight:17,label:'0'},
 {coins:300,gems:0,iron:0,wood:0,weight:18,label:'300 عملة'},
 {coins:0,gems:2,iron:0,wood:0,weight:10,label:'2 جوهرة'},
 {coins:1000,gems:0,iron:0,wood:0,weight:8,label:'1000 عملة'},
 {coins:5000,gems:0,iron:0,wood:0,weight:2,label:'5000 عملة'},
 {coins:0,gems:10,iron:0,wood:0,weight:1,label:'10 جواهر'},
 {coins:0,gems:0,iron:0,wood:0,weight:17,label:'0'},
 {coins:150,gems:0,iron:0,wood:0,weight:9,label:'150 عملة'},
 {coins:0,gems:0,iron:100,wood:0,weight:9,label:'100 وحدة حديد'},
 {coins:0,gems:0,iron:0,wood:50,weight:9,label:'50 وحدة خشب'},
] as const;
export function wheelResult(random:number) {
 if(!Number.isFinite(random)||random<0||random>=1)throw Error('Invalid random sample');
 let roll=random*100;
 for(let i=0;i<WHEEL_PRIZES.length;i++){roll-=WHEEL_PRIZES[i].weight;if(roll<0)return i;}
 return WHEEL_PRIZES.length-1;
}
export const COIN_PACKS = [
 {coins:10000,gems:2,cents:10},{coins:20000,gems:3,cents:15},
 {coins:50000,gems:5,cents:45},{coins:100000,gems:8,cents:80},
] as const;
export const GEM_PACKS = [{gems:10,cents:5},{gems:50,cents:25},{gems:100,cents:50}] as const;
/** Disabled contracts, not mock successful providers. Never credit from client receipts. */
export interface CommerceProvider {
 createCheckout(productId:string):Promise<{url:string}>;
}
export interface RewardedAdProvider {
 requestVerifiedReward():Promise<{serverReceiptId:string}>;
}
export const commerceProvider: CommerceProvider|null = null;
export const rewardedAdProvider: RewardedAdProvider|null = null;
