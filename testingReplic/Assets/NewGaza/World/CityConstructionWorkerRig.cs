using UnityEngine;

namespace NewGaza
{
    /// <summary>Low-poly, articulated human silhouette emitted into the crew's shared mesh.</summary>
    internal sealed class CityConstructionWorkerRig
    {
        private const int VerticesPerBox = 24;
        private const int Skin = 0;
        private const int Vest = 1;
        private const int Cloth = 2;
        private const int Helmet = 3;
        private const int Tool = 4;
        private readonly int workerIndex;
        private readonly int[] partCounts = new int[5];

        internal CityConstructionWorkerRig(int workerIndex)
        {
            this.workerIndex = workerIndex;
        }

        internal void Write(Vector3[][] target, Vector3[] cubeVertices, int worker,
            Vector3 groundPosition, float yaw, float elapsed, CityConstructionPhase phase)
        {
            for (int material = 0; material < partCounts.Length; material++)
                partCounts[material] = 0;

            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            float gait = elapsed * 5.2f + workerIndex * 2.1f;
            float leftStride = Mathf.Sin(gait);
            float rightStride = Mathf.Sin(gait + Mathf.PI);
            float bob = Mathf.Abs(Mathf.Sin(gait * 2f)) * .018f;
            float action = Mathf.Sin(elapsed * 4.4f + workerIndex * 1.7f);
            float actionLift = Mathf.Abs(action) * .045f;

            Vector3 leftAnkle = new Vector3(-.105f, .13f + Mathf.Max(0f, Mathf.Cos(gait)) * .065f,
                leftStride * .105f);
            Vector3 rightAnkle = new Vector3(.105f, .13f + Mathf.Max(0f, -Mathf.Cos(gait)) * .065f,
                rightStride * .105f);
            Vector3 leftKnee = new Vector3(-.12f, .39f + bob * .3f, leftStride * .035f);
            Vector3 rightKnee = new Vector3(.12f, .39f + bob * .3f, rightStride * .035f);
            Vector3 leftHip = new Vector3(-.105f, .69f + bob, 0f);
            Vector3 rightHip = new Vector3(.105f, .69f + bob, 0f);

            Vector3 leftShoulder = new Vector3(-.195f, 1.30f + bob, 0f);
            Vector3 rightShoulder = new Vector3(.195f, 1.30f + bob, 0f);
            Vector3 leftElbow;
            Vector3 rightElbow;
            Vector3 leftWrist;
            Vector3 rightWrist;
            GetArms(phase, action, actionLift, leftShoulder, rightShoulder,
                out leftElbow, out leftWrist, out rightElbow, out rightWrist);

            // Skin: head, neck, and two hands.
            Box(target, cubeVertices, Skin, worker, heading, groundPosition,
                new Vector3(0f, 1.49f + bob, .015f), new Vector3(.205f, .235f, .19f), Quaternion.identity);
            Box(target, cubeVertices, Skin, worker, heading, groundPosition,
                new Vector3(0f, 1.345f + bob, 0f), new Vector3(.11f, .10f, .12f), Quaternion.identity);
            Box(target, cubeVertices, Skin, worker, heading, groundPosition,
                leftWrist, new Vector3(.105f, .105f, .11f), Quaternion.identity);
            Box(target, cubeVertices, Skin, worker, heading, groundPosition,
                rightWrist, new Vector3(.105f, .105f, .11f), Quaternion.identity);

            // High-visibility orange vest, with two recognizable reflective chest bands.
            Box(target, cubeVertices, Vest, worker, heading, groundPosition,
                new Vector3(0f, 1.015f + bob, -.008f), new Vector3(.365f, .47f, .225f),
                Quaternion.identity);
            Box(target, cubeVertices, Vest, worker, heading, groundPosition,
                new Vector3(0f, 1.105f + bob, .108f), new Vector3(.34f, .035f, .012f),
                Quaternion.identity);
            Box(target, cubeVertices, Vest, worker, heading, groundPosition,
                new Vector3(0f, .925f + bob, .108f), new Vector3(.34f, .035f, .012f),
                Quaternion.identity);

            // Blue work shirt, articulated trousers, and grounded boots.
            Box(target, cubeVertices, Cloth, worker, heading, groundPosition,
                new Vector3(0f, 1.015f + bob, -.022f), new Vector3(.245f, .43f, .18f),
                Quaternion.identity);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                leftHip, leftKnee, .15f, .15f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                leftKnee, leftAnkle, .115f, .115f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                rightHip, rightKnee, .15f, .15f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                rightKnee, rightAnkle, .115f, .115f);
            Box(target, cubeVertices, Cloth, worker, heading, groundPosition,
                new Vector3(leftAnkle.x, .055f, leftAnkle.z + .045f), new Vector3(.14f, .11f, .23f),
                Quaternion.identity);
            Box(target, cubeVertices, Cloth, worker, heading, groundPosition,
                new Vector3(rightAnkle.x, .055f, rightAnkle.z + .045f), new Vector3(.14f, .11f, .23f),
                Quaternion.identity);

