using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewGaza
{
    public sealed class CityHoldRotateButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public CityDevelopment Development { get; set; }
        private bool held;
        private int pointer;
        public void OnPointerDown(PointerEventData data)
        {
            if (held || data.button != PointerEventData.InputButton.Left || Development == null ||
                !Development.Placing || !GetComponent<Button>().IsInteractable()) return;
            held = true; pointer = data.pointerId;
        }
        public void OnPointerUp(PointerEventData data) { if (data.pointerId == pointer) Stop(); }
        public void OnPointerExit(PointerEventData data) { if (data.pointerId == pointer) Stop(); }
        private void Update()
        {
            if (!held) return;
            if (Development == null || !Development.Placing) { Stop(); return; }
            Development.RotateBy(45f * Time.unscaledDeltaTime);
        }
        private void Stop()
        {
            if (!held) return;
            held = false;
            if (Development != null) Development.FinishRotation();
        }
        private void OnDisable() { Stop(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Stop(); }
        private void OnApplicationPause(bool paused) { if (paused) Stop(); }
    }
}