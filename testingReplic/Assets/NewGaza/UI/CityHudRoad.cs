using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private void OnRoadSelected(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                if (page == Page.Road) ClosePage();
                return;
            }
            selectedPlot = -1;
            selectionObject.SetActive(false);
            OpenPage(Page.Road);
        }

        private void BuildRoad()
        {
            var segment = world.Roads.FindSegment(session.SelectedRoadId);
            PageHeading("إصلاح وتطوير الشارع", "السرعة تتغيّر على المقطع الذي تسير عليه الآلية، لا على شبكة المدينة كلها.");
            if (segment == null) { Note("اضغط على شارع في الخريطة لاختيار مقطعه.", Muted); return; }
            var definition = segment.Definition;
            bool drivable = CityRoadNetwork.IsDrivable(segment.Kind);
            int level = RoadEconomy.GetLevel(session.State, definition.id);
            string condition = level == 0 ? "رملي / مدمّر" : level == 1 ? "مصلح ومسوّى" : "مرصوف ومطوّر";
            var card = ListCard(definition.name, 294);
            CardLabel(card, "المقطع: " + definition.id + "\nالطول: " +
                Mathf.RoundToInt(definition.lengthMeters) + " متر · " + condition,
                58, 70, 19, Cream);
            CardLabel(card, drivable ? "سرعة الشاحنة على المقطع: " +
                (15.84f * RoadEconomy.SpeedMultiplier(level)).ToString("0.0") +
                " كم/ساعة\nرملي 7.9 ← مصلح 15.8 ← مرصوف 31.7 كم/ساعة" :
                "هذا مسار مشاة أو دراجات؛ لا تسلكه الآليات الثقيلة.\nتحسينه يغيّر سطحه فقط، ولا يزيد سرعة نقل الآليات.",
                138, 80, 18, Muted);
            CardButton(card, "إظهار المقطع على الخريطة", 0, 228, () =>
            {
                ClosePage();
                cityCamera.Focus(RoadMidpoint(segment));
            }, true, Card, 1);
            RoadActionCard(definition, 1, level, drivable);
            RoadActionCard(definition, 2, level, drivable);
            Note("الطرق الطويلة مقسمة كل كيلومتر تقريبًا؛ الطرق الأقصر تبقى مقاطع مستقلة. " +
                "حالة الطرق تمثيل للّعبة وليست مسحًا لحالة الشوارع الحالية. " +
                "إصلاح الشوارع مستقل عن مشروع الطريق ضمن إنجاز الحي.", Muted);
            if (!string.IsNullOrEmpty(world.Fleet.RouteStatus))
                Note(world.Fleet.RouteStatus, Muted);
        }

        private static Vector3 RoadMidpoint(CityRoadSegment segment)
        {
            float remaining = segment.Definition.lengthMeters / 40f;
            for (int i = 1; i < segment.Points.Length; i++)
            {
                float length = Vector3.Distance(segment.Points[i - 1], segment.Points[i]);
                if (length >= remaining && length > 0f)
                    return Vector3.Lerp(segment.Points[i - 1], segment.Points[i], remaining / length);
                remaining -= length;
            }
            return segment.Points[segment.Points.Length - 1];
        }

        private void RoadActionCard(RoadSegmentDefinition definition, int target, int current, bool drivable)
        {
            var cost = RoadEconomy.GetCost(definition, target);
            string title = target == 1 ? "إصلاح وتسوية المقطع" : "تطوير ورصف المقطع";
            var card = ListCard(title, 260);
            CardLabel(card, !drivable ? "تحسين بصري لسطح المسار؛ لا يُستخدم لنقل الآليات الثقيلة." : target == 1
                ? "إزالة آثار التلف وتسوية التربة · تصبح السرعة ضعف الطريق المدمّر."
                : "أسفلت وأرصفة وعلامات أرضية · تصبح السرعة ضعف الطريق المصلح.",
                57, 68, 18, Muted);
            CardLabel(card, N(cost.coins) + " عملة · خرسانة " + cost.concrete +
                " · حديد " + cost.iron, 134, 45, 19, Cream);
            bool next = current == target - 1;
            string label = current >= target ? "تم التنفيذ" : !next ? "أصلح المقطع أولاً" : "مراجعة وتأكيد التكلفة";
            var button = CardButton(card, label, 0, 190, () =>
                Confirm(title, definition.name + "\nطول المقطع: " +
                    Mathf.RoundToInt(definition.lengthMeters) + " متر\nسيُخصم " +
                    N(cost.coins) + " عملة · خرسانة " + cost.concrete + " · حديد " + cost.iron +
                    "\nلا تُخصم الموارد قبل التأكيد.",
                    () =>
                    {
                        session.ImproveRoad(definition.id, target);
                        if (page == Page.Road) RebuildPage(true);
                    }), next, Teal, 1);
            if (next) BindButton(button, () => session.State.coins >= cost.coins &&
                session.State.stock.concrete >= cost.concrete && session.State.stock.iron >= cost.iron &&
                RoadEconomy.GetLevel(session.State, definition.id) == target - 1);
            if (next) CardLabel(card, "يتطلب رصيدًا ومواد كافية؛ التكلفة حسب طول المقطع.", 236, 22, 14, Muted);
        }
    }
}