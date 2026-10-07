---
name: Mechanically consistent 2D artwork
description: Fresh painted 2D machinery, consistent articulation and verification inside the city.
---

رسومات آليات النسخة 2D يجب أن تُرسم من جديد. لا تستخدم أشكال أو ملفات الآليات القديمة الخاصة بلعبة الثري دي، ولا تفتحها أو تعيد رندرها لتجهيز رسوم النسخة 2D.

**Why:** المستخدم قال: «اريد شكل آليات خرافي بالرسم ... ما تأخذ الاشكال ... الموجودة بلعبة الثري دي ما تتعامل مع ملفاتها اصلا». هذا يلغي تفضيل المصدر الثلاثي الأبعاد القديم.

**How to apply:** استخدم رسومات 2D جديدة، مع اتساق الهوية بين الاتجاهات والوضعيات ومفاصل الرسوم. ثبّت منظر الكاميرا والإضاءة ونقاط ارتكاز الهيكل، وراجع التقاء الدلو بصندوق الشاحنة فعليًا داخل الحي. الرسوم المرجعية الجديدة توجه الجودة والواقعية، وليست تصريحًا لإعادة استعمال الأصول القديمة.

الآليات المتاحة يجب أن تبقى داخل مصنع إعادة التدوير، لا أن تنتظر ظاهرة على الخريطة. يفتح الباب عند انطلاق الفريق ويخرج الحفار والجرافة والشاحنة تباعًا بفاصل بسيط؛ وفي العودة تدخل تباعًا وتختفي ثم يغلق الباب.

**Why:** المستخدم لا يريد أن تتراكب الآليات عند الإرسال أو الرجوع، ويريد باب المصنع والحركة معه جزءًا مقنعًا من اللعبة.

**How to apply:** طابق دخول الآليات وخروجها مع باب المصنع نفسه واتجاه واجهته، واختبر الازدحام عند تشغيل أكثر من فريق والاستئناف من حفظ أثناء الذهاب أو العودة. أبقِ العدّاد والموارد مرتبطين بعودة المهمة الحقيقية، لا بالمؤثرات وحدها.

ترتيب ظهور الآلية أثناء الالتفاف يجب أن يعتمد على امتداد هيكلها الملامس للأرض، لا على مركز صورتها فقط، وأن يتغير في الإطار نفسه دون انتظار مسافة إضافية.

**Why:** لاحظ المستخدم أن الآلية تظهر فوق المبنى أول الالتفاف، ثم لا تصبح خلفه إلا بعد أن تمشي مسافة؛ المركز وحده يتجاهل ذيل الهيكل وامتداده خلال الدوران.

**How to apply:** اختبر بداية الالتفاف وخروج الآلية إلى الأمام أيضًا، وأبقِ الحساب متصلًا عند تبديل اتجاهات الصور. لا تستخدم ارتفاع الذراع أو قمة الصورة لترتيب العمق.

Keep working-fleet occlusion checks CPU-side, using opacity prepared from immutable artwork outside the animation loop. Avoid synchronous canvas pixel readback during machinery animation.

**Why:** Single-pixel opacity reads caused intermittent stalls during rubble clearing; reading only one pixel does not avoid the browser's readback cost. Preparing opacity removes that stall without replacing detailed artwork or changing fleet simulation.

**How to apply:** Preserve exact source alpha and refresh opacity when the artwork source changes. Check travel, work and return separately; compare render hot paths, and do not present headless frame-rate measurements as guarantees for physical phones.

The user approved the existing worker animation: «انيميشن العمال جميل ولكن يحتاجون حركة على المبنى او حوله اثناء البناء».

**Why:** They want the good worker animation to become visibly active across the construction site, not just animate in place.

**How to apply:** Preserve the accepted worker appearance while improving movement between actual work locations and the building’s surroundings. Keep feet grounded, tasks readable, and front/behind ordering consistent with position.

Keep machinery bodies opaque during travel; conceal the portion inside a factory with doorway clipping rather than fading the entire vehicle.

**Why:** The user noticed translucent moving vehicles. Two source-over sprites weighted `1-t` and `t` do not produce full opacity: their shared body is only 75% opaque at the midpoint, despite the weights adding to one.

**How to apply:** Prefer an opaque directional pose or a proper single-pass texture blend, not overlapping translucent sprites. Preserve intentional transparent windows, edges and shadows. Clip depot passage geometrically and leave unrelated street travel unmasked.

Factory entrances must match the actual front/rear artwork, not an arbitrary point on the lot boundary.

**Why:** The user found both an unnatural exit route and a door whose size and visual identity did not match the factory.

**How to apply:** Calibrate against each visible facade and use the same entrance geometry for door drawing, travel and passage clipping. Keep existing convoy spacing and saved job deadlines.

Recheck tool-contact calibration whenever the parcel layout or equipment parking positions change.

**Why:** Expanded parcels changed the truck's position while the painted digging cycle retained the previous unloading target; valid animation frames still dumped away from the truck.

**How to apply:** Compare the bucket endpoint with the actual parked bed in projected ground coordinates, including the painted bed's offset from its ground anchor. Keep old and expanded layouts consistent.

Review the assembled working crew, not only isolated sprite sheets.

**Why:** Individually correct vehicle paintings still looked like a malformed machine when the bulldozer and excavator occupied overlapping work positions.

**How to apply:** Inspect digging, lifting, unloading and recovery at real parcel scale on a phone; check body spacing, bucket contact and nearby building occlusion together.
