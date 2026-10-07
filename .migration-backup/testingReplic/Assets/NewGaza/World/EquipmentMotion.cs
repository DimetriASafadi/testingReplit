using UnityEngine;

namespace NewGaza
{
    /// <summary>Deterministic, eased equipment articulation independent of job timers.</summary>
    internal static class EquipmentMotion
    {
        internal const float DigCycleSeconds = 14f;
        // Pre-initialize values for static motion checks; CityFleet.Initialize replaces
        // them with heights measured from the generated steel-shoe and wheel meshes.
        internal static float WorkRootHeightModel { get; private set; } = .006f / .07f + .0375f;
        internal static float TruckWorkRootHeightModel { get; private set; } = .006f / .07f + .0268f;
        private static readonly float[] DigKeys = { 0f, .20f, .31f, .48f, .64f, .72f, .80f, 1f };
        private static Vector3[] DigTargets = BuildDigTargets();
        private static readonly float[] DigBucketAngles = { -20f, 8f, -48f, -25f, 8f, 120f, -25f, -20f };
        private static readonly float[] BoomPreferences =
            { -1.96f, 44.18f, 56.16f, 1.84f, 7.64f, 13.15f, 9.48f, -1.96f };
        private static readonly float[] StickPreferences =
            { 19.49f, -34.32f, -11.66f, 22.80f, -35.52f, -75.65f, -43.02f, 19.49f };
        private static readonly float[] DigLoads = { 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0f };
        private static readonly float[] DigEfforts = { .12f, .35f, .72f, .78f, .58f, .42f, .34f, .12f };

        private static Vector3[] BuildDigTargets()
        {
            // Targets are authored in the site-grade frame. Dig converts them to the
            // excavator-root frame after the live crawler and wheel bounds are measured.
            // Keep the lip 0.12 m above the actual truck wall crest at bucket release.
            float dumpLipHeight = TruckWorkRootHeightModel + .55f + .635f + .12f;
            return new[]
            {
                new Vector3(0f,WorkRootHeightModel + .92f,1.62f),
                new Vector3(0f,.025f,2.40f),
                new Vector3(0f,.025f,1.84f),
                new Vector3(0f,WorkRootHeightModel + .86f,1.55f),
                new Vector3(-2.5f,dumpLipHeight,-1.05f),
                new Vector3(-2.5f,dumpLipHeight,-1.05f),
                new Vector3(-2.5f,WorkRootHeightModel + 1.70f,-1.05f),
                new Vector3(0f,WorkRootHeightModel + .92f,1.62f)
            };
        }

        internal static void ConfigureGroundedRootHeights(float workRootHeightModel,
            float truckWorkRootHeightModel)
        {
            WorkRootHeightModel = workRootHeightModel;
            TruckWorkRootHeightModel = truckWorkRootHeightModel;
            DigTargets = BuildDigTargets();
        }

        internal struct ExcavationPose
        {
            internal float turretYaw;
            internal float boomAngle;
            internal float stickAngle;
            internal float bucketAngle;
            internal float bucketLoad;
            internal float hydraulicEffort;
        }

        internal static float Ease(float from, float to, float value)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
            return Mathf.Lerp(from, to, t);
        }

        internal static ExcavationPose Dig(float seconds)
        {
            float t = Mathf.Repeat(seconds, DigCycleSeconds) / DigCycleSeconds;
            int segment = 0;
            while (segment < DigKeys.Length - 2 && t > DigKeys[segment + 1]) segment++;
            float amount = Mathf.InverseLerp(DigKeys[segment], DigKeys[segment + 1], t);
            Vector3 target = Vector3.Lerp(DigTargets[segment], DigTargets[segment + 1],
                Mathf.SmoothStep(0f, 1f, amount));
            target.y -= WorkRootHeightModel;
            float bucketAngle = Ease(DigBucketAngles[segment], DigBucketAngles[segment + 1], amount);
            float boomPreference = Mathf.Lerp(BoomPreferences[segment], BoomPreferences[segment + 1],
                Mathf.SmoothStep(0f, 1f, amount));
            float stickPreference = Mathf.Lerp(StickPreferences[segment], StickPreferences[segment + 1],
                Mathf.SmoothStep(0f, 1f, amount));
            SolveToothTarget(target, bucketAngle, boomPreference, stickPreference,
                out float yaw, out float boom, out float stick);
            return new ExcavationPose
            {
                turretYaw = yaw,
                boomAngle = boom,
                stickAngle = stick,
                bucketAngle = bucketAngle,
                bucketLoad = Ease(DigLoads[segment], DigLoads[segment + 1], amount),
                hydraulicEffort = Ease(DigEfforts[segment], DigEfforts[segment + 1], amount)
            };
        }

