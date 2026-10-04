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

All native pages, confirmation dialogs and small world panels need distinguishable purposes and an explicit close path. Layout must adapt to Unity Game View dimensions and safe areas.

**Why:** The user could not distinguish the game's many windows, reported missing close controls, and saw buttons outside the screen/canvas in Unity.

**How to apply:** Use one scale/coordinate policy for every canvas. Do not mix raw screen pixels and scaled logical dimensions. Coordinate window visibility and input blocking centrally so one window cannot obscure another's close controls.

«واريد زوم التصغير والتكبير اكثر سرعة لانه بطئ جدا حاليا».

**Why:** The user wants faster native camera zoom in both directions.

**How to apply:** Preserve responsive mouse-wheel and two-finger zoom; avoid reintroducing long zoom easing when changing camera controls.