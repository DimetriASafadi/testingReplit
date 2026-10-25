import { ADS } from './config';
import { AdError, loadAdScript, type AdProvider, type PreparedAd } from './provider';
import { webConsent, webPrivacy } from './web-consent';

// Small local structural types keep the ad SDK optional and out of the offline game bundle.
interface Slot { addService(service: PubAds): Slot }
interface Event { slot: Slot; isEmpty?: boolean; makeRewardedVisible?: () => boolean }
type Listener = (event: Event) => void;
interface PubAds {
  addEventListener(name: string, fn: Listener): void;
  removeEventListener(name: string, fn: Listener): void;
}
interface GPT {
  cmd: { push(fn: () => void): unknown };
  enums: { OutOfPageFormat: { REWARDED: unknown } };
  defineOutOfPageSlot(path: string, format: unknown): Slot | null;
  pubads(): PubAds;
  enableServices(): void;
  display(slot: Slot): void;
  destroySlots(slots: Slot[]): void;
}
declare global { interface Window { googletag?: GPT } }
let enabled = false;
export const webAdProvider: AdProvider = {
  privacy: webPrivacy,
  async prepare(_id, _placement, onEarned) {
    if (!navigator.onLine) throw new AdError('OFFLINE');
    const path = ADS.testMode ? ADS.web.testAdUnitPath : ADS.web.adUnitPath;
    if (!/^\/\d+\/[\w/.-]+$/.test(path)) throw new AdError('CONFIG');
    await webConsent();
    // Google's command queue is intentionally established before the async script.
    window.googletag ??= { cmd: [] } as unknown as GPT;
    await loadAdScript('newgaza-gpt', 'https://securepubads.g.doubleclick.net/tag/js/gpt.js');
    return new Promise<PreparedAd>((resolve, reject) => {
      let slot: Slot | null = null, pub: PubAds | null = null;
      let alive = true, shown = false, earned = false, ready = false;
      let present: (() => boolean) | undefined;
      let end: ((v: 'earned' | 'cancelled') => void) | undefined;
      let failShow: ((e: unknown) => void) | undefined;
      let displayTimer = 0, closeTimer = 0;
      const listeners: [string, Listener][] = [];
      const cleanup = () => {
        if (!alive) return; alive = false; clearTimeout(loadTimer); clearTimeout(displayTimer); clearTimeout(closeTimer);
        for (const [name, fn] of listeners) pub?.removeEventListener(name, fn);
        if (slot) window.googletag?.destroySlots([slot]);
      };
      const fail = (error: unknown) => { cleanup(); if (ready) failShow?.(error); else reject(error); };
      const loadTimer = window.setTimeout(() => fail(new AdError('TIMEOUT')), 30000);
      const listen = (name: string, fn: Listener) => {
        const wrapped: Listener = e => { if (alive && e.slot === slot) fn(e); };
        pub!.addEventListener(name, wrapped); listeners.push([name, wrapped]);
      };
      window.googletag!.cmd.push(() => {
        if (!alive) return;
        try {
          const g = window.googletag!;
          slot = g.defineOutOfPageSlot(path, g.enums.OutOfPageFormat.REWARDED);
          if (!slot) { fail(new AdError('UNSUPPORTED')); return; }
          pub = g.pubads(); slot.addService(pub);
          listen('slotRenderEnded', e => { if (e.isEmpty) fail(new AdError('NO_FILL')); });
          listen('rewardedSlotGranted', () => {
            if (!shown || earned) return;
            earned = true; onEarned();
          });
          listen('rewardedSlotClosed', () => {
            // A grant may arrive just after close; this grace NEVER creates a reward.
            closeTimer = window.setTimeout(() => { const result = earned ? 'earned' : 'cancelled'; cleanup(); end?.(result); }, 750);
          });
          listen('rewardedSlotReady', e => {
            if (ready) return; clearTimeout(loadTimer); ready = true;
            present = e.makeRewardedVisible?.bind(e);
            resolve({
              dispose: cleanup,
              show: () => new Promise((done, bad) => {
                if (!alive || shown || !present) { bad(new AdError('UNSUPPORTED')); return; }
                end = done; failShow = bad; shown = true;
                displayTimer = window.setTimeout(() => fail(new AdError('TIMEOUT')), 10 * 60000);
                try { if (present() === false) fail(new AdError('UNSUPPORTED')); } catch (err) { fail(err); }
              }),
            });
          });
          if (!enabled) { g.enableServices(); enabled = true; }
          g.display(slot);
        } catch (err) { fail(err); }
      });
    });
  },
};