        internal static Vector3 BucketToothTip(ExcavationPose pose)
        {
            // The sharp apex in EquipmentGeometry.WedgeTooth, after its BuildExcavator
            // position (-.16,.40) and scale (.13,.24) are applied.
            Quaternion yaw = Quaternion.Euler(0f, pose.turretYaw, 0f);
            Quaternion boom = Quaternion.Euler(pose.boomAngle, 0f, 0f);
            Quaternion stick = Quaternion.Euler(pose.stickAngle, 0f, 0f);
            Quaternion bucket = Quaternion.Euler(pose.bucketAngle, 0f, 0f);
            Vector3 tooth = new Vector3(0f, -.1964f, .52f);
            Vector3 dipper = new Vector3(0f,-1.15f,.35f) + bucket * tooth;
            Vector3 arm = new Vector3(0f,1.28f,.78f) + stick * dipper;
            return yaw * (new Vector3(.17f,.99f,.36f) + boom * arm);
        }

        internal static Vector3 BucketToothTipAtWorkModel(ExcavationPose pose)
        {
            return BucketToothTip(pose) + Vector3.up * WorkRootHeightModel;
        }

        private static void SolveToothTarget(Vector3 target, float bucketAngle,
            float boomPreference, float stickPreference, out float yaw,
            out float boomAngle, out float stickAngle)
        {
            const float boomLateralOffset = .17f;
            float horizontalRadius = Mathf.Sqrt(target.x * target.x + target.z * target.z);
            float yawOffset = Mathf.Asin(Mathf.Clamp(boomLateralOffset /
                Mathf.Max(.001f, horizontalRadius), -1f, 1f)) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg - yawOffset;
            float radial = Mathf.Sqrt(Mathf.Max(.001f,
                horizontalRadius * horizontalRadius - boomLateralOffset * boomLateralOffset));
            Vector2 requested = new Vector2(target.y - .99f, radial - .36f);
            const int orientationSamples = 72;
            const float firstLength = 1.498933f; // BuildExcavator pivot spacing: (1.28, .78)
            const float secondLength = 1.202081f; // BuildExcavator pivot spacing: (-1.15, .35)
            float firstAngle = Mathf.Atan2(.78f, 1.28f) * Mathf.Rad2Deg;
            float secondAngle = Mathf.Atan2(.35f, -1.15f) * Mathf.Rad2Deg;
            bool foundSolution = false;
            float bestCost = float.MaxValue;
            float bestBoom = boomPreference;
            float bestStick = stickPreference;

            // Fix the bucket's absolute pitch temporarily, solve the boom/dipper two-link
            // geometry analytically, then find the closed pitch where their joints agree.
            // This avoids the divergent fixed-point iteration near folded/straight reaches.
            for (int branch = -1; branch <= 1; branch += 2)
            {
                float previousOrientation = -180f;
                bool previousValid = TryAnglesAtToothPitch(requested, bucketAngle,
                    previousOrientation, branch, firstLength, secondLength, firstAngle,
                    secondAngle, out _, out _, out float previousResidual);

                for (int sample = 1; sample <= orientationSamples; sample++)
                {
                    float orientation = -180f + sample * (360f / orientationSamples);
                    bool valid = TryAnglesAtToothPitch(requested, bucketAngle,
                        orientation, branch, firstLength, secondLength, firstAngle,
                        secondAngle, out float currentBoom, out float currentStick,
                        out float currentResidual);
                    if (previousValid && valid &&
                        Mathf.Abs(previousResidual - currentResidual) < 180f &&
                        (Mathf.Abs(previousResidual) < .001f ||
                         Mathf.Abs(currentResidual) < .001f ||
                         previousResidual * currentResidual < 0f))
                    {
                        float low = previousOrientation;
                        float high = orientation;
                        float lowResidual = previousResidual;
                        bool bracketValid = true;
                        for (int refine = 0; refine < 18; refine++)
                        {
                            float middle = (low + high) * .5f;
                            if (!TryAnglesAtToothPitch(requested, bucketAngle, middle,
                                branch, firstLength, secondLength, firstAngle, secondAngle,
                                out float middleBoom, out float middleStick, out float residual))
                            {
                                bracketValid = false;
                                break;
                            }
                            currentBoom = middleBoom;
                            currentStick = middleStick;
                            currentResidual = residual;
                            if (Mathf.Abs(residual) < .0001f) break;
                            if (lowResidual * residual <= 0f)
                                high = middle;
                            else
                            {
                                low = middle;
                                lowResidual = residual;
                            }
                        }
                        if (bracketValid)
                        {
                            float cost = PosePreferenceCost(currentBoom, currentStick,
                                boomPreference, stickPreference);
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestBoom = currentBoom;
                                bestStick = currentStick;
                                foundSolution = true;
                            }
                        }
                    }
                    previousOrientation = orientation;
                    previousValid = valid;
                    previousResidual = currentResidual;
                }
            }

