using UnityEngine;

namespace NewGaza
{
    internal sealed partial class CityConstructionWorkerRig
    {
        private void GetTaskArms(float elapsed, float bob, Vector3 leftShoulder, Vector3 rightShoulder,
            out Vector3 leftElbow, out Vector3 leftWrist, out Vector3 rightElbow, out Vector3 rightWrist)
        {
            float action = Mathf.Sin(elapsed * 3.2f);
            if (agricultural && workerIndex == 0)
            {
                leftWrist = new Vector3(-.24f, .88f + bob, .20f);
                rightWrist = new Vector3(.26f + action * .12f, .92f + action * .10f, .28f + action * .12f);
            }
            else if (agricultural && workerIndex == 1)
            {
                leftWrist = new Vector3(-.08f, .83f, .28f);
                rightWrist = new Vector3(.24f, .85f + action * .05f, .30f);
            }
            else if (agricultural)
            {
                rightWrist = new Vector3(.15f, .94f + action * .08f, .30f);
                Vector3 blade = HoeBlade(elapsed);
                leftWrist = Vector3.Lerp(rightWrist, blade, .26f);
            }
            else if (workerIndex == 0)
            {
                float cycle = Mathf.Repeat(elapsed, 14);
                float lower = cycle < 2 ? 1 - cycle / 2 : cycle >= 7 && cycle < 9 ? (cycle - 7) / 2 : 0;
                bool carrying = cycle < 9;
                leftWrist = new Vector3(-.23f, .94f + bob - lower * .28f, carrying ? .30f : action * .10f);
                rightWrist = new Vector3(.23f, .94f + bob - lower * .28f, carrying ? .30f : -action * .10f);
            }
            else if (workerIndex == 1)
            {
                float lift = HammerLift(elapsed);
                leftWrist = new Vector3(-.12f, .95f, .30f);
                rightWrist = new Vector3(.24f, .70f + lift * .60f, .38f - lift * .12f);
            }
            else
            {
                float pointing = (Mathf.Sin(elapsed * 1.2f) + 1) * .5f;
                leftWrist = new Vector3(-.24f, 1.00f, .23f);
                rightWrist = Vector3.Lerp(new Vector3(-.06f, 1.03f, .37f),
                    new Vector3(.43f, 1.30f, .38f), pointing);
            }
            leftElbow = Vector3.Lerp(leftShoulder, leftWrist, .5f) + new Vector3(-.045f, -.035f, -.03f);
            rightElbow = Vector3.Lerp(rightShoulder, rightWrist, .5f) + new Vector3(.045f, -.035f, -.03f);
        }

        private static float HammerLift(float elapsed) => (1 - Mathf.Cos(elapsed * Mathf.PI * 1.6f)) * .5f;
        private static Vector3 HoeBlade(float elapsed) =>
            new Vector3(.19f, .035f, .56f + Mathf.Sin(elapsed * 3.2f) * .13f);

