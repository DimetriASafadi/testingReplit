using UnityEngine;

namespace NewGaza
{
    /// <summary>Low-poly, articulated human silhouette emitted into the crew's shared mesh.</summary>
    internal sealed partial class CityConstructionWorkerRig
    {
        private const int VerticesPerBox = 24;
        private const int Skin = 0;
        private const int Vest = 1;
        private const int Cloth = 2;
        private const int Helmet = 3;
        private const int Tool = 4;
        private readonly int workerIndex;
        private const int Masonry = 5;
        private const int Paper = 6;
        private const int Accent = 7;
        private readonly bool agricultural;
        private readonly int[] partCounts = new int[8];

        internal CityConstructionWorkerRig(int workerIndex, bool agricultural = false)
        {
            this.workerIndex = workerIndex;
            this.agricultural = agricultural;
        }

        internal void Write(Vector3[][] target, Vector3[] cubeVertices, int worker,
            Vector3 groundPosition, float yaw, float elapsed, CityConstructionPhase phase, bool walking = false)
        {
            for (int material = 0; material < partCounts.Length; material++)
                partCounts[material] = 0;

            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            float gait = elapsed * 5.2f + workerIndex * 2.1f;
            float leftStride = walking ? Mathf.Sin(gait) : 0;
            float rightStride = walking ? Mathf.Sin(gait + Mathf.PI) : 0;
            float bob = walking ? Mathf.Abs(Mathf.Sin(gait * 2f)) * .018f : 0;

            Vector3 leftAnkle = new Vector3(-.105f, .13f + (walking ? Mathf.Max(0f, Mathf.Cos(gait)) * .065f : 0),
                leftStride * .105f);
            Vector3 rightAnkle = new Vector3(.105f, .13f + (walking ? Mathf.Max(0f, -Mathf.Cos(gait)) * .065f : 0),
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
            GetTaskArms(elapsed, bob, leftShoulder, rightShoulder,
                out leftElbow, out leftWrist, out rightElbow, out rightWrist);

            // Skin: head, neck, and two hands.
            Box(target, cubeVertices, Skin, worker, heading, groundPosition,
                new Vector3(0f, 1.49f + bob, .015f), new Vector3(.205f, .235f, .19f),
                !agricultural && workerIndex == 2
                    ? Quaternion.Euler(12 + Mathf.Sin(elapsed * 1.2f) * 10, Mathf.Sin(elapsed * 1.2f) * 18, 0)
                    : Quaternion.identity);
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
                new Vector3(0f, 1.65f + bob, .015f), new Vector3(.265f, agricultural ? .09f : .105f, .24f),
                !agricultural && workerIndex == 2
                    ? Quaternion.Euler(12 + Mathf.Sin(elapsed * 1.2f) * 10, Mathf.Sin(elapsed * 1.2f) * 18, 0)
                    : Quaternion.identity);
            Box(target, cubeVertices, Helmet, worker, heading, groundPosition,
                new Vector3(0f, 1.607f + bob, .015f),
                new Vector3(agricultural ? .44f : .29f, .018f, agricultural ? .40f : .275f),
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
            WriteTaskTools(target, cubeVertices, worker, heading, groundPosition, elapsed,
                leftWrist, rightWrist);

            // Stable counts keep all human, equipment and task detail buffers allocation-free.
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
                case Helmet: return 2;
                case Masonry: return 4;
                case Paper: return 1;
                case Accent: return 8;
                default: return 5;
            }
        }
    }
}