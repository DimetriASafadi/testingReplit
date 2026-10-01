using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NewGaza
{
    /// <summary>Native, session-owned Arabic mobile interface. No scene prefab or web view is required.</summary>
    public sealed class CityHud : MonoBehaviour
    {
        private enum Page { None, Map, Projects, Fleet, Investments, Resources, Gift, Settings, Finale }
        private sealed class Binding
        {
            public ArabicLabel label;
            public Func<string> value;
            public Button button;
            public Func<bool> enabled;
            public Image fill;
            public Func<float> progress;
        }

        private static readonly Color Navy = new Color32(13, 30, 45, 255);
        private static readonly Color Panel = new Color32(22, 43, 58, 248);
        private static readonly Color Card = new Color32(30, 53, 67, 255);
        private static readonly Color Cream = new Color32(246, 240, 219, 255);
        private static readonly Color Muted = new Color32(167, 190, 190, 255);
        private static readonly Color Teal = new Color32(45, 155, 142, 255);
        private static readonly Color Gold = new Color32(230, 190, 103, 255);
        private static readonly Color Red = new Color32(248, 163, 140, 255);
        private GameSession session;
        private CityWorld world;
        private CityCamera cityCamera;
        private Font arabicFont;
        private Canvas canvas;
        private CanvasScaler scaler;
        private RectTransform safe;
        private RectTransform header, activity, tutorial, navigation, selection;
        private ArabicLabel titleLabel, balanceLabel, resourceLabel, districtLabel, activityLabel, tutorialLabel, selectionLabel;
        private Image districtFill;
        private Button tutorialButton, selectionButton;
        private GameObject selectionObject, tutorialObject, modalObject, confirmationObject, toastObject;
        private RectTransform modalPanel, modalContent, confirmationPanel, toastPanel;
        private ScrollRect modalScroll;
        private ArabicLabel modalTitle, modalSubtitle, toastLabel;
        private InputField nameInput;
        private readonly List<Binding> modalBindings = new List<Binding>();
        private Page page;
        private int selectedPlot = -1;
        private int lastDistrict = -1;
        private bool changed = true;
        private string structuralKey = "";
        private float nextRefresh;
        private float toastUntil;
        private float releaseBlockUntil;
        private bool closingCameraBlock;
        private Rect previousSafe;
        private int previousWidth, previousHeight;
        private Action pendingConfirmation;
        private Sprite roundedSprite;
        private bool initialized;
        private bool finaleShown;

        public void Initialize(GameSession gameSession, CityWorld cityWorld, CityCamera camera)
        {
            if (initialized) return;
            if (gameSession == null || gameSession.State == null) throw new ArgumentException("جلسة اللعبة غير جاهزة");
            session = gameSession;
            world = cityWorld;
            cityCamera = camera;
            arabicFont = Resources.Load<Font>("NewGazaArabic");
            if (arabicFont == null)
                throw new InvalidOperationException("الخط العربي مفقود: Resources/NewGazaArabic.ttf");
            roundedSprite = CreateRoundedSprite();
            EnsureEventSystem();
            BuildCanvas();
            BuildHud();
            session.Changed += OnChanged;
            session.Notification += ShowToast;
            session.PlotSelected += OnPlotSelected;
            initialized = true;
            LayoutSafeArea(true);
            RefreshHud();
            structuralKey = StateKey();
            finaleShown = session.State.cityCompletedUtc > 0;
        }

        private static void EnsureEventSystem()
        {
            EventSystem events = EventSystem.current;
            if (events == null)
                events = new GameObject("New Gaza touch event system", typeof(EventSystem)).GetComponent<EventSystem>();
            var legacy = events.GetComponent<StandaloneInputModule>();
            if (legacy != null) { legacy.enabled = false; Destroy(legacy); }
            var input = events.GetComponent<InputSystemUIInputModule>();
            if (input == null) input = events.gameObject.AddComponent<InputSystemUIInputModule>();
            if (input.actionsAsset == null) input.AssignDefaultActions();
            input.enabled = true;
        }

        private void BuildCanvas()
        {
            var root = new GameObject("Native Arabic interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referenceResolution = new Vector2(1280, 720);
            safe = Rect("Safe area", root.transform);
            Stretch(safe, 0, 0, 0, 0);
        }

        private void BuildHud()
        {
            header = Surface("City header", safe, Panel);
            titleLabel = Label(header, "غزة الجديدة", 32, Gold);
            balanceLabel = Label(header, "", 25, Cream);
            resourceLabel = Label(header, "", 18, Muted);
            var gift = ActionButton(header, "هدية", () => OpenPage(Page.Gift), Teal);
            gift.name = "Daily gift";
            var settings = ActionButton(header, "خيارات", () => OpenPage(Page.Settings), Card);
            settings.name = "Settings";
            var districtStrip = Rect("Neighborhood progress", header);
            districtLabel = Label(districtStrip, "", 18, Cream);
            var track = Surface("Progress track", districtStrip, Navy);
            districtFill = AddImage(Rect("Progress fill", track), Teal);
            Stretch(districtFill.rectTransform, 0, 0, 0, 0);
            districtFill.type = Image.Type.Filled;
            districtFill.fillMethod = Image.FillMethod.Horizontal;
            districtFill.fillOrigin = 1; // RTL progress

            activity = Surface("Active work", safe, Panel);
            activityLabel = Label(activity, "", 18, Cream);
            Stretch(activityLabel.rectTransform, 14, 10, 14, 10);
            activityLabel.alignment = TextAnchor.UpperRight;

            tutorial = Surface("Next useful step", safe, Panel);
            tutorialObject = tutorial.gameObject;
            tutorialLabel = Label(tutorial, "", 18, Cream);
            tutorialLabel.alignment = TextAnchor.UpperRight;
            tutorialButton = ActionButton(tutorial, "ابدأ", TutorialAction, Teal);

            selection = Surface("Deliberate selection", safe, Panel);
            selectionObject = selection.gameObject;
            selectionLabel = Label(selection, "", 17, Cream);
            selectionLabel.alignment = TextAnchor.UpperRight;
            selectionButton = ActionButton(selection, "تفاصيل", SelectionAction, Teal);
            var deselect = ActionButton(selection, "×", ClearSelection, Card);
            deselect.name = "Clear selection";
            selectionObject.SetActive(false);

            navigation = Surface("Bottom navigation", safe, Navy);
            AddNav("الخريطة", Page.Map);
            AddNav("المشاريع", Page.Projects);
            AddNav("الأسطول", Page.Fleet);
            AddNav("استثمارات", Page.Investments);
            AddNav("الموارد", Page.Resources);

            toastPanel = Surface("Notification", safe, Navy);
            toastObject = toastPanel.gameObject;
            toastLabel = Label(toastPanel, "", 20, Cream);
            Stretch(toastLabel.rectTransform, 20, 12, 20, 12);
            toastPanel.GetComponent<Image>().raycastTarget = false;
            toastObject.SetActive(false);
        }

        private void AddNav(string text, Page target)
        {
            var button = ActionButton(navigation, text, () => OpenPage(target), Panel);
            button.name = target.ToString();
        }

        private void Update()
        {
            if (!initialized) return;
            LayoutSafeArea(false);
            if (closingCameraBlock && Time.unscaledTime >= releaseBlockUntil && !PointerHeld())
            {
                closingCameraBlock = false;
                CityCamera.ModalOpen = modalObject != null || confirmationObject != null;
            }
            if (toastObject.activeSelf && Time.unscaledTime > toastUntil) toastObject.SetActive(false);
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (confirmationObject != null) CloseConfirmation();
                else if (page != Page.None) ClosePage();
                else OpenPage(Page.Settings);
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.35f;
            if (session.State.selectedDistrict != lastDistrict)
            {
                selectedPlot = -1;
                selectionObject.SetActive(false);
                changed = true;
            }
            if (changed)
            {
                string key = StateKey();
                if (key != structuralKey)
                {
                    structuralKey = key;
                    // Preserve the name field, open confirmation, and scroll position.
                    // Timers never enter this key, so one-second Changed events don't rebuild UI.
                    if (page != Page.None && page != Page.Settings && confirmationObject == null) RebuildPage(true);
                }
                changed = false;
            }
            RefreshHud();
            RefreshBindings();
            if (!finaleShown && session.State.cityCompletedUtc > 0 && confirmationObject == null)
            {
                finaleShown = true;
                OpenPage(Page.Finale);
            }
        }

        private static bool PointerHeld()
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches) if (touch.press.isPressed) return true;
            return false;
        }

        private void LayoutSafeArea(bool force)
        {
            Rect area = Screen.safeArea;
            if (!force && area == previousSafe && previousWidth == Screen.width && previousHeight == Screen.height) return;
            previousSafe = area;
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            bool portrait = Screen.height > Screen.width;
            scaler.referenceResolution = portrait ? new Vector2(720, 1280) : new Vector2(1280, 720);
            safe.anchorMin = new Vector2(area.xMin / Math.Max(1, Screen.width), area.yMin / Math.Max(1, Screen.height));
            safe.anchorMax = new Vector2(area.xMax / Math.Max(1, Screen.width), area.yMax / Math.Max(1, Screen.height));
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            float width = safe.rect.width;
            float height = safe.rect.height;
            if (width < 10 || height < 10) return;
            float head = portrait ? 170 : 132;
            Place(header, 12, 12, width - 24, head, true);
            float titleWidth = portrait ? width - 270 : 265;
            Place(titleLabel.rectTransform, width - 24 - titleWidth - 14, 6, titleWidth, 48, true);
            Place(header.Find("Daily gift") as RectTransform, 12, 9, 96, 64, true);
            Place(header.Find("Settings") as RectTransform, 116, 9, 96, 64, true);
            if (portrait)
            {
                Place(balanceLabel.rectTransform, 14, 71, width - 52, 38, true);
                Place(resourceLabel.rectTransform, 14, 110, width - 52, 28, true);
            }
            else
            {
                Place(balanceLabel.rectTransform, 235, 7, Math.Max(160, width - 565), 43, true);
                Place(resourceLabel.rectTransform, 235, 52, Math.Max(160, width - 290), 29, true);
            }
            var strip = header.Find("Neighborhood progress") as RectTransform;
            Place(strip, 14, head - 32, width - 52, 27, true);
            Place(districtLabel.rectTransform, strip.rect.width * 0.44f, 0, strip.rect.width * 0.56f, 26, true);
            Place(strip.Find("Progress track") as RectTransform, 0, 9, strip.rect.width * 0.40f, 8, true);
            Place(activity, 12, head + 24, portrait ? width - 24 : 355, portrait ? 102 : 116, true);
            Place(navigation, 12, 12, width - 24, 84, false);
            float navWidth = (width - 44) / 5f;
            for (int i = 0; i < navigation.childCount; i++)
                Place(navigation.GetChild(i) as RectTransform, 10 + (4 - i) * navWidth, 10, navWidth - 5, 64, false);
            float tutorialWidth = portrait ? width - 24 : Mathf.Min(420, width * 0.38f);
            Place(tutorial, 12, 108, tutorialWidth, 122, false);
            Place(tutorialLabel.rectTransform, 128, 10, tutorialWidth - 142, 101, true);
            Place(tutorialButton.GetComponent<RectTransform>(), 12, 29, 104, 64, true);
            float selectionWidth = portrait ? width - 24 : Mathf.Min(530, width * 0.47f);
            Place(selection, width - selectionWidth - 12, 108, selectionWidth, 154, false);
            Place(selectionLabel.rectTransform, 134, 10, selectionWidth - 150, 125, true);
            Place(selectionButton.GetComponent<RectTransform>(), 12, 75, 110, 64, true);
            Place(selection.Find("Clear selection") as RectTransform, 12, 8, 64, 64, true);
            Place(toastPanel, Mathf.Max(12, (width - 620) * 0.5f), 105, Mathf.Min(width - 24, 620), 104, false);
            LayoutModal();
            if (confirmationPanel != null)
                Center(confirmationPanel, Mathf.Min(width - 32, 620), Mathf.Min(height - 32, 500));
            // Children use stretch anchors and ArabicLabel reflows on width change.
            // A rotation must not recreate an in-progress native text field.
        }

        private void RefreshHud()
        {
            var s = session.State;
            int d = s.selectedDistrict;
            lastDistrict = d;
            titleLabel.SetText("غزة الجديدة");
            balanceLabel.SetText(N(s.coins) + "  عملة");
            resourceLabel.SetText("خرسانة " + N(s.stock.concrete) + "   ·   حديد " + N(s.stock.iron) +
                "   ·   خشب " + N(s.stock.wood) + "   ·   أخرى " + N(s.stock.other));
            districtLabel.SetText(GameCatalog.Districts[d].name + "   ·   " + Percent(session.Economy.Progress(d)) + " إنجاز");
            districtFill.fillAmount = session.Economy.Progress(d);
            var text = new StringBuilder();
            if (s.jobStage == JobStage.Idle)
                text.Append("فريق التدوير جاهز\n").Append("الركام المحلي: ").Append(s.districts[d].clearedLoads)
                    .Append(" / ").Append(GameCatalog.Districts[d].rubbleLoads).Append(" دفعات");
            else text.Append(Stage(s.jobStage)).Append(" · ").Append(TimeLeft(s.jobFinishUtc - session.Now))
                    .Append("\n").Append(GameCatalog.Districts[s.jobDistrict].name).Append(" · انتقال المراحل تلقائي");
            int activeCount = 0;
            long nearest = long.MaxValue;
            for (int i = 0; i < s.districts.Length; i++)
                foreach (var p in s.districts[i].projects)
                    if (p.finishUtc > session.Now)
                    {
                        activeCount++;
                        nearest = Math.Min(nearest, p.finishUtc);
                    }
            text.Append("\n").Append(activeCount > 0 ? "قيد التنفيذ: " + activeCount + " · أقرب إنجاز " + TimeLeft(nearest - session.Now) : "المشاريع: اختر قطعة للاطلاع قبل البناء");
            activityLabel.SetText(text.ToString());
            RefreshTutorial();
            RefreshSelection();
            bool portrait = Screen.height > Screen.width;
            tutorialObject.SetActive(!(portrait && selectedPlot != -1));
        }

        private int TutorialStep()
        {
            var s = session.State;
            if (s.factoryLevel == 0) return 0;
            if (s.excavators == 0) return 1;
            if (s.trucks < 2) return 2;
            if (s.bulldozers == 0) return 3;
            if (s.jobStage != JobStage.Idle) return 5;
            if (s.districts[s.selectedDistrict].clearedLoads == 0) return 4;
            if (session.Economy.Progress(s.selectedDistrict) >= 1) return 8;
            if (s.coins < 18000) return 6;
            return 7;
        }

        private void RefreshTutorial()
        {
            string text, action;
            switch (TutorialStep())
            {
                case 0: text = "خطوتك الأولى\nمصنع التدوير يحوّل الركام إلى مواد بناء.\nميزانية البداية: 50,000 عملة"; action = "المصنع"; break;
                case 1: text = "جهّز فريقك\nاشتر حفارة واحدة لإزالة الركام.\nالتكلفة: 8,000 عملة"; action = "حفارة"; break;
                case 2: text = "النقل أولاً\nشاحنتان تسرّعان النقل.\n6,000 عملة لكل شاحنة"; action = "شاحنات"; break;
                case 3: text = "آخر معدات البداية\nاشتر جرافة واحدة بـ 5,000.\nالحزمة كاملة: 40,000 عملة"; action = "جرافة"; break;
                case 4: text = "ابدأ الإعمار\nأزل الركام ثم انقله ودوّره تلقائياً.\nالمواد تُحفظ للبناء أو البيع."; action = "إزالة"; break;
                case 5: text = "العمل مستمر\nالمراحل تعمل أثناء غيابك.\nابدأ الزراعة لدخل قابل للتكرار."; action = "استثمر"; break;
                case 6: text = "نمِّ ميزانيتك\nزراعة بـ 500 تعطي 1,500 بعد 5 دقائق.\nاحتفظ بمواد للمياه والمنازل."; action = "استثمر"; break;
                case 8: text = "الحي جاهز 100%\nاستلم مكافأته لفتح الحي التالي.\nشارع الرشيد بعد الأحياء العشرة."; action = "مكافأة"; break;
                default: text = "ابنِ بالتدرّج\nابدأ بشبكة المياه؛ ثم المنزل والطريق.\nراجع المواد والمتطلبات أولاً."; action = "المشاريع"; break;
            }
            tutorialLabel.SetText(text);
            ButtonText(tutorialButton, action);
        }

        private void TutorialAction()
        {
            switch (TutorialStep())
            {
                case 0: case 1: case 2: case 3: OpenPage(Page.Fleet); break;
                case 4: selectedPlot = -2; ShowSelection(); break;
                case 5: case 6: OpenPage(Page.Investments); break;
                case 8: OpenPage(Page.Map); break;
                default: OpenPage(Page.Projects); break;
            }
        }

        private void OnChanged() { changed = true; }

        private void OnPlotSelected(int index)
        {
            selectedPlot = index;
            if (index == -3) { OpenPage(Page.Fleet); return; }
            if (index == -1) { ClearSelection(); return; }
            // World taps only reveal context; no economy action occurs in this handler.
            ShowSelection();
        }

        private void ShowSelection()
        {
            selectionObject.SetActive(selectedPlot != -1);
            RefreshSelection();
            if (toastObject != null) toastObject.transform.SetAsLastSibling();
        }

        private void ClearSelection()
        {
            selectedPlot = -1;
            selectionObject.SetActive(false);
            if (world != null) world.SetSelectedPlot(-1);
            ReleaseCameraAfterTouch();
        }

        private void RefreshSelection()
        {
            if (selectedPlot == -1) return;
            int d = session.State.selectedDistrict;
            if (selectedPlot == -2)
            {
                bool imported = session.State.districts[d].clearedLoads >= GameCatalog.Districts[d].rubbleLoads;
                selectionLabel.SetText(imported
                    ? "تدوير ركام مستورد\nيوفّر مواد فقط؛ لا يزيد إنجاز الحي.\nإزالة ثم نقل ثم تدوير"
                    : "إزالة الركام المحلي\nالمراحل: إزالة ثم نقل ثم تدوير.\nتُضاف المواد إلى المخزن تلقائياً.");
                ButtonText(selectionButton, session.State.jobStage == JobStage.Idle ? "تفاصيل" : "قيد العمل");
                return;
            }
            var definitions = GameCatalog.Districts[d].projects;
            if (selectedPlot < 0 || selectedPlot >= definitions.Length) { ClearSelection(); return; }
            var p = definitions[selectedPlot];
            string shortStatus = ProjectStatus(d, p);
            int lineBreak = shortStatus.IndexOf('\n');
            if (lineBreak >= 0) shortStatus = shortStatus.Substring(0, lineBreak);
            selectionLabel.SetText(p.name + "\n" + shortStatus + "\n" +
                N(p.cost) + " عملة · " + Duration(p.durationSeconds) + "\n" +
                "خرسانة " + p.concreteCost + " · حديد " + p.ironCost);
            ButtonText(selectionButton, "تفاصيل");
        }

        private void SelectionAction()
        {
            if (selectedPlot == -2) { OpenPage(Page.Resources); return; }
            OpenPage(Page.Projects);
            // The selected project is displayed first, while preserving the remaining catalog.
        }

        private void OpenPage(Page requested)
        {
            if (requested == Page.None) { ClosePage(); return; }
            if (confirmationObject != null) CloseConfirmation();
            if (modalObject != null) Destroy(modalObject);
            modalObject = null;
            modalBindings.Clear();
            nameInput = null;
            page = requested;
            CityCamera.ModalOpen = true;
            closingCameraBlock = false;
            BuildModalShell();
            RebuildPage(false);
            if (toastObject != null) toastObject.transform.SetAsLastSibling();
        }

        private void BuildModalShell()
        {
            var blocker = Surface("Modal touch shield", safe, new Color(0.015f, 0.035f, 0.055f, 0.80f), false);
            Stretch(blocker, 0, 0, 0, 0);
            modalObject = blocker.gameObject;
            modalPanel = Surface("Modal panel", blocker, Panel);
            modalTitle = Label(modalPanel, "", 30, Gold);
            modalSubtitle = Label(modalPanel, "", 17, Muted);
            var close = ActionButton(modalPanel, "إغلاق ×", ClosePage, Card);
            close.name = "Close modal";
            var scroll = Rect("Scrollable page", modalPanel);
            modalScroll = scroll.gameObject.AddComponent<ScrollRect>();
            modalScroll.horizontal = false;
            modalScroll.vertical = true;
            modalScroll.movementType = ScrollRect.MovementType.Clamped;
            modalScroll.inertia = true;
            modalScroll.decelerationRate = 0.08f;
            modalScroll.scrollSensitivity = 32;
            var viewport = Surface("Viewport", scroll, new Color(0, 0, 0, 0), false);
            Stretch(viewport, 0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            modalContent = Rect("Page content", viewport);
            modalContent.anchorMin = new Vector2(0, 1);
            modalContent.anchorMax = new Vector2(1, 1);
            modalContent.pivot = new Vector2(0.5f, 1);
            modalContent.anchoredPosition = Vector2.zero;
            modalContent.sizeDelta = Vector2.zero;
            var layout = modalContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(2, 2, 2, 16);
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = modalContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            modalScroll.viewport = viewport;
            modalScroll.content = modalContent;
            LayoutModal();
        }

        private void LayoutModal()
        {
            if (modalPanel == null) return;
            float width = Mathf.Min(safe.rect.width - 24, 850);
            float height = Mathf.Min(safe.rect.height - 24, 900);
            Center(modalPanel, width, height);
            Place(modalTitle.rectTransform, 150, 10, width - 170, 49, true);
            Place(modalSubtitle.rectTransform, 18, 60, width - 36, 55, true);
            Place(modalPanel.Find("Close modal") as RectTransform, 14, 12, 126, 64, true);
            var scroll = modalPanel.Find("Scrollable page") as RectTransform;
            Stretch(scroll, 16, 123, 16, 16);
        }

        private void RebuildPage(bool preserveScroll)
        {
            if (modalContent == null) return;
            float scroll = preserveScroll && modalScroll != null ? modalScroll.verticalNormalizedPosition : 1;
            for (int i = modalContent.childCount - 1; i >= 0; i--)
            {
                var child = modalContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            modalBindings.Clear();
            switch (page)
            {
                case Page.Map: BuildMap(); break;
                case Page.Projects: BuildProjects(false); break;
                case Page.Fleet: BuildFleet(); break;
                case Page.Investments: BuildProjects(true); break;
                case Page.Resources: BuildResources(); break;
                case Page.Gift: BuildGift(); break;
                case Page.Settings: BuildSettings(); break;
                case Page.Finale: BuildFinale(); break;
            }
            RefreshBindings();
            Canvas.ForceUpdateCanvases();
            modalScroll.verticalNormalizedPosition = scroll;
        }

        private void PageHeading(string title, string subtitle)
        {
            modalTitle.SetText(title);
            modalSubtitle.SetText(subtitle);
        }

        private void BuildMap()
        {
            PageHeading("خريطة الإعمار", "من الشجاعية شرقًا إلى البحر غربًا · مواقع مرجعية، وليست حدود الأحياء. استلام مكافأة 100% يفتح التالي.");
            for (int d = 0; d < GameCatalog.Districts.Length; d++)
            {
                int index = d;
                var definition = GameCatalog.Districts[d];
                var state = session.State.districts[d];
                var card = ListCard(definition.name + "   ·   " + definition.rarity, 280);
                if (d == session.State.selectedDistrict) card.GetComponent<Image>().color = new Color32(29, 68, 77, 255);
                var description = CardLabel(card, definition.description, 64, 57, 17, Muted);
                var status = CardLabel(card, "", 132, 31, 19, Cream);
                Bind(status, () => (session.State.districts[index].unlocked ? "متاح" : "مقفل") +
                    "   ·   " + Percent(session.Economy.Progress(index)) + "   ·   المكافأة " + N(definition.completionReward));
                var progress = Surface("District progress", card, Navy);
                PlaceCard(progress, 16, 174, -32, 8);
                var fill = AddImage(Rect("Filled progress", progress), Teal);
                Stretch(fill.rectTransform, 0, 0, 0, 0);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = 1;
                modalBindings.Add(new Binding { fill = fill, progress = () => session.Economy.Progress(index) });
                var visit = CardButton(card, state.unlocked ? "زيارة الحي" : "مقفل", 0, 199,
                    () => { session.ChooseDistrict(index); ClosePage(); }, state.unlocked, Teal, 2);
                var reward = CardButton(card, state.rewardClaimed ? "تم الاستلام" : "مكافأة 100%", 1, 199,
                    () => session.Perform(e => e.ClaimDistrictReward(index, session.Now)), true, Card, 2);
                BindButton(reward, () => session.State.districts[index].unlocked &&
                    !session.State.districts[index].rewardClaimed && session.Economy.Progress(index) >= 1f);
                if (!state.unlocked)
                    description.SetText(d == 10 ? "مقفل حتى استلام مكافآت الأحياء العشرة. منطقة ختامية مميزة." :
                        "أكمل " + GameCatalog.Districts[d - 1].name + " بنسبة 100% واستلم المكافأة لفتح هذا الحي.");
            }
            Note("السهم يشير إلى الشمال. مواقع الأحياء والساحل مستندة إلى خرائط؛ قطع البناء والطرق المحلية تمثيلية.", Muted);
            Note(GameGeography.Attribution, Muted);
            ListAction("حقوق بيانات OpenStreetMap",
                () => Application.OpenURL("https://www.openstreetmap.org/copyright"), Card);
        }

        private void BuildProjects(bool investmentsOnly)
        {
            int d = session.State.selectedDistrict;
            PageHeading(investmentsOnly ? "استثمارات منتجة" : "مشاريع الحي",
                GameCatalog.Districts[d].name + " · الوقت حقيقي ويستمر خارج اللعبة. الإنشاء يحتاج تأكيداً؛ الاختيار لا يخصم أي عملة.");
            if (investmentsOnly)
                Note("ابدأ الزراعة بـ 500 عملة فقط؛ احصد 1,500 بعد 5 دقائق ثم أعد الزراعة يدوياً. التجارة دخل متكرر؛ الصناعة تنتج مواد ودخلاً.", Gold);
            var definitions = GameCatalog.Districts[d].projects;
            if (!investmentsOnly && selectedPlot >= 0 && selectedPlot < definitions.Length)
                ProjectCard(d, definitions[selectedPlot]);
            for (int p = 0; p < definitions.Length; p++)
            {
                if (!investmentsOnly && p == selectedPlot) continue;
                if (investmentsOnly && definitions[p].kind != ProjectKind.Investment) continue;
                ProjectCard(d, definitions[p]);
            }
            Note("إنجاز الحي يتطلب إزالة الركام وجميع المشاريع. المواد في المخزن لا تُباع تلقائياً، ولا توجد تسريعات مدفوعة أو مكافآت إعلانية وهمية.", Muted);
        }

        private void ProjectCard(int district, ProjectDefinition definition)
        {
            var state = session.Economy.FindProject(district, definition.id);
            bool active = state.finishUtc > 0 && state.finishUtc > session.Now;
            bool batch = IsBatch(definition);
            bool canStart = !active && ((!state.completed && state.finishUtc == 0) || (batch && state.finishUtc == 0));
            var card = ListCard(definition.name, 398);
            CardLabel(card, definition.description, 58, 62, 17, Muted);
            CardLabel(card, N(definition.cost) + " عملة   ·   " + Duration(definition.durationSeconds) +
                "\nخرسانة: " + N(definition.concreteCost) + "   ·   حديد: " + N(definition.ironCost), 128, 63, 19, Cream);
            CardLabel(card, PrerequisiteText(district, definition), 200, 48, 17, Gold);
            var status = CardLabel(card, "", 254, 58, 18, Cream);
            Bind(status, () => ProjectStatus(district, definition) + (canStart &&
                (!HasPrerequisite(district, definition) || !CanAfford(definition)) ? "\n" + MissingFor(definition, district) : ""));
            if (active)
            {
                CardButton(card, "قيد التنفيذ", 0, 320, null, false, Card, 1);
            }
            else if (canStart)
            {
                string action = batch && state.completed ? "دورة جديدة" : "مراجعة وإنشاء";
                var button = CardButton(card, action, 0, 320, () => ConfirmProject(district, definition), true, Teal, 1);
                BindButton(button, () => HasPrerequisite(district, definition) && CanAfford(definition));
            }
            else if (definition.income > 0)
            {
                var income = CardButton(card, "جمع الدخل / الإنتاج", 0, 320,
                    () => session.Perform(e => e.CollectIncome(district, definition.id, session.Now)), true, Teal, 1);
                BindButton(income, () => session.Economy.PendingIncome(district, definition.id, session.Now) > 0);
            }
            else CardButton(card, "مكتمل", 0, 320, null, false, Card, 1);
        }

        private void ConfirmProject(int d, ProjectDefinition p)
        {
            Confirm("تأكيد " + p.name,
                "سيُخصم من الرصيد: " + N(p.cost) + " عملة\nخرسانة: " + p.concreteCost + "   ·   حديد: " + p.ironCost +
                "\nالمدة الحقيقية: " + Duration(p.durationSeconds) + "\n" + PrerequisiteText(d, p) +
                "\nالرصيد الحالي: " + N(session.State.coins) + "\nلن تُخصم أي تكلفة قبل الضغط على «تأكيد».",
                () => session.Perform(e => e.StartProject(d, p.id, session.Now)));
        }

        private string ProjectStatus(int d, ProjectDefinition definition)
        {
            var p = session.Economy.FindProject(d, definition.id);
            if (p.finishUtc > session.Now)
                return "قيد التنفيذ · " + TimeLeft(p.finishUtc - session.Now) + "\nبدأ: " + Stamp(p.startedUtc) + " · ينتهي: " + Stamp(p.finishUtc);
            long income = session.Economy.PendingIncome(d, definition.id, session.Now);
            if (income > 0) return (IsBatch(definition) ? "الإنتاج جاهز" : "مكتمل · دخل جاهز") + " · " + N(income) + " عملة";
            if (IsBatch(definition) && p.finishUtc == 0)
                return p.completed ? "جاهز لدورة جديدة · الإنجاز محفوظ" : "جاهز للدورة الأولى";
            if (p.completed)
                return definition.income > 0 ? "مكتمل · الدخل " + N(definition.income) + " كل " + Duration(definition.incomeSeconds) : "مكتمل";
            return "لم يبدأ بعد";
        }

        private string PrerequisiteText(int d, ProjectDefinition p)
        {
            if (string.IsNullOrEmpty(p.prerequisite)) return "لا يحتاج مشروعاً سابقاً";
            string name = p.prerequisite;
            foreach (var item in GameCatalog.Districts[d].projects)
                if (item.id == p.prerequisite) { name = item.name; break; }
            return (HasPrerequisite(d, p) ? "المتطلب مكتمل: " : "يلزم إكمال: ") + name;
        }

        private bool HasPrerequisite(int d, ProjectDefinition p)
        {
            if (string.IsNullOrEmpty(p.prerequisite)) return true;
            var state = session.Economy.FindProject(d, p.prerequisite);
            return state != null && state.completed;
        }
        private bool CanAfford(ProjectDefinition p)
        {
            return session.State.coins >= p.cost && session.State.stock.concrete >= p.concreteCost && session.State.stock.iron >= p.ironCost;
        }
        private string MissingFor(ProjectDefinition p, int d)
        {
            if (!HasPrerequisite(d, p)) return "أكمل المتطلب أولاً";
            if (session.State.coins < p.cost) return "ينقصك " + N(p.cost - session.State.coins) + " عملة";
            return "المواد غير كافية؛ خزّن إنتاج التدوير";
        }
        private static bool IsBatch(ProjectDefinition p) { return p.kind == ProjectKind.Investment && (p.id == "farm" || p.id == "industry"); }

        private void BuildFleet()
        {
            PageHeading("الأسطول والمصنع", "خطة البداية: مصنع 15,000 + حفارة 8,000 + شاحنتان 12,000 + جرافة 5,000 = 40,000. يتبقى 10,000 من ميزانية البداية.");
            FleetCard("factory", "مصنع التدوير", GameCatalog.FactoryCost,
                "يحوّل كل دفعة إلى خرسانة وحديد وخشب ومواد أخرى. زيادة المستوى تحسّن الإنتاج والسرعة.",
                () => session.State.factoryLevel, true);
            FleetCard("excavator", "الحفارات", GameCatalog.ExcavatorCost, "تسرّع إزالة الركام مع الجرافة. ابدأ بحفارة واحدة.", () => session.State.excavators, false);
            FleetCard("truck", "الشاحنات", GameCatalog.TruckCost, "المزيد من الشاحنات يعني نقلاً أسرع. خطتك الأولى شاحنتان.", () => session.State.trucks, false);
            FleetCard("bulldozer", "الجرافات", GameCatalog.BulldozerCost, "تعمل مع الحفارة في مرحلة الإزالة. ابدأ بجرافة واحدة.", () => session.State.bulldozers, false);
            var factory = ListCard("ترقية مصنع التدوير", 226);
            CardLabel(factory, "المستوى: " + session.State.factoryLevel + " / 5\n" +
                (session.State.factoryLevel >= 5 ? "وصل المصنع إلى أعلى مستوى." : "تكلفة الترقية: " + N(session.State.factoryLevel * 20000L) + " عملة") +
                "\nانتظر انتهاء أي عقد قبل الترقية.", 62, 78, 18, Muted);
            CardButton(factory, "مراجعة الترقية", 0, 147, () => Confirm("ترقية المصنع",
                "التكلفة: " + N(session.State.factoryLevel * 20000L) + " عملة\nالمستوى التالي: " + (session.State.factoryLevel + 1) +
                "\nمزيد من المواد وتدوير أسرع.", () => session.Perform(e => e.UpgradeFactory(session.Now))),
                session.State.factoryLevel > 0 && session.State.factoryLevel < 5 && session.State.jobStage == JobStage.Idle &&
                session.State.coins >= session.State.factoryLevel * 20000L, Teal, 1);
            var equipment = ListCard("ترقية جميع المعدات", 226);
            CardLabel(equipment, "المستوى: " + session.State.equipmentLevel + " / 5\n" +
                (session.State.equipmentLevel >= 5 ? "المعدات في أعلى مستوى." : "تكلفة الترقية: " + N(session.State.equipmentLevel * 12000L) + " عملة") +
                "\nتحتاج حفارة وشاحنة وجرافة، وفريقاً غير مشغول.", 62, 78, 18, Muted);
            CardButton(equipment, "مراجعة الترقية", 0, 147, () => Confirm("ترقية المعدات",
                "التكلفة: " + N(session.State.equipmentLevel * 12000L) + " عملة\nالمستوى التالي: " + (session.State.equipmentLevel + 1) +
                "\nإزالة ونقل أسرع.", () => session.Perform(e => e.UpgradeEquipment(session.Now))),
                session.State.equipmentLevel < 5 && session.State.excavators > 0 && session.State.trucks > 0 && session.State.bulldozers > 0 &&
                session.State.jobStage == JobStage.Idle && session.State.coins >= session.State.equipmentLevel * 12000L, Teal, 1);
        }

        private void FleetCard(string kind, string name, long cost, string description, Func<int> amount, bool factory)
        {
            var card = ListCard(name, 254);
            CardLabel(card, description, 62, 58, 18, Muted);
            var count = CardLabel(card, "", 123, 44, 20, Cream);
            Bind(count, () => (factory ? "المستوى: " : "المملوك: ") + amount() + "   ·   تكلفة الشراء: " + N(cost));
            CardButton(card, factory && amount() > 0 ? "المصنع موجود" : "مراجعة الشراء", 0, 177,
                () => Confirm("شراء " + name, "التكلفة: " + N(cost) + " عملة\nرصيدك: " + N(session.State.coins) +
                    "\nالشراء اختياري ولا يتم بمجرد اختيار المعدة.",
                    () => session.Perform(e => e.BuyEquipment(kind, session.Now))),
                (!factory || amount() == 0) && session.State.coins >= cost, Teal, 1);
        }

        private void BuildResources()
        {
            int d = session.State.selectedDistrict;
            bool imported = session.State.districts[d].clearedLoads >= GameCatalog.Districts[d].rubbleLoads;
            PageHeading("المواد والتدوير", "المواد محفوظة في المخزن حتى تستخدمها أو تبيعها. احتفظ بالخرسانة والحديد للمشاريع؛ البيع يستهلك كامل النوع المحدد.");
            var salvage = ListCard(imported ? "عقد تدوير ركام مستورد" : "إزالة الركام المحلي", 306);
            CardLabel(salvage, imported ? "انتهى الركام المحلي. العقد المستورد ينتج مواد لكنه لا يزيد نسبة إنجاز الحي."
                : "الإزالة ثم النقل ثم التدوير تلقائياً. كل دفعة محلية مكتملة تزيد تقدم إزالة الركام.", 62, 60, 18, Muted);
            var job = CardLabel(salvage, "", 131, 79, 18, Cream);
            Bind(job, () => (session.State.jobStage == JobStage.Idle ? "الفريق جاهز" : Stage(session.State.jobStage) + " · " +
                TimeLeft(session.State.jobFinishUtc - session.Now)) + "\nدفعات محلية: " + session.State.districts[d].clearedLoads +
                " / " + GameCatalog.Districts[d].rubbleLoads +
                "\nإنتاج الدفعة: " + 40 * session.State.factoryLevel + " خرسانة · " + 15 * session.State.factoryLevel + " حديد");
            var start = CardButton(salvage, "بدء عقد التدوير", 0, 225, () => Confirm(imported ? "عقد ركام مستورد" : "بدء إزالة الركام",
                "لا تكلفة عملات لهذا العقد.\nتحتاج مصنعاً وحفارة وشاحنة وجرافة.\nالمراحل تعمل تلقائياً أثناء غيابك.\n" +
                (imported ? "العقد المستورد لا يزيد إنجاز الحي." : "إكمال الدفعة المحلية يزيد تقدم إزالة الركام."),
                () => session.Perform(e => e.StartSalvage(d, session.Now))), true, Teal, 1);
            BindButton(start, () => session.State.jobStage == JobStage.Idle && session.State.factoryLevel > 0 &&
                session.State.excavators > 0 && session.State.trucks > 0 && session.State.bulldozers > 0);
            if (session.State.factoryLevel == 0 || session.State.excavators == 0 || session.State.trucks == 0 || session.State.bulldozers == 0)
            {
                Note("الفريق غير مكتمل. اشتر المعدات المطلوبة من «الأسطول» أولاً.", Gold);
                ListAction("فتح الأسطول", () => OpenPage(Page.Fleet), Teal);
            }
            ResourceCard("concrete", "الخرسانة", () => session.State.stock.concrete, 20, "أساسية للمياه والمنازل والطريق. لا تبع احتياجات البناء.");
            ResourceCard("iron", "الحديد", () => session.State.stock.iron, 60, "تحتاجه الشبكات والمشاريع. احتفظ بالكمية المطلوبة.");
            ResourceCard("wood", "الخشب", () => session.State.stock.wood, 35, "يُخزَّن من التدوير ويمكن بيع الفائض.");
            ResourceCard("other", "مواد أخرى", () => session.State.stock.other, 10, "مواد محفوظة من الإنتاج والتدوير.");
            ListAction("مراجعة بيع كل المخزون", ConfirmAllResources, Card);
        }

        private void ResourceCard(string kind, string name, Func<int> amount, int price, string hint)
        {
            var card = ListCard(name, 238);
            CardLabel(card, hint, 60, 52, 17, Muted);
            var count = CardLabel(card, "", 116, 39, 19, Cream);
            Bind(count, () => "المخزون: " + N(amount()) + "   ·   سعر الوحدة: " + price + "   ·   قيمة البيع: " + N(amount() * (long)price));
            var sell = CardButton(card, "مراجعة بيع هذا النوع", 0, 160, () =>
            {
                int reviewedAmount = amount();
                Confirm("بيع " + name,
                    "بيع كامل هذا النوع: " + N(reviewedAmount) + " وحدة\nالسعر: " + price + " عملة للوحدة\nالمجموع الحالي: " +
                    N(reviewedAmount * (long)price) + " عملة\nالبيع اختياري؛ المواد تبقى بالمخزن إذا ألغيت.",
                    () => session.Perform(e => amount() != reviewedAmount
                        ? ActionResult.Fail("تغيّر المخزون أثناء المراجعة؛ راجع الكمية الجديدة قبل البيع")
                        : e.SellResources(kind, session.Now)));
            }, true, Card, 1);
            BindButton(sell, () => amount() > 0);
        }

        private void ConfirmAllResources()
        {
            int concrete = session.State.stock.concrete;
            int iron = session.State.stock.iron;
            int wood = session.State.stock.wood;
            int other = session.State.stock.other;
            Confirm("بيع كامل المخزون",
                "ستبيع جميع الخرسانة والحديد والخشب والمواد الأخرى.\nقد يؤخّر ذلك البناء.\nالقيمة الحالية: " + N(StockValue()) +
                " عملة\nلا يمكن التراجع بعد التأكيد.", () => session.Perform(e =>
                {
                    var stock = e.State.stock;
                    if (stock.concrete != concrete || stock.iron != iron || stock.wood != wood || stock.other != other)
                        return ActionResult.Fail("تغيّر المخزون أثناء المراجعة؛ راجع الكميات الجديدة قبل البيع");
                    return e.SellResources("all", session.Now);
                }));
        }

        private long StockValue()
        {
            var s = session.State.stock;
            return s.concrete * 20L + s.iron * 60L + s.wood * 35L + s.other * 10L;
        }

        private void BuildGift()
        {
            PageHeading("الهدية اليومية", "هدية مضمونة كل 24 ساعة حقيقية. لا عجلة حظ ولا مشاهدة إعلان ولا شراء.");
            int complete = 0;
            for (int d = 0; d < session.State.districts.Length; d++)
                if (session.Economy.Progress(d) >= 1) complete++;
            var card = ListCard("دعم يوم جديد", 360);
            CardLabel(card, "الهدية الحالية:\n" + N(3000 + complete * 1000L) + " عملة\n" +
                (20 + complete * 5) + " خرسانة · " + (5 + complete * 2) + " حديد · 5 خشب · 3 أخرى\nتكبر الهدية مع عدد الأحياء المكتملة.", 61, 157, 21, Cream);
            var timer = CardLabel(card, "", 225, 46, 18, Gold);
            Bind(timer, () => session.Economy.CanClaimDailyGift(session.Now) ? "جاهزة للاستلام الآن" :
                "الهدية التالية بعد " + TimeLeft(session.State.lastGiftUtc + 86400 - session.Now));
            var claim = CardButton(card, "استلام الهدية", 0, 280,
                () => session.Perform(e => e.ClaimDailyGift(session.Now)), true, Teal, 1);
            BindButton(claim, () => session.Economy.CanClaimDailyGift(session.Now));
            var ads = ListCard("الإعلانات غير متاحة", 236);
            CardLabel(ads, "لا يوجد مزوّد إعلانات حقيقي متصل.\nلا يمكن تشغيل إعلان أو منح أي مكافأة إعلانية دون التحقق من مزوّد فعلي.\nالهدية اليومية مستقلة عن الإعلانات.", 61, 94, 19, Muted);
            CardButton(ads, "إعلان غير متاح · لا مكافأة", 0, 157, null, false, Card, 1);
        }

        private void BuildSettings()
        {
            PageHeading("استراحة وخيارات", "تتوقف حركة كاميرا المدينة أثناء فتح القوائم؛ أوقات البناء والدخل الحقيقية لا تتوقف.");
            Note("اسم البنّاء: " + session.State.playerName, Gold);
            var nameCard = ListCard("تعديل الاسم", 264);
            var field = Surface("Logical name entry", nameCard, Cream, false);
            PlaceCard(field, 16, 61, -32, 72);
            nameInput = field.gameObject.AddComponent<InputField>();
            nameInput.lineType = InputField.LineType.SingleLine;
            nameInput.characterLimit = 24;
            nameInput.contentType = InputField.ContentType.Standard;
            nameInput.shouldHideMobileInput = false;
            var editable = Rect("Native editable logical text", field).gameObject.AddComponent<Text>();
            editable.font = arabicFont;
            editable.fontSize = 24;
            editable.color = Navy;
            editable.alignment = TextAnchor.MiddleRight;
            editable.supportRichText = false;
            editable.raycastTarget = false;
            Stretch(editable.rectTransform, 12, 4, 12, 4);
            nameInput.textComponent = editable;
            nameInput.text = session.State.playerName;
            var preview = CardLabel(nameCard, "", 142, 39, 20, Gold);
            preview.SetText("معاينة: " + nameInput.text);
            nameInput.onValueChanged.AddListener(value => preview.SetText("معاينة: " + value));
            CardButton(nameCard, "حفظ الاسم", 0, 188, () =>
            {
                if (nameInput == null || string.IsNullOrWhiteSpace(nameInput.text)) { ShowToast("أدخل اسماً قبل الحفظ"); return; }
                string name = CleanName(nameInput.text);
                if (string.IsNullOrWhiteSpace(name)) { ShowToast("الاسم غير صالح"); return; }
                session.SetPlayerName(name);
                ShowToast("تم حفظ اسم البنّاء");
                ClosePage();
            }, true, Teal, 1);
            Note("الكتابة داخل الحقل تستخدم ترتيب الإدخال المنطقي للحفاظ على المؤشر واللوحة الأصلية؛ المعاينة تعرض العربية المتصلة.", Muted);
            ListAction(session.State.cityCompletedUtc > 0 ? "التقاط صورة مع ملخص المدينة" : "صورة الإنجاز · تُفتح بعد الرشيد",
                () => { ClosePage(); CityCapture.Capture(session, cityCamera); }, Teal);
            ListAction("عرض ملخص الإنجاز", () => OpenPage(Page.Finale), Card);
            ListAction("إعادة تأطير المدينة", () => { ClosePage(); cityCamera.FrameCity(); }, Card);
            if (!string.IsNullOrEmpty(session.SaveError)) Note(session.SaveError + " لم يُعد ضبط تقدمك.", Red);
            ListAction("العودة إلى المدينة", ClosePage, Teal);
        }

        private static string CleanName(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value.Trim())
                if (!char.IsControl(c) && !(c >= '\u202A' && c <= '\u202E') && !(c >= '\u2066' && c <= '\u2069') &&
                    c != '\u200E' && c != '\u200F' && c != '\u061C') result.Append(c);
            return result.ToString();
        }

        private void BuildFinale()
        {
            bool complete = session.State.cityCompletedUtc > 0;
            PageHeading(complete ? "غزة الجديدة · اكتمل الإعمار" : "رحلة الإعمار", "ملخص مدينتك · " + session.State.playerName);
            int claimed = 0, built = 0;
            foreach (var district in session.State.districts)
            {
                if (district.rewardClaimed) claimed++;
                foreach (var p in district.projects) if (p.completed) built++;
            }
            var card = ListCard(complete ? "مدينة تستحق الحياة" : "كل خطوة تصنع فرقاً", 390);
            CardLabel(card, "مكافآت الأحياء المستلمة: " + claimed + " / 11\nالمشاريع المكتملة: " + built +
                "\nالرصيد: " + N(session.State.coins) + " عملة\nالأسطول: " + session.State.excavators + " حفارة · " +
                session.State.trucks + " شاحنة · " + session.State.bulldozers + " جرافة\nالمصنع: المستوى " +
                session.State.factoryLevel + (complete ? "\nتاريخ اكتمال المدينة: " + Stamp(session.State.cityCompletedUtc) : ""),
                61, 305, 22, Cream);
            ListAction(complete ? "التقاط صورة وملخص" : "صورة الإنجاز · تُفتح بعد الرشيد",
                () => { ClosePage(); CityCapture.Capture(session, cityCamera); }, Teal);
            if (complete)
                ListAction("مشهد المدينة الختامي", () => { ClosePage(); world.PlayFinale(); cityCamera.PlayFinale(); }, Card);
            ListAction("العودة إلى المدينة", ClosePage, Card);
            Note("التقاط الصور متاح. تسجيل الفيديو غير مدمج؛ لا تُعرض وظيفة تسجيل وهمية.", Muted);
        }

        private void Confirm(string title, string description, Action commit)
        {
            if (confirmationObject != null) CloseConfirmation();
            CityCamera.ModalOpen = true;
            var shield = Surface("Confirmation touch shield", safe, new Color(0.01f, 0.025f, 0.04f, 0.88f), false);
            Stretch(shield, 0, 0, 0, 0);
            confirmationObject = shield.gameObject;
            confirmationPanel = Surface("Review before transaction", shield, Panel);
            Center(confirmationPanel, Mathf.Min(safe.rect.width - 32, 620), Mathf.Min(safe.rect.height - 32, 500));
            var heading = Label(confirmationPanel, title, 26, Gold);
            PlaceCard(heading.rectTransform, 20, 15, -40, 69);
            var scroll = Rect("Scrollable confirmation", confirmationPanel);
            Stretch(scroll, 20, 88, 20, 100);
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Surface("Confirmation viewport", scroll, new Color(0, 0, 0, 0), false);
            Stretch(viewport, 0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Confirmation description", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 355);
            var body = Label(content, description, 21, Cream);
            Stretch(body.rectTransform, 0, 0, 0, 0);
            body.alignment = TextAnchor.UpperRight;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            pendingConfirmation = commit;
            var accept = ActionButton(confirmationPanel, "تأكيد", () =>
            {
                Action action = pendingConfirmation;
                CloseConfirmation();
                action?.Invoke();
                changed = true;
            }, Teal);
            var cancel = ActionButton(confirmationPanel, "إلغاء · دون خصم", CloseConfirmation, Card);
            var acceptRect = accept.GetComponent<RectTransform>();
            acceptRect.anchorMin = new Vector2(0.51f, 0);
            acceptRect.anchorMax = new Vector2(1, 0);
            acceptRect.pivot = new Vector2(0.5f, 0);
            acceptRect.offsetMin = new Vector2(4, 20);
            acceptRect.offsetMax = new Vector2(-20, 84);
            var cancelRect = cancel.GetComponent<RectTransform>();
            cancelRect.anchorMin = Vector2.zero;
            cancelRect.anchorMax = new Vector2(0.49f, 0);
            cancelRect.pivot = new Vector2(0.5f, 0);
            cancelRect.offsetMin = new Vector2(20, 20);
            cancelRect.offsetMax = new Vector2(-4, 84);
            if (toastObject != null) toastObject.transform.SetAsLastSibling();
        }

        private void CloseConfirmation()
        {
            pendingConfirmation = null;
            if (confirmationObject != null) Destroy(confirmationObject);
            confirmationObject = null;
            confirmationPanel = null;
            // A background completion may have changed context while rebuilding
            // was deferred behind this confirmation. Refresh once after closing.
            structuralKey = "";
            changed = true;
            ReleaseCameraAfterTouch();
        }

        private void ClosePage()
        {
            if (confirmationObject != null) CloseConfirmation();
            if (modalObject != null) Destroy(modalObject);
            modalObject = null;
            modalPanel = null;
            modalContent = null;
            modalScroll = null;
            nameInput = null;
            modalBindings.Clear();
            page = Page.None;
            ReleaseCameraAfterTouch();
        }

        private void ReleaseCameraAfterTouch()
        {
            // Keep the just-closed overlay's pointer-up from becoming a world tap.
            CityCamera.ModalOpen = true;
            closingCameraBlock = true;
            releaseBlockUntil = Time.unscaledTime + 0.22f;
        }

        private void ShowToast(string message)
        {
            if (toastObject == null || string.IsNullOrWhiteSpace(message)) return;
            toastLabel.SetText(message);
            toastUntil = Time.unscaledTime + 5f;
            toastObject.SetActive(true);
            toastObject.transform.SetAsLastSibling();
        }

        private void RefreshBindings()
        {
            foreach (var bind in modalBindings)
            {
                if (bind.label != null && bind.value != null) bind.label.SetText(bind.value());
                if (bind.button != null && bind.enabled != null) bind.button.interactable = bind.enabled();
                if (bind.fill != null && bind.progress != null) bind.fill.fillAmount = bind.progress();
            }
        }
        private void Bind(ArabicLabel label, Func<string> value) { modalBindings.Add(new Binding { label = label, value = value }); }
        private void BindButton(Button button, Func<bool> enabled) { modalBindings.Add(new Binding { button = button, enabled = enabled }); }

        private string StateKey()
        {
            var s = session.State;
            var key = new StringBuilder(1024);
            key.Append(s.selectedDistrict).Append('|').Append(s.coins).Append('|').Append(s.factoryLevel).Append('|')
                .Append(s.excavators).Append('|').Append(s.trucks).Append('|').Append(s.bulldozers).Append('|').Append(s.equipmentLevel)
                .Append('|').Append(s.stock.concrete).Append('|').Append(s.stock.iron).Append('|').Append(s.stock.wood).Append('|')
                .Append(s.stock.other).Append('|').Append((int)s.jobStage).Append('|').Append(s.jobFinishUtc).Append('|')
                .Append(s.lastGiftUtc).Append('|').Append(s.playerName).Append('|').Append(s.cityCompletedUtc);
            foreach (var d in s.districts)
            {
                key.Append('|').Append(d.unlocked).Append(',').Append(d.rewardClaimed).Append(',').Append(d.clearedLoads);
                foreach (var p in d.projects)
                    key.Append(';').Append(p.completed).Append(',').Append(p.startedUtc).Append(',').Append(p.finishUtc).Append(',').Append(p.lastIncomeUtc)
                        // A repeatable batch retains completed=true across later cycles.
                        // Include only deadline CROSSINGS, never the live countdown.
                        .Append(',').Append(p.finishUtc > session.Now);
            }
            return key.ToString();
        }

        private RectTransform ListCard(string title, float height)
        {
            var card = Surface(title, modalContent, Card);
            var size = card.gameObject.AddComponent<LayoutElement>();
            size.minHeight = height;
            size.preferredHeight = height;
            var heading = Label(card, title, 24, Cream);
            PlaceCard(heading.rectTransform, 16, 8, -32, 47);
            var accent = Surface("Gold accent", card, Gold, false);
            accent.anchorMin = new Vector2(1, 0.16f);
            accent.anchorMax = new Vector2(1, 0.84f);
            accent.pivot = new Vector2(1, 0.5f);
            accent.offsetMin = new Vector2(-4, 0);
            accent.offsetMax = Vector2.zero;
            accent.GetComponent<Image>().raycastTarget = false;
            return card;
        }
        private ArabicLabel CardLabel(RectTransform parent, string text, float top, float height, int size, Color color)
        {
            var label = Label(parent, text, size, color);
            PlaceCard(label.rectTransform, 16, top, -32, height);
            label.alignment = TextAnchor.UpperRight;
            return label;
        }
        private Button CardButton(RectTransform parent, string label, int index, float top, Action action, bool enabled, Color color, int count)
        {
            var button = ActionButton(parent, label, action, color);
            button.interactable = enabled;
            var rect = button.GetComponent<RectTransform>();
            // index zero is rightmost in RTL.
            float min = 1f - (index + 1f) / count;
            float max = 1f - index / (float)count;
            rect.anchorMin = new Vector2(min, 1);
            rect.anchorMax = new Vector2(max, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(16, -top - 64);
            rect.offsetMax = new Vector2(-16, -top);
            return button;
        }
        private void Note(string text, Color color)
        {
            var card = Surface("Information", modalContent, Navy);
            var element = card.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 118;
            element.preferredHeight = 118;
            var label = Label(card, text, 19, color);
            Stretch(label.rectTransform, 16, 10, 16, 10);
            label.alignment = TextAnchor.MiddleRight;
        }
        private void ListAction(string text, Action action, Color color)
        {
            var button = ActionButton(modalContent, text, action, color);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 68;
            layout.preferredHeight = 68;
        }

        private RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj.GetComponent<RectTransform>();
        }
        private RectTransform Surface(string name, Transform parent, Color color, bool round = true)
        {
            var rect = Rect(name, parent);
            var image = AddImage(rect, color);
            if (round) { image.sprite = roundedSprite; image.type = Image.Type.Sliced; }
            return rect;
        }
        private static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }
        private ArabicLabel Label(Transform parent, string text, int size, Color color)
        {
            var rect = Rect("Arabic label", parent);
            var label = rect.gameObject.AddComponent<ArabicLabel>();
            label.font = arabicFont;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleRight;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            // Arabic fonts need room for ascenders and descenders. This keeps the
            // baselines readable while avoiding wasteful gaps in mobile cards.
            label.lineSpacing = 0.92f;
            label.raycastTarget = false;
            label.SetText(text);
            return label;
        }
        private Button ActionButton(Transform parent, string text, Action action, Color color)
        {
            var rect = Surface(text, parent, color);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1);
            colors.pressedColor = new Color(0.70f, 0.86f, 0.86f, 1);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.58f, 0.58f, 0.72f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var label = Label(rect, text, 20, Cream);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform, 7, 2, 7, 2);
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }
        private static void ButtonText(Button button, string text)
        {
            var label = button.GetComponentInChildren<ArabicLabel>();
            if (label != null) label.SetText(text);
        }
        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height, bool top)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0, top ? 1 : 0);
            rect.pivot = new Vector2(0, top ? 1 : 0);
            rect.anchoredPosition = new Vector2(x, top ? -y : y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void PlaceCard(RectTransform rect, float left, float top, float widthDelta, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(left + widthDelta, -top);
        }
        private static void Center(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }
        private static string N(long value) { return value.ToString("N0", CultureInfo.InvariantCulture); }
        private static string Percent(float value) { return Mathf.RoundToInt(value * 100) + "%"; }
        private static string Duration(long seconds)
        {
            if (seconds >= 86400) return N(seconds / 86400) + " يوم";
            if (seconds >= 3600) return (seconds / 3600f).ToString("0.#", CultureInfo.InvariantCulture) + " ساعة";
            if (seconds >= 60) return (seconds / 60f).ToString("0.#", CultureInfo.InvariantCulture) + " دقيقة";
            return N(seconds) + " ثانية";
        }
        private static string TimeLeft(long seconds)
        {
            seconds = Math.Max(0, seconds);
            if (seconds >= 86400) return seconds / 86400 + " يوم " + (seconds % 86400 / 3600) + " ساعة";
            return (seconds / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (seconds % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        private static string Stamp(long seconds)
        {
            if (seconds <= 0) return "غير محدد";
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return "تاريخ غير صالح"; }
        }
        private static string Stage(JobStage value)
        {
            return value == JobStage.Clearing ? "إزالة الركام" : value == JobStage.Hauling ? "نقل الركام" : value == JobStage.Recycling ? "تدوير المواد" : "جاهز";
        }
        private static Sprite CreateRoundedSprite()
        {
            const int size = 32;
            const float radius = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Native rounded HUD surface";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius));
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius));
                    float distance = new Vector2(Mathf.Max(0, dx), Mathf.Max(0, dy)).magnitude;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 0.5f - distance) * 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(9, 9, 9, 9));
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.Changed -= OnChanged;
                session.Notification -= ShowToast;
                session.PlotSelected -= OnPlotSelected;
            }
            CityCamera.ModalOpen = false;
            if (roundedSprite != null)
            {
                var texture = roundedSprite.texture;
                Destroy(roundedSprite);
                Destroy(texture);
            }
        }
    }
}