        private void WriteTaskTools(Vector3[][] target, Vector3[] cubeVertices, int worker,
            Quaternion heading, Vector3 groundPosition, float elapsed, Vector3 leftWrist, Vector3 rightWrist)
        {
            if (agricultural) WriteFarmTools(target, cubeVertices, worker, heading, groundPosition,
                elapsed, leftWrist, rightWrist);
            else if (workerIndex == 0)
            {
                // The load is cradled between both hands, and absent on the empty return leg.
                if (Mathf.Repeat(elapsed, 14) < 8.5f)
                {
                    Vector3 center = (leftWrist + rightWrist) * .5f + new Vector3(0, .075f, .025f);
                    for (int stone = 0; stone < 4; stone++)
                        Box(target, cubeVertices, Masonry, worker, heading, groundPosition,
                            center + new Vector3((stone % 2 - .5f) * .22f, stone / 2 * .15f, .015f),
                            new Vector3(.21f, .14f, .21f), Quaternion.Euler(0, stone * 13, 0));
                    Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                        center - Vector3.up * .10f, new Vector3(.51f, .035f, .26f), Quaternion.identity);
                }
            }
            else if (workerIndex == 1)
            {
                // Hammer head reaches the masonry top on each downstroke, not a walking hand wave.
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    rightWrist + Vector3.up * .075f, new Vector3(.035f, .24f, .035f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    rightWrist + Vector3.up * .19f, new Vector3(.18f, .07f, .08f), Quaternion.identity);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    new Vector3(.24f, .69f, .38f), new Vector3(.48f, .20f, .34f), Quaternion.identity);
                for (int leg = 0; leg < 2; leg++)
                    Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                        new Vector3(.02f + leg * .44f, .295f, .38f),
                        new Vector3(.045f, .59f, .10f), Quaternion.identity);
                Box(target, cubeVertices, Masonry, worker, heading, groundPosition,
                    new Vector3(.24f, .825f, .38f), new Vector3(.28f, .08f, .20f), Quaternion.identity);
            }
            else
            {
                Vector3 sheet = leftWrist + new Vector3(.015f, .025f, .12f);
                Box(target, cubeVertices, Paper, worker, heading, groundPosition,
                    sheet, new Vector3(.47f, .018f, .30f), Quaternion.identity);
                // Visible blue floor-plan lines on the white sheet.
                for (int line = 0; line < 3; line++)
                {
                    Box(target, cubeVertices, Accent, worker, heading, groundPosition,
                        sheet + new Vector3(0, .014f, -.09f + line * .085f),
                        new Vector3(.35f, .008f, .013f), Quaternion.identity);
                    Box(target, cubeVertices, Accent, worker, heading, groundPosition,
                        sheet + new Vector3(-.14f + line * .14f, .014f, 0),
                        new Vector3(.012f, .008f, .22f), Quaternion.identity);
                }
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    sheet - Vector3.up * .025f, new Vector3(.49f, .025f, .32f), Quaternion.identity);
            }
            // Reuse a fixed mesh: no instantiated stones, particles or tools per frame.
            PadMaterial(target, cubeVertices, Tool, worker, heading, groundPosition, 5);
            PadMaterial(target, cubeVertices, Masonry, worker, heading, groundPosition, 4);
            PadMaterial(target, cubeVertices, Paper, worker, heading, groundPosition, 1);
            PadMaterial(target, cubeVertices, Accent, worker, heading, groundPosition, 8);
        }

        private void WriteFarmTools(Vector3[][] target, Vector3[] cubeVertices, int worker,
            Quaternion heading, Vector3 groundPosition, float elapsed, Vector3 leftWrist, Vector3 rightWrist)
        {
            if (workerIndex == 0)
            {
                Box(target, cubeVertices, Masonry, worker, heading, groundPosition,
                    leftWrist - Vector3.up * .08f, new Vector3(.22f, .26f, .18f), Quaternion.identity);
                // Separate grains follow a throw arc from the scattering hand down to the soil.
                for (int seed = 0; seed < 3; seed++)
                {
                    float drop = Mathf.Repeat(elapsed * .8f + seed / 3f, 1);
                    Vector3 grain = Vector3.Lerp(rightWrist, new Vector3(.35f, .025f, .8f), drop) +
                        new Vector3((seed - 1) * .05f, Mathf.Sin(drop * Mathf.PI) * .12f, 0);
                    Box(target, cubeVertices, Masonry, worker, heading, groundPosition,
                        grain, new Vector3(.025f, .025f, .025f), Quaternion.identity);
                }
            }
            else if (workerIndex == 1)
            {
                Vector3 can = rightWrist + new Vector3(.02f, -.085f, .08f);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    can, new Vector3(.23f, .25f, .20f), Quaternion.Euler(15, 0, 0));
                Vector3 spout = can + new Vector3(0, .02f, .30f);
                Segment(target, cubeVertices, Tool, worker, heading, groundPosition,
                    can + Vector3.forward * .08f, spout, .045f, .045f);
                Segment(target, cubeVertices, Tool, worker, heading, groundPosition,
                    can + new Vector3(-.12f, .14f, -.02f),
                    can + new Vector3(.12f, .14f, -.02f), .03f, .03f);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    spout, new Vector3(.10f, .035f, .075f), Quaternion.Euler(15, 0, 0));
                for (int drop = 0; drop < 8; drop++)
                {
                    float travel = Mathf.Repeat(elapsed * 1.2f + drop / 8f, 1);
                    Vector3 point = Vector3.Lerp(spout, new Vector3(.26f, .025f, .94f), travel);
                    Box(target, cubeVertices, Accent, worker, heading, groundPosition,
                        point, new Vector3(.018f, .045f, .018f), Quaternion.Euler(22, 0, 0));
                }
            }
            else
            {
                Vector3 blade = HoeBlade(elapsed);
                Segment(target, cubeVertices, Tool, worker, heading, groundPosition,
                    rightWrist + Vector3.up * .08f, blade, .035f, .035f);
                Box(target, cubeVertices, Tool, worker, heading, groundPosition,
                    blade, new Vector3(.26f, .055f, .11f), Quaternion.identity);
                for (int clod = 0; clod < 4; clod++)
                    Box(target, cubeVertices, Masonry, worker, heading, groundPosition,
                        blade + new Vector3((clod - 1.5f) * .075f,
                            Mathf.Abs(Mathf.Sin(elapsed * 3.2f + clod)) * .045f, .09f),
                        new Vector3(.06f, .035f, .06f), Quaternion.Euler(0, clod * 27, 0));
            }
        }

        private void PadMaterial(Vector3[][] target, Vector3[] cubeVertices, int material,
            int worker, Quaternion heading, Vector3 groundPosition, int count)
        {
            while (partCounts[material] < count)
                Box(target, cubeVertices, material, worker, heading, groundPosition,
                    new Vector3(0, .78f, 0), new Vector3(.002f, .002f, .002f), Quaternion.identity);
        }
    }
}