using UnityEngine;

namespace NewGaza
{
    public sealed partial class CityFleet
    {
        private Material excavatorYellow;
        private Material excavatorYellowDark;
        private Material truckCabOffwhite;
        private Material truckLowerGreen;
        private Material truckBedGreen;
        private Material hydraulicLine;
        private Material workLamp;
        private Material signalLamp;

        private void InitializeFleetAppearance(Material construction, Material exposedSteel)
        {
            // The original machinery enamel is shared by the two crawler machines.
            excavatorYellow = paint;
            excavatorYellowDark = Finish(construction, "deep yellow structural enamel",
                new Color(.69f, .43f, .045f), .12f, .25f, 22);
            truckCabOffwhite = Finish(construction, "aged off-white truck cab enamel",
                new Color(.78f, .78f, .68f), .12f, .31f, 31);
            truckLowerGreen = Finish(construction, "deep green cab lower panels",
                new Color(.14f, .30f, .19f), .16f, .3f, 37);
            truckBedGreen = Finish(construction, "green ribbed dump body enamel",
                new Color(.18f, .38f, .22f), .2f, .29f, 41);
            hydraulicLine = Finish(exposedSteel, "rubber hydraulic hose with green tracer",
                new Color(.075f, .105f, .085f), .04f, .22f, 0);
            workLamp = Finish(exposedSteel, "clear work lamp lens",
                new Color(.96f, .83f, .49f), .06f, .74f, 0);
            signalLamp = Finish(exposedSteel, "amber safety lamp lens",
                new Color(.95f, .34f, .045f), .05f, .48f, 0);
        }

        private void AddHose(CityMeshBatch batch, Material material, params Vector3[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 direction = points[i + 1] - points[i];
                if (direction.sqrMagnitude < .0001f) continue;
                batch.Add(geometry.Cylinder, material, (points[i] + points[i + 1]) * .5f,
                    new Vector3(.027f, direction.magnitude, .027f),
                    Quaternion.FromToRotation(Vector3.up, direction));
            }
        }
    }
}