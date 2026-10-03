using System.Text;
using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed partial class CityHud
    {
        private readonly CityFeedbackTracker feedbackTracker = new CityFeedbackTracker();
        private RectTransform feedbackPanel;
        private CanvasGroup feedbackGroup;
        private ArabicLabel feedbackText;
        private Image feedbackGlow;
        private readonly RectTransform[] feedbackCoins = new RectTransform[4];
        private float feedbackStart = -10;
        private int feedbackPendingDistrict = -1;

        private void BuildFeedback()
        {
            feedbackTracker.Capture(session.State);
            feedbackPanel = Surface("Actual event feedback", safe, Navy);
            feedbackPanel.GetComponent<Image>().raycastTarget = false;
            feedbackGroup = feedbackPanel.gameObject.AddComponent<CanvasGroup>();
            feedbackGroup.blocksRaycasts = false;
            feedbackGroup.interactable = false;
            feedbackGlow = AddImage(Rect("Reward light", feedbackPanel), new Color(1, .78f, .16f, .15f));
            feedbackGlow.raycastTarget = false;
            Stretch(feedbackGlow.rectTransform, 0, 0, 0, 0);
            feedbackText = Label(feedbackPanel, "", CityTypography.HeadingSize, Cream);
            Stretch(feedbackText.rectTransform, 20, 14, 20, 54);
            feedbackText.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < feedbackCoins.Length; i++)
            {
                feedbackCoins[i] = Rect("Reward coin " + i, feedbackPanel);
                var image = feedbackCoins[i].gameObject.AddComponent<Image>();
                HomeArt(image, 5);
            }
            feedbackPanel.gameObject.SetActive(false);
        }

        private void DetectFeedback()
        {
            var frame = feedbackTracker.Capture(session.State);
            if (!frame.Any || feedbackPanel == null) return;
            var text = new StringBuilder();
            if (frame.coins > 0) text.Append("+").Append(N(frame.coins)).Append(" عملة\n");
            if (frame.completed > 0) text.Append("✓ اكتمل ").Append(frame.completed).Append(" من أعمال المدينة\n");
            if (frame.reward) text.Append("استلمت المكافأة\n");
            if (frame.unlocked.Length > 0)
            {
                for (int i = 0; i < frame.unlocked.Length; i++)
                    text.Append("حيّ جديد متاح · ").Append(GameCatalog.Districts[frame.unlocked[i]].name).Append("\n");
                int district = frame.unlocked[frame.unlocked.Length - 1];
                // Show the earned district; never teleport/interrupt another active operation.
                if (page == Page.None && confirmationObject == null &&
                    !session.Development.Placing && session.State.jobStage == JobStage.Idle)
                    session.ChooseDistrict(district);
                else feedbackPendingDistrict = district;
            }
            feedbackText.SetText(text.ToString().TrimEnd());
            feedbackStart = Time.unscaledTime;
            int lines = text.ToString().TrimEnd().Split('\n').Length;
            Center(feedbackPanel, Mathf.Min(540, safe.rect.width - 40), Mathf.Min(safe.rect.height - 48, 66 + lines * 38));
            feedbackPanel.anchoredPosition = new Vector2(0, safe.rect.height * .13f);
            feedbackPanel.gameObject.SetActive(true);
            feedbackPanel.SetAsLastSibling();
            feedbackGlow.gameObject.SetActive(frame.reward || frame.unlocked.Length > 0);
            foreach (var coin in feedbackCoins) coin.gameObject.SetActive(frame.coins > 0);
            AnimateFeedback();
        }

        private void AnimateFeedback()
        {
            if (feedbackPendingDistrict >= 0 && page == Page.None && confirmationObject == null &&
                !session.Development.Placing && session.State.jobStage == JobStage.Idle)
            {
                int district = feedbackPendingDistrict; feedbackPendingDistrict = -1;
                session.ChooseDistrict(district);
            }
            if (feedbackPanel == null || !feedbackPanel.gameObject.activeSelf) return;
            if (confirmationObject != null)
            { feedbackStart = Time.unscaledTime; feedbackGroup.alpha = 0; return; }
            if (feedbackPanel.GetSiblingIndex() != safe.childCount - 1) feedbackPanel.SetAsLastSibling();
            float age = Time.unscaledTime - feedbackStart;
            feedbackGroup.alpha = Mathf.Min(Mathf.Clamp01(age / .2f), Mathf.Clamp01((2.6f - age) / .4f));
            feedbackPanel.localScale = Vector3.one * Mathf.Lerp(.98f, 1, Mathf.Clamp01(age / .2f));
            feedbackGlow.color = new Color(1, .78f, .16f, .07f + .08f * Mathf.Sin(Mathf.Clamp01(age / 1.2f) * Mathf.PI));
            for (int i = 0; i < feedbackCoins.Length; i++)
            {
                Center(feedbackCoins[i], 26, 26);
                feedbackCoins[i].anchoredPosition = new Vector2(-50 + i * 34,
                    -feedbackPanel.rect.height * .5f + 20 + Mathf.Clamp01(age / .8f) * 15);
                feedbackCoins[i].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(age * 3 + i) * 8);
            }
            if (age >= 2.6f) feedbackPanel.gameObject.SetActive(false);
        }
    }
}