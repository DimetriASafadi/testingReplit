using System;
using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityFleet
    {
        private static float HeadingAngle(Quaternion from, Quaternion to)
        {
            Vector3 a = from * Vector3.forward, b = to * Vector3.forward;
            return (float)(Math.Acos(Math.Max(-1d, Math.Min(1d,
                Vector3.Dot(a.normalized, b.normalized)))) * 180d / Math.PI);
        }
        private static void SmoothHeading(Transform root, Quaternion target, float dt, float degrees = 60f)
        {
            float angle = HeadingAngle(root.localRotation, target);
            if (dt <= 0f) return;
            root.localRotation = Quaternion.Slerp(root.localRotation, target,
                Mathf.Min(1f, degrees * dt / Mathf.Max(.001f, angle)));
        }
        private static void SmoothTravelHeading(Transform root, Vector3 direction, float dt, float degrees = 60f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > .000001f)
                SmoothHeading(root, Quaternion.LookRotation(direction, Vector3.up), dt, degrees);
        }
        private float tripTurnDuration = .75f;
    }
}
