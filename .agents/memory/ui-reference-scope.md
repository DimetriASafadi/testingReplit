---
name: UI reference scope
description: How to use the user's menu reference without inventing unsupported gameplay.
---

نوافذ اللعبة التي يفتحها المستخدم يجب أن تبقى ظاهرة وثابتة أثناء تحديث العناصر والعدّادات، مع إغلاق صريح. يريد الأزرار والتصنيفات واضحة أمامه، دون البحث عنها داخل التمرير.

**Why:** المستخدم كرر طلب ثبات النوافذ وسهولة الوصول إلى الخيارات، خصوصًا على الهواتف.

**How to apply:** افصل الإغلاق والإجراءات الرئيسية والتنقل عن المحتوى المتحرك. استخدم صفحات قصيرة وأقسامًا واضحة للقوائم الطويلة بدل دفن الخيارات في التمرير، وحافظ على النافذة والتركيز خلال التحديث.

«اذا كان هناك محتوى زائد او ليس موجود في لوجيك اللعبة فلا تأخذه».

«الصورة فقط لتستوحي الشكل والخلفية ونظام الرئيسية كيف يبدو».

**Why:** The user supplied a menu description and reference image for appearance, while explicitly excluding content that is not supported by this game's logic.

**How to apply:** Treat the description/image as visual references, not permission to invent resources, player progression, social features, purchase grants, or sale actions. Bind menus and progress to authoritative game state and preserve existing district access and saves.

«اريد ان تكون الواجهة مريحة بالنسبة للهواتف ومتناسبة وتظهر كل شيئ براحة مباني تصنيفات وما يحدث».

**Why:** The user could only find the initial building category and wants the entire real catalog, categories and ongoing activities discoverable on phones.

**How to apply:** Keep phone browsing and confirmation understandable without multiple competing scrollers. Preserve reading position and focus during live timer updates. Verify actual touch hit targets, not only rectangles inside the viewport: clipped panels and informational overlays can still block actions. Update dialog state synchronously when closing; an asynchronous native close event can race with the live renderer and leave a reopened window blocking the game.

«يجب ألا تكون المؤثرات مبالغًا فيها أو تؤثر في سرعة اللعبة».

**Why:** The user explicitly wants subtle visual feedback, not elaborate effects that reduce performance.

**How to apply:** Use bounded, brief UI animation and shared low-poly factory parts; trigger achievements, currency and district reveals only from actual state changes and suppress replay when loading saved progress.

All native pages, confirmation dialogs and small world panels need distinguishable purposes and an explicit close path. Layout must adapt to Unity Game View dimensions and safe areas.

**Why:** The user could not distinguish the game's many windows, reported missing close controls, and saw buttons outside the screen/canvas in Unity.

**How to apply:** Use one scale/coordinate policy for every canvas. Do not mix raw screen pixels and scaled logical dimensions. Coordinate window visibility and input blocking centrally so one window cannot obscure another's close controls.

«واريد زوم التصغير والتكبير اكثر سرعة لانه بطئ جدا حاليا».

**Why:** The user wants faster native camera zoom in both directions.

**How to apply:** Preserve responsive mouse-wheel and two-finger zoom; avoid reintroducing long zoom easing when changing camera controls.

«اريد اضل ضاغط على الزر يعمل تدوير بشكل ناعم للمبنى طالما انا ضاغط على زر الروتيت».

**Why:** يريد المستخدم دوران معاينة البناء أثناء الضغط المستمر، لا قفزة زاوية عند النقر.

**How to apply:** أوقف الدوران عند الرفع أو إغلاق وضع البناء أو فقدان التركيز، واحفظ زاوية التوقف الدقيقة مع الحفاظ على اتجاهات الأبنية القديمة.

يريد المستخدم استبدال بوكس «مصنع إعادة التدوير جاهز • مصنع مركزي» بأيقونة وهوفر حول المبنى لتمييزه عن البقية.

**Why:** طلب المستخدم إزالة صندوق النص الكبير من الخريطة والإبقاء على تمييز المصنع بصريًا.

**How to apply:** استخدم أيقونة تدوير قابلة للضغط مع حلقة/هالة حول المصنع؛ لا تعد إلى لافتة مستطيلة كبيرة أو تزيل وظيفة تحديد المصنع.

«لا أستطيع التمييز بين بكوس النص وبكوس الزر»؛ يريد المستخدم للأزرار مؤثرات وهيبة أوضح، مع بقاء التأثيرات خفيفة على الأداء.

**Why:** صناديق الأزرار والنصوص المتشابهة لم توضح له أين يمكن الضغط، وأبلغ مجددًا عن زر خارج الشاشة ونافذة بلا إغلاق واضح.

**How to apply:** ميّز الإجراءات بحدود وتظليل ولمعة ورد فعل للضغط، لا بمجرد لون لوحة النص. يجب أن تبقى جميع أزرار التنقل داخل المنطقة الآمنة وأن تبقى نافذة اسم الحي قابلة للرؤية والإظهار.

«أريد النصوص تكون سنتر بالزر وما تكون ملتصقة بحواف الحاويات ... UI ممتاز بالمقاسات والثيم ... بناءً على الألوان والمواصفات التي زودتك بها».

**Why:** المستخدم أبلغ مجددًا عن ضعف محاذاة النصوص والمسافات الداخلية، ويريد جودة واجهة موحّدة لا إصلاح زر منفرد.

**How to apply:** وسّط نصوص جميع الأزرار، مع حشو متوازن ومساحة كافية للأيقونة والنص. حافظ على الأزرق الداكن والملكي والفاتح والذهبي والألوان البرتقالية والخضراء للإجراءات، والخطوط العربية الحالية. راجع المقاسات والأزرار القصيرة وبطاقات المتجر والحاويات معًا، ولا تُدخل فحصًا مكلفًا لكل زر في كل إطار.

For the standalone 2D game, menus and windows should use dark navy-blue surfaces and white text rather than the earlier green/cream theme.

**Why:** The user supplied a navy panel reference with white Arabic labels and small green completion indicators and explicitly requested these colors throughout the menus/windows.

**How to apply:** Keep menu, construction, material, settings and HUD surfaces consistent; reserve green for completion/status indicators and red for destructive/error states. Preserve the uploaded multicolor icon artwork, scene art, Arabic font and existing portrait/landscape layouts. The earlier broader action-color guidance above is not a reason to restore green primary buttons in the 2D game.

مؤشرات المشاريع ومربعات شروط استلام الحي تكون خضراء، حتى قبل الاكتمال، بدرجات تميّز حالة الشرط.

**Why:** المستخدم حدّد هذه المؤشرات في صور القوائم وطلب تحويلها إلى الأخضر؛ اللون الأخضر وحده لا يعني اكتمال الشرط.

**How to apply:** استخدم أخضر هادئًا للشروط غير المكتملة وأخضر أوضح مع علامة التأكيد للمكتملة، دون تغيير شروط التقدم أو تحويل خلفيات القوائم إلى الأخضر.