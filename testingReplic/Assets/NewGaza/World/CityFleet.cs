using NewGaza.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza
{
    /// <summary>Transform-only animation. No rebuilding, physics simulation or saved state.</summary>
    public sealed class CityFleet : MonoBehaviour
    {
        private CityGeometry geometry;
        private Material yellow, glass, dark, iron, teal, rubble;
        private Transform excavator, turret, boom, stick, bucket, bulldozer, blade, truck, truckBed, cargo;
        private Transform[] wheels;
        private JobStage stage;
        private Vector3 jobCenter;
        private Vector3 depot;
        private int districtIndex;
        private float phase;
        private int previousDistrict = -1;
        private JobStage previousStage = JobStage.Idle;
        private bool excavatorOwned, truckOwned, bulldozerOwned;
        private bool importedJob;
        private Vector3[] route = new Vector3[8];
        private float[] routeLengths = new float[8];
        private int routeCount;
        private float routeLength;

        internal void Initialize(CityGeometry source, Material construction, Material windows, Material rubber,
            Material steel, Material paint, Material stone)
        {
            geometry = source;
            yellow = construction;
            glass = windows;
            dark = rubber;
            iron = steel;
            teal = paint;
            rubble = stone;
            BuildExcavator();
            BuildTruck();
            BuildBulldozer();
            excavator.gameObject.SetActive(false);
            truck.gameObject.SetActive(false);
            bulldozer.gameObject.SetActive(false);
        }

        internal void Refresh(GameState state, Vector3 worldJobCenter, Vector3 worldDepot)
        {
            stage = state.jobStage;
            districtIndex = state.jobDistrict;
            jobCenter = transform.InverseTransformPoint(worldJobCenter);
            // Caller gives city-local depot coordinates; the world normally has identity TRS.
            depot = worldDepot;
            excavatorOwned = state.excavators > 0;
            truckOwned = state.trucks > 0;
            bulldozerOwned = state.bulldozers > 0;
            importedJob = districtIndex >= 0 && districtIndex < state.districts.Length &&
                state.districts[districtIndex].clearedLoads >= GameCatalog.Districts[districtIndex].rubbleLoads;
            excavator.gameObject.SetActive(excavatorOwned);
            truck.gameObject.SetActive(truckOwned);
            bulldozer.gameObject.SetActive(bulldozerOwned);
            if (previousDistrict != districtIndex || previousStage != stage)
            {
                previousDistrict = districtIndex;
                previousStage = stage;
                phase = 0f;
                CreateRoute();
            }
            if (stage == JobStage.Idle)
            {
                excavator.localPosition = depot + new Vector3(0f,.18f,5.8f);
                excavator.localRotation = Quaternion.Euler(0f,180f,0f);
                bulldozer.localPosition = depot + new Vector3(0f,.18f,-5.1f);
                bulldozer.localRotation = Quaternion.Euler(0f,180f,0f);
                truck.localPosition = depot + new Vector3(-1.5f,.18f,0f);
                truck.localRotation = Quaternion.identity;
                truckBed.localRotation = Quaternion.identity;
                cargo.gameObject.SetActive(false);
            }
        }

        private Transform Root(string name, Transform parent)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            return root;
        }

        private Transform Part(string name, Transform parent, Mesh mesh, Material material,
            Vector3 position, Vector3 scale, Quaternion rotation, bool shadows = false)
        {
            Transform root = Root(name, parent);
            root.localPosition = position;
            root.localRotation = rotation;
            root.localScale = scale;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return root;
        }

        private void Cube(string name, Transform parent, Material material, Vector3 position, Vector3 scale, bool shadows = false)
        {
            Part(name,parent,geometry.Box,material,position,scale,Quaternion.identity,shadows);
        }

        private void Tracks(CityMeshBatch batch, float width, float length)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                batch.Box(dark,new Vector3(side * width,.23f,0f),new Vector3(.34f,.42f,length));
                for (int shoe = 0; shoe < 7; shoe++)
                {
                    float z = -length * .44f + shoe * length * .145f;
                    batch.Box(iron,new Vector3(side * width,.04f,z),new Vector3(.39f,.09f,length * .105f));
                    batch.Box(iron,new Vector3(side * width,.46f,z),new Vector3(.39f,.06f,length * .105f));
                }
                for (int wheel = 0; wheel < 4; wheel++)
                    batch.Add(geometry.Cylinder,iron,new Vector3(side * (width + .175f),.25f,-length * .32f + wheel * length * .21f),
                        new Vector3(.27f,.045f,.27f),Quaternion.Euler(0f,0f,90f));
            }
        }

        private void BuildExcavator()
        {
            excavator = Root("Excavator • tracks / slewing cab / hydraulic arm",transform);
            var tracks = new CityMeshBatch(geometry);
            Tracks(tracks,.55f,1.95f);
            tracks.Box(yellow,new Vector3(0f,.48f,0f),new Vector3(.95f,.16f,1.1f));
            tracks.Round(dark,new Vector3(0f,.59f,0f),new Vector3(.7f,.12f,.7f));
            tracks.Build("Undercarriage",excavator,Vector3.zero);
            turret = Root("Slewing platform",excavator);
            turret.localPosition = new Vector3(0f,.66f,0f);
            var upper = new CityMeshBatch(geometry);
            upper.Box(yellow,new Vector3(0f,.12f,-.1f),new Vector3(1.2f,.35f,1.35f));
            upper.Box(yellow,new Vector3(0f,.38f,-.6f),new Vector3(1.2f,.32f,.35f));
            upper.Box(dark,new Vector3(.27f,.49f,-.47f),new Vector3(.4f,.05f,.2f));
            upper.Box(yellow,new Vector3(-.33f,.59f,.06f),new Vector3(.51f,.76f,.75f));
            upper.Box(glass,new Vector3(-.33f,.68f,.455f),new Vector3(.42f,.46f,.04f));
            upper.Box(glass,new Vector3(-.6f,.68f,.06f),new Vector3(.03f,.45f,.55f));
            upper.Box(yellow,new Vector3(-.33f,1.01f,.06f),new Vector3(.61f,.1f,.84f));
            upper.Round(yellow,new Vector3(-.2f,1.15f,-.15f),new Vector3(.12f,.19f,.12f));
            upper.Build("Cab / windows / counterweight",turret,Vector3.zero,true);
            boom = Root("Hydraulic boom pivot",turret);
            boom.localPosition = new Vector3(.28f,.28f,.3f);
            Cube("Main boom",boom,yellow,new Vector3(0f,.7f,.49f),new Vector3(.23f,1.55f,.27f),true);
            boom.GetChild(0).localRotation = Quaternion.Euler(35f,0f,0f);
            Part("Lift hydraulic ram",boom,geometry.Cylinder,iron,new Vector3(.17f,.55f,.34f),
                new Vector3(.09f,1.15f,.09f),Quaternion.Euler(32f,0f,0f));
            stick = Root("Dipper pivot",boom);
            stick.localPosition = new Vector3(0f,1.31f,.94f);
            Cube("Dipper arm",stick,yellow,new Vector3(0f,-.62f,.29f),new Vector3(.2f,1.34f,.23f),true);
            stick.GetChild(0).localRotation = Quaternion.Euler(-25f,0f,0f);
            Part("Bucket ram",stick,geometry.Cylinder,iron,new Vector3(.16f,-.5f,.17f),
                new Vector3(.075f,.95f,.075f),Quaternion.Euler(-23f,0f,0f));
            bucket = Root("Bucket curl pivot",stick);
            bucket.localPosition = new Vector3(0f,-1.2f,.55f);
            var scoop = new CityMeshBatch(geometry);
            scoop.Box(dark,new Vector3(0f,-.1f,.08f),new Vector3(.56f,.12f,.62f));
            scoop.Box(yellow,new Vector3(0f,.1f,-.2f),new Vector3(.56f,.42f,.12f));
            for (int side = -1; side <= 1; side += 2)
                scoop.Box(yellow,new Vector3(side * .25f,.04f,.05f),new Vector3(.07f,.33f,.54f));
            for (int tooth = 0; tooth < 4; tooth++)
                scoop.Box(iron,new Vector3(-.2f + tooth * .13f,-.14f,.43f),new Vector3(.07f,.09f,.2f));
            scoop.Build("Open scoop / four steel teeth",bucket,Vector3.zero);
        }

        private void BuildBulldozer()
        {
            bulldozer = Root("Bulldozer • continuous tracks / engine / wide blade",transform);
            var batch = new CityMeshBatch(geometry);
            Tracks(batch,.56f,1.95f);
            batch.Box(yellow,new Vector3(0f,.65f,.25f),new Vector3(.9f,.54f,1.15f));
            batch.Box(yellow,new Vector3(0f,.98f,-.46f),new Vector3(.87f,.69f,.73f));
            batch.Box(glass,new Vector3(0f,1.07f,-.075f),new Vector3(.7f,.42f,.05f));
            batch.Box(glass,new Vector3(-.45f,1.07f,-.46f),new Vector3(.04f,.43f,.6f));
            batch.Box(glass,new Vector3(.45f,1.07f,-.46f),new Vector3(.04f,.43f,.6f));
            batch.Box(yellow,new Vector3(0f,1.37f,-.46f),new Vector3(.97f,.1f,.83f));
            batch.Round(dark,new Vector3(.3f,1.13f,.6f),new Vector3(.09f,.66f,.09f));
            for (int i = 0; i < 4; i++)
                batch.Box(dark,new Vector3(-.25f + i * .17f,.66f,.84f),new Vector3(.08f,.33f,.05f));
            for (int side = -1; side <= 1; side += 2)
                batch.Beam(iron,new Vector3(side * .53f,.37f,-.1f),new Vector3(side * .63f,.4f,1.25f),.11f);
            batch.Build("Dozer chassis / glazed cab / engine grille",bulldozer,Vector3.zero,true);
            blade = Root("Blade lift",bulldozer);
            blade.localPosition = new Vector3(0f,.45f,1.17f);
            var front = new CityMeshBatch(geometry);
            front.Box(yellow,Vector3.zero,new Vector3(1.95f,.64f,.15f));
            front.Box(iron,new Vector3(0f,-.32f,.11f),new Vector3(2.05f,.12f,.25f));
            front.Box(yellow,new Vector3(-.96f,0f,.13f),new Vector3(.12f,.64f,.37f),-15f);
            front.Box(yellow,new Vector3(.96f,0f,.13f),new Vector3(.12f,.64f,.37f),15f);
            front.Build("Steel-edged curved blade",blade,Vector3.zero);
        }

        private void BuildTruck()
        {
            truck = Root("Tipper truck • six wheels / cab / lifting bed",transform);
            var body = new CityMeshBatch(geometry);
            body.Box(dark,new Vector3(0f,.4f,0f),new Vector3(.95f,.17f,2.95f));
            body.Box(teal,new Vector3(0f,.86f,1.04f),new Vector3(1.13f,.95f,.86f));
            body.Box(glass,new Vector3(0f,1.05f,1.495f),new Vector3(.94f,.48f,.055f));
            for (int side = -1; side <= 1; side += 2)
            {
                body.Box(glass,new Vector3(side * .582f,1.05f,1.08f),new Vector3(.045f,.45f,.65f));
                body.Box(iron,new Vector3(side * .65f,1.1f,1.35f),new Vector3(.18f,.2f,.07f));
                body.Box(yellow,new Vector3(side * .36f,.7f,1.51f),new Vector3(.18f,.14f,.04f));
            }
            body.Box(teal,new Vector3(0f,1.38f,1.04f),new Vector3(1.2f,.12f,.95f));
            body.Box(iron,new Vector3(0f,.4f,1.52f),new Vector3(1.2f,.13f,.12f));
            body.Box(dark,new Vector3(0f,.68f,1.51f),new Vector3(.36f,.25f,.055f));
            body.Build("Cab / mirrors / bumper / headlamps",truck,Vector3.zero,true);
            wheels = new Transform[6];
            for (int axle = 0; axle < 3; axle++)
                for (int side = 0; side < 2; side++)
                {
                    int index = axle * 2 + side;
                    wheels[index] = Root("Rolling wheel " + index,truck);
                    wheels[index].localPosition = new Vector3(side == 0 ? -.61f : .61f,.32f,
                        axle == 0 ? 1.02f : axle == 1 ? -.45f : -1.1f);
                    Part("Tyre",wheels[index],geometry.Cylinder,dark,Vector3.zero,new Vector3(.57f,.21f,.57f),
                        Quaternion.Euler(0f,0f,90f));
                    Part("Wheel hub",wheels[index],geometry.Cylinder,iron,
                        new Vector3(side == 0 ? -.115f : .115f,0f,0f),new Vector3(.26f,.03f,.26f),
                        Quaternion.Euler(0f,0f,90f));
                }
            truckBed = Root("Rear tip hinge",truck);
            truckBed.localPosition = new Vector3(0f,.54f,-1.35f);
            var bed = new CityMeshBatch(geometry);
            bed.Box(yellow,new Vector3(0f,.04f,.94f),new Vector3(1.25f,.13f,1.94f));
            bed.Box(yellow,new Vector3(0f,.37f,0f),new Vector3(1.25f,.65f,.1f));
            bed.Box(yellow,new Vector3(0f,.37f,1.89f),new Vector3(1.25f,.65f,.1f));
            for (int side = -1; side <= 1; side += 2)
            {
                bed.Box(yellow,new Vector3(side * .6f,.37f,.94f),new Vector3(.12f,.65f,1.94f));
                for (int i = 0; i < 4; i++)
                    bed.Box(iron,new Vector3(side * .67f,.37f,.2f + i * .5f),
                        new Vector3(.035f,.62f,.06f));
            }
            bed.Build("Open reinforced tipper bed",truckBed,Vector3.zero);
            var load = new CityMeshBatch(geometry);
            for (int i = 0; i < 9; i++)
                load.Add(geometry.Box,rubble,new Vector3((i % 3 - 1) * .3f,.39f + i % 2 * .17f,.3f + i / 3 * .58f),
                    new Vector3(.42f,.29f,.43f),Quaternion.Euler(i * 9f,i * 43f,i * 5f));
            cargo = load.Build("Visible rubble load",truckBed,Vector3.zero).transform;
        }

        private void CreateRoute()
        {
            if (importedJob)
            {
                route[0] = depot + new Vector3(-5f,.22f,-4.5f);
                route[1] = depot + new Vector3(-5f,.22f,4.5f);
                route[2] = route[0];
                routeCount = 3;
                routeLength = 18f;
                routeLengths[0] = routeLengths[1] = 9f;
                return;
            }
            // Rectilinear route uses the shared city streets. Imported contracts remain
            // at the depot; the coastline feeds its dedicated corniche road.
            float nearestZ = -47.5f + Mathf.Clamp(Mathf.RoundToInt((jobCenter.z + 47.5f) / 19f),0,5) * 19f;
            float accessX = districtIndex == 10 ? -30.1f : jobCenter.x < 0f ? 0f : 30.1f;
            route[0] = new Vector3(30.1f,.22f,-35f);
            route[1] = new Vector3(30.1f,.22f,nearestZ);
            route[2] = new Vector3(accessX,.22f,nearestZ);
            route[3] = new Vector3(accessX,.22f,jobCenter.z);
            route[4] = route[2];
            route[5] = route[1];
            route[6] = route[0];
            routeCount = 7;
            routeLength = 0f;
            for (int i = 0; i < routeCount - 1; i++)
            {
                routeLengths[i] = Vector3.Distance(route[i],route[i + 1]);
                routeLength += routeLengths[i];
            }
        }

        private void Update()
        {
            if (stage == JobStage.Idle || geometry == null) return;
            phase += Time.deltaTime;
            bool clearing = stage == JobStage.Clearing;
            Vector3 work = importedJob ? depot + new Vector3(.3f,.2f,5.6f) :
                jobCenter + new Vector3(districtIndex == 10 ? 1.1f : -9.7f,.2f,
                districtIndex == 10 ? -40f : -5.9f);
            if (excavatorOwned)
            {
                excavator.localPosition = clearing ? work :
                    depot + new Vector3(.3f,.2f,5.6f);
                excavator.localRotation = Quaternion.Euler(0f,clearing ? 70f : 170f,0f);
                turret.localRotation = Quaternion.Euler(0f,clearing ? Mathf.Sin(phase * .42f) * 32f : -25f,0f);
                boom.localRotation = Quaternion.Euler(clearing ? -12f + Mathf.Sin(phase * 1.05f) * 13f : -9f,0f,0f);
                stick.localRotation = Quaternion.Euler(clearing ? 16f + Mathf.Sin(phase * 1.05f + .8f) * 24f : 28f,0f,0f);
                bucket.localRotation = Quaternion.Euler(clearing ? Mathf.Sin(phase * 1.05f + 1.5f) * 28f : 0f,0f,0f);
            }
            if (bulldozerOwned)
            {
                bulldozer.localPosition = clearing ? work + new Vector3(3.1f,0f,Mathf.Sin(phase * .45f) * 1.15f) :
                    depot + new Vector3(0f,.2f,-5.1f);
                bulldozer.localRotation = Quaternion.Euler(0f,clearing ? 0f : 180f,0f);
                blade.localRotation = Quaternion.Euler(clearing ? Mathf.Sin(phase * .9f) * 4f : -8f,0f,0f);
            }
            if (!truckOwned) return;
            if (stage == JobStage.Hauling && routeLength > .1f)
            {
                float distance = (phase * 3.2f + routeLength * .48f) % routeLength;
                for (int i = 0; i < routeCount - 1; i++)
                {
                    if (distance > routeLengths[i]) { distance -= routeLengths[i]; continue; }
                    Vector3 direction = route[i + 1] - route[i];
                    truck.localPosition = Vector3.Lerp(route[i],route[i + 1],distance / Mathf.Max(.001f,routeLengths[i]));
                    if (direction.sqrMagnitude > .01f) truck.localRotation = Quaternion.LookRotation(direction,Vector3.up);
                    break;
                }
                foreach (Transform wheel in wheels) wheel.localRotation = Quaternion.Euler(phase * 350f,0f,0f);
                truckBed.localRotation = Quaternion.identity;
                cargo.gameObject.SetActive(true);
            }
            else if (stage == JobStage.Recycling)
            {
                truck.localPosition = depot + new Vector3(-1.7f,.22f,2.4f);
                truck.localRotation = Quaternion.Euler(0f,180f,0f);
                truckBed.localRotation = Quaternion.Euler(32f + Mathf.Sin(phase * .7f) * 9f,0f,0f);
                cargo.gameObject.SetActive(Mathf.Sin(phase * .25f) > -.3f);
            }
            else
            {
                truck.localPosition = importedJob ? depot + new Vector3(-1.7f,.22f,2.4f) :
                    jobCenter + new Vector3(districtIndex == 10 ? 6.5f : -12.6f,.22f,-5.8f);
                truck.localRotation = Quaternion.Euler(0f,180f,0f);
                truckBed.localRotation = Quaternion.identity;
                cargo.gameObject.SetActive(true);
            }
        }
    }
}