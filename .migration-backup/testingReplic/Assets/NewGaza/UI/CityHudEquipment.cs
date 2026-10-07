using System;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private int equipmentPage;
        private void StartFactoryPlacement()
        {
            if (session.Development == null) { session.Notify("لم تُحمّل خريطة البناء بعد"); return; }
            bool constructing = Array.Exists(session.State.development.buildings,
                b => b.definitionId == "recycling" && !b.completed);
            if (constructing) { OpenPage(Page.Fleet); session.Notify("المصنع قيد البناء؛ يمكنك تجهيز المعدات الآن"); return; }
            ClosePage(); ShowMainMenu(false);
            session.Development.ChooseBuilding("recycling");
        }
        private void PlacedFactoryCard()
        {
            var card = ListCard("مصنع إعادة تدوير الركام", 242);
            bool placed = Array.Exists(session.State.development.buildings, b => b.definitionId == "recycling");
            CardLabel(card, placed ? "المصنع موضوع على الخريطة.\nيمكنك شراء المعدات أثناء البناء؛ تبدأ إزالة الركام بعد اكتماله." :
                "حدد مكان المصنع على أرض نظيفة أو رمال فارغة.\nالتكلفة 15,000 عملة دون مواد بناء.\nلا يمكن وضعه فوق ركام أو مبنى أو شارع.", 62, 100, 18, Muted);
            CardButton(card, placed ? "المصنع موجود" : "اختر موقع المصنع", 0, 177,
                StartFactoryPlacement, !placed && session.State.coins >= GameCatalog.FactoryCost, Teal, 1);
        }
        private void IndividualEquipmentCards()
        {
            const int size = 12;
            var units = session.State.equipmentUnits;
            equipmentPage = Mathf.Clamp(equipmentPage, 0, Math.Max(0, (units.Length - 1) / size));
            for (int i = equipmentPage * size; i < Math.Min(units.Length, (equipmentPage + 1) * size); i++)
            {
                var unit = units[i];
                string name = EquipmentEconomy.Name(unit.kind) + " #" + (i + 1);
                long cost = EquipmentEconomy.UpgradeCost(unit);
                var card = ListCard(name + " · ترقية مستقلة", 240);
                CardLabel(card, "التطوير: " + (unit.level - 1) + " / 3\nسعر الشراء الأصلي: " + N(unit.purchasePrice) +
                    "\nسعر كل تطوير: " + N(cost) + " عملة\nلا تتغير مستويات الآلات الأخرى.", 62, 108, 18, Muted);
                CardButton(card, unit.level >= 4 ? "اكتملت الترقيات الثلاث" : "تطوير " + name, 0, 178,
                    () => Confirm("تطوير " + name, "تكلفة ثابتة: " + N(cost) + " عملة\nالتطوير " + unit.level +
                        " من 3 · يزيد قدرة هذه الآلة فقط.\nلن تُخصم العملات قبل التأكيد.",
                        () => session.Perform(e => e.UpgradeEquipment(unit.id, session.Now))),
                    unit.level < 4 && session.State.jobStage == JobStage.Idle && session.State.coins >= cost, Teal, 1);
            }
            if (units.Length > size)
            {
                var navigation = ListCard("آلات الأسطول · الصفحة " + (equipmentPage + 1), 160);
                CardButton(navigation, "السابق", 0, 78, () => { equipmentPage--; RebuildPage(false); }, equipmentPage > 0, Card, 2);
                CardButton(navigation, "التالي", 1, 78, () => { equipmentPage++; RebuildPage(false); },
                    (equipmentPage + 1) * size < units.Length, Teal, 2);
            }
        }
    }
}