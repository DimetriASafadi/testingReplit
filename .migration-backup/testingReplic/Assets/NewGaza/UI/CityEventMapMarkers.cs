using System.Collections.Generic;
using NewGaza.Core;
using NewGaza.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed partial class CityDevelopmentUI
    {
        private struct MapActivity { internal CityActivityItem item; internal Vector3 point; }
        private struct MapGroup { internal CityActivityItem first; internal Vector2 point; internal int count; }
        private sealed class EventMarker
        {
            internal RectTransform rect; internal Image icon; internal ArabicLabel count;
            internal int shownCount = -1; internal CityActivityKind shownKind;
        }
        private readonly List<MapActivity> mapActivities = new List<MapActivity>();
        private readonly Dictionary<int, MapGroup> eventGroups = new Dictionary<int, MapGroup>();
        private readonly List<EventMarker> eventMarkerPool = new List<EventMarker>();
        private RectTransform eventMarkerLayer;
        private float nextActivityScan;

        private void UpdateEventMarkers(Camera mapCamera)
        {
            bool hidden = homeVisible || StoreOpen || (session.Hud != null && session.Hud.BlockingWindowVisible);
            float alpha = FleetMarkerPresentation.Opacity(mapCamera.orthographicSize);
            if (hidden || alpha <= .005f)
            { HideEventMarkers(0); return; }
            if (Time.unscaledTime >= nextActivityScan)
            {
                nextActivityScan = Time.unscaledTime + 1;
                mapActivities.Clear();
                foreach (var item in CityActivityCatalog.Collect(session.State, session.Now))
                {
                    // The current rubble operation already has its own stage-specific annotation.
                    if (item.kind == CityActivityKind.Rubble) continue;
                    if (development.TryGetActivityPosition(item, out var point))
                        mapActivities.Add(new MapActivity { item = item, point = point });
                }
            }
            if (eventMarkerLayer == null)
            {
                eventMarkerLayer = Rect("Live construction income and reward locations", transform);
                Stretch(eventMarkerLayer); eventMarkerLayer.SetAsFirstSibling();
            }
            eventGroups.Clear();
            int columns = Mathf.Clamp(Mathf.FloorToInt(eventMarkerLayer.rect.width / 96), 2, 16);
            int rows = Mathf.Clamp(Mathf.FloorToInt(eventMarkerLayer.rect.height / 96), 2, 16);
            foreach (var activity in mapActivities)
            {
                var screen = mapCamera.WorldToViewportPoint(activity.point + Vector3.up * .12f);
                if (screen.z <= 0 || screen.x < 0 || screen.x >= 1 || screen.y < 0 || screen.y >= 1) continue;
                int cell = Mathf.FloorToInt(screen.y * rows) * columns + Mathf.FloorToInt(screen.x * columns);
                if (eventGroups.TryGetValue(cell, out var group))
                { group.count++; eventGroups[cell] = group; continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(eventMarkerLayer,
                    new Vector2(screen.x * Screen.width, screen.y * Screen.height), null, out var point);
                eventGroups[cell] = new MapGroup { first = activity.item, point = point, count = 1 };
            }
            int used = 0;
            foreach (var group in eventGroups.Values)
            {
                var marker = GetEventMarker(used++);
                if (!marker.rect.gameObject.activeSelf) marker.rect.gameObject.SetActive(true);
                marker.rect.anchoredPosition = group.point + new Vector2(0, 22);
                marker.icon.sprite = factoryIcons.Get(EventIcon(group.first.kind));
                marker.icon.color = new Color(1f, .82f, .22f, alpha);
                marker.count.color = new Color(1, 1, 1, alpha);
                if (marker.shownCount != group.count || marker.shownKind != group.first.kind)
                {
                    marker.shownCount = group.count; marker.shownKind = group.first.kind;
                    marker.count.SetText(group.count > 1 ? "أحداث ×" + group.count : EventCaption(group.first.kind));
                }
            }
            HideEventMarkers(used);
        }
        private EventMarker GetEventMarker(int index)
        {
            if (index < eventMarkerPool.Count) return eventMarkerPool[index];
            var rect = Rect("Pooled map event", eventMarkerLayer);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(40, 40);
            var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = false;
            var label = Label(rect, "", 12); label.alignment = TextAnchor.MiddleCenter;
            Box(label.rectTransform, -24, 42, 88, 24, false);
            var marker = new EventMarker { rect = rect, icon = image, count = label };
            eventMarkerPool.Add(marker); return marker;
        }
        private void HideEventMarkers(int start)
        {
            for (int i = start; i < eventMarkerPool.Count; i++)
                if (eventMarkerPool[i].rect.gameObject.activeSelf) eventMarkerPool[i].rect.gameObject.SetActive(false);
        }
        private static CityHudIcons.Icon EventIcon(CityActivityKind kind) =>
            kind == CityActivityKind.Construction || kind == CityActivityKind.ProjectConstruction ? CityHudIcons.Icon.Projects :
            kind == CityActivityKind.DistrictReward ? CityHudIcons.Icon.Gift : CityHudIcons.Icon.Resources;
        private static string EventCaption(CityActivityKind kind) =>
            kind == CityActivityKind.Construction || kind == CityActivityKind.ProjectConstruction ? "قيد البناء" :
            kind == CityActivityKind.DistrictReward ? "مكافأة" : "دخل جاهز";
    }
}