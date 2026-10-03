---
name: UI reference scope
description: How to use the user's menu reference without inventing unsupported gameplay.
---

«اذا كان هناك محتوى زائد او ليس موجود في لوجيك اللعبة فلا تأخذه».

«الصورة فقط لتستوحي الشكل والخلفية ونظام الرئيسية كيف يبدو».

**Why:** The user supplied a menu description and reference image for appearance, while explicitly excluding content that is not supported by this game's logic.

**How to apply:** Treat the description/image as visual references, not permission to invent resources, player progression, social features, purchase grants, or sale actions. Bind menus and progress to authoritative game state and preserve existing district access and saves.

«يجب ألا تكون المؤثرات مبالغًا فيها أو تؤثر في سرعة اللعبة».

**Why:** The user explicitly wants subtle visual feedback, not elaborate effects that reduce performance.

**How to apply:** Use bounded, brief UI animation and shared low-poly factory parts; trigger achievements, currency and district reveals only from actual state changes and suppress replay when loading saved progress.