            // The shell and its brim read as an actual safety hardhat at city scale.
            Box(target, cubeVertices, Helmet, worker, heading, groundPosition,
                new Vector3(0f, 1.65f + bob, .015f), new Vector3(.265f, .105f, .24f),
                Quaternion.identity);

            // Blue upper and lower sleeves stay connected to the shoulders and hands.
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                leftShoulder, leftElbow, .105f, .11f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                leftElbow, leftWrist, .088f, .09f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                rightShoulder, rightElbow, .105f, .11f);
            Segment(target, cubeVertices, Cloth, worker, heading, groundPosition,
                rightElbow, rightWrist, .088f, .09f);
            WriteTool(target, cubeVertices, worker, heading, groundPosition, phase,
                action, leftWrist, rightWrist);

            // Stable counts keep the single mesh's five material submeshes allocation-free.
            // Every tool phase writes five purposeful pieces (unused ones collapse at the hip).
        }

        private void GetArms(CityConstructionPhase phase, float action, float actionLift,
            Vector3 leftShoulder, Vector3 rightShoulder, out Vector3 leftElbow,
            out Vector3 leftWrist, out Vector3 rightElbow, out Vector3 rightWrist)
        {
            leftElbow = leftShoulder + new Vector3(-.055f, -.20f, .035f);
            rightElbow = rightShoulder + new Vector3(.055f, -.20f, .035f);
            leftWrist = leftShoulder + new Vector3(-.09f, -.39f, .075f);
            rightWrist = rightShoulder + new Vector3(.09f, -.39f, .075f);

            if (workerIndex == 0)
            {
                if (phase == CityConstructionPhase.Foundation)
                {
                    rightElbow = new Vector3(.31f, 1.03f - actionLift, .12f);
                    rightWrist = new Vector3(.35f, .77f - actionLift, .21f + action * .035f);
                }
                else if (phase == CityConstructionPhase.Frame)
                {
                    rightElbow = new Vector3(.31f, 1.13f + action * .055f, .10f);
                    rightWrist = new Vector3(.33f, .91f + action * .09f, .17f);
                }
                else if (phase == CityConstructionPhase.Finishing)
                {
                    rightElbow = new Vector3(.30f, 1.10f + action * .03f, .12f);
                    rightWrist = new Vector3(.34f, .88f + action * .05f, .20f);
                }
            }
            else if (workerIndex == 1 && phase == CityConstructionPhase.Foundation)
            {
                leftElbow = new Vector3(-.27f, 1.02f, .08f);
                leftWrist = new Vector3(-.29f, .79f, .13f);
                rightElbow = new Vector3(.27f, 1.11f, .11f);
                rightWrist = new Vector3(.27f, .93f, .17f);
            }
            else if (phase == CityConstructionPhase.Frame)
            {
                leftElbow = new Vector3(-.27f, 1.02f, .10f);
                leftWrist = new Vector3(-.24f, .98f, .20f);
                rightElbow = new Vector3(.27f, 1.02f, .10f);
                rightWrist = new Vector3(.24f, .98f, .20f);
            }
            else if (phase == CityConstructionPhase.Finishing && workerIndex == 1)
            {
                rightElbow = new Vector3(.30f, 1.03f, .08f);
                rightWrist = new Vector3(.32f, .77f, .14f);
                leftElbow = new Vector3(-.26f, 1.04f, .08f);
                leftWrist = new Vector3(-.24f, .88f, .13f);
            }
            else if (phase == CityConstructionPhase.Finishing && workerIndex == 2)
            {
                leftElbow = new Vector3(-.27f, 1.02f, .10f);
                leftWrist = new Vector3(-.24f, .98f, .20f);
                rightElbow = new Vector3(.27f, 1.02f, .10f);
                rightWrist = new Vector3(.24f, .98f, .20f);
            }
        }

        private void WriteTool(Vector3[][] target, Vector3[] cubeVertices, int worker,
            Quaternion heading, Vector3 groundPosition, CityConstructionPhase phase,
            float action, Vector3 leftWrist, Vector3 rightWrist)
        {
            Vector3 collapsed = new Vector3(0f, .78f, 0f);
            Vector3 tiny = new Vector3(.001f, .001f, .001f);
            Vector3 toolCenter;
            Quaternion toolRotation;

            if (workerIndex == 0 && phase == CityConstructionPhase.Foundation)
            {
                // Spade shaft runs from the gripping hand to an actual ground-contact blade.
                Vector3 blade = new Vector3(.39f + action * .035f, .045f, .34f + action * .025f);
                Vector3 grip = rightWrist;
                Vector3 shaft = (grip + blade) * .5f;
                Vector3 delta = blade - grip;
                toolRotation = Quaternion.FromToRotation(Vector3.up, delta);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    shaft, new Vector3(.035f, delta.magnitude, .035f), toolRotation);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blade, new Vector3(.17f, .045f, .13f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    grip + new Vector3(0f, .13f, 0f), new Vector3(.11f, .035f, .035f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (workerIndex == 1 && phase == CityConstructionPhase.Foundation)
            {
                Vector3 basePoint = new Vector3(-.29f, .035f, .16f);
                Vector3 top = new Vector3(-.29f, .84f, .16f);
                Vector3 delta = top - basePoint;
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    (top + basePoint) * .5f, new Vector3(.035f, delta.magnitude, .035f),
                    Quaternion.FromToRotation(Vector3.up, delta));
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(-.29f, .05f, .16f), new Vector3(.13f, .04f, .11f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(-.29f, .70f, .16f), new Vector3(.09f, .025f, .025f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (workerIndex == 2 && phase == CityConstructionPhase.Foundation)
            {
                // A site board is carried in front of the chest, between both hands.
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(0f, 1.08f, .22f), new Vector3(.53f, .075f, .34f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(0f, 1.08f, .045f), new Vector3(.10f, .09f, .035f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(0f, 1.08f, .395f), new Vector3(.10f, .09f, .035f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (workerIndex == 0 && phase == CityConstructionPhase.Frame)
            {
                Vector3 grip = rightWrist;
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    grip + new Vector3(0f, .105f, .015f), new Vector3(.035f, .23f, .035f),
                    Quaternion.Euler(0f, 0f, action * 8f));
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    grip + new Vector3(0f, .21f, .015f), new Vector3(.17f, .065f, .075f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    grip, new Vector3(.04f, .055f, .04f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (phase == CityConstructionPhase.Frame)
            {
                Vector3 blockLeft = new Vector3(-.23f, 1.05f, .22f);
                Vector3 blockRight = new Vector3(.23f, 1.05f, .22f);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blockLeft, new Vector3(.24f, .17f, .18f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blockRight, new Vector3(.24f, .17f, .18f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blockLeft + Vector3.up * .17f, new Vector3(.20f, .14f, .16f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blockRight + Vector3.up * .17f, new Vector3(.20f, .14f, .16f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (workerIndex == 0 && phase == CityConstructionPhase.Finishing)
            {
                toolCenter = rightWrist + new Vector3(.025f, .105f, .04f);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    toolCenter, new Vector3(.035f, .22f, .035f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    toolCenter + new Vector3(.015f, .13f, 0f),
                    new Vector3(.095f, .11f, .08f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    toolCenter + Vector3.up * .08f, new Vector3(.055f, .035f, .06f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (workerIndex == 1 && phase == CityConstructionPhase.Finishing)
            {
                Vector3 hand = rightWrist;
                Vector3 head = new Vector3(.38f + action * .10f, .035f, .45f);
                Vector3 shaftDelta = head - hand;
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    (head + hand) * .5f, new Vector3(.032f, shaftDelta.magnitude, .032f),
                    Quaternion.FromToRotation(Vector3.up, shaftDelta));
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    head, new Vector3(.29f, .055f, .105f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    head + new Vector3(-.10f, .045f, 0f), new Vector3(.025f, .055f, .055f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    head + new Vector3(.10f, .045f, 0f), new Vector3(.025f, .055f, .055f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else if (phase == CityConstructionPhase.Finishing)
            {
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    leftWrist + new Vector3(0f, -.02f, .09f), new Vector3(.16f, .19f, .16f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    rightWrist + new Vector3(0f, -.02f, .09f), new Vector3(.16f, .19f, .16f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    leftWrist + new Vector3(0f, .09f, .09f), new Vector3(.12f, .08f, .12f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    rightWrist + new Vector3(0f, .09f, .09f), new Vector3(.12f, .08f, .12f),
                    Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    collapsed, tiny, Quaternion.identity);
            }
            else
            {
                // Inactive/complete are never updated, but retain deterministic initialized geometry.
                toolCenter = leftWrist;
                toolRotation = Quaternion.identity;
                for (int piece = 0; piece < 5; piece++)
                    Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                        toolCenter, tiny, toolRotation);
            }
        }

        private void Segment(Vector3[][] target, Vector3[] cubeVertices, int material,
            int worker, Quaternion heading, Vector3 groundPosition, Vector3 start, Vector3 end,
            float width, float depth)
        {
            Vector3 delta = end - start;
            Box(target, cubeVertices, material, worker, heading, groundPosition,
                (start + end) * .5f, new Vector3(width, Mathf.Max(.001f, delta.magnitude), depth),
                Quaternion.FromToRotation(Vector3.up, delta));
        }

        private void Box(Vector3[][] target, Vector3[] cubeVertices, int material, int worker,
            Quaternion heading, Vector3 groundPosition, Vector3 center, Vector3 size,
            Quaternion localRotation)
        {
            int part = partCounts[material]++;
            int index = (worker * CrewBoxesPerWorker(material) + part) * VerticesPerBox;
            int offset = index;
            Quaternion rotation = heading * localRotation;
            Vector3 worldCenter = groundPosition + heading * center;
            for (int vertex = 0; vertex < cubeVertices.Length; vertex++)
            {
                Vector3 scaled = Vector3.Scale(cubeVertices[vertex], size);
                target[material][offset + vertex] = worldCenter + rotation * scaled;
            }
        }

        private static int CrewBoxesPerWorker(int material)
        {
            switch (material)
            {
                case Skin: return 4;
                case Vest: return 3;
                case Cloth: return 11;
                case Helmet: return 1;
                default: return 5;
            }
        }
    }
}