using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewGaza.UI
{
    /// <summary>Small press response with no layout rebuild or per-frame allocation.</summary>
    public sealed class CityButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Button button;
        private Vector3 rest;
        private float target = 1, amount = 1;
        private Coroutine routine;
        private void Awake() { button = GetComponent<Button>(); rest = transform.localScale; }
        public void OnPointerDown(PointerEventData eventData)
        { if (button != null && button.IsInteractable()) { target = 1.025f; StartMotion(); } }
        public void OnPointerUp(PointerEventData eventData) { target = 1; StartMotion(); }
        public void OnPointerExit(PointerEventData eventData) { target = 1; StartMotion(); }
        private void StartMotion()
        {
            if (routine == null && Mathf.Abs(amount - target) > .0001f) routine = StartCoroutine(Animate());
        }
        private IEnumerator Animate()
        {
            while (Mathf.Abs(amount - target) > .0001f)
            {
                amount = Mathf.MoveTowards(amount, target, Time.unscaledDeltaTime * .4f);
                transform.localScale = rest * amount;
                yield return null;
            }
            routine = null;
        }
        private void OnDisable()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null; target = amount = 1; transform.localScale = rest;
        }
    }
}