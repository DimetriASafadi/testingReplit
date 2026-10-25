import { ADS } from './config';
import { AdError, loadAdScript } from './provider';

type TC = { eventStatus?: string; cmpStatus?: string; gdprApplies?: boolean; tcString?: string; listenerId?: number };
type TCF = (command: string, version: number, cb: (data: TC, success: boolean) => void, id?: number) => void;
declare global {
  interface Window {
    __tcfapi?: TCF;
    googlefc?: { callbackQueue: Record<string, () => void>[]; showRevocationMessage?: () => void };
  }
}
/** Production requires the publisher's Google-certified CMP, not a made-up consent checkbox. */
export async function webConsent() {
  if (ADS.testMode) return; // Official sample inventory only; never use this bypass with live units.
  const url = ADS.web.cmpScriptUrl;
  if (!/^https:\/\/fundingchoicesmessages\.google\.com\/i\/pub-\d+/.test(url)) throw new AdError('CMP_CONFIG');
  window.googlefc ??= { callbackQueue: [] };
  if (!document.querySelector('iframe[name="googlefcPresent"]')) {
    const iframe = document.createElement('iframe'); iframe.name = 'googlefcPresent'; iframe.hidden = true; document.body.append(iframe);
  }
  await loadAdScript('newgaza-cmp', url);
  await new Promise<void>((resolve, reject) => {
    let listener: number | undefined, done = false;
    const stop = () => { clearTimeout(timer); if (listener !== undefined) window.__tcfapi?.('removeEventListener', 2, () => {}, listener); };
    const timer = window.setTimeout(() => { done = true; stop(); reject(new AdError('CONSENT_TIMEOUT')); }, 30000);
    if (!window.__tcfapi) { stop(); reject(new AdError('CMP_UNAVAILABLE')); return; }
    window.__tcfapi('addEventListener', 2, (tc, ok) => {
      listener = tc.listenerId;
      if (done) { stop(); return; }
      if (ok && tc.cmpStatus === 'loaded' && ['tcloaded', 'useractioncomplete'].includes(tc.eventStatus ?? '') &&
          typeof tc.gdprApplies === 'boolean' && (!tc.gdprApplies || !!tc.tcString)) {
        done = true; stop(); resolve();
      }
    });
  });
}
export async function webPrivacy() {
  if (ADS.testMode) { throw new AdError('PRIVACY_TEST'); }
  await webConsent();
  if (!window.googlefc?.showRevocationMessage) throw new AdError('PRIVACY_UNAVAILABLE');
  window.googlefc.showRevocationMessage();
}
