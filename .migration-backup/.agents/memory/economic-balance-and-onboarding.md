---
name: Economic balance and onboarding
description: User rules for rubble income, initial recycling-factory placement, clean-land construction and equipment upgrades.
---

«كل ركام لوحدة او مبنى يعادل ثلث سعر المبنى ... رفع الركام يكون مربح وليس بشكل مبالغ فيه ... نسبة وتناسب مناسب ومريح بالنسبة للوقت المستهلك وبالنسبة للمال الذي احصل عليه من رفع الركام».

«بداية اللعبة مطلوب من ابني مصنع اعادة تدوير الركام بمكان متاح على الخريطة وايضا اشتري المعدات اللازمة لرفع الركام ... امكانية ازالة الركام والحصول على المال والبناء في المناطق النظيفة والخالية من الركام او اي مناطق لا يوجد فوقها شيئ يعني فارغة رمال».

«بالنسبة لأسعار المعدات يكون سعر تطوريها بنصف سعرها الحالي ويكون موجود لها 3 تطويرات».

The user confirmed: each individual machine has three independent upgrades, each costing half its original purchase price, not half an accumulating value.

**Why:** The user selected independent machines and the original purchase price explicitly.

**How to apply:** Keep machine identity, purchase price and upgrade state in saves; do not implement a single fleet-wide upgrade or escalating per-stage upgrade fees.

**Why:** The user wants modest, time-proportionate rubble income, a player-placed recycling factory as the opening objective, clean/empty-land building, and three equipment upgrades priced at half the current equipment price.

**How to apply:** Preserve earned access and existing saves. Do not replace player placement with an automatic central factory for new games. Anchor rubble income to the associated building's price, and ensure the clearing-time/reward balance is comfortable rather than excessively profitable.

Preserve the effective performance of already-upgraded legacy machines when introducing independent upgrades; never charge a remaining upgrade that only changes the level label without improving capacity.

**Why:** The previous fleet had up to five levels while the new purchase-plus-three-upgrades model has four. A direct replacement would downgrade old progress or make paid upgrades ineffective.

**How to apply:** Carry forward earned efficiency independently of the new visible upgrade cap and verify remaining legacy upgrades actually add capacity.

الموارد تأتي من إزالة الركام فقط وتُباع للحصول على المال؛ بناء المباني يتطلب المال فقط، لا موارد. يبدأ الإجراء بطلب مصنع إعادة تدوير، ثم حفار وجرافة وشاحنة نقل؛ تخرج المعدات من المصنع إلى الركام عبر الطرق، تعمل نحو دقيقة، ثم تعود إلى المصنع.

**Why:** طلب المستخدم هذا التسلسل صراحةً، وأن تكون الموارد للبيع فقط. يحل هذا محل المكافأة النقدية التلقائية عند إزالة الركام؛ تظل قيمة بيع الناتج متناسبة مع ثلث سعر المبنى.

**How to apply:** انتظر الوصول قبل حساب وقت العمل. يصبح الموقع نظيفًا فور انتهاء العمل، وتبقى آليات فريقه محجوزة حتى العودة؛ يمكن للفرق الزائدة إزالة مواقع أخرى بالتوازي. لا تمنح موارد كهدايا أو تخصمها للبناء، ولا تمنح ثمنها تلقائيًا. احتفظ بالمخزون والتقدم السابقين عند تحديث الحفظ.

رصيد البداية المطلوب مليون عملة. رفع رصيد الحفظ القديم الأقل من مليون يكون مرة واحدة، دون إنقاص رصيد أعلى ودون تجديد المال المصروف عند كل تشغيل.

**Why:** طلب المستخدم «اجعل معي مبلغ مليون»، مع بقاء التقدم المحفوظ؛ لا ينبغي أن يتحول تعديل البداية إلى مصدر مال متكرر.

**How to apply:** اجعل إعادة البداية اختيارًا صريحًا بتحذير وأرشفة الحفظ؛ مسح تفضيلات Unity وحده لا يعبّر عن إذن لمسح التقدم في ملفات الحفظ.