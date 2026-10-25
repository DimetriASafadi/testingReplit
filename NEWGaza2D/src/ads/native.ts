import { Capacitor, registerPlugin } from '@capacitor/core';
import { ADS } from './config';
import { AdError, type AdProvider } from './provider';
interface NativeAds {
  initialize(): Promise<{ canRequest: boolean; privacyRequired: boolean }>;
  prepare(options: { requestId: string; unitId: string }): Promise<void>;
  show(options: { requestId: string }): Promise<{ earned: boolean }>;
  discard(): Promise<void>;
  privacy(): Promise<void>;
  addListener(event: 'rewardEarned', fn: (e: { requestId: string }) => void): Promise<{ remove(): Promise<void> }>;
}
const native = registerPlugin<NativeAds>('NewGazaAds');
export const isNativeAds = () => Capacitor.getPlatform() === 'android' && Capacitor.isNativePlatform();
export const nativeAdProvider: AdProvider = {
  async initialize() { await native.initialize(); },
  async privacy() { await native.privacy(); },
  async prepare(id, _placement, onEarned) {
    if (!navigator.onLine) throw new AdError('OFFLINE');
    const unitId = ADS.testMode ? ADS.android.testRewardedUnitId : ADS.android.rewardedUnitId;
    if (!/^ca-app-pub-\d+\/\d+$/.test(unitId) || (!ADS.testMode && !ADS.android.appId)) throw new AdError('CONFIG');
    const consent = await native.initialize();
    if (!consent.canRequest) throw new AdError('CONSENT');
    await native.prepare({ requestId: id, unitId });
    let delivered = false, shown = false;
    const grant = () => { if (shown && !delivered) { delivered = true; onEarned(); } };
    const listener = await native.addListener('rewardEarned', e => { if (e.requestId === id) grant(); });
    return {
      dispose() { void listener.remove(); void native.discard(); },
      async show() {
        if (shown) throw new AdError('BUSY');
        shown = true;
        const result = await native.show({ requestId: id });
        if (result.earned) grant(); // Same SDK callback persisted in native memory, not a timer.
        return delivered ? 'earned' : 'cancelled';
      },
    };
  },
};
