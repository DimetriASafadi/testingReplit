using System;
using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    /// <summary>
    /// Session-backed home screen. It is an overlay only: the live city camera remains
    /// the background, and all navigation and rewards continue through GameSession.
    /// </summary>
    public sealed partial class CityHud
    {
        private RectTransform mainMenuRoot;
        private RectTransform mainMenuHeader;
        private RectTransform mainMenuRail;
        private RectTransform mainMenuCenter;
        private RectTransform mainMenuDistrict;
        private RectTransform mainMenuActions;
        private RectTransform mainMenuActionContent;
        private ScrollRect mainMenuActionScroll;
        private RectTransform mainMenuIdentity;
        private RectTransform mainMenuHero;
        private RawImage mainMenuThumbnail;
        private readonly ArabicLabel[] mainMenuActionTitles = new ArabicLabel[5];
        private readonly ArabicLabel[] mainMenuActionDescriptions = new ArabicLabel[5];
        private ArabicLabel mainMenuLogo;
        private ArabicLabel mainMenuPlayer;
        private ArabicLabel mainMenuJob;
        private ArabicLabel mainMenuDistrictName;
        private ArabicLabel mainMenuDistrictProgress;
        private ArabicLabel mainMenuDistrictRubric;
        private ArabicLabel mainMenuNeeds;
        private Image mainMenuProgressFill;
        private Button mainMenuRewardButton;
        private ArabicLabel mainMenuRewardLabel;
        private GameObject mainMenuGiftBadge;
        private readonly ArabicLabel[] mainMenuResourceLabels = new ArabicLabel[5];
        private readonly Button[] mainMenuResourceButtons = new Button[5];
        private readonly RectTransform[] mainMenuActionCards = new RectTransform[5];
        private readonly RectTransform[] mainMenuRailItems = new RectTransform[6];
        private bool mainMenuBuilt;

        /// <summary>True while the native home menu is displayed over the city.</summary>
        public bool MainMenuVisible => mainMenuRoot != null && mainMenuRoot.gameObject.activeSelf;
        private float HomeTopHeight => safe.rect.height > safe.rect.width ? 166 : safe.rect.height < 680 ? 76 : 92;

        private void BringHomeTopForward()
        {
            if (MainMenuVisible && mainMenuHeader != null) mainMenuHeader.SetAsLastSibling();
        }

        /// <summary>Creates the native home-screen hierarchy once under the safe-area root.</summary>
        public void BuildMainMenu()
        {
            if (mainMenuBuilt || safe == null) return;

            mainMenuRoot = Surface("Main menu overlay", safe, new Color(5f / 255f, 17f / 255f, 34f / 255f, 0.11f), false);
            Stretch(mainMenuRoot, 0, 0, 0, 0);
            mainMenuRoot.SetAsLastSibling();

            mainMenuHeader = Surface("Home resource strip", safe, new Color(7f / 255f, 27f / 255f, 54f / 255f, 0.94f));
            mainMenuRail = Surface("Home navigation rail", mainMenuRoot, new Color(7f / 255f, 27f / 255f, 54f / 255f, 0.94f));
            mainMenuCenter = Rect("Home focus area", mainMenuRoot);
            mainMenuDistrict = Surface("Current district card", mainMenuRoot, new Color(12f / 255f, 42f / 255f, 75f / 255f, 0.95f));
            mainMenuActions = Surface("Home action row", mainMenuRoot, new Color(7f / 255f, 27f / 255f, 54f / 255f, 0.94f));
            mainMenuActions.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            mainMenuActionContent = Rect("Quick action scroll content", mainMenuActions);
            mainMenuActionScroll = mainMenuActions.gameObject.AddComponent<ScrollRect>();
            mainMenuActionScroll.viewport = mainMenuActions;
            mainMenuActionScroll.content = mainMenuActionContent;
            mainMenuActionScroll.horizontal = true;
            mainMenuActionScroll.vertical = false;
            mainMenuActionScroll.movementType = ScrollRect.MovementType.Clamped;

            BuildHomeHeader();
            BuildHomeRail();
            BuildHomeCenter();
            BuildHomeDistrict();
            BuildHomeActions();
            mainMenuBuilt = true;
            LayoutMainMenu(safe.rect.width, safe.rect.height);
            RefreshMainMenu();
            mainMenuRoot.gameObject.SetActive(false);
            mainMenuHeader.gameObject.SetActive(false);
        }

        private void BuildHomeHeader()
        {
            var identity = Surface("Game identity and builder", mainMenuHeader, new Color(18f / 255f, 61f / 255f, 112f / 255f, 0.78f));
            mainMenuIdentity = identity;
            var gameName = Label(identity, "نيو غزة", CityTypography.HeadingSize, Gold);
            gameName.alignment = TextAnchor.MiddleLeft;
            var profile = ActionButton(identity, "البنّاء  ·  الإعدادات", () => OpenHomePage(Page.Settings), Card);
            profile.name = "Home profile and settings";
            mainMenuPlayer = profile.GetComponentInChildren<ArabicLabel>();
            mainMenuPlayer.alignment = TextAnchor.MiddleCenter;

            string[] names = { "العملات", "الخرسانة", "الحديد", "الخشب", "مواد أخرى" };
            int[] resourceArt = { 5, 7, 6, 8, 7 };
            for (int i = 0; i < mainMenuResourceButtons.Length; i++)
            {
                Button resource = ActionButton(mainMenuHeader, "", () => OpenHomePage(Page.Resources),
                    new Color(23f / 255f, 61f / 255f, 99f / 255f, 0.9f));
                resource.name = "Home resource " + names[i];
                mainMenuResourceButtons[i] = resource;
                var artRect = Rect("Resource art", resource.transform);
                artRect.sizeDelta = new Vector2(34, 34);
                var art = artRect.gameObject.AddComponent<Image>();
                art.raycastTarget = false;
                HomeArt(art, resourceArt[i]);
                var label = Label(resource.transform, names[i], 15, Muted);
                label.alignment = TextAnchor.MiddleRight;
                mainMenuResourceLabels[i] = label;
            }
        }

        private void BuildHomeRail()
        {
            string[] labels = { "المدينة", "الخريطة", "المصنع", "المكافآت", "الإحصائيات", "المعدات" };
            Action[] actions =
            {
                () => ShowMainMenu(true),
                () => OpenHomePage(Page.Map),
                () => OpenHomePage(Page.Fleet),
                () => OpenHomePage(Page.Gift),
                () => OpenHomePage(Page.Finale),
                () => OpenHomePage(Page.Fleet)
            };
            int[] artSlots = { -1, -1, 9, 4, -1, 1 };
            for (int i = 0; i < labels.Length; i++)
            {
                var button = ActionButton(mainMenuRail, labels[i], actions[i],
                    i == 0 ? new Color(22f / 255f, 139f / 255f, 219f / 255f, 0.72f) :
                    new Color(18f / 255f, 61f / 255f, 112f / 255f, 0.88f), CityHudIcons.Icon.None, 16);
                button.name = "Home navigation " + labels[i];
                mainMenuRailItems[i] = button.GetComponent<RectTransform>();
                if (artSlots[i] >= 0)
                {
                    var iconRect = Rect("Navigation art", button.transform);
                    var icon = iconRect.gameObject.AddComponent<Image>();
                    icon.raycastTarget = false;
                    HomeArt(icon, artSlots[i]);
                }
            }
        }

        private void BuildHomeCenter()
        {
            mainMenuLogo = Label(mainMenuCenter, "نيو غزة\nNEW GAZA", CityTypography.DisplaySize, Gold);
            mainMenuLogo.name = "Home game logo";
            mainMenuLogo.alignment = TextAnchor.MiddleCenter;
            var eyebrow = Label(mainMenuCenter, "مستقبل المدينة يبدأ بخطوة", 18, Muted);
            eyebrow.name = "Home slogan";
            eyebrow.alignment = TextAnchor.MiddleCenter;
            mainMenuHero = Surface("Reconstruction call to action", mainMenuCenter,
                new Color(242f / 255f, 140f / 255f, 40f / 255f, 1f));
            var heroButton = mainMenuHero.gameObject.AddComponent<Button>();
            heroButton.targetGraphic = mainMenuHero.GetComponent<Image>();
            heroButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = heroButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.88f, 0.82f, 0.7f, 1f);
            colors.fadeDuration = 0.08f;
            heroButton.colors = colors;
            heroButton.onClick.AddListener(() => { session.Audio?.PlayClick(); ContinueReconstruction(); });
            var hammer = Rect("Reconstruction icon", mainMenuHero);
            var hammerImage = hammer.gameObject.AddComponent<Image>();
            hammerImage.raycastTarget = false;
            HomeArt(hammerImage, 1);
            var heroText = Label(mainMenuHero, "ابدأ إعادة الإعمار", 27, Cream);
            CityTypography.Apply(heroText, CityTextRole.Button);
            heroText.fontSize = CityTypography.HeadingSize;
            heroText.alignment = TextAnchor.MiddleCenter;
            mainMenuJob = Label(mainMenuCenter, "", 17, Cream);
            mainMenuJob.alignment = TextAnchor.MiddleCenter;
        }

        private void BuildHomeDistrict()
        {
            var title = Label(mainMenuDistrict, "الحي الجاري العمل عليه", 18, Gold);
            title.alignment = TextAnchor.MiddleRight;
            mainMenuDistrictName = Label(mainMenuDistrict, "", 26, Cream);
            mainMenuDistrictProgress = Label(mainMenuDistrict, "", 19, Cream);
            mainMenuThumbnail = Rect("Live district preview", mainMenuDistrict).gameObject.AddComponent<RawImage>();
            HomeDistrictPreview(mainMenuThumbnail);
            var track = Surface("District progress track", mainMenuDistrict, new Color(7f / 255f, 27f / 255f, 54f / 255f, 1f));
            mainMenuProgressFill = AddImage(Rect("District progress fill", track), new Color32(22, 139, 219, 255));
            Stretch(mainMenuProgressFill.rectTransform, 0, 0, 0, 0);
            mainMenuProgressFill.type = Image.Type.Filled;
            mainMenuProgressFill.fillMethod = Image.FillMethod.Horizontal;
            mainMenuProgressFill.fillOrigin = 1;
            mainMenuDistrictRubric = Label(mainMenuDistrict, "", 16, Muted);
            mainMenuNeeds = Label(mainMenuDistrict, "", 16, Muted);
            mainMenuNeeds.alignment = TextAnchor.UpperRight;
            mainMenuRewardButton = ActionButton(mainMenuDistrict, "استلام مكافأة الحي", ClaimHomeDistrictReward,
                new Color32(36, 184, 90, 255));
            mainMenuRewardButton.name = "Claim district reward";
            mainMenuRewardLabel = mainMenuRewardButton.GetComponentInChildren<ArabicLabel>();
            var details = ActionButton(mainMenuDistrict, "تفاصيل الحي", () =>
            {
                var summary = CityHomeSummary.Create(session.Economy);
                session.ChooseDistrict(summary.District);
                OpenHomePage(Page.Map);
            }, new Color32(18, 61, 112, 255));
            details.name = "District details";
        }

        private void BuildHomeActions()
        {
            string[] titles = { "المخزن", "المعدات", "تأهيل الطرق", "المتجر", "صندوق الهدايا" };
            string[] descriptions = { "مواد المدينة", "الأسطول والمصنع", "اختر طريقاً في الحي", "مبانٍ وأعمال", "هدية كل 24 ساعة" };
            int[] slots = { 0, 1, 2, 3, 4 };
            Action[] actions =
            {
                () => OpenHomePage(Page.Resources),
                () => OpenHomePage(Page.Fleet),
                OpenRoadSelectionInstruction,
                OpenHomeStore,
                () => OpenHomePage(Page.Gift)
            };
            for (int i = 0; i < titles.Length; i++)
            {
                var button = ActionButton(mainMenuActionContent, "", actions[i],
                    new Color(18f / 255f, 61f / 255f, 112f / 255f, 0.9f), CityHudIcons.Icon.None);
                button.name = "Home action " + titles[i];
                var rect = button.GetComponent<RectTransform>();
                mainMenuActionCards[i] = rect;
                var iconRect = Rect("Action artwork", button.transform);
                var icon = iconRect.gameObject.AddComponent<Image>();
                icon.raycastTarget = false;
                HomeArt(icon, slots[i]);
                var title = Label(button.transform, titles[i], 18, Cream);
                CityTypography.Apply(title, CityTextRole.Heading);
                mainMenuActionTitles[i] = title;
                title.alignment = TextAnchor.MiddleCenter;
                var description = Label(button.transform, descriptions[i], 14, Muted);
                mainMenuActionDescriptions[i] = description;
                description.alignment = TextAnchor.MiddleCenter;
                if (i == 4)
                {
                    var badge = Surface("Gift ready badge", button.transform, new Color32(36, 184, 90, 255));
                    mainMenuGiftBadge = badge.gameObject;
                    var badgeText = Label(badge, "جاهزة", 13, Cream);
                    badgeText.alignment = TextAnchor.MiddleCenter;
                }
            }
        }

        /// <summary>Positions menu regions using safe-area logical pixels in either orientation.</summary>
        public void LayoutMainMenu(float width, float height)
        {
            if (!mainMenuBuilt && mainMenuRoot == null) return;
            if (mainMenuRoot == null || mainMenuHeader == null || width < 10 || height < 10) return;
            bool portrait = height > width;
            bool compact = !portrait && height < 680;
            float margin = portrait ? 10f : 14f;
            float headerHeight = portrait ? 166f : compact ? 76f : 92f;
            float actionHeight = portrait ? 140f : compact ? 104f : 126f;
            float railWidth = portrait ? width - margin * 2f : 132f;
            float railHeight = portrait ? 66f : Mathf.Max(260f, height - headerHeight - actionHeight - margin * 4f);
            float districtWidth = portrait ? width - margin * 2f : Mathf.Min(334f, width * 0.29f);
            float contentLeft = portrait ? margin : margin + railWidth + 14f;
            float contentTop = portrait ? margin + headerHeight + railHeight + 10f : margin + headerHeight + 14f;
            float bottomTop = height - actionHeight - margin;

            Place(mainMenuHeader, margin, margin, width - margin * 2f, headerHeight, true);
            Place(mainMenuRail, margin, portrait ? margin + headerHeight + 8f : margin + headerHeight + 14f,
                railWidth, railHeight, true);
            Place(mainMenuActions, portrait ? margin : contentLeft, bottomTop,
                portrait ? width - margin * 2f : width - contentLeft - margin, actionHeight, true);

            Place(mainMenuIdentity, 10, 8, portrait ? width - margin * 2 - 20 : 222, portrait ? 48 : headerHeight - 16, true);
            var identity = mainMenuIdentity;
            Place(identity.Find("Arabic label") as RectTransform, 9, 2, portrait ? identity.rect.width - 212 : identity.rect.width - 18, 40, true);
            Place(identity.Find("Home profile and settings") as RectTransform, portrait ? identity.rect.width - 194 : 8, portrait ? 2 : 32,
                portrait ? 184 : identity.rect.width - 16, portrait ? 42 : headerHeight - 46, true);

            float resourceStart = portrait ? 12 : 244;
            float resourceWidth = portrait
                ? (width - 24 - 20) / 5f
                : (width - resourceStart - margin * 2 - 10) / 5f;
            for (int i = 0; i < mainMenuResourceButtons.Length; i++)
            {
                var button = mainMenuResourceButtons[i].GetComponent<RectTransform>();
                float x = portrait ? resourceStart + i * resourceWidth : resourceStart + i * resourceWidth;
                float y = portrait ? headerHeight - 56 : 14;
                float h = portrait ? 48 : headerHeight - 28;
                Place(button, x, y, resourceWidth - (portrait ? 4 : 7), h, true);
                var icon = button.Find("Resource art") as RectTransform;
                Place(icon, 8, 7, 30, 30, true);
                Place(mainMenuResourceLabels[i].rectTransform, 40, 3, resourceWidth - 46, h - 6, true);
                ButtonText(mainMenuResourceButtons[i], "");
            }

            if (portrait)
            {
                float itemWidth = (railWidth - 12f) / mainMenuRailItems.Length;
                for (int i = 0; i < mainMenuRailItems.Length; i++)
                {
                    Place(mainMenuRailItems[i], 6 + i * itemWidth, 5, itemWidth - 3, railHeight - 10, true);
                    var icon = mainMenuRailItems[i].Find("Navigation art") as RectTransform;
                    if (icon != null) Place(icon, 4, 3, 24, 24, true);
                    var label = mainMenuRailItems[i].GetComponentInChildren<ArabicLabel>();
                    Place(label.rectTransform, 2, 29, itemWidth - 7, 26, true);
                }
            }
            else
            {
                float navHeight = (railHeight - 18f) / mainMenuRailItems.Length;
                for (int i = 0; i < mainMenuRailItems.Length; i++)
                {
                    Place(mainMenuRailItems[i], 8, 9 + i * navHeight, railWidth - 16, navHeight - 5, true);
                    var icon = mainMenuRailItems[i].Find("Navigation art") as RectTransform;
                    if (icon != null) Place(icon, 8, 8, 30, 30, true);
                    var label = mainMenuRailItems[i].GetComponentInChildren<ArabicLabel>();
                    Place(label.rectTransform, icon != null ? 42 : 4, 4, railWidth - (icon != null ? 64 : 24), navHeight - 12, true);
                }
            }

            float districtTop, districtHeight, centerWidth;
            if (portrait)
            {
                districtTop = contentTop + 224f;
                districtHeight = Mathf.Max(260, bottomTop - districtTop - 14);
                centerWidth = width - margin * 2;
                Place(mainMenuCenter, margin, contentTop, centerWidth, 218, true);
                Place(mainMenuDistrict, margin, districtTop, width - margin * 2, districtHeight, true);
            }
            else
            {
                districtTop = contentTop;
                districtHeight = Mathf.Max(270, bottomTop - districtTop - 14);
                centerWidth = width - contentLeft - districtWidth - margin * 3f;
                Place(mainMenuCenter, contentLeft, contentTop + Mathf.Max(0, (districtHeight - 360) * 0.24f),
                    centerWidth, Mathf.Min(360, districtHeight), true);
                Place(mainMenuDistrict, width - margin - districtWidth, districtTop, districtWidth, districtHeight, true);
            }

            Place(mainMenuLogo.rectTransform, 8, 4, centerWidth - 16, portrait ? 62 : 92, true);
            Place(mainMenuCenter.Find("Home slogan") as RectTransform, 8, portrait ? 66 : 100, centerWidth - 16, 30, true);
            Place(mainMenuHero, centerWidth * 0.06f, portrait ? 100 : 138, centerWidth * 0.88f, portrait ? 60 : 84, true);
            Place(mainMenuHero.Find("Reconstruction icon") as RectTransform, 14, 14, portrait ? 48 : 58, portrait ? 48 : 58, true);
            Place(mainMenuHero.Find("Arabic label") as RectTransform, portrait ? 68 : 84, 4, centerWidth * 0.88f - (portrait ? 78 : 94), portrait ? 52 : 76, true);
            Place(mainMenuJob.rectTransform, 8, portrait ? 166 : 232, centerWidth - 16, 50, true);

            Place(mainMenuDistrict.Find("Arabic label") as RectTransform, 14, 12, districtWidth - 28, 24, true);
            Place(mainMenuDistrictName.rectTransform, 14, 38, districtWidth - 28, 36, true);
            Place(mainMenuDistrictProgress.rectTransform, 14, 78, districtWidth - 28, 28, true);
            Place(mainMenuDistrict.Find("District progress track") as RectTransform, 18, 112, districtWidth - 36, 12, true);
            float previewHeight = Mathf.Min(140, Mathf.Max(48, districtHeight - 334));
            float previewTop = compact ? 108 : 134;
            if (compact)
            {
                Place(mainMenuDistrict.Find("Arabic label") as RectTransform, 14, 8, districtWidth - 28, 22, true);
                Place(mainMenuDistrictName.rectTransform, 14, 30, districtWidth - 28, 32, true);
                Place(mainMenuDistrictProgress.rectTransform, 14, 64, districtWidth - 28, 24, true);
                Place(mainMenuDistrict.Find("District progress track") as RectTransform, 18, 94, districtWidth - 36, 10, true);
            }
            Place(mainMenuThumbnail.rectTransform, 16, previewTop, districtWidth - 32, previewHeight, true);
            float rubricTop = previewTop + 8 + previewHeight, needsTop = rubricTop + 46;
            Place(mainMenuDistrictRubric.rectTransform, 16, rubricTop, districtWidth - 32, 44, true);
            bool reward = mainMenuRewardButton.gameObject.activeSelf;
            Place(mainMenuNeeds.rectTransform, 16, needsTop, districtWidth - 32, Mathf.Max(28, districtHeight - (reward ? 124 : 68) - needsTop), true);
            Place(mainMenuRewardButton.GetComponent<RectTransform>(), 16, districtHeight - 112, districtWidth - 32, 44, true);
            Place(mainMenuDistrict.Find("District details") as RectTransform, 16, districtHeight - 58, districtWidth - 32, 44, true);

            float actionAreaWidth = portrait ? width - margin * 2 : width - contentLeft - margin;
            float cardWidth = Mathf.Max(portrait ? 176 : 160, (actionAreaWidth - 32) / 5f);
            float scrollWidth = 16 + 5 * (cardWidth + 4);
            Place(mainMenuActionContent, 0, 0, Mathf.Max(actionAreaWidth, scrollWidth), actionHeight, true);
            mainMenuActionScroll.horizontal = scrollWidth > actionAreaWidth;
            for (int i = 0; i < mainMenuActionCards.Length; i++)
            {
                Place(mainMenuActionCards[i], 8 + i * (cardWidth + 4), 8, cardWidth, actionHeight - 16, true);
                Place(mainMenuActionCards[i].Find("Action artwork") as RectTransform, 8, 4, cardWidth - 16, compact ? 34 : 44, true);
                Place(mainMenuActionTitles[i].rectTransform, 4, compact ? 38 : 48, cardWidth - 8, 34, true);
                mainMenuActionDescriptions[i].gameObject.SetActive(!compact);
                Place(mainMenuActionDescriptions[i].rectTransform, 4, 78, cardWidth - 8, 34, true);
                if (i == 4 && mainMenuGiftBadge != null)
                    Place(mainMenuGiftBadge.GetComponent<RectTransform>(), cardWidth - 62, 4, 54, 23, true);
            }
        }

        /// <summary>Refreshes all home-screen figures from the authoritative economy.</summary>
        public void RefreshMainMenu()
        {
            if (!mainMenuBuilt || session == null || session.Economy == null) return;
            CityHomeSummary summary = CityHomeSummary.Create(session.Economy);
            var state = session.State;
            mainMenuPlayer.SetText(string.IsNullOrWhiteSpace(summary.PlayerName) ? "البنّاء" : summary.PlayerName);
            mainMenuResourceLabels[0].SetText("العملات\n" + N(summary.Coins));
            mainMenuResourceLabels[1].SetText("الخرسانة\n" + N(summary.Concrete));
            mainMenuResourceLabels[2].SetText("الحديد\n" + N(summary.Iron));
            mainMenuResourceLabels[3].SetText("الخشب\n" + N(summary.Wood));
            mainMenuResourceLabels[4].SetText("مواد أخرى\n" + N(summary.Other));

            int d = Mathf.Clamp(summary.District, 0, GameCatalog.Districts.Length - 1);
            float progress = Mathf.Clamp01(summary.Progress);
            mainMenuDistrictName.SetText(summary.DistrictName);
            mainMenuDistrictProgress.SetText(Percent(progress) + " إنجاز");
            mainMenuProgressFill.fillAmount = progress;
            int remainingSites = Mathf.Max(0, summary.TotalSites - summary.ClearedSites);
            mainMenuDistrictRubric.SetText("المواقع المُزالة: " + summary.ClearedSites + " / " + summary.TotalSites +
                "\nالمباني المكتملة: " + summary.CompletedBuildings + " · أعمال البناء: " + summary.BuildingWorks);
            mainMenuNeeds.SetText(string.IsNullOrWhiteSpace(summary.NeedsText) ? "متطلبات الحي: لا توجد بيانات إضافية." :
                summary.NeedsText);

            bool unclaimedReward = progress >= 1f && !state.districts[d].rewardClaimed;
            bool changedReward = mainMenuRewardButton.gameObject.activeSelf != unclaimedReward;
            mainMenuRewardButton.gameObject.SetActive(unclaimedReward);
            if (unclaimedReward) mainMenuNeeds.SetText("اكتملت متطلبات الحي");
            if (changedReward) LayoutMainMenu(safe.rect.width, safe.rect.height);
            mainMenuRewardButton.interactable = unclaimedReward;
            mainMenuRewardLabel.SetText(state.districts[d].rewardClaimed ? "تم استلام مكافأة الحي" :
                progress >= 1f ? "استلام مكافأة الحي" : "تُتاح المكافأة عند 100%");

            mainMenuJob.SetText(BuildHomeWorkStatus(summary));
            if (mainMenuGiftBadge != null)
                mainMenuGiftBadge.SetActive(session.Economy.CanClaimDailyGift(session.Now));
            if (mainMenuRoot != null && mainMenuRoot.gameObject.activeSelf)
            {
                if (header != null) header.gameObject.SetActive(false);
                if (activity != null) activity.gameObject.SetActive(false);
                if (tutorialObject != null) tutorialObject.SetActive(false);
                if (navigation != null) navigation.gameObject.SetActive(false);
                if (selectionObject != null) selectionObject.SetActive(false);
                if (modalObject == null && confirmationObject == null) mainMenuRoot.SetAsLastSibling();
                if (confirmationObject == null) BringHomeTopForward();
                RefreshHomePreview(d);
            }
        }

        private string BuildHomeWorkStatus(CityHomeSummary summary)
        {
            var state = session.State;
            string work = state.jobStage == JobStage.Idle ? "الفريق جاهز" :
                Stage(state.jobStage) + " · " + TimeLeft(state.jobFinishUtc - session.Now) +
                " · " + GameCatalog.Districts[Mathf.Clamp(state.jobDistrict, 0, GameCatalog.Districts.Length - 1)].name;
            return work + "\nالمصنع " + summary.FactoryLevel + " · حفارات " + summary.Excavators +
                " · شاحنات " + summary.Trucks + " · جرافات " + summary.Bulldozers;
        }

        /// <summary>Shows or hides the home overlay and synchronizes all other HUD layers.</summary>
        public void ShowMainMenu(bool show)
        {
            if (show && !mainMenuBuilt) BuildMainMenu();
            if (mainMenuRoot == null) return;
            bool wasVisible = MainMenuVisible;
            if (show && page != Page.None) ClosePage();
            if (show && session != null && session.Development != null && session.Development.Placing)
                session.Development.Cancel();
            mainMenuRoot.gameObject.SetActive(show);
            mainMenuHeader.gameObject.SetActive(show);
            if (header != null) header.gameObject.SetActive(!show);
            if (activity != null) activity.gameObject.SetActive(!show);
            if (tutorialObject != null) tutorialObject.SetActive(!show && !(Screen.height > Screen.width && selectedPlot != -1));
            if (navigation != null) navigation.gameObject.SetActive(!show);
            if (selectionObject != null) selectionObject.SetActive(!show && selectedPlot != -1);
            if (session != null && session.Development != null) session.Development.SetHomeVisible(show);
            if (!show && wasVisible) ReleaseCameraAfterTouch();
            else
            {
                CityCamera.ModalOpen = show || modalObject != null || confirmationObject != null;
                closingCameraBlock = false;
            }
            if (show)
            {
                if (cityCamera != null) cityCamera.FrameCity();
                selectedPlot = -1;
                if (selectionObject != null) selectionObject.SetActive(false);
                if (world != null) world.SetSelectedPlot(-1);
                RefreshMainMenu();
                mainMenuRoot.SetAsLastSibling();
                BringHomeTopForward();
            }
        }

        /// <summary>Chooses the active job's district, selected unlocked district, or first open incomplete one.</summary>
        public void ContinueReconstruction()
        {
            if (session == null) return;
            int district = CityHomeSummary.ResumeDistrict(session.State);
            if (district < 0) return;
            session.ChooseDistrict(district);
            cityCamera.Focus(world.DistrictPosition(district));
            ShowMainMenu(false);
        }

        private void ClaimHomeDistrictReward()
        {
            CityHomeSummary summary = CityHomeSummary.Create(session.Economy);
            int district = Mathf.Clamp(summary.District, 0, session.State.districts.Length - 1);
            if (summary.Progress < 1f || session.State.districts[district].rewardClaimed) return;
            session.Perform(e => e.ClaimDistrictReward(district, session.Now));
            RefreshMainMenu();
        }

        private void OpenHomePage(Page destination)
        {
            OpenPage(destination);
        }

        private void OpenHomeStore()
        {
            ShowMainMenu(false);
            if (session.Development != null) session.Development.OpenStore();
            else session.Notify("المتجر غير متاح حالياً.");
        }

        private void OpenRoadSelectionInstruction()
        {
            int district = CityHomeSummary.ResumeDistrict(session.State);
            session.ChooseDistrict(district);
            ShowMainMenu(false);
            cityCamera.Focus(world.DistrictPosition(district));
            session.Notify("اختر قطعة طريق ظاهرة في الحي لعرض خيارات التأهيل. لم يبدأ أي عمل.");
        }
    }
}