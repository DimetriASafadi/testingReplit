using System;

namespace NewGaza.Core
{
    /// <summary>Unity-independent audio mixing math shared with deterministic tests.</summary>
    public static class CityAudioMix
    {
        public const float MaximumVoiceGain = 0.72f;
        public const float MaximumAmbienceGain = 0.42f;
        public const float MaximumUiGain = 0.8f;
        public const float MaximumSoundscapeEnergy = 1.25f;
        public const float MaximumSourceDistance = 1600f;
        public const float DefaultMasterVolume = 0.68f;
        public const float DefaultEquipmentVolume = 0.42f;
        public const float DefaultAmbienceVolume = 0.24f;
        public const float DefaultInterfaceVolume = 0.52f;

        public struct MachineMix
        {
            public float Engine;
            public float Hydraulics;
            public float Tracks;
        }

        public struct AmbienceMix
        {
            public float Wind;
            public float CoastalSurf;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static float Clamp01Finite(float value)
        {
            if (!IsFinite(value)) return 0f;
            if (value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }

        public static float ClampGain(float value, float ceiling)
        {
            if (!IsFinite(value) || !IsFinite(ceiling) || ceiling <= 0f) return 0f;
            return value <= 0f ? 0f : value >= ceiling ? ceiling : value;
        }

        public static float SanitizeZoom(float zoom)
        {
            if (!IsFinite(zoom)) return 24f;
            return Clamp(zoom, 0.6f, 2500f);
        }

        public static float SanitizeDistance(float distance)
        {
            if (!IsFinite(distance) || distance < 0f) return MaximumSourceDistance;
            return distance >= MaximumSourceDistance ? MaximumSourceDistance : distance;
        }

        public static float SanitizePreference(float value, float fallback)
        {
            return IsFinite(value) ? Clamp01Finite(value) : Clamp01Finite(fallback);
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (!IsFinite(value)) return minimum;
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        public static float SmoothStep(float edge0, float edge1, float value)
        {
            if (!IsFinite(value) || !IsFinite(edge0) || !IsFinite(edge1)) return 0f;
            if (edge1 <= edge0) return value >= edge1 ? 1f : 0f;
            float t = Clamp01Finite((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A focus-aware ground-plane falloff in city map units (20 m each). The radius
        /// grows gently with orthographic framing and is capped for citywide views.
        /// </summary>
        public static float FocusAttenuation(float horizontalDistance, float zoom)
        {
            float distance = SanitizeDistance(horizontalDistance);
            float safeZoom = SanitizeZoom(zoom);
            // Coordinates are city map units (20 metres each), not camera distance.
            // Keep close-up focus local and cap the widest audible machinery radius.
            float radius = Clamp(1.5f + safeZoom * 0.09f, 1.5f, 24f);
            float normalized = distance / radius;
            float fade = 1f - SmoothStep(0f, 1f, normalized);
            // Square the broad camera envelope so a machine under the focus wins
            // against a busier vehicle just outside a tight close-up.
            return Clamp01Finite(fade * fade);
        }

        public static float EquipmentZoomPresence(float zoom)
        {
            float safeZoom = SanitizeZoom(zoom);
            float ratio = safeZoom / 6f;
            return Clamp01Finite(1f / (1f + ratio * ratio));
        }

        public static float TotalEnergyScale(float totalEnergy)
        {
            if (!IsFinite(totalEnergy)) return 0f;
            if (totalEnergy <= 0f) return 1f;
            return totalEnergy <= MaximumSoundscapeEnergy ?
                1f : MaximumSoundscapeEnergy / totalEnergy;
        }

        /// <summary>
        /// Subdues the combined fleet bed without changing which nearby machine leads.
        /// Call with finite attenuations in [0,1], including inactive machines as zero.
        /// </summary>
        public static float DominanceMultiplier(float ownAttenuation, float otherAttenuationSum)
        {
            float own = Clamp01Finite(ownAttenuation);
            float others = Clamp(otherAttenuationSum, 0f, 64f);
            return Clamp01Finite(1f / (1f + others * 0.42f));
        }

        public static MachineMix EvaluateMachine(bool activeOwnedMachine, int machineIndex,
            float horizontalDistance, float zoom, float load, float movement, float hydraulics,
            float otherMachineAttenuationSum)
        {
            var result = new MachineMix();
            if (!activeOwnedMachine || machineIndex < 0 || machineIndex > 2) return result;

            float focus = FocusAttenuation(horizontalDistance, zoom);
            if (focus <= 0f) return result;

            float loadLevel = Clamp01Finite(load);
            float movementLevel = Clamp01Finite(movement);
            float hydraulicLevel = Clamp01Finite(hydraulics);
            float dominance = DominanceMultiplier(focus, otherMachineAttenuationSum);
            float audible = focus * dominance * EquipmentZoomPresence(zoom);

            // Idle is intentionally a quiet mechanical bed. Work load and movement
            // bring the engine forward without requiring any economy-state changes.
            float engineActivity = 0.085f + 0.39f * loadLevel + 0.22f * movementLevel;
            result.Engine = ClampGain(audible * engineActivity, MaximumVoiceGain);

            if (hydraulicLevel > 0.015f)
            {
                result.Hydraulics = ClampGain(audible * hydraulicLevel * 0.48f, MaximumVoiceGain);
            }
            if (movementLevel > 0.015f && (machineIndex == 0 || machineIndex == 2))
                result.Tracks = ClampGain(audible * movementLevel * 0.29f, MaximumVoiceGain);
            return result;
        }

        public static AmbienceMix EvaluateAmbience(float zoom, float nearestMachineDistance,
            float coastDistance)
        {
            float safeZoom = SanitizeZoom(zoom);
            float wide = SmoothStep(12f, 80f, safeZoom);
            float nearest = FocusAttenuation(nearestMachineDistance, safeZoom);
            float wind = (0.075f + 0.245f * wide) * (1f - 0.2f * nearest);
            // One city unit is 20 m: surf is full near 2 units (40 m) and absent
            // by 20 units (400 m), not audible across the city from kilometres away.
            float coast = 1f - SmoothStep(2f, 20f, SanitizeDistance(coastDistance));
            float surf = coast * (0.13f + 0.11f * wide);
            return new AmbienceMix
            {
                Wind = ClampGain(wind, MaximumAmbienceGain),
                CoastalSurf = ClampGain(surf, MaximumAmbienceGain)
            };
        }

        public static float ChannelGain(float master, float channel, bool muted, float ceiling)
        {
            if (muted) return 0f;
            return ClampGain(Clamp01Finite(master) * Clamp01Finite(channel), ceiling);
        }

        public static float SmoothFrame(float current, float target, float deltaTime,
            float smoothingSeconds)
        {
            float from = Clamp01Finite(current);
            float to = Clamp01Finite(target);
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return from;
            if (!IsFinite(smoothingSeconds) || smoothingSeconds <= 0f) return to;
            float blend = 1f - (float)Math.Exp(-deltaTime / smoothingSeconds);
            return Clamp01Finite(from + (to - from) * blend);
        }

        public static float HorizontalDistance(float ax, float az, float bx, float bz)
        {
            if (!IsFinite(ax) || !IsFinite(az) || !IsFinite(bx) || !IsFinite(bz))
                return MaximumSourceDistance;
            float dx = ax - bx;
            float dz = az - bz;
            double squared = (double)dx * dx + (double)dz * dz;
            if (double.IsNaN(squared) || double.IsInfinity(squared)) return MaximumSourceDistance;
            return SanitizeDistance((float)Math.Sqrt(squared));
        }

        /// <summary>Returns the closest point on a polyline using flat city X/Z units.</summary>
        public static float NearestPolylinePoint(float x, float z, float[] pointX, float[] pointZ,
            int pointCount, out float nearestX, out float nearestZ)
        {
            nearestX = x;
            nearestZ = z;
            if (!IsFinite(x) || !IsFinite(z) || pointX == null || pointZ == null ||
                pointCount < 1 || pointCount > pointX.Length || pointCount > pointZ.Length)
                return MaximumSourceDistance;

            float bestSquared = float.MaxValue;
            int segmentCount = pointCount == 1 ? 1 : pointCount - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = pointCount == 1 ? i : i + 1;
                float ax = pointX[i], az = pointZ[i];
                float bx = pointX[next], bz = pointZ[next];
                if (!IsFinite(ax) || !IsFinite(az) || !IsFinite(bx) || !IsFinite(bz)) continue;
                float dx = bx - ax, dz = bz - az;
                float denominator = dx * dx + dz * dz;
                float t = denominator > 0f ? Clamp01Finite(((x - ax) * dx + (z - az) * dz) /
                    denominator) : 0f;
                float px = ax + dx * t, pz = az + dz * t;
                float ox = x - px, oz = z - pz;
                float squared = ox * ox + oz * oz;
                if (!IsFinite(squared) || squared >= bestSquared) continue;
                bestSquared = squared;
                nearestX = px;
                nearestZ = pz;
            }
            return bestSquared == float.MaxValue ? MaximumSourceDistance :
                SanitizeDistance((float)Math.Sqrt(bestSquared));
        }
    }
}