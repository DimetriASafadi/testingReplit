---
name: توقعات حفظ التقدم
description: طلب المستخدم بشأن استعادة التقدم والحركة والمؤقتات بعد إغلاق اللعبة.
---

يطلب المستخدم حفظ المنجزات وكل التقدم والحركات عند إغلاق اللعبة وفتحها، بمخزن مناسب وليس بالضرورة PlayerPrefs، وأن تتعامل المؤقتات مع وقت النظام.

**Why:** قال المستخدم إنه سيختبر لاحقًا على Unity، وأن المطلوب حاليًا تنفيذ الحفظ نفسه لا تعليق التنفيذ إلى توفر المحرر.

**How to apply:** عند إضافة إجراء لعب جديد، اجعل أثره قابلًا للاستعادة دون خصم أو مكافأة متكررة، وبيّن حدود الحفظ المحلي والاختبارات غير المشغّلة داخل Unity دون ادعاء تحقق المحرر.

Optional camera and vehicle poses must not make otherwise valid earned progress unreadable. Repair these only after verifying the original checksum, report the presentation reset, preserve the original payload, and keep gameplay validation and new-save validation strict.

**Why:** After introducing view persistence, the user's Unity startup rejected an existing save with “Invalid saved camera.” Unity can materialize an absent optional camera as a zero-valued object, unlike the .NET serializer used by the fixtures.

**How to apply:** Cover both missing references and materialized zero-valued objects in save-load compatibility tests. Never recover invalid currency, buildings, timers or progression by resetting the campaign.

Unity JSON may round-trip null optional string references as empty strings. Treat an empty optional target/depot reference as absence at the checksummed load boundary, not as an unknown identifier.

**Why:** A Unity startup report rejected development data during save migration; the existing validator treated every non-null string, including empty strings, as a real work-site/depot ID. The .NET fixture did not exercise this representation.

**How to apply:** Normalize only explicitly optional zero-length references; keep nonempty unknown IDs, required active-job relationships, timers and progress strict. Preserve raw files and include compatibility archives in the no-new-campaign guard.

يجب تمييز وقت وصول الآليات عن وقت إزالة الركام في الواجهة؛ انتظار الحركة ليس عدّاد عمل متجمّدًا.

**Why:** رأى المستخدم نافذة «إزالة الركام · 00:00:29» تبقى حتى بعد إعادة التشغيل، بينما كانت قاعدة بدء العمل تنتظر وصول الفريق. إعادة بناء الحركة قد تستأنف الانتظار، ولا تعني ضياع التقدم أو إذنًا بإلغاء المهمة.

**How to apply:** اعرض مرحلة السفر صراحة، وابدأ عدّاد الإزالة عند الوصول الفعلي فقط. إخفاء نافذة النشاط يجب ألا يلغي العمل أو يمنح الموارد؛ احتفظ بالمهمة والموقع والتقدم عند استعادة الحفظ.