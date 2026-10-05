using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed partial class CityDevelopmentUI
    {
        private RectTransform[] fleetMarkers = new RectTransform[3];
        private Image[] fleetMarkerImages = new Image[3];
        private ArabicLabel[] fleetMarkerLabels = new ArabicLabel[3];
        private float[] fleetMarkerOpacity = new float[3];
        private RectTransform fleetMarkerLayer;

        private void UpdateFleetMarkers(Camera mapCamera)
        {
            int count = development.VisibleFleetCount * 3;
            if (fleetMarkers.Length < count)
            {
                System.Array.Resize(ref fleetMarkers, count);
                System.Array.Resize(ref fleetMarkerImages, count);
                System.Array.Resize(ref fleetMarkerLabels, count);
                System.Array.Resize(ref fleetMarkerOpacity, count);
            }
            bool hidden = homeVisible || StoreOpen || (session.Hud != null && session.Hud.BlockingWindowVisible);
            float targetAlpha = FleetMarkerPresentation.Opacity(mapCamera.orthographicSize);
            for (int index = 0; index < count; index++)
            {
                var fleet = development.VisibleFleet(index / 3);
                if (hidden || fleet == null ||
                    !fleet.TryGetMachineAudioState(index % 3, out var worldPosition, out _, out _, out _))
                {
                    if (fleetMarkers[index] != null && fleetMarkers[index].gameObject.activeSelf)
                        fleetMarkers[index].gameObject.SetActive(false);
                    continue;
                }
                Vector3 screen = mapCamera.WorldToViewportPoint(worldPosition + Vector3.up * .16f);
                bool onScreen = screen.z > 0 && screen.x >= 0 && screen.x <= 1 && screen.y >= 0 && screen.y <= 1;
                if (!onScreen)
                {
                    if (fleetMarkers[index] != null) fleetMarkers[index].gameObject.SetActive(false);
                    continue;
                }
                EnsureFleetMarker(index);
                fleetMarkerOpacity[index] = Mathf.MoveTowards(fleetMarkerOpacity[index], targetAlpha,
                    Time.unscaledDeltaTime * 3f);
                var marker = fleetMarkers[index];
                bool visible = fleetMarkerOpacity[index] > .005f;
                if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
                if (!visible) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(fleetMarkerLayer,
                    new Vector2(screen.x * Screen.width, screen.y * Screen.height), null, out var point);
                marker.anchoredPosition = point + new Vector2(0, 22);
                Color tint = index == 1 ? new Color(.30f, 1f, .60f) : new Color(1f, .80f, .18f);
                tint.a = fleetMarkerOpacity[index];
                fleetMarkerImages[index].color = tint;
                fleetMarkerLabels[index].color = new Color(1f, 1f, 1f, tint.a);
            }
        }

        private void EnsureFleetMarker(int index)
        {
            if (fleetMarkers[index] != null) return;
            if (fleetMarkerLayer == null)
            {
                fleetMarkerLayer = Rect("Actual fleet positions / fading overhead markers", transform);
                Stretch(fleetMarkerLayer); fleetMarkerLayer.SetAsFirstSibling();
            }
            int kind = index % 3;
            string name = (kind == 0 ? "حفارة" : kind == 1 ? "شاحنة" : "جرافة") + " " + (index / 3 + 1);
            var marker = Rect("Fleet marker " + name, fleetMarkerLayer);
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(.5f, .5f);
            marker.sizeDelta = new Vector2(44, 44);
            var image = marker.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = factoryIcons.Get(kind == 0 ? CityHudIcons.Icon.Excavator :
                kind == 1 ? CityHudIcons.Icon.Fleet : CityHudIcons.Icon.Bulldozer);
            var label = Label(marker, name, 12);
            label.alignment = TextAnchor.MiddleCenter;
            Box(label.rectTransform, -14, 44, 72, 22, false);
            fleetMarkers[index] = marker; fleetMarkerImages[index] = image; fleetMarkerLabels[index] = label;
        }
    }
}