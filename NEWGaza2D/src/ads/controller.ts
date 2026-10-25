import { GameStore } from '../engine';
import { ADS, adRewardLabel, type AdPlacement } from './config';
import { adUnavailable } from './ledger';
import { isNativeAds, nativeAdProvider } from './native';
import { webAdProvider } from './web';
import { adErrorText, type AdProvider, type PreparedAd } from './provider';
import './rewarded.css';

export class AdsController {
  busy = false;
  private overlay?: HTMLElement;
  private provider: AdProvider;
  constructor(private store: GameStore, private blocked: (busy: boolean) => void,
      private notify: (text: string, error?: boolean) => void, provider?: AdProvider) {
    this.provider = provider ?? (isNativeAds() ? nativeAdProvider : webAdProvider);
  }
  initialize() { void this.provider.initialize?.().catch(() => {}); } // Ad consent failure cannot stop offline play.
  private panel(title: string, text: string, buttons: { label: string; run: () => void }[] = []) {
    this.overlay?.remove();
    const box = this.overlay = document.createElement('section');
    box.className = 'rewarded-overlay'; box.setAttribute('role', 'dialog'); box.setAttribute('aria-modal', 'true');
    box.setAttribute('aria-label', title); box.tabIndex = -1;
    const card = document.createElement('div'); card.className = 'rewarded-card';
    const h = document.createElement('h2'); h.textContent = title;
    const p = document.createElement('p'); p.setAttribute('role', 'status'); p.textContent = text;
    card.append(h, p);
    for (const b of buttons) {
      const btn = document.createElement('button'); btn.className = 'btn gold'; btn.textContent = b.label; btn.onclick = b.run; card.append(btn);
    }
    box.append(card); document.body.append(box); (card.querySelector('button') ?? box).focus();
  }
  private lock(v: boolean) { this.busy = v; this.blocked(v); }
  async privacy() {
    if (this.busy) return;
    this.lock(true);
    try { await this.provider.privacy(); }
    catch (e) { this.notify(ADS.testMode ? 'وضع الإعلانات التجريبية: خيارات الخصوصية تظهر عندما يطلبها مزود الموافقة. إعدادات الإنتاج موضّحة في دليل الإعلانات.' : adErrorText(e), true); }
    finally { this.lock(false); }
  }
  request(placement: AdPlacement) {
    if (this.busy) return;
    const unavailable = adUnavailable(this.store.state, placement, Math.max(Date.now(), this.store.state.lastSeen));
    if (unavailable) { this.notify(unavailable, true); return; }
    this.lock(true);
    const previousFocus = document.activeElement as HTMLElement | null;
    let prepared: PreparedAd | undefined, id = '', cancelled = false, sdkEarned = false, credited = false, showing = false;
    const release = () => {
      cancelled = true; prepared?.dispose(); this.overlay?.remove(); this.overlay = undefined;
      this.lock(false); if (previousFocus?.isConnected) previousFocus.focus();
    };
    const cancel = () => {
      if (id) {
        try { this.store.cancelRewardedAd(id); }
        catch { this.notify('تعذر حفظ إلغاء الإعلان. لن نمنح مكافأة دون استحقاق من المزود.', true); }
      }
      release();
    };
    const credit = () => {
      if (!sdkEarned || credited) return;
      this.store.earnedRewardedAd(id);
      credited = true;
    };
    const complete = () => {
      if (sdkEarned) {
        try { credit(); this.notify(`تمت إضافة ${adRewardLabel(placement)} إلى حفظك.`); release(); }
        catch {
          this.panel('تعذر حفظ المكافأة', 'اكتمل الإعلان، لكن التخزين محظور أو ممتلئ. اسمح بالتخزين ثم أعد الحفظ. إن أُغلق التطبيق قبل تسجيل الاستحقاق فقد لا يمكن استعادته.',
            [{ label: 'إعادة حفظ المكافأة', run: complete }, { label: 'العودة للعبة', run: release }]);
        }
      } else { this.notify('أُغلق الإعلان قبل استحقاق المكافأة. لم يتغير رصيدك.'); cancel(); }
    };
    const show = async () => {
      if (!prepared || cancelled || showing) return;
      showing = true;
      // GPT inserts its full-screen ad directly into body, outside our inert game UI.
      this.overlay?.remove(); this.overlay = undefined;
      try { await prepared.show(); complete(); }
      catch (e) {
        if (sdkEarned) complete();
        else { this.notify(adErrorText(e), true); cancel(); }
      }
    };
    const load = async () => {
      if (cancelled || id) return;
      try { id = this.store.beginRewardedAd(placement); }
      catch (e) { this.notify(e instanceof Error ? e.message : 'تعذر حفظ الطلب', true); release(); return; }
      this.panel('تحميل إعلان المكافأة', 'لا تُمنح المكافأة إلا بعد استحقاقها من مزود الإعلان.', [{ label: 'إلغاء', run: cancel }]);
      try {
        prepared = await this.provider.prepare(id, placement, () => {
          if (cancelled || sdkEarned) return;
          sdkEarned = true;
          try { credit(); } catch { /* Keep the earned attempt in memory; show a save retry after ad close. */ }
        });
        if (cancelled) { prepared.dispose(); return; }
        this.panel('الإعلان جاهز', `شاهد الإعلان للحصول على ${adRewardLabel(placement)}.${ADS.testMode ? ' هذا إعلان تجريبي ولا يحقق إيرادات.' : ''}`,
          [{ label: 'شاهد الآن', run: () => { void show(); } }, { label: 'ليس الآن', run: cancel }]);
      } catch (e) { if (!cancelled) { this.notify(adErrorText(e), true); cancel(); } }
    };
    this.panel('مكافأة اختيارية', `هل تريد مشاهدة إعلان مقابل ${adRewardLabel(placement)}؟ لن يُخصم شيء من رصيدك. يمكنك رفض الإعلان.${ADS.testMode ? ' وضع الاختبار مفعّل.' : ''}`,
      [{ label: 'تحميل الإعلان', run: () => { void load(); } }, { label: 'رجوع', run: cancel }]);
  }
}