            boomAngle = foundSolution ? bestBoom : boomPreference;
            stickAngle = foundSolution ? bestStick : stickPreference;
        }

        private static bool TryAnglesAtToothPitch(Vector2 requested, float bucketAngle,
            float toothPitch, int branch, float firstLength, float secondLength,
            float firstAngle, float secondAngle, out float boom, out float stick,
            out float closureError)
        {
            Vector2 tooth = new Vector2(-.1964f, .52f);
            Vector2 endpoint = requested - RotatePlane(tooth, toothPitch);
            float distance = endpoint.magnitude;
            float minimum = Mathf.Abs(firstLength - secondLength) + .00001f;
            float maximum = firstLength + secondLength - .00001f;
            if (distance < minimum || distance > maximum)
            {
                boom = stick = closureError = 0f;
                return false;
            }

            Vector2 direction = endpoint / distance;
            float along = (firstLength * firstLength - secondLength * secondLength +
                distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, firstLength * firstLength - along * along));
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 elbow = direction * along + perpendicular * (height * branch);
            AnglesForElbow(elbow, endpoint, firstAngle, secondAngle, out boom, out stick);
            closureError = Mathf.DeltaAngle(toothPitch, boom + stick + bucketAngle);
            return true;
        }

        private static void AnglesForElbow(Vector2 elbow, Vector2 endpoint,
            float firstAngle, float secondAngle, out float boom, out float stick)
        {
            Vector2 first = elbow;
            Vector2 second = endpoint - elbow;
            float firstDirection = Mathf.Atan2(first.y, first.x) * Mathf.Rad2Deg;
            float secondDirection = Mathf.Atan2(second.y, second.x) * Mathf.Rad2Deg;
            boom = Mathf.DeltaAngle(0f, firstDirection - firstAngle);
            stick = Mathf.DeltaAngle(0f, secondDirection - secondAngle - boom);
        }

        private static float PosePreferenceCost(float boom, float stick,
            float boomPreference, float stickPreference)
        {
            float boomDelta = Mathf.DeltaAngle(boomPreference, boom);
            float stickDelta = Mathf.DeltaAngle(stickPreference, stick);
            return Mathf.Abs(boomDelta) + Mathf.Abs(stickDelta) * .72f +
                Mathf.Max(0f, boom - 105f) * 3f +
                Mathf.Max(0f, -stick - 125f) * 1.5f;
        }

        private static Vector2 RotatePlane(Vector2 value, float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        internal static float PushBlade(float seconds)
        {
            float cycle = Mathf.Repeat(seconds, 11f) / 11f;
            if (cycle < .58f) return Ease(-2f, 1f, cycle / .58f);
            return Ease(1f, -7f, (cycle - .58f) / .42f);
        }

        internal static float Unload(float seconds)
        {
            if (seconds < 2.4f) return Mathf.SmoothStep(0f, 1f, seconds / 2.4f);
            if (seconds < 4.7f) return 1f;
            if (seconds < 7.2f) return 1f - Mathf.SmoothStep(0f, 1f, (seconds - 4.7f) / 2.5f);
            return 0f;
        }

        internal static float DumpedAmount(float seconds)
        {
            if (seconds < 2.4f) return 0f;
            if (seconds < 4.7f) return Mathf.SmoothStep(0f, 1f, (seconds - 2.4f) / 2.3f);
            return 1f;
        }

        internal static float MaximumDigEnvelope(Vector3 rootWorkOffsetModel)
        {
            // Sweep the exact articulated pivot chain and tooth-tip corners through the
            // eased cycle. The returned radius includes the authored root work offset.
            float maximum = 0f;
            const int samples = 64;
            for (int i = 0; i <= samples; i++)
            {
                ExcavationPose pose = Dig(DigCycleSeconds * i / samples);
                Vector3 tip = BucketToothTip(pose);
                float horizontal = new Vector2(rootWorkOffsetModel.x + tip.x,
                    rootWorkOffsetModel.z + tip.z).magnitude;
                maximum = Mathf.Max(maximum, horizontal);
                Quaternion yaw = Quaternion.Euler(0f, pose.turretYaw, 0f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 corner = yaw * (new Vector3(.17f,.99f,.36f) +
                        Quaternion.Euler(pose.boomAngle, 0f, 0f) *
                        (Quaternion.Euler(pose.stickAngle, 0f, 0f) *
                            (Quaternion.Euler(pose.bucketAngle, 0f, 0f) *
                                new Vector3(side * .32f,-.1964f,.52f)) +
                         Quaternion.Euler(pose.stickAngle, 0f, 0f) * new Vector3(0f,-1.15f,.35f) +
                         new Vector3(0f,1.28f,.78f)));
                    horizontal = new Vector2(rootWorkOffsetModel.x + corner.x,
                        rootWorkOffsetModel.z + corner.z).magnitude;
                    maximum = Mathf.Max(maximum, horizontal);
                }
            }
            return maximum;
        }

        internal static float MaximumFleetWorkEnvelope(Vector3 rootWorkOffsetModel)
        {
            float maximum = MaximumDigEnvelope(rootWorkOffsetModel);
            // Bounds of the parked 4.13 m tipper (including mirrors and the hinged box),
            // placed beside the digger while the bucket transfers material.
            float truckX = rootWorkOffsetModel.x - 2.5f;
            float truckZ = rootWorkOffsetModel.z + .35f;
            for (int side = -1; side <= 1; side += 2)
                foreach (float z in new[] { -2.55f, 1.60f })
                {
                    float radius = new Vector2(truckX + side * .72f, truckZ + z).magnitude;
                    maximum = Mathf.Max(maximum, radius);
                }

            // Dozer chassis and its concave blade move only through the authored short push stroke.
            float dozerX = rootWorkOffsetModel.x + 1.55f;
            float dozerZ = rootWorkOffsetModel.z - .48f;
            for (int side = -1; side <= 1; side += 2)
                foreach (float z in new[] { -1.2f, 1.38f })
                    foreach (float push in new[] { -.22f, .22f })
                    {
                        float radius = new Vector2(dozerX + side * (z > 0f ? 1.02f : 1.15f),
                            dozerZ + push + z).magnitude;
                        maximum = Mathf.Max(maximum, radius);
                    }
            return maximum;
        }

        internal static int VisibleCargoPieces(float amount, int available)
        {
            if (available <= 0) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(amount) * available), 0, available);
        }
    }
}