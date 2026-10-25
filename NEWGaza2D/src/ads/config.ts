import config from '../../ads.config.json';
export const ADS = config;
export type AdPlacement = 'coins' | 'gems' | 'wheel';
export const isAdPlacement = (s: unknown): s is AdPlacement => s === 'coins' || s === 'gems' || s === 'wheel';
export const adRewardLabel = (p: AdPlacement) => p === 'coins' ? `${ADS.rewards.coins} عملة` : p === 'gems' ? `${ADS.rewards.gems} جوهرة` : 'دورة إضافية واحدة لدولاب الحظ';
