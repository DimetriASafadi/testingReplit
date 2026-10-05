using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed partial class CityDevelopmentUI
    {
        private RectTransform activeWorkMarker, activeWorkMarkerLayer;
        private Image activeWorkMarkerImage;
        private ArabicLabel activeWorkMarkerLabel;
        private string markerWorkId;
        private string markerWorkStatus;
        private int markerWorkDistrict = -1;
        private Vector3 markerWorkPosition;
        private CanvasGroup activeWorkOpacity;

        private void UpdateActiveWorkMarker(Camera mapCamera)
        {
            var state = session.State;
            bool hasWork = state.jobStage != JobStage.Idle;
            if (!hasWork || homeVisible || StoreOpen || (session.Hud != null && session.Hud.BlockingWindowVisible))
            {
                if (activeWorkMarker != null && activeWorkMarker.gameObject.activeSelf)
                    activeWorkMarker.gameObject.SetActive(false);
                if (!hasWork) { markerWorkId = null; markerWorkDistrict = -1; }
                return;
            }
            string id = state.development?.activeRubbleId;
            if (markerWorkDistrict != state.jobDistrict || markerWorkId != id)
            {
                // Work-site geometry is stationary: avoid searching thousands of sites every frame.
                markerWorkPosition = development.WorkPosition;
                markerWorkDistrict = state.jobDistrict; markerWorkId = id;
            }
            Vector3 screen = mapCamera.WorldToViewportPoint(markerWorkPosition + Vector3.up * .10f);
            if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1)
            {
                if (activeWorkMarker != null) activeWorkMarker.gameObject.SetActive(false);
                return;
            }
            EnsureActiveWorkMarker();
            activeWorkOpacity.alpha = Mathf.MoveTowards(activeWorkOpacity.alpha,
                FleetMarkerPresentation.Opacity(mapCamera.orthographicSize), Time.unscaledDeltaTime * 3);
            if (!activeWorkMarker.gameObject.activeSelf) activeWorkMarker.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(activeWorkMarkerLayer,
                new Vector2(screen.x * Screen.width, screen.y * Screen.height), null, out var point);
            activeWorkMarker.anchoredPosition = point + new Vector2(0, 68);
            activeWorkMarkerImage.sprite = factoryIcons.Get(state.jobStage == JobStage.Clearing ?
                CityHudIcons.Icon.Excavator : state.jobStage == JobStage.Hauling ?
                CityHudIcons.Icon.Fleet : CityHudIcons.Icon.Recycling);
            activeWorkMarkerImage.color = new Color(1f, .80f, .18f, 1f);
            string status = ActiveWorkPresentation.Status(state, session.Now);
            if (markerWorkStatus != status)
            {
                markerWorkStatus = status;
                activeWorkMarkerLabel.SetText("موقع العمل\n" + status);
            }
        }

        private void EnsureActiveWorkMarker()
        {
            if (activeWorkMarker != null) return;
            activeWorkMarkerLayer = Rect("Active rubble work location", transform);
            Stretch(activeWorkMarkerLayer); activeWorkMarkerLayer.SetAsFirstSibling();
            activeWorkMarker = Panel("Active work / current stage", activeWorkMarkerLayer, false);
            activeWorkMarker.anchorMin = activeWorkMarker.anchorMax = activeWorkMarker.pivot = new Vector2(.5f, .5f);
            activeWorkMarker.sizeDelta = new Vector2(230, 68);
            activeWorkOpacity = activeWorkMarker.gameObject.AddComponent<CanvasGroup>();
            activeWorkOpacity.alpha = 0;
            activeWorkOpacity.blocksRaycasts = false; activeWorkOpacity.interactable = false;
            var icon = Rect("Work phase icon", activeWorkMarker);
            Box(icon, 8, 14, 40, 40, false);
            activeWorkMarkerImage = icon.gameObject.AddComponent<Image>();
            activeWorkMarkerImage.raycastTarget = false;
            activeWorkMarkerLabel = Label(activeWorkMarker, "", 13);
            Box(activeWorkMarkerLabel.rectTransform, 54, 5, 168, 58, false);
            activeWorkMarkerLabel.alignment = TextAnchor.MiddleRight;
        }
    }
}