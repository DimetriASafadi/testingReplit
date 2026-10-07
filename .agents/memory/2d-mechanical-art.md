---
name: Mechanically consistent 2D artwork
description: Why vehicle animation uses offline articulated rendering and must be checked inside the city.
---

For multi-heading digging/loading animations, prefer rendering a single articulated source offline over generating each pose independently. Keep Phaser as the 2D runtime, not a second live 3D engine.

**Why:** Independent generated views can change vehicle identity and pivot geometry between frames. An articulated source keeps the chassis, bucket, payload and tipping hinge consistent, while packed sprites avoid a phone-side 3D model/rendering cost.

**How to apply:** Use a common orthographic view, light direction and grade; project actual tool endpoints and ground anchors into the sprite plane. Check bucket/bed contact in world coordinates and then inspect the playable city: foreground buildings can hide otherwise correct machinery. Preserve depth ordering and selectively reveal genuine occluders rather than drawing vehicles on top of roofs.

الآليات المتاحة يجب أن تبقى داخل مصنع إعادة التدوير، لا أن تنتظر ظاهرة على الخريطة. يفتح الباب عند انطلاق الفريق ويخرج الحفار والجرافة والشاحنة تباعًا بفاصل بسيط؛ وفي العودة تدخل تباعًا وتختفي ثم يغلق الباب.

**Why:** المستخدم لا يريد أن تتراكب الآليات عند الإرسال أو الرجوع، ويريد باب المصنع والحركة معه جزءًا مقنعًا من اللعبة.

**How to apply:** طابق دخول الآليات وخروجها مع باب المصنع نفسه واتجاه واجهته، واختبر الازدحام عند تشغيل أكثر من فريق والاستئناف من حفظ أثناء الذهاب أو العودة. أبقِ العدّاد والموارد مرتبطين بعودة المهمة الحقيقية، لا بالمؤثرات وحدها.
