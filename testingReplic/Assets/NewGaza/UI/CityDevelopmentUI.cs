using System;
using System.Text;
using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed class CityDevelopmentUI : MonoBehaviour
    {
        private CityDevelopment development;
        private GameSession session;
        private Font font;
        private RectTransform safe, titlePanel, actionPanel, shopPanel, shopContent;
        private RectTransform toolbar, shopShield;
        private Canvas canvas;
        private CanvasScaler scaler;
        private ArabicLabel actionHeading;
        private bool regionVisible;
        private int dismissedClaimDistrict = -1, lastFocus = -2;
        public bool StoreOpen => shop != null && shop.activeInHierarchy;
        public bool WorkPanelVisible => actionPanel != null && actionPanel.gameObject.activeInHierarchy;
        private ArabicLabel title, needs, actionText, categoryText;
        private Button action, rotate, dismiss;
        private GameObject shop;
        private int category = -1;
        private Vector3? storeLocation;
        private int lastWidth, lastHeight;
        private Rect lastSafe;
        private readonly Color panel = new Color(7f / 255, 27f / 255, 54f / 255, .96f);
        private readonly Color teal = new Color(22f / 255, 139f / 255, 219f / 255);

        internal void Initialize(CityDevelopment controller, GameSession game)
        {
            development = controller; session = game;
            font = CityTypography.FontFor(CityTextRole.Body);
            if (font == null) throw new InvalidOperationException("Arabic UI font is missing");
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = gameObject.AddComponent<CanvasScaler>();
            canvas.sortingOrder = 25; gameObject.AddComponent<GraphicRaycaster>();
            safe = Rect("Safe area", transform); Stretch(safe);
            titlePanel = Panel("Camera region", safe, false);
            title = Label(titlePanel, "", 19); Box(title.rectTransform, 8, 5, 0, 28, true);
            needs = Label(titlePanel, "", 12); Box(needs.rectTransform, 8, 35, 0, 58, true);
            var closeRegion = Button(titlePanel, "إغلاق", () =>
            { regionVisible = false; session.Hud.EndStoreWindow(); Refresh(); });
            closeRegion.name = "Close region";
            toolbar = Rect("World tools", safe);
            var shopButton = Button(toolbar, "متجر المباني", () => development.OpenStore());
            Box(shopButton.transform as RectTransform, 0, 0, 184, 44, true);
            var regionButton = Button(toolbar, "احتياجات الحي", () => { regionVisible = !regionVisible; Refresh(); });
            Box(regionButton.transform as RectTransform, 192, 0, 148, 44, true);
            actionPanel = Panel("Local work / building confirmation", safe, true);
            actionPanel.GetComponent<Image>().color = new Color(18f / 255, 61f / 255, 112f / 255, .98f);
            actionHeading = Label(actionPanel, "تفاصيل الموقع", 19);
            actionText = Label(actionPanel, "", 15);
            var closeAction = Button(actionPanel, "إغلاق", DismissAction); closeAction.name = "Close work panel";
            action = Button(actionPanel, "تأكيد", DoAction);
            rotate = Button(actionPanel, "تدوير", () => development.Rotate());
            dismiss = Button(actionPanel, "إغلاق", DismissAction);
            shopShield = Panel("Store touch shield", safe, true); Stretch(shopShield);
            shopShield.GetComponent<Image>().color = new Color(0, 0, 0, .8f);
            var storeCanvas = shopShield.gameObject.AddComponent<Canvas>();
            storeCanvas.overrideSorting = true; storeCanvas.sortingOrder = 200;
            shopShield.gameObject.AddComponent<GraphicRaycaster>();
            shop = shopShield.gameObject;
            shopPanel = Panel("Building shop", shopShield, true);
            var heading = Label(shopPanel, "صفحة · متجر المباني", 21); Box(heading.rectTransform, 12, 16, 0, 38, true);
            var close = Button(shopPanel, "إغلاق", CloseStore); Box(close.transform as RectTransform, 12, 60, 90, 36, true);
            var filter = Button(shopPanel, "التصنيف التالي", () => { category = (category + 2) % 8 - 1; PopulateStore(); });
            Box(filter.transform as RectTransform, 112, 60, 150, 36, true);
            var legacy = Button(shopPanel, development.Rules.Data.legacyProgress ? "مشاريع الحفظ السابق" : "البنية التحتية",
                () => { CloseStore(); session.Hud.OpenLegacyProjects(); });
            Box(legacy.transform as RectTransform, 12, 103, 170, 32, true);
            categoryText = Label(shopPanel, "الكل", 15); Box(categoryText.rectTransform, 12, 103, 0, 32, true);
            var viewport = Panel("Scroll viewport", shopPanel, true);
            viewport.anchorMin = new Vector2(0, 0); viewport.anchorMax = new Vector2(1, 1);
            viewport.offsetMin = new Vector2(12, 12); viewport.offsetMax = new Vector2(-12, -146);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.viewport = viewport;
            shopContent = Rect("Building cards", viewport);
            shopContent.anchorMin = new Vector2(0, 1); shopContent.anchorMax = new Vector2(1, 1);
            shopContent.pivot = new Vector2(.5f, 1); shopContent.sizeDelta = Vector2.zero;
            var layout = shopContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            shopContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = shopContent; shop.SetActive(false);
            Resize(); Refresh();
        }

        private void Update()
        {
            if (lastWidth != Screen.width || lastHeight != Screen.height || lastSafe != Screen.safeArea) Resize();
        }

        private void Resize()
        {
            lastWidth = Screen.width; lastHeight = Screen.height; lastSafe = Screen.safeArea;
            if (!CityUiLayout.Apply(canvas, scaler, safe)) return;
            float safeWidth = safe.rect.width, safeHeight = safe.rect.height;
            bool portrait = safeHeight > safeWidth;
            float width = Mathf.Min(safeWidth - 24, 420);
            float toolbarTop = portrait ? 308 : 156;
            Box(toolbar, 12, toolbarTop, 340, 44, true);
            Box(titlePanel, 12, toolbarTop + 52, width, 142, true);
            Box(title.rectTransform, 12, 8, width - 110, 32, false);
            Box(needs.rectTransform, 12, 48, width - 24, 84, false);
            Box(titlePanel.Find("Close region") as RectTransform, 8, 8, 84, 36, true);
            actionPanel.anchorMin = new Vector2(.5f, 0); actionPanel.anchorMax = new Vector2(.5f, 0);
            actionPanel.pivot = new Vector2(.5f, 0); actionPanel.anchoredPosition = new Vector2(0, 116);
            actionPanel.sizeDelta = new Vector2(Mathf.Min(safeWidth - 24, 620), 212);
            if (!portrait)
            {
                actionPanel.anchorMin = actionPanel.anchorMax = Vector2.zero;
                actionPanel.pivot = Vector2.zero; actionPanel.anchoredPosition = new Vector2(12, 116);
                actionPanel.sizeDelta = new Vector2(Mathf.Min(safeWidth * .47f, 620), 212);
            }
            shopPanel.anchorMin = shopPanel.anchorMax = shopPanel.pivot = new Vector2(.5f, .5f);
            shopPanel.anchoredPosition = Vector2.zero;
            shopPanel.sizeDelta = new Vector2(Mathf.Min(safeWidth - 24, 850), Mathf.Min(safeHeight - 24, 900));
            Box(categoryText.rectTransform, 12, 103, shopPanel.sizeDelta.x - 206, 32, false);
            categoryText.alignment = TextAnchor.UpperLeft;
            float actionWidth = actionPanel.sizeDelta.x;
            Box(actionHeading.rectTransform, 12, 8, actionWidth - 112, 32, false);
            Box(actionPanel.Find("Close work panel") as RectTransform, 8, 8, 84, 36, true);
            Box(actionText.rectTransform, 12, 48, actionWidth - 24, 96, false);
            float buttonWidth = (actionPanel.sizeDelta.x - 32) / 3;
            Box(action.transform as RectTransform, 8, 152, buttonWidth, 44, false);
            Box(rotate.transform as RectTransform, 16 + buttonWidth, 152, buttonWidth, 44, false);
            Box(dismiss.transform as RectTransform, 24 + buttonWidth * 2, 152, buttonWidth, 44, false);
        }

        public void OpenStore() => OpenStore(null);
        internal void OpenStore(Vector3? target)
        {
            session.Hud.BeginStoreWindow();
            storeLocation = target; shop.SetActive(true); CityCamera.ModalOpen = true; Resize(); PopulateStore(); Refresh();
        }

        public void CloseStore()
        {
            bool wasOpen = StoreOpen;
            shop.SetActive(false);
            if (wasOpen && session.Hud != null) session.Hud.EndStoreWindow();
            Refresh();
        }

        internal void DismissAction()
        {
            development.ClearSelection();
            dismissedClaimDistrict = development.FocusDistrict;
            session.Hud.EndStoreWindow();
            Refresh();
        }

        private void PopulateStore()
        {
            foreach (Transform child in shopContent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            categoryText.SetText(category < 0 ? "كل المباني" : CityBuildingCatalog.CategoryNames[category]);
            foreach (var definition in CityBuildingCatalog.All)
            {
                if (category >= 0 && (int)definition.category != category) continue;
                var captured = definition;
                var row = Button(shopContent,
                    definition.name + "  ·  " + definition.widthMeters + " × " + definition.depthMeters + " متر\n" +
                    definition.cost + " عملة — البناء بالمال فقط\n" +
                    definition.duration + " ثانية  ·  " + definition.hourlyIncome + " دخل/ساعة" +
                    (definition.EquipmentDepot ? "  ·  نقطة انطلاق معدات" : ""),
                    () =>
                    {
                        var target = storeLocation;
                        development.ChooseBuilding(captured.id);
                        if (target.HasValue) development.PreviewAt(target.Value);
                    });
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 106;
                var text = row.GetComponentInChildren<ArabicLabel>(); CityTypography.Apply(text, CityTextRole.Small);
                text.alignment = TextAnchor.MiddleRight;
                text.rectTransform.offsetMin = new Vector2(12, 4); text.rectTransform.offsetMax = new Vector2(-12, -4);
            }
            shopContent.anchoredPosition = Vector2.zero;
        }

        internal void Refresh()
        {
            if (title == null || development.Rules == null) return;
            int d = development.FocusDistrict;
            if (lastFocus != d) { lastFocus = d; dismissedClaimDistrict = -1; }
            bool blocked = session.Hud != null && session.Hud.BlockingWindowVisible;
            toolbar.gameObject.SetActive(!blocked && !StoreOpen);
            titlePanel.gameObject.SetActive(regionVisible && !blocked && !StoreOpen);
            if (d < 0) { title.SetText("خارج الأحياء — الساحل أو حدود المدينة"); needs.SetText(""); }
            else
            {
                title.SetText(GameCatalog.Districts[d].name + (session.State.districts[d].unlocked ? "" : " — مقفل"));
                var required = CityDevelopmentService.Needs(d); var supplied = CityDevelopmentService.Supplied(session.State, d);
                string[] names = { "إسكان", "زراعة", "صناعة", "تجارة", "رفاهية", "إسكان عمراني/سياحي" };
                var text = new StringBuilder("احتياجات الحي: ");
                for (int n = 0; n < required.Length; n++)
                    if (required[n] > 0) text.Append(names[n]).Append(" ").Append(Math.Min(required[n], supplied[n]))
                        .Append("/").Append(required[n]).Append("  ");
                needs.SetText(text.ToString());
            }
            bool placing = development.Placing;
            bool site = development.SelectedSite != null;
            bool building = development.SelectedBuilding != null;
            bool claim = d >= 0 && session.State.districts[d].unlocked && !session.State.districts[d].rewardClaimed &&
                CityDevelopmentService.Complete(session.State, d);
            actionPanel.gameObject.SetActive(!blocked && !StoreOpen &&
                (placing || site || building || (claim && dismissedClaimDistrict != d)));
            actionHeading.SetText(placing ? "تأكيد البناء" : site ? "تفاصيل موقع الركام" :
                building ? "تفاصيل المبنى" : "مكافأة الحي");
            SetButton(dismiss, placing ? "إلغاء الوضع" : "إغلاق");
            rotate.gameObject.SetActive(placing);
            if (placing)
            {
                var definition = development.Chosen;
                actionText.SetText(definition.name + " · " + definition.widthMeters + " × " + definition.depthMeters +
                    " م · " + definition.cost + " عملة\n" +
                    (string.IsNullOrEmpty(development.PlacementProblem) ? "مساحة نظيفة مناسبة — أكّد البناء أو غيّر الموقع" : development.PlacementProblem));
                SetButton(action, "تأكيد البناء");
                action.interactable = string.IsNullOrEmpty(development.PlacementProblem);
            }
            else if (site)
            {
                var selected = development.Rules.Site(development.SelectedSite);
                bool active = development.Rules.Data.activeRubbleId == selected.id;
                actionText.SetText(selected.cleared ? "أرض نظيفة — اختر مبنى من المتجر وضعه هنا أو على أي مساحة نظيفة مناسبة" :
                    active ? "الآليات في الطريق أو تعمل على الإزالة والنقل والتدوير\n" + StageText(session.State.jobStage) :
                    "موارد للبيع بقيمة تقارب " + RubbleEconomy.Reward(selected) + " عملة" +
                    "\nمصنع تدوير ← حفار وجرافة وشاحنة ← وصول عبر الطرق" +
                    "\nعمل نحو دقيقة بالمعدات الأساسية ثم العودة إلى المصنع. بع الموارد من المخزن لتحصل على المال.");
                SetButton(action, selected.cleared ? "اختر مبنى" : active ? "قيد التنفيذ" : "إزالة الدمار");
                action.interactable = selected.cleared || session.State.jobStage == JobStage.Idle;
            }
            else if (building)
            {
                var selected = development.Rules.Building(development.SelectedBuilding);
                var definition = CityBuildingCatalog.Find(selected.definitionId);
                long income = CityDevelopmentService.PendingIncome(selected, session.Now);
                actionText.SetText(definition.name + (selected.completed ? " — مكتمل" : " — قيد البناء") +
                    "\n" + (selected.completed ? "دخل جاهز: " + income + " عملة" : "المتبقي: " + Math.Max(0, selected.finishUtc - session.Now) + " ثانية") +
                    (definition.EquipmentDepot ? "\nنقطة انطلاق للآليات عند اختيار موقع العمل الأقرب" : ""));
                SetButton(action, "جمع الدخل"); action.interactable = income > 0;
            }
            else if (claim)
            {
                actionText.SetText("اكتملت إزالة الركام واحتياجات " + GameCatalog.Districts[d].name + " — استلم المكافأة لفتح الحي التالي");
                SetButton(action, "استلام المكافأة"); action.interactable = true;
            }
        }

        internal void SetHomeVisible(bool visible)
        {
            GetComponentInChildren<Canvas>(true).gameObject.SetActive(!visible);
            if (!visible) { Resize(); Refresh(); }
        }

        private static string StageText(JobStage stage) => stage == JobStage.Clearing ? "الانتقال ثم إزالة الركام" :
            stage == JobStage.Hauling ? "نقل الركام إلى المصنع" : stage == JobStage.Recycling ? "إعادة التدوير" : "جاهز";

        private void DoAction()
        {
            if (development.Placing) development.Confirm();
            else if (development.SelectedSite != null)
            {
                if (development.Rules.Site(development.SelectedSite).cleared) development.BuildOnSelectedLand();
                else development.StartClear();
            }
            else if (development.SelectedBuilding != null) development.Collect();
            else development.ClaimRegion();
        }

        private RectTransform Rect(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform)); root.transform.SetParent(parent, false);
            return (RectTransform)root.transform;
        }

        private RectTransform Panel(string name, Transform parent, bool block)
        {
            var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>();
            image.color = panel; image.raycastTarget = block; return rect;
        }

        private ArabicLabel Label(Transform parent, string text, int size)
        {
            var rect = Rect("Arabic text", parent);
            var label = rect.gameObject.AddComponent<ArabicLabel>(); CityTypography.Apply(label, CityTypography.RoleForSize(size));
            label.color = new Color(.97f, .94f, .85f); label.alignment = TextAnchor.UpperRight;
            label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate; label.SetText(text); return label;
        }

        private Button Button(Transform parent, string text, UnityEngine.Events.UnityAction callback)
        {
            var rect = Rect(text, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = teal;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            rect.gameObject.AddComponent<CityButtonMotion>();
            button.onClick.AddListener(callback);
            var label = Label(rect, text, 15); Stretch(label.rectTransform); label.alignment = TextAnchor.MiddleCenter;
            CityTypography.Apply(label, CityTextRole.Button);
            return button;
        }

        private static void SetButton(Button button, string value) => button.GetComponentInChildren<ArabicLabel>().SetText(value);
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Box(RectTransform rect, float x, float y, float width, float height, bool right)
        {
            rect.anchorMin = new Vector2(right ? 1 : 0, 1); rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(right ? 1 : 0, 1); rect.anchoredPosition = new Vector2(right ? -x : x, -y);
            if (width == 0)
            { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -y); rect.sizeDelta = new Vector2(-x * 2, height); }
            else rect.sizeDelta = new Vector2(width, height);
        }
    }
}