---
name: Mobile gesture boundaries
description: Keep camera gestures independent of automatically refreshed UI panels.
---

Do not reset camera gestures on ordinary UI rendering, countdown refreshes, or gameplay ticks. Reset on a genuine interaction with the UI or interruption of the gesture instead.

**Why:** The interface refreshes while the player is dragging or pinching. Treating those refreshes as gesture boundaries would repeatedly interrupt valid touches and create another mobile control bug.

**How to apply:** Preserve one-finger panning and two-finger zoom when changing menus. Gesture cleanup belongs to actual input and lifecycle transitions, not to panel updates.

اللعبة تدعم الوضعين الأفقي والعمودي مع مراعاة مقاسات الواجهة، دون إجبار اللاعب على اتجاه واحد.

**Why:** المستخدم طلب دعم الوضعية الأفقية لجميع مقاسات الواجهة، وليس استبدال تجربة الهاتف العمودية.

**How to apply:** عند تعديل الواجهة تحقق من ارتفاع الهاتف القصير أفقيًا، لا من العرض وحده؛ أبقِ أزرار الإجراءات والرجوع والتكبير والصوت قابلة للوصول، وحافظ على النافذة المفتوحة ومركز الكاميرا عند تغيير الاتجاه.

اختبارات اللمس في المتصفح يجب أن تبدأ بمحاكاة جهاز يدعم اللمس قبل تحميل اللعبة، لا أن تُفعّل المحاكاة بعد تشغيلها.

**Why:** Phaser يكتشف دعم اللمس عند الإقلاع؛ تفعيل المحاكاة لاحقًا قد يجعل سحبًا صحيحًا يبدو معطلًا لأن اللعبة بدأت أصلًا كجهاز غير لمسي.

**How to apply:** تحقق من قدرات الجهاز عند الإقلاع قبل تفسير فشل السحب كتراجع في الكود. تغييرات مقاسات الشاشة أثناء نفس جلسة اللمس تبقى اختبارًا صحيحًا للدوران.
