---
name: Native save performance
description: Camera-stop stutters, immutable save snapshots, and the user's overhead fleet-marker requirements.
---

Camera movement checkpoints must not run whole-city validation, JSON serialization, disk flushes or world rebuilding on the gameplay thread.

**Why:** The user reported heavy stutters when zoom stopped, while zoom itself was smooth. Camera-settle checkpoints were performing full-city persistence on the gameplay thread in a dense city.

**How to apply:** Keep Unity view/vehicle capture on the main thread, then serialize detached plain-state snapshots with a single ordered writer. Bound queued requests, preserve checksums and atomic replacement, report worker failures, and flush the latest snapshot at suspension/quit. Real-device frame timing is separate from .NET fixture timing.

Every new mutable saved reference must be deep-copied in the snapshot boundary.

**Why:** Unity allows background ToJson only while the supplied object remains unchanged. Sharing live arrays or nested state with a writer can mix different gameplay moments or lose earned progress.

**How to apply:** Extend snapshot isolation tests whenever saved reference fields are added; immutable strings may be shared, mutable records and arrays may not.

«اريد ان ارى ايقونات على الآليات من أعلى لأعرف اينهم بالضبط ... يعمل opacity للأيقونات لما اقرب».

**Why:** The user could not locate machines while they were traveling or working at another site.

**How to apply:** Track actual rendered machine positions, distinguish excavator/truck/dozer, and fade smoothly near the camera. Do not substitute the destination or an approximate fixed work-site position.

The user wants a button during rubble removal that moves and zooms the camera to the work site, plus a clear on-site work/status icon.

**Why:** They want to know exactly where the ongoing job is and what state it is in.

**How to apply:** Keep the work locator available when activity details are collapsed. Locate the active job, not the last selection; viewing must not restart the job, reset its timer or charge money. Distinguish travel, removal, hauling and recycling, and remove the marker on completion.

«اريد UX قوي وواضح ... اعرف شو حاليا يحصل ... زر يوديني على اي احداث او اماكن عمل في الخريطة ... ايقونة على الآليات من بعيد ... لما اقترب تصير opacity».

**Why:** يريد المستخدم معرفة النشاط الحالي والانتقال إلى الأحداث ومواقع العمل كلها، لا الاقتصار على مهمة إزالة الركام.

**How to apply:** اجمع الأعمال الجارية والأحداث الجاهزة الحقيقية في مدخل واضح من الرئيسية والخريطة، مع انتقال للموقع لا ينفّذ العمل أو يجمع الأموال تلقائيًا. طبّق التلاشي عند الاقتراب على علامات المواقع والأحداث والمصانع أيضًا، ولا تُخفِ معلومات الحالة معها. حافظ على تجميع العلامات المتقاربة وحدود عرض القوائم حتى لا يضعف الأداء في المدن الكثيفة.