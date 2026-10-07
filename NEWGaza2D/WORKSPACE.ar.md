# تشغيل نسخة الويب المنفصلة

هذه تهيئة فقط، وليست لعبة أو نقلًا لمشروع Unity إلى الويب.
وثيقتا README وPRE_IMPLEMENTATION_NOTES محفوظتان دون تعديل.

## حدود الملفات

- كل شيفرة اللعبة وأصولها وإعداداتها واختباراتها وتوثيقها داخل `NEWGaza2D/`.
- `artifacts/new-gaza-2d/` استثناء تسجيل المنصة فقط: ملف تعريف المعاينة وحزمة صغيرة تفوّض أوامر التشغيل إلى هذا المجلد. لا يحتوي نسخة ثانية من اللعبة ولا واجهة React.
- مشروع Unity وأدواته وأصوله المحلية تبقى في مساراتها الأصلية. لا تُنشئ ملفات `.meta`.
- حزم API وقاعدة البيانات الموجودة هي هيكل المنصة السابق فقط؛ اللعبة لا تعتمد عليها ولا تحتاج قاعدة بيانات. لا تشغّل أوامر دفع مخطط قاعدة البيانات.

## أوامر التشغيل

- شغّل المعاينة بواسطة workflow المسجّل: `artifacts/new-gaza-2d: web`.
- المنصة تزوّد `PORT` و`BASE_PATH`؛ لا تستخدم منفذًا ثابتًا في شيفرة اللعبة.
- `pnpm --filter @workspace/new-gaza-2d-game run typecheck` لفحص TypeScript.
- ناتج بناء الويب في `NEWGaza2D/dist/`. استخدم بادئة Vite `import.meta.env.BASE_URL` لمسارات الأصول مستقبلًا.
- قبل تنفيذ اللعبة اقرأ الملاحظات السابقة في هذا المجلد؛ صفحة الحالة الحالية ليست نموذج لعب.

## فحوص المشروع الأصلي

حسب توجيه المستخدم، تُترك نسخة Unity دون عمل إضافي، ولا تُشغّل فحوصها ضمن تهيئة الويب أو التحقق من لعبة 2D. الأوامر التالية مرجع تاريخي فقط:

```sh
dotnet run --project testingReplic/Tests/Persistence/PersistenceTests.csproj --configuration Release && dotnet run --project testingReplic/Tests/Domain/DomainTests.csproj --configuration Release && dotnet run --project testingReplic/Tests/Development/DevelopmentTests.csproj --configuration Release
```

```sh
dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release && cd testingReplic && dotnet run --project Tests/Equipment/EquipmentTests.csproj --configuration Release -- --no-export
```

هذه فحوص مصادر ومنطق وحركة معزولة، وليست تجميعًا أو تشغيلًا داخل Unity.
