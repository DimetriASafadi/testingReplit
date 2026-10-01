using System;
using System.Collections;
using System.IO;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NewGaza
{
    /// <summary>Local PNG export. Native share/video recording requires a platform integration.</summary>
    public static class CityCapture
    {
        private static bool capturing;
        public static void Capture(GameSession session, CityCamera camera)
        {
            if (capturing || session == null || !session.Ready) return;
            if (session.State.cityCompletedUtc <= 0)
            {
                session.Notify("تُفتح صورة الإنجاز بعد إكمال شارع الرشيد.");
                return;
            }
            session.StartCoroutine(CaptureRoutine(session, camera));
        }

        private static IEnumerator CaptureRoutine(GameSession session, CityCamera camera)
        {
            capturing = true;
            camera.FrameCity();
            yield return new WaitForSecondsRealtime(1.5f);
            var card = new GameObject("Completion photo card", typeof(Canvas), typeof(CanvasScaler));
            var canvas = card.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = card.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var panel = new GameObject("Summary", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(card.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.04f, 0.05f);
            rect.anchorMax = new Vector2(0.96f, 0.28f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.04f, 0.13f, 0.17f, 0.96f);
            var labelObject = new GameObject("Statistics", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(panel.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16, 10); labelRect.offsetMax = new Vector2(-16, -10);
            int projects = 0;
            long investments = 0;
            for (int i = 0; i < session.State.districts.Length; i++)
                for (int p = 0; p < session.State.districts[i].projects.Length; p++)
                    if (session.State.districts[i].projects[p].completed)
                    {
                        projects++;
                        var definition = GameCatalog.Districts[i].projects[p];
                        if (definition.kind == ProjectKind.Investment) investments += definition.cost;
                    }
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.Load<Font>("NewGazaArabic");
            label.fontSize = 25; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16; label.resizeTextMaxSize = 25;
            string date = DateTimeOffset.FromUnixTimeSeconds(session.State.cityCompletedUtc).ToString("yyyy-MM-dd");
            label.text = ArabicText.Shape("نيو غزة — مدينة تُبنى من جديد\n" +
                session.State.playerName + " | الإنجاز 100% | الأحياء 10/10\n" +
                "المشاريع " + projects + " | قيمة الاستثمارات " + investments.ToString("N0") + " | " + date);
            yield return new WaitForEndOfFrame();
            Texture2D image = null;
            try
            {
                image = ScreenCapture.CaptureScreenshotAsTexture();
                string path = Path.Combine(Application.persistentDataPath, "NewGaza-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                session.Notify("تم حفظ صورة الإنجاز داخل ملفات اللعبة.");
                Debug.Log("New Gaza completion screenshot: " + path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                session.Notify("تعذّر حفظ الصورة. تحقق من مساحة التخزين.");
            }
            finally
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(card);
                capturing = false;
            }
        }
    }
}