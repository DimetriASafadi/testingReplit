using UnityEngine;
using UnityEngine.UI;

namespace NewGaza.UI
{
    public sealed class CityProgressMotion : MonoBehaviour
    {
        private Image image;
        private float target;
        private int context = int.MinValue;
        private bool primed;
        public static void Set(Image image, float value, int context)
        {
            var motion = image.GetComponent<CityProgressMotion>();
            if (motion == null) motion = image.gameObject.AddComponent<CityProgressMotion>();
            motion.image = image; motion.target = Mathf.Clamp01(value);
            // New views/district switches are not a fabricated progress increase.
            if (!motion.primed || motion.context != context || value < image.fillAmount)
                image.fillAmount = motion.target;
            motion.context = context; motion.primed = true;
            motion.enabled = Mathf.Abs(image.fillAmount - motion.target) > .0001f;
        }
        private void Update()
        {
            image.fillAmount = Mathf.MoveTowards(image.fillAmount, target, Time.unscaledDeltaTime * 1.5f);
            if (Mathf.Abs(image.fillAmount - target) < .0001f) enabled = false;
        }
    }
}