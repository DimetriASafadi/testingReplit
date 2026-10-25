import type { GameState } from '../model';
import { WHEEL_SPIN_MS } from '../rewards';
import { ADS, isAdPlacement, type AdPlacement } from './config';

export interface AdLedger {
  nextAt: number;
  spinCredits: number;
  lastClaimId?: string;
  pending?: { id: string; placement: AdPlacement; status: 'requested' | 'earned'; createdAt: number };
}
const ensure = (ok: unknown, message: string) => { if (!ok) throw Error(message); };
export function adUnavailable(s: GameState, p: AdPlacement, now: number): string {
  if (s.ads?.pending) return s.ads.pending.status==='earned'?'هناك مكافأة مستحقة لم تُحفظ بعد. أعد فتح اللعبة بعد السماح بالتخزين لاستعادتها.':'هناك طلب إعلان قيد المعالجة؛ انتظر أو أعد فتح اللعبة لإلغاء الطلب المنقطع.';
  if (now < (s.ads?.nextAt ?? 0)) return `المكافأة التالية بعد ${Math.ceil((s.ads!.nextAt - now) / 1000)} ثانية`;
  if (p === 'wheel') {
    if (s.ads?.spinCredits) return 'استخدم الدورة الإضافية المحفوظة أولًا.';
    if (!s.wheel || now >= s.wheel.nextAt) return 'استخدم دورتك المجانية أولًا.';
    if (now < s.wheel.startedAt + WHEEL_SPIN_MS) return 'انتظر انتهاء دوران الدولاب.';
  }
  return '';
}
export function beginAd(s: GameState, p: AdPlacement, now: number) {
  ensure(isAdPlacement(p), 'نوع المكافأة غير صالح');
  ensure(!adUnavailable(s, p, now), adUnavailable(s, p, now));
  s.ads ??= { nextAt: 0, spinCredits: 0 };
  const id = `ad-${++s.sequence}`;
  s.ads.pending = { id, placement: p, status: 'requested', createdAt: now };
  return id;
}
/** SDK grant evidence is client-side, NOT server verification or an anti-cheat boundary. */
export function markAdEarned(s: GameState, id: string) {
  if (s.ads?.lastClaimId === id) return;
  ensure(s.ads?.pending?.id === id, 'انتهت صلاحية طلب الإعلان');
  s.ads!.pending!.status = 'earned';
}
export function settleEarnedAd(s: GameState, now: number): boolean {
  const a = s.ads, p = a?.pending;
  if (!a || !p || p.status !== 'earned') return false;
  if (p.id !== a.lastClaimId) {
    if (p.placement === 'coins') s.coins += ADS.rewards.coins;
    if (p.placement === 'gems') s.gems += ADS.rewards.gems;
    if (p.placement === 'wheel') a.spinCredits = 1;
    ensure(Number.isSafeInteger(s.coins) && Number.isSafeInteger(s.gems), 'تجاوز الرصيد الحد المسموح');
    a.lastClaimId = p.id; a.nextAt = now + ADS.rewards.cooldownMs;
  }
  delete a.pending;
  return true;
}
export function cancelAd(s: GameState, id: string) {
  if (s.ads?.pending?.id === id && s.ads.pending.status === 'requested') delete s.ads.pending;
}
/** Interrupted requests never imply a completed ad. Earned records can be recovered exactly once. */
export function recoverAd(s: GameState, now: number) {
  if (!settleEarnedAd(s, now) && s.ads?.pending) delete s.ads.pending;
}
