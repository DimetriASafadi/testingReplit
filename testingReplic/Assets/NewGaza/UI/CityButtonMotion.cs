using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewGaza.UI
{
    /// <summary>Small press response with no layout rebuild or per-frame allocation.</summary>
    public sealed class CityButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        private Button button;
        private Vector3 rest;
        private float target = 1, amount = 1;
        private Coroutine routine;
        private Outline edge;
        private Shadow depth;
        private Image shine;
        private bool hovering;
        private void Awake()
        {
            button = GetComponent<Button>(); rest = transform.localScale;
            edge = gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(.42f, .85f, 1f, .65f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            depth = gameObject.AddComponent<Shadow>();
            depth.effectColor = new Color(0, .025f, .08f, .85f);
            depth.effectDistance = new Vector2(0, -4);
            var top = new GameObject("Button top highlight", typeof(RectTransform), typeof(Image));
            top.transform.SetParent(transform, false);
            var rect = (RectTransform)top.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1); rect.offsetMin = new Vector2(6, -3);
            rect.offsetMax = new Vector2(-6, -1);
            shine = top.GetComponent<Image>(); shine.raycastTarget = false;
            shine.color = new Color(1, 1, 1, .2f);
        }
        public void SetIconOnly()
        {
            edge.enabled = depth.enabled = false;
            shine.gameObject.SetActive(false);
        }
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovering = true;
            if (button != null && button.IsInteractable()) { target = 1.012f; StartMotion(); }
        }
        public void OnPointerDown(PointerEventData eventData)
        { if (button != null && button.IsInteractable()) { target = .945f; StartMotion(); } }
        public void OnPointerUp(PointerEventData eventData) { target = hovering ? 1.012f : 1; StartMotion(); }
        public void OnPointerExit(PointerEventData eventData) { hovering = false; target = 1; StartMotion(); }
        private void StartMotion()
        {
            if (routine == null && Mathf.Abs(amount - target) > .0001f) routine = StartCoroutine(Animate());
        }
        private IEnumerator Animate()
        {
            while (Mathf.Abs(amount - target) > .0001f)
            {
                amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime * 1.2f);
                transform.localScale = rest * amount;
                if (shine != null) shine.color = new Color(1, 1, 1, target < 1 ? .08f : hovering ? .4f : .2f);
                if (depth != null) depth.effectDistance = new Vector2(0, target < 1 ? -1 : -4);
                yield return null;
            }
            routine = null;
        }
        private void OnDisable()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null; hovering = false; target = amount = 1; transform.localScale = rest;
            if (shine != null) shine.color = new Color(1, 1, 1, .2f);
            if (depth != null) depth.effectDistance = new Vector2(0, -4);
        }
    }
}