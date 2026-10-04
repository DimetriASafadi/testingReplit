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