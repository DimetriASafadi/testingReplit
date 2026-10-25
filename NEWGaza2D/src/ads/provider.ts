import type { AdPlacement } from './config';
export interface PreparedAd {
  show(): Promise<'earned' | 'cancelled'>;
  dispose(): void;
}
export interface AdProvider {
  prepare(id: string, placement: AdPlacement, onEarned: () => void): Promise<PreparedAd>;
  privacy(): Promise<void>;
  initialize?(): Promise<void>;
}
export class AdError extends Error {
  constructor(readonly code: string) { super(code); }
}
export function adErrorText(e: unknown) {
  const code = e instanceof Error ? e.message : '';
  if (/OFFLINE/.test(code)) return 'لا يوجد اتصال بالإنترنت. لم تُمنح مكافأة.';
  if (/NO_FILL/.test(code)) return 'لا يوجد إعلان متاح الآن. حاول لاحقًا.';
  if (/UNSUPPORTED/.test(code)) return 'إعلانات المكافآت غير مدعومة في هذه الصفحة أو على هذا الجهاز. جرّب متصفح الهاتف مباشرة.';
  if (/CONSENT|PRIVACY|CMP/.test(code)) return 'تعذر تجهيز موافقة الخصوصية. افتح خيارات الخصوصية أو حاول لاحقًا.';
  if (/CONFIG/.test(code)) return 'إعدادات الإعلانات غير مكتملة. لم تُمنح مكافأة.';
  if (/TIMEOUT|SCRIPT/.test(code)) return 'تعذر تحميل الإعلان؛ تحقق من الاتصال أو مانع الإعلانات، ثم حاول مجددًا.';
  return 'تعذر عرض الإعلان. لم تُمنح مكافأة؛ يمكنك المحاولة لاحقًا.';
}
const scriptJobs = new Map<string, Promise<void>>();
export function loadAdScript(id: string, src: string): Promise<void> {
  const old = scriptJobs.get(id); if (old) return old;
  const job = new Promise<void>((resolve, reject) => {
    const el = document.createElement('script'); el.id = id; el.async = true; el.src = src;
    const timer = window.setTimeout(() => fail(), 20000);
    const fail = () => { clearTimeout(timer); el.remove(); scriptJobs.delete(id); reject(new AdError('SCRIPT')); };
    el.onerror = fail;
    el.onload = () => { clearTimeout(timer); resolve(); };
    document.head.append(el);
  });
  scriptJobs.set(id, job); return job;
}
