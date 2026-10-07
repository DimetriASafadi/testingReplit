using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private int activityPage;
        private float nextActivitySummary;
        private int liveConstructionCount, liveReadyCount;
        private long nearestConstructionUtc;
        private void RefreshActivitySummary()
        {
            if (Time.unscaledTime < nextActivitySummary) return;
            nextActivitySummary = Time.unscaledTime + 1;
            liveConstructionCount = liveReadyCount = 0; nearestConstructionUtc = long.MaxValue;
            foreach (var item in CityActivityCatalog.Collect(session.State, session.Now))
                if (item.kind == CityActivityKind.Construction || item.kind == CityActivityKind.ProjectConstruction)
                { liveConstructionCount++; nearestConstructionUtc = System.Math.Min(nearestConstructionUtc, item.finishUtc); }
                else if (item.kind != CityActivityKind.Rubble) liveReadyCount++;
        }
        public void OpenActivities() { activityPage = 0; OpenPage(Page.Activities); }
        private void BuildActivities()
        {
            PageHeading("ما يحدث الآن", "حالة العمل الحقيقية · اضغط «اذهب إلى الموقع» للانتقال والزوم دون تغيير المهمة.");
            var items = CityActivityCatalog.Collect(session.State, session.Now);
            const int pageSize = 16;
            int pages = Mathf.Max(1, (items.Count + pageSize - 1) / pageSize);
            activityPage = Mathf.Clamp(activityPage, 0, pages - 1);
            Note("الأحداث الحالية: " + items.Count + " · الصفحة " + (activityPage + 1) + " / " + pages +
                "\nعلامات الخريطة: بناء، دخل، مكافأة. العدد بجانب العلامة يجمع أحداثًا متقاربة؛ تتلاشى العلامات عند الاقتراب.", Muted);
            if (items.Count == 0) Note("لا توجد أعمال جارية أو أحداث جاهزة حاليًا. اختر موقع ركام أو ابدأ بناءً من المتجر.", Muted);
            for (int eventIndex = activityPage * pageSize; eventIndex < Mathf.Min(items.Count, (activityPage + 1) * pageSize); eventIndex++)
            {
                var item = items[eventIndex];
                var captured = item;
                var card = ListCard(item.title, 228);
                var status = CardLabel(card, item.status, 60, 52, 18, Cream);
                Bind(status, () => ActivityStatus(captured));
                CardLabel(card, GameCatalog.Districts[item.district].name, 116, 28, 16, Muted);
                CardButton(card, "اذهب إلى الموقع", 0, 146, () =>
                {
                    ClosePage(); ShowMainMenu(false);
                    session.Development?.FocusActivity(captured);
                    ReleaseCameraAfterTouch();
                }, true, Teal, 1);
            }
            if (pages > 1)
            {
                var navigation = ListCard("بقية الأحداث", 148);
                CardButton(navigation, "السابق", 0, 66, () => { activityPage--; RebuildPage(false); },
                    activityPage > 0, Card, 2);
                CardButton(navigation, "التالي", 1, 66, () => { activityPage++; RebuildPage(false); },
                    activityPage + 1 < pages, Teal, 2);
            }
            bool anyMachine = false;
            for (int index = 0; index < 3; index++)
            {
                if (session.Development == null || !session.Development.ActiveFleet.TryGetMachineAudioState(index,
                    out _, out _, out _, out _)) continue;
                anyMachine = true;
                int capturedIndex = index;
                string name = index == 0 ? "الحفارة" : index == 1 ? "الشاحنة" : "الجرافة";
                var machineCard = ListCard(name + " — موقعها الحالي", 180);
                var machineState = CardLabel(machineCard, "", 58, 40, 18, Cream);
                Bind(machineState, () => MachineStatus(capturedIndex));
                CardButton(machineCard, "اذهب إلى " + name, 0, 104, () =>
                {
                    ClosePage(); ShowMainMenu(false);
                    session.Development.FocusMachine(capturedIndex);
                    ReleaseCameraAfterTouch();
                }, true, Card, 1);
            }
            if (!anyMachine) Note("لا توجد آليات فعّالة على الخريطة الآن.", Muted);
        }
        private string MachineStatus(int index)
        {
            if (!session.Development.ActiveFleet.TryGetMachineAudioState(index, out _, out _,
                out float movement, out float hydraulics)) return "غير موجودة الآن على الخريطة";
            return movement > .01f ? "الآلية تتحرك — الزر يتتبع موقعها الحالي" :
                hydraulics > .01f ? "الآلية تعمل في الموقع" :
                session.State.jobStage == JobStage.Idle ? "متوقفة — لا توجد مهمة إزالة جارية" : "في الموقع أو بانتظار المرحلة التالية";
        }
        private string ActivityStatus(CityActivityItem item)
        {
            if (item.kind == CityActivityKind.Rubble)
                return ActiveWorkPresentation.Status(session.State, session.Now);
            return item.status + (item.finishUtc > session.Now ? "\nالمتبقي: " + TimeLeft(item.finishUtc - session.Now) : "");
        }
    }
}