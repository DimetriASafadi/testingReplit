using NewGaza.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza.UI
{
    internal static class CityUiLayout
    {
        internal static bool Apply(Canvas canvas, CanvasScaler scaler, RectTransform safe)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return false;
            Rect area = Screen.safeArea;
            var viewport = InterfaceViewport.Calculate(Screen.width, Screen.height,
                area.x, area.y, area.width, area.height);
            // Size all canvases in the SAME logical units, never mix raw screen
            // pixels with scaled RectTransform dimensions.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = viewport.scale;
            canvas.scaleFactor = viewport.scale;
            safe.anchorMin = new Vector2(viewport.x / Screen.width, viewport.y / Screen.height);
            safe.anchorMax = new Vector2((viewport.x + viewport.width) / Screen.width,
                (viewport.y + viewport.height) / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            return safe.rect.width > 0 && safe.rect.height > 0;
        }
    }
}