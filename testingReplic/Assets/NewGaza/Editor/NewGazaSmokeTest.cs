using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Runs only inside a real Unity editor. Does not grant currency or erase saves.</summary>
    [InitializeOnLoad]
    public static class NewGazaSmokeTest
    {
        private const string PendingKey = "NewGaza.Smoke.Pending";
        private const string BatchKey = "NewGaza.Smoke.Batch";
        private static double began;

        static NewGazaSmokeTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Check;
            if (SessionState.GetBool(PendingKey, false))
                began = EditorApplication.timeSinceStartup;
        }

        [MenuItem("New Gaza/Run play mode smoke test", priority = 4)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Stop Play Mode before running the New Gaza smoke test.");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(NewGazaProjectSetup.ScenePath);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(BatchKey, Application.isBatchMode);
            began = EditorApplication.timeSinceStartup;
            EditorApplication.EnterPlaymode();
        }

        public static void RunBatch() { Run(); }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) began = EditorApplication.timeSinceStartup;
        }

        private static void Check()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - began < 3) return;
            var session = GameSession.Instance;
            if (session == null || !session.Ready)
            {
                if (EditorApplication.timeSinceStartup - began > 30)
                    Finish(false, "GameSession did not become ready; inspect the Console startup error.");
                return;
            }
            try
            {
                Require(Camera.main != null && Camera.main.isActiveAndEnabled, "Missing active tagged camera.");
                var world = UnityEngine.Object.FindFirstObjectByType<CityWorld>();
                Require(world != null, "Missing city world.");
                Require(UnityEngine.Object.FindFirstObjectByType<CityHud>() != null, "Missing HUD.");
                CheckAudioRuntime(session);
                CheckEquipmentRuntime(world, session.State);
                Require(UnityEngine.Object.FindObjectsByType<CitySelectable>(FindObjectsSortMode.None).Length >= Core.GameCatalog.Districts.Length,
                    "Missing selectable city districts/plots.");
                Require(UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                    "Missing UI EventSystem.");
                Require(Resources.Load<Font>("NewGazaArabic") != null, "Arabic font did not import.");
                var lit = Resources.Load<Material>("NewGazaLit");
                Require(lit != null, "URP material did not import.");
                CheckImportedCityModels(world, lit);
                CheckSourcedCityBasemap();
                var fogShader = Resources.Load<Shader>("NewGazaFog");
                Require(fogShader != null && fogShader.isSupported, "District fog shader missing or unsupported.");
                for (int d = 0; d < session.State.districts.Length; d++)
                    Require(world.IsDistrictFogged(d) == !session.State.districts[d].unlocked,
                        "Fog/access mismatch in district " + d + "; unfinished unlocked districts must stay visible.");
                foreach (var fog in world.GetComponentsInChildren<DistrictFog>(true))
                {
                    Require(fog.GetComponentsInChildren<Collider>(true).Length == 0,
                        "Fog must not intercept city selection.");
                    foreach (var renderer in fog.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader == fogShader,
                            "Fog renderer does not use the retained fog shader.");
                        bool fogExpected = false;
                        for (int d = 0; d < session.State.districts.Length; d++)
                            if (fog.transform.parent != null &&
                                fog.transform.parent.name.StartsWith("District " + (d + 1) + " •", StringComparison.Ordinal))
                                fogExpected = !session.State.districts[d].unlocked;
                        Require(renderer.enabled == fogExpected,
                            "Fog renderer visibility does not match district access after startup fade.");
                    }
                }
                Require(world.GetComponentsInChildren<DistrictFog>(true).Length == Core.GameCatalog.Districts.Length,
                    "Expected one fog component per district, including Rashid.");
                foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled && renderer.transform.parent != null &&
                        renderer.transform.parent.name.StartsWith("District plot batch", StringComparison.Ordinal))
                        Require(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On,
                            "Visible district architecture batch must cast shadows.");
                Require(RenderSettings.sun != null, "Missing directional daylight.");
                var lightData = RenderSettings.sun.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                Require(lightData != null && !lightData.usePipelineSettings,
                    "URP is overriding the configured neighborhood light bias.");
                Require(string.IsNullOrEmpty(session.SaveError), "Save could not be written.");
                Require(session.State.districts.Length == Core.GameCatalog.Districts.Length,
                    "Expected all catalog neighborhoods and the final Rashid district.");
                Finish(true, "Startup, camera, city selection, HUD, authored audio, equipment geometry/rig, font, retained fog shader, district fog/access and local save passed in Unity Play Mode.");
            }
            catch (Exception e) { Finish(false, e.Message); }
        }

        private static void CheckAudioRuntime(GameSession session)
        {
            Camera camera = Camera.main;
            Require(camera != null, "Missing camera for the spatial audio listener.");
            if (camera == null) return;
            var listener = camera.GetComponent<AudioListener>();
            Require(listener != null && listener.enabled && listener.gameObject.activeInHierarchy,
                "The rendering camera must retain the one active AudioListener.");
            int sceneListeners = 0;
            foreach (AudioListener candidate in Resources.FindObjectsOfTypeAll<AudioListener>())
                if (candidate != null && candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.scene.isLoaded && candidate.enabled &&
                    candidate.gameObject.activeInHierarchy)
                    sceneListeners++;
            Require(sceneListeners == 1,
                "Exactly one enabled AudioListener may exist in the loaded player scene.");

            CityAudio audio = session.Audio;
            Require(audio != null && audio.isActiveAndEnabled, "GameSession did not initialize its CityAudio runtime.");
            if (audio == null) return;
            Require(audio.AudioVoiceCount == 12 &&
                audio.GetComponentsInChildren<AudioSource>(true).Length == 12,
                "CityAudio must create exactly twelve bounded runtime voices.");
            Require(audio.ActiveLoopCount >= 0 && audio.ActiveLoopCount <= 11,
                "CityAudio active loop count exceeds its bounded loop pool.");
            foreach (CityAudioChannel channel in Enum.GetValues(typeof(CityAudioChannel)))
            {
                float volume = audio.Volume(channel);
                Require(!float.IsNaN(volume) && !float.IsInfinity(volume) &&
                    volume >= 0f && volume <= 1f,
                    "Audio preference outside [0,1] for channel " + channel + ".");
            }

            TextAsset manifestSource = Resources.Load<TextAsset>("Audio/AudioManifest");
            Require(manifestSource != null, "Missing runtime AudioManifest text resource.");
            if (manifestSource == null) return;
            AudioManifest manifest = JsonUtility.FromJson<AudioManifest>(manifestSource.text);
            Require(manifest != null && manifest.schema == 1 && manifest.clips != null &&
                manifest.clips.Length == 9,
                "Runtime audio manifest must contain all nine authored sound resources.");
            if (manifest == null || manifest.clips == null) return;

            long decodedPcm16Bytes = 0;
            for (int i = 0; i < manifest.clips.Length; i++)
            {
                AudioClipManifest item = manifest.clips[i];
                Require(item != null && !string.IsNullOrEmpty(item.key),
                    "Audio manifest contains an empty clip key.");
                if (item == null || string.IsNullOrEmpty(item.key)) continue;
                AudioClip clip = Resources.Load<AudioClip>("Audio/" + item.key);
                Require(clip != null, "Missing imported runtime audio clip Audio/" + item.key + ".");
                if (clip == null) continue;
                Require(clip.loadState == AudioDataLoadState.Loaded,
                    "Authored sound was not preloaded before player use: " + item.key + ".");
                Require(clip.channels == 1 && clip.frequency == 24000 && clip.samples > 0 &&
                    Mathf.Abs(clip.length - item.durationSeconds) < 0.025f,
                    "Imported clip disagrees with its mono 24 kHz manifest metadata: " + item.key + ".");
                decodedPcm16Bytes += (long)clip.samples * clip.channels * sizeof(short);
            }
            Require(decodedPcm16Bytes <= 5L * 1024 * 1024,
                "The nine uncompressed PCM assets exceed the 5 MiB aggregate audio memory budget.");
        }

        private static void CheckEquipmentRuntime(CityWorld world, Core.GameState state)
        {
            CityFleet[] fleets = world.GetComponentsInChildren<CityFleet>(true);
            Require(fleets.Length == 1, "CityWorld must own exactly one production CityFleet.");
            if (fleets.Length != 1) return;
            CityFleet fleet = fleets[0];
            Transform excavator = PrivateField<Transform>(fleet, "excavator");
            Transform turret = PrivateField<Transform>(fleet, "turret");
            Transform boom = PrivateField<Transform>(fleet, "boom");
            Transform stick = PrivateField<Transform>(fleet, "stick");
            Transform bucket = PrivateField<Transform>(fleet, "bucket");
            Transform truck = PrivateField<Transform>(fleet, "truck");
            Transform truckBed = PrivateField<Transform>(fleet, "truckBed");
            Transform bulldozer = PrivateField<Transform>(fleet, "bulldozer");
            Transform blade = PrivateField<Transform>(fleet, "blade");
            Require(excavator != null && turret != null && boom != null && stick != null && bucket != null &&
                truck != null && truckBed != null && bulldozer != null && blade != null,
                "Production fleet is missing a vehicle body or articulated rig transform.");
            if (excavator == null || turret == null || boom == null || stick == null ||
                bucket == null || truck == null || truckBed == null || bulldozer == null || blade == null)
                return;
            Require(turret.parent == excavator && boom.parent == turret &&
                stick.parent == boom && bucket.parent == stick &&
                truckBed.parent == truck && blade.parent == bulldozer,
                "Excavator, tipper and dozer production joints must retain their real parented articulation.");

            CheckVehicleMeshBudget("excavator", excavator, 12000, 3.48f);
            CheckVehicleMeshBudget("truck", truck, 12000, 5f);
            CheckVehicleMeshBudget("dozer", bulldozer, 12000, 3.48f);
            CheckHydraulicLink(fleet, "boomLift");
            CheckHydraulicLink(fleet, "stickRam");
            CheckHydraulicLink(fleet, "bucketRam");
            CheckHydraulicLink(fleet, "bladeLiftLeft");
            CheckHydraulicLink(fleet, "bladeLiftRight");
            CheckHydraulicLink(fleet, "bedLiftLeft");
            CheckHydraulicLink(fleet, "bedLiftRight");

            MethodInfo stateMethod = typeof(CityFleet).GetMethod("TryGetMachineAudioState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(stateMethod != null, "CityFleet is missing its indexed live machine-audio state API.");
            if (stateMethod == null) return;
            int[] owned = { state.excavators, state.trucks, state.bulldozers };
            for (int index = 0; index < owned.Length; index++)
            {
                object[] arguments = { index, Vector3.zero, 0f, 0f, 0f };
                bool active = (bool)stateMethod.Invoke(fleet, arguments);
                Require(active == (owned[index] > 0),
                    "Machine audio index " + index + " must reflect current excavator/truck/dozer ownership.");
                if (!active) continue;
                Vector3 position = (Vector3)arguments[1];
                float load = (float)arguments[2];
                float movement = (float)arguments[3];
                float hydraulics = (float)arguments[4];
                Require(IsFinite(position) && IsUnit(load) && IsUnit(movement) && IsUnit(hydraulics),
                    "Owned machine audio state must provide finite position and normalized load/movement/hydraulics.");
            }
            CheckEquipmentContactAndTrackRuntime(world, fleet);
        }

        private static void CheckEquipmentContactAndTrackRuntime(CityWorld world, CityFleet fleet)
        {
            var probeState = new Core.GameState
            {
                excavators = 1,
                trucks = 1,
                bulldozers = 1,
                jobStage = Core.JobStage.Clearing,
                jobDistrict = 0,
                districts = Enumerable.Range(0, Math.Max(1, Core.GameCatalog.Districts.Length))
                    .Select(_ => new Core.DistrictState { clearedLoads = 0 }).ToArray()
            };
            Vector3 cityRootOrigin = fleet.transform.parent.position;
            Vector3 groundOrigin = fleet.transform.parent.TransformPoint(new Vector3(0f, -.09f, 0f));
            const int frames = 64;
            float sampleDelta = EquipmentMotion.DigCycleSeconds * .70f / (frames - 1);
            float oldCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = sampleDelta;
            try
            {
                CheckFleetGroundSupportClearance(fleet, probeState, groundOrigin);
                Transform bucket = PrivateField<Transform>(fleet, "bucket");
                Require(bucket != null, "Production contact test is missing the live excavator bucket.");
                if (bucket == null) return;

                float groundY = groundOrigin.y;
                float minimumDigClearance = float.PositiveInfinity;
                float minimumLiftedClearance = float.PositiveInfinity;
                float contactDistance = float.PositiveInfinity;
                for (int frame = 0; frame < frames; frame++)
                {
                    if (frame > 0) UpdateFleetOneFrame(fleet);
                    float phase = PrivateField<float>(fleet, "phase");
                    float expectedPhase = EquipmentMotion.DigCycleSeconds * .70f * frame / (frames - 1);
                    Require(Mathf.Abs(phase - expectedPhase) < .002f,
                        "Smoke contact poses must be advanced by active production Update and Unity delta time.");
                    Vector3[] tips = GetActualBucketToothTipWorldPoints(bucket);
                    Require(tips.Length >= 4,
                        "Actual render meshes must expose the five bucket tooth leading-edge vertices.");
                    float minimumFrameClearance = float.PositiveInfinity;
                    float closestFrameClearance = float.PositiveInfinity;
                    for (int i = 0; i < tips.Length; i++)
                    {
                        Require(IsFinite(tips[i]),
                            "Actual transformed bucket tooth vertices must remain finite through digging.");
                        float clearance = (tips[i].y - groundY) / .07f;
                        minimumFrameClearance = Mathf.Min(minimumFrameClearance, clearance);
                        closestFrameClearance = Mathf.Min(closestFrameClearance, Mathf.Abs(clearance));
                    }
                    float cycle = phase / EquipmentMotion.DigCycleSeconds;
                    if (cycle >= .20f && cycle <= .31f)
                    {
                        minimumDigClearance = Mathf.Min(minimumDigClearance, minimumFrameClearance);
                        contactDistance = Mathf.Min(contactDistance, closestFrameClearance);
                        Require(minimumFrameClearance >= -.02f,
                            "Production metal cutting teeth may enter the intended ground-contact arc by at most 0.02 model metres.");
                    }
                    else if (cycle > .31f)
                    {
                        minimumLiftedClearance = Mathf.Min(minimumLiftedClearance, minimumFrameClearance);
                        Require(minimumFrameClearance >= -.001f,
                            "Production teeth must remain above ground after lift, through swing, release and return.");
                    }
                }
                Require(contactDistance <= .05f,
                    "An actual transformed tooth-leading-edge mesh vertex must contact ground within 0.05 model metres.");

                Time.captureDeltaTime = .005f;
                int releaseFrames = 0;
                while (PrivateField<int>(fleet, "transferredCargoPieces") == 0 &&
                    releaseFrames++ < 200)
                    UpdateFleetOneFrame(fleet);
                Time.captureDeltaTime = sampleDelta;
                Require(PrivateField<int>(fleet, "transferredCargoPieces") == 3,
                    "The production docked excavation cycle must naturally release all three cargo fragments.");
                Transform bed = PrivateField<Transform>(fleet, "truckBed");
                Require(bed != null, "Production bucket-release check is missing the live truck bed.");
                if (bed == null) return;
                SmokeBedInterior interior = MeasureActualBedInterior(bed);
                Vector3[] releaseTips = GetActualBucketToothTipWorldPoints(bucket);
                bool releaseFitsInsideBed = releaseTips.Length >= 4;
                for (int i = 0; i < releaseTips.Length; i++)
                {
                    Vector3 point = bed.InverseTransformPoint(releaseTips[i]);
                    bool interiorXz = point.x > interior.InnerXMin + .02f &&
                        point.x < interior.InnerXMax - .02f &&
                        point.z > interior.InnerZMin + .02f &&
                        point.z < interior.InnerZMax - .02f;
                    bool aboveFloor = point.y > interior.FloorTop + .02f;
                    bool clearOfWallTops = point.y > interior.WallTop + .02f;
                    releaseFitsInsideBed &= interiorXz && aboveFloor && clearOfWallTops;
                }
                Require(interior.WallTop > interior.FloorTop && releaseFitsInsideBed,
                    "At release, actual bucket tooth vertices must clear the measured bed floor/wall tops and lie inside its real interior XZ; visible stored cargo alone is not contact proof.");

                CheckVisibleTrackTravel(fleet, probeState, groundOrigin);
                CheckTruckRouteRuntime(fleet, probeState, groundOrigin);
            }
            finally
            {
                // Restore the real session-owned display state; the probe state is never written to the save.
                Time.captureDeltaTime = oldCaptureDeltaTime;
                world.Refresh();
            }
        }

        private static Vector3[] GetActualBucketToothTipWorldPoints(Transform bucket)
        {
            var vertices = new List<(Vector3 bucketPoint, Vector3 worldPoint)>();
            float maximumLeadingZ = float.NegativeInfinity;
            foreach (MeshFilter filter in bucket.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!HasNamedAncestor(filter.transform, bucket,
                        "Replaceable beveled cutting teeth / heel cheeks") ||
                    filter.sharedMesh == null) continue;
                Vector3[] meshVertices = filter.sharedMesh.vertices;
                for (int vertex = 0; vertex < meshVertices.Length; vertex++)
                {
                    Vector3 world = filter.transform.TransformPoint(meshVertices[vertex]);
                    Vector3 local = bucket.InverseTransformPoint(world);
                    maximumLeadingZ = Mathf.Max(maximumLeadingZ, local.z);
                    vertices.Add((local, world));
                }
            }
            if (vertices.Count == 0) return Array.Empty<Vector3>();
            float leadingTolerance = .002f;
            const float toothApexY = -.1964f;
            return vertices.Where(vertex =>
                    vertex.bucketPoint.z >= maximumLeadingZ - leadingTolerance &&
                    Mathf.Abs(vertex.bucketPoint.y - toothApexY) <= leadingTolerance)
                .Select(vertex => vertex.worldPoint).ToArray();
        }

        private static bool HasNamedAncestor(Transform node, Transform stopAt, string name)
        {
            for (Transform current = node; current != null; current = current.parent)
            {
                if (current.name == name) return true;
                if (current == stopAt) break;
            }
            return false;
        }

        private static void CheckFleetGroundSupportClearance(CityFleet fleet,
            Core.GameState probeState, Vector3 gradeOrigin)
        {
            probeState.jobStage = Core.JobStage.Idle;
            probeState.jobDistrict = -1;
            probeState.districts[0].clearedLoads = 0;
            fleet.Refresh(probeState, gradeOrigin, gradeOrigin);
            for (int frame = 0; frame < 24; frame++) UpdateFleetOneFrame(fleet);
            CheckGroundSupportAtPose(fleet, "PARK", gradeOrigin.y);

            probeState.jobStage = Core.JobStage.Clearing;
            probeState.jobDistrict = 0;
            PrepareTruckAtWorkDock(fleet, probeState, gradeOrigin, gradeOrigin);
            CheckGroundSupportAtPose(fleet, "NORMALWORK", gradeOrigin.y);

            probeState.districts[0].clearedLoads = Core.GameCatalog.Districts[0].rubbleLoads;
            PrepareTruckAtWorkDock(fleet, probeState, gradeOrigin, gradeOrigin);
            CheckGroundSupportAtPose(fleet, "IMPORTEDWORK", gradeOrigin.y);

            probeState.districts[0].clearedLoads = 0;
            PrepareTruckAtWorkDock(fleet, probeState, gradeOrigin, gradeOrigin);
        }

        private static void CheckGroundSupportAtPose(CityFleet fleet, string pose, float gradeY)
        {
            const float surfaceTolerance = .005f;
            const float penetrationTolerance = .002f;
            float nativeSurfaceRise = pose == "PARK" ? .003f : .006f;
            float maximumGap = nativeSurfaceRise + surfaceTolerance;
            Transform excavator = PrivateField<Transform>(fleet, "excavator");
            Transform bulldozer = PrivateField<Transform>(fleet, "bulldozer");
            Transform truck = PrivateField<Transform>(fleet, "truck");
            CheckLowestTreadVertex(excavator, "track shoes", "excavator", pose,
                gradeY, maximumGap, penetrationTolerance);
            CheckLowestTreadVertex(bulldozer, "track shoes", "bulldozer", pose,
                gradeY, maximumGap, penetrationTolerance);
            Transform[] wheels = PrivateField<Transform[]>(fleet, "wheels");
            MeshFilter[] tires = wheels.SelectMany(wheel =>
                    wheel.GetComponentsInChildren<MeshFilter>(true))
                .Where(filter => filter.gameObject.activeInHierarchy).ToArray();
            Require(wheels.Length == 6 && tires.Length >= 6,
                "Ground-support sampling needs the six actual truck tire/wheel meshes.");
            CheckLowestTreadVertex(tires, "truck tires", pose,
                gradeY, maximumGap, penetrationTolerance);
            Require(IsVisibleMachine(excavator) && IsVisibleMachine(bulldozer) &&
                IsVisibleMachine(truck),
                pose + " ground-support samples require the production fleet fade/reveal to finish.");
        }

        private static void CheckLowestTreadVertex(Transform machine, string componentName,
            string machineName, string pose, float gradeY, float maximumGap,
            float penetrationTolerance)
        {
            MeshFilter[] filters = machine.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.gameObject.name.IndexOf(componentName,
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                    filter.gameObject.activeInHierarchy).ToArray();
            Require(filters.Length >= 2,
                pose + " must expose the actual " + machineName + " " + componentName + ".");
            CheckLowestTreadVertex(filters, machineName + " " + componentName, pose,
                gradeY, maximumGap, penetrationTolerance);
        }

        private static void CheckLowestTreadVertex(MeshFilter[] filters, string componentName,
            string pose, float gradeY, float maximumGap, float penetrationTolerance)
        {
            float minimumY = float.PositiveInfinity;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    minimumY = Mathf.Min(minimumY, filter.transform.TransformPoint(vertex).y);
            }
            float gap = minimumY - gradeY;
            Require(IsFinite(gap) && gap >= -penetrationTolerance && gap <= maximumGap,
                pose + " actual " + componentName +
                " lowest mesh vertex must remain between grade-0.002 and native apron surface+0.005 city units; ground gap=" +
                gap.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                ", allowed=[" + (-penetrationTolerance).ToString("F3",
                    System.Globalization.CultureInfo.InvariantCulture) + "," +
                maximumGap.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "] city units. Measure only tread/tire meshes, not bucket or blade bottoms.");
        }

        private static SmokeBedInterior MeasureActualBedInterior(Transform bed)
        {
            Transform meshRoot = bed.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform =>
                    transform.name == "Reinforced steel subframe / lined tipper box");
            Require(meshRoot != null, "The actual production truck-bed mesh group is missing.");
            if (meshRoot == null) return new SmokeBedInterior();
            var points = new List<Vector3>();
            foreach (MeshFilter filter in meshRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    points.Add(bed.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
            }
            float floorTop = points.Where(point => Mathf.Abs(point.x) < .8f &&
                    point.z > .20f && point.z < 2.10f && point.y < .20f)
                .Select(point => point.y).DefaultIfEmpty(float.NegativeInfinity).Max();
            float innerXMin = points.Where(point => point.x < 0f && point.x > -.8f &&
                    Mathf.Abs(point.z - .9f) < .8f)
                .Select(point => point.x).DefaultIfEmpty(float.NegativeInfinity).Max();
            float innerXMax = points.Where(point => point.x > 0f && point.x < .8f &&
                    Mathf.Abs(point.z - .9f) < .8f)
                .Select(point => point.x).DefaultIfEmpty(float.PositiveInfinity).Min();
            float innerZMin = points.Where(point => point.z >= 0f && point.z < .2f &&
                    Mathf.Abs(point.x) < .8f)
                .Select(point => point.z).DefaultIfEmpty(float.NegativeInfinity).Max();
            float innerZMax = points.Where(point => point.z > 2.1f && point.z < 2.5f &&
                    Mathf.Abs(point.x) < .8f)
                .Select(point => point.z).DefaultIfEmpty(float.PositiveInfinity).Min();
            float wallTop = points.Where(point => point.y > floorTop + .08f &&
                    ((Mathf.Abs(point.x) > .52f && point.z > .05f && point.z < 2.25f) ||
                     ((point.z < .16f || point.z > 2.13f) && Mathf.Abs(point.x) < .7f)))
                .Select(point => point.y).DefaultIfEmpty(float.NegativeInfinity).Max();
            return new SmokeBedInterior
            {
                FloorTop = floorTop, WallTop = wallTop,
                InnerXMin = innerXMin, InnerXMax = innerXMax,
                InnerZMin = innerZMin, InnerZMax = innerZMax
            };
        }

        private static void CheckVisibleTrackTravel(CityFleet fleet, Core.GameState probeState,
            Vector3 workCenter)
        {
            probeState.jobStage = Core.JobStage.Idle;
            probeState.jobDistrict = -1;
            fleet.Refresh(probeState, workCenter, workCenter);
            UpdateFleetOneFrame(fleet);
            float depotOffset = 0f;
            foreach (string field in new[] { "excavator", "bulldozer" })
            {
                Transform machine = PrivateField<Transform>(fleet, field);
                MeshFilter[] shoes = machine.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.name.IndexOf("steel track shoes",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                        filter.gameObject.activeInHierarchy).ToArray();
                Transform[] rollers = machine.GetComponentsInChildren<Transform>(true)
                    .Where(transform => transform.name.IndexOf("road roller",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                        transform.gameObject.activeInHierarchy).ToArray();
                Require(shoes.Length >= 2 && rollers.Length >= 10 &&
                    shoes.All(filter => filter.GetComponent<MeshRenderer>() != null),
                    "Production " + field + " must have two rendered circulating shoe loops and ten visible road rollers.");
                if (shoes.Length == 0 || rollers.Length == 0) continue;
                foreach (MeshFilter shoe in shoes)
                    CheckTrackShoeMeshGeometry(shoe, field, "stationary capsule/straight");
                Vector3[][] stationaryShoes = shoes.Select(filter =>
                    (Vector3[])filter.sharedMesh.vertices.Clone()).ToArray();
                Quaternion[] stationaryRollers = rollers.Select(roller => roller.localRotation).ToArray();
                Vector3 parkedPosition = machine.localPosition;
                UpdateFleetOneFrame(fleet);
                Require(Vector3.Distance(parkedPosition, machine.localPosition) < .0001f &&
                    shoes.Select((filter, index) => MeshVerticesMatch(
                        stationaryShoes[index], filter.sharedMesh.vertices)).All(unchanged => unchanged) &&
                    rollers.Select((roller, index) => Quaternion.Angle(
                        stationaryRollers[index], roller.localRotation) < .1f).All(unchanged => unchanged),
                    "Stationary production " + field + " shoes and rollers must not creep.");

                depotOffset += 1f;
                fleet.Refresh(probeState, workCenter, workCenter + Vector3.forward * depotOffset);
                UpdateFleetOneFrame(fleet);
                bool shoesMoved = shoes.Select((filter, index) => !MeshVerticesMatch(
                    stationaryShoes[index], filter.sharedMesh.vertices)).Any(changed => changed);
                bool rollersMoved = rollers.Select((roller, index) => Quaternion.Angle(
                    stationaryRollers[index], roller.localRotation) > .5f).Any(changed => changed);
                Require(Vector3.Distance(parkedPosition, machine.localPosition) > .5f &&
                    shoesMoved && rollersMoved,
                    "Actual crawler travel must circulate production shoe vertices and rotate visible rollers.");
                foreach (MeshFilter shoe in shoes)
                    CheckTrackShoeMeshGeometry(shoe, field, "travelled capsule/straight");
                Vector3[][] travelShoes = shoes.Select(filter =>
                    (Vector3[])filter.sharedMesh.vertices.Clone()).ToArray();
                Quaternion[] travelRollers = rollers.Select(roller => roller.localRotation).ToArray();
                Vector3 travelledPosition = machine.localPosition;
                UpdateFleetOneFrame(fleet);
                Require(Vector3.Distance(travelledPosition, machine.localPosition) < .0001f &&
                    shoes.Select((filter, index) => MeshVerticesMatch(
                        travelShoes[index], filter.sharedMesh.vertices)).All(unchanged => unchanged) &&
                    rollers.Select((roller, index) => Quaternion.Angle(
                        travelRollers[index], roller.localRotation) < .1f).All(unchanged => unchanged),
                    "Production crawler shoes and rollers must hold their rotation while stationary after travel.");
            }
        }

        private static void CheckTrackShoeMeshGeometry(MeshFilter filter, string machine,
            string travelPose)
        {
            const int shoesPerLoop = 20;
            const int verticesPerShoe = 8;
            const int indicesPerShoe = 36;
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Require(vertices.Length == shoesPerLoop * verticesPerShoe &&
                triangles.Length == shoesPerLoop * indicesPerShoe,
                travelPose + " " + machine +
                " render mesh must contain 20 separately indexed 8-vertex steel shoes.");
            if (vertices.Length != shoesPerLoop * verticesPerShoe ||
                triangles.Length != shoesPerLoop * indicesPerShoe) return;

            int straightShoes = 0;
            int capsuleShoes = 0;
            bool allShoesAreClosed = true;
            bool allTrianglesHaveArea = true;
            float minimumArea = float.PositiveInfinity;
            float minimumVolume = float.PositiveInfinity;
            float maximumNormalTangentDot = 0f;
            for (int shoe = 0; shoe < shoesPerLoop; shoe++)
            {
                int vertexBase = shoe * verticesPerShoe;
                int triangleBase = shoe * indicesPerShoe;
                Vector3 normal = (vertices[vertexBase + 3] - vertices[vertexBase]).normalized;
                Vector3 tangent = (vertices[vertexBase + 4] - vertices[vertexBase]).normalized;
                float normalTangentDot = Mathf.Abs(Vector3.Dot(normal, tangent));
                maximumNormalTangentDot = Mathf.Max(maximumNormalTangentDot, normalTangentDot);
                Require(normal.sqrMagnitude > .99f && tangent.sqrMagnitude > .99f &&
                    normalTangentDot < .0001f,
                    travelPose + " " + machine + " actual shoe " + shoe +
                    " normal/tangent bases must be perpendicular; |dot|=" +
                    normalTangentDot.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ".");
                if (Mathf.Abs(normal.y) >= .999f && Mathf.Abs(normal.z) < .05f)
                    straightShoes++;
                else capsuleShoes++;

                var edgeUseCount = new int[verticesPerShoe, verticesPerShoe];
                double signedVolume = 0d;
                int faceCount = 0;
                for (int index = triangleBase; index < triangleBase + indicesPerShoe; index += 3)
                {
                    int first = triangles[index];
                    int second = triangles[index + 1];
                    int third = triangles[index + 2];
                    bool validFace = first >= vertexBase && first < vertexBase + verticesPerShoe &&
                        second >= vertexBase && second < vertexBase + verticesPerShoe &&
                        third >= vertexBase && third < vertexBase + verticesPerShoe;
                    Require(validFace, travelPose + " " + machine +
                        " every actual shoe face must use vertices from its own eight-vertex shoe.");
                    if (!validFace) { allTrianglesHaveArea = false; continue; }

                    Vector3 a = vertices[first], b = vertices[second], c = vertices[third];
                    float area = Vector3.Cross(b - a, c - a).magnitude * .5f;
                    minimumArea = Mathf.Min(minimumArea, area);
                    allTrianglesHaveArea &= IsFinite(area) && area > 1e-9f;
                    signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6d;
                    AddTrackShoeEdgeUse(edgeUseCount, first - vertexBase, second - vertexBase);
                    AddTrackShoeEdgeUse(edgeUseCount, second - vertexBase, third - vertexBase);
                    AddTrackShoeEdgeUse(edgeUseCount, third - vertexBase, first - vertexBase);
                    faceCount++;
                }

                int edgeCount = 0;
                for (int first = 0; first < verticesPerShoe; first++)
                    for (int second = first + 1; second < verticesPerShoe; second++)
                    {
                        int uses = edgeUseCount[first, second];
                        if (uses == 0) continue;
                        edgeCount++;
                        if (uses != 2) allShoesAreClosed = false;
                    }
                double volume = Math.Abs(signedVolume);
                minimumVolume = Mathf.Min(minimumVolume, (float)volume);
                allShoesAreClosed &= faceCount == 12 && edgeCount == 18 &&
                    IsFinite((float)volume) && volume > 1e-9d;
            }
            Require(straightShoes >= 2 && capsuleShoes >= 2,
                travelPose + " " + machine + " shoe loop must sample both straight and capsule-end poses.");
            Require(allTrianglesHaveArea,
                travelPose + " " + machine +
                " all 240 actual shoe triangles (720 indices) must have area > 1e-9 m^2; minimum=" +
                minimumArea.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + ".");
            Require(allShoesAreClosed,
                travelPose + " " + machine +
                " every actual shoe must form a closed 12-face/18-edge mesh with nonzero measured volume; minimum=" +
                minimumVolume.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + " m^3.");
            Require(maximumNormalTangentDot < .0001f,
                travelPose + " " + machine +
                " capsule/straight shoe normal and tangent must be perpendicular; maximum |dot|=" +
                maximumNormalTangentDot.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        private static void AddTrackShoeEdgeUse(int[,] edgeUseCount, int first, int second)
        {
            int low = Math.Min(first, second);
            int high = Math.Max(first, second);
            edgeUseCount[low, high]++;
        }

        private static void CheckTruckRouteRuntime(CityFleet fleet, Core.GameState probeState,
            Vector3 cityRootOrigin)
        {
            float deltaTime = Time.deltaTime;
            Require(deltaTime > .0001f,
                "Truck route speed needs a positive Unity frame interval.");
            if (deltaTime <= .0001f) return;
            Transform truck = PrivateField<Transform>(fleet, "truck");
            foreach (float dispatchDistance in new[] { 10f, 100f })
            {
                Vector3 workCenter = cityRootOrigin + Vector3.forward * dispatchDistance;
                PrepareTruckAtWorkDock(fleet, probeState, workCenter, cityRootOrigin);
                probeState.jobStage = Core.JobStage.Hauling;
                fleet.Refresh(probeState, workCenter, cityRootOrigin);

                Vector3 previousPosition = Vector3.zero;
                bool previousWasVisible = false;
                float maximumSpeed = 0f;
                float travelDistance = 0f;
                float minimumHeadingAlignment = 1f;
                for (int frame = 0; frame < 180; frame++)
                {
                    UpdateFleetOneFrame(fleet);
                    if (!IsVisibleMachine(truck))
                    {
                        previousWasVisible = false;
                        continue;
                    }
                    Vector3 currentPosition = truck.position;
                    if (previousWasVisible)
                    {
                        Vector3 displacement = currentPosition - previousPosition;
                        float distance = displacement.magnitude;
                        travelDistance += distance;
                        maximumSpeed = Mathf.Max(maximumSpeed, distance / deltaTime);
                        if (distance > .001f)
                            minimumHeadingAlignment = Mathf.Min(minimumHeadingAlignment,
                                Vector3.Dot(displacement.normalized,
                                    truck.rotation * Vector3.forward));
                    }
                    previousPosition = currentPosition;
                    previousWasVisible = true;
                }
                Require(travelDistance > .05f && maximumSpeed <= .35f,
                    "Measured actual truck route motion over " +
                    dispatchDistance.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
                    " city units must stay below 0.35 unscaled city units/second.");
                Require(minimumHeadingAlignment >= -.05f,
                    "Truck heading must not point backwards relative to forward route displacement.");
            }

            PrepareTruckAtWorkDock(fleet, probeState, cityRootOrigin, cityRootOrigin);
        }

        private static void PrepareTruckAtWorkDock(CityFleet fleet, Core.GameState probeState,
            Vector3 workCenter, Vector3 depot)
        {
            SetPrivateField(fleet, "truckTripState", GetTruckTripStateValue("ParkedAtDepot"));
            SetPrivateField(fleet, "pendingRouteRebuild", false);
            probeState.jobStage = Core.JobStage.Idle;
            probeState.jobDistrict = -1;
            fleet.Refresh(probeState, workCenter, depot);
            probeState.jobStage = Core.JobStage.Clearing;
            probeState.jobDistrict = 0;
            fleet.Refresh(probeState, workCenter, depot);
            SetPrivateField(fleet, "transferredCargoPieces", 0);
            SetPrivateField(fleet, "excavatorWaitingForTruck", false);
            UpdateFleetOneFrame(fleet);

            Vector3[] tripRoute = PrivateField<Vector3[]>(fleet, "tripRoute");
            float routeSpan = Vector3.Distance(workCenter, depot);
            Transform truck = PrivateField<Transform>(fleet, "truck");
            float deltaTime = Time.deltaTime;
            Require(deltaTime > .0001f, "Work-dock preparation needs a positive active Unity delta time.");
            if (deltaTime <= .0001f) return;
            int maximumFrames = Mathf.CeilToInt((routeSpan + 8f) /
                CityFleet.HaulingSpeed / deltaTime) + 40;
            int frame = 0;
            while (PrivateField<object>(fleet, "truckTripState").ToString() != "ParkedAtWork" &&
                frame++ < maximumFrames)
                UpdateFleetOneFrame(fleet);
            Require(PrivateField<object>(fleet, "truckTripState").ToString() == "ParkedAtWork" &&
                Vector3.Distance(truck.localPosition, tripRoute[0]) < .02f &&
                Quaternion.Angle(truck.localRotation, Quaternion.identity) < 1f,
                "Work-dock preparation must complete the actual inbound truck route and stopped dock turn.");
        }

        private static object GetTruckTripStateValue(string name)
        {
            FieldInfo field = typeof(CityFleet).GetField("truckTripState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Production truck route state is missing.");
            return Enum.Parse(field.FieldType, name);
        }

        private static void UpdateFleetOneFrame(CityFleet fleet)
        {
            MethodInfo update = typeof(CityFleet).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(update != null, "Production CityFleet.Update is required to observe truck routes.");
            update?.Invoke(fleet, null);
        }

        private static bool IsVisibleMachine(Transform machine)
        {
            if (!machine.gameObject.activeInHierarchy) return false;
            MeshRenderer[] renderers = machine.gameObject.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.gameObject.activeInHierarchy && renderer.enabled).ToArray();
            return renderers.Any(renderer => renderer.sharedMaterial == null ||
                renderer.sharedMaterial.GetColor("_BaseColor").a > .05f);
        }

        private static bool MeshVerticesMatch(Vector3[] left, Vector3[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (Vector3.Distance(left[i], right[i]) > .00001f) return false;
            return true;
        }

        private static void SetPrivateField(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing private test field " + name + ".");
            field.SetValue(owner, value);
        }

        private sealed class SmokeBedInterior
        {
            public float FloorTop, WallTop, InnerXMin, InnerXMax, InnerZMin, InnerZMax;
        }

        private static void CheckVehicleMeshBudget(string name, Transform vehicle,
            int triangleBudget, float maximumModelReach)
        {
            MeshFilter[] filters = vehicle.GetComponentsInChildren<MeshFilter>(true);
            int triangles = 0;
            float maximumRadius = 0f;
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                triangles += mesh.triangles.Length / 3;
                Vector3[] vertices = mesh.vertices;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    Vector3 modelPoint = vehicle.InverseTransformPoint(
                        filters[i].transform.TransformPoint(vertices[vertex]));
                    if (!IsFinite(modelPoint)) continue;
                    maximumRadius = Mathf.Max(maximumRadius, modelPoint.magnitude);
                }
            }
            Require(filters.Length > 0 && triangles > 0,
                "Production " + name + " has no generated render meshes.");
            Require(triangles <= triangleBudget,
                "Production " + name + " exceeds its " + triangleBudget + " triangle vehicle budget (" +
                triangles + ").");
            Require(maximumRadius > 0f && maximumRadius <= maximumModelReach,
                "Production " + name + " exceeds its physical model-space envelope (" +
                maximumRadius.ToString("F3") + " m radius).");
        }

        private static void CheckHydraulicLink(CityFleet fleet, string fieldName)
        {
            object link = PrivateField<object>(fleet, fieldName);
            Require(link != null, "Production hydraulic linkage is missing: " + fieldName + ".");
            if (link == null) return;
            Transform barrel = PrivateField<Transform>(link, "barrel");
            Transform rod = PrivateField<Transform>(link, "rod");
            Transform basePin = PrivateField<Transform>(link, "basePin");
            Transform rodPin = PrivateField<Transform>(link, "rodPin");
            Require(barrel != null && rod != null && basePin != null && rodPin != null,
                "Hydraulic linkage must retain its barrel, exposed piston rod and both pinned eyes: " + fieldName + ".");
            if (barrel == null || rod == null || basePin == null || rodPin == null) return;
            float span = Vector3.Distance(basePin.localPosition, rodPin.localPosition);
            float barrelLength = barrel.localScale.y;
            float rodLength = rod.localScale.y;
            Require(span > 0.025f && barrelLength >= 0.04f && rodLength >= 0.025f &&
                Mathf.Abs(span - barrelLength - rodLength) < 0.015f &&
                Mathf.Abs(Vector3.Distance(basePin.localPosition, barrel.localPosition) -
                    barrelLength * 0.5f) < 0.015f &&
                Mathf.Abs(Vector3.Distance(rodPin.localPosition, rod.localPosition) -
                    rodLength * 0.5f) < 0.015f,
                "Hydraulic barrel/rod lengths and pinned endpoints must match the current live rig span: " +
                fieldName + ".");
        }

        private static T PrivateField<T>(object owner, string name)
        {
            if (owner == null) return default(T);
            FieldInfo field = owner.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return default(T);
            object value = field.GetValue(owner);
            return value is T typed ? typed : default(T);
        }

        private static bool IsUnit(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        [Serializable]
        private sealed class AudioManifest
        {
            public int schema;
            public AudioClipManifest[] clips;
        }

        [Serializable]
        private sealed class AudioClipManifest
        {
            public string key;
            public float durationSeconds;
        }

        private static void CheckImportedCityModels(CityWorld world, Material lit)
        {
            Require(lit != null && lit.shader != null &&
                lit.shader.name == "Universal Render Pipeline/Lit",
                "NewGazaLit must retain the Universal Render Pipeline/Lit shader.");
            Require(world.ImportedModelCount == 5,
                "CityWorld must explicitly load all three primary models and both context LOD models without procedural fallback.");
            string[] keys =
            {
                "apartment", "ruined_building", "rubble_heap",
                "apartment_context", "ruined_building_context"
            };
            string[] albedoKeys =
            {
                "apartment", "ruined_building", "rubble_heap",
                "apartment", "ruined_building"
            };
            int[] triangleBudgets = { 2500, 4000, 1400, 350, 450 };
            var albedos = new Texture2D[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                GameObject source = Resources.Load<GameObject>("Models/" + key);
                Require(source != null, "Missing imported model resource Models/" + key + ".");
                if (source == null) continue;

                Texture2D albedo = Resources.Load<Texture2D>("Models/" + albedoKeys[i] + "_albedo");
                Require(albedo != null,
                    "Missing primary imported albedo resource Models/" + albedoKeys[i] + "_albedo.");
                if (albedo == null) continue;
                albedos[i] = albedo;
                Require(albedo.width <= 1024 && albedo.height <= 1024,
                    "Imported albedo exceeds its 1024-pixel mobile texture limit: " + key + ".");

                MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
                int meshCount = 0;
                int triangleCount = 0;
                bool hasDetailedMesh = false;
                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh mesh = filters[f].sharedMesh;
                    if (mesh == null) continue;
                    meshCount++;
                    Require(mesh.isReadable,
                        "Imported mesh is not CPU-readable for CityMeshBatch.CombineMeshes: " + key + ".");
                    Vector3[] vertices = mesh.vertices;
                    Vector2[] uvs = mesh.uv;
                    Vector3[] normals = mesh.normals;
                    Require(uvs.Length == vertices.Length,
                        "Imported mesh UV count must equal its vertex count: " + key + ".");
                    Require(normals.Length == vertices.Length,
                        "Imported normals must be present for every source vertex: " + key + ".");
                    bool hasNormal = false;
                    for (int n = 0; n < normals.Length; n++)
                        if (normals[n].sqrMagnitude > .5f) { hasNormal = true; break; }
                    Require(hasNormal, "Imported mesh has no usable imported normals: " + key + ".");
                    if (vertices.Length > 64) hasDetailedMesh = true;
                    triangleCount += mesh.triangles.Length / 3;
                }

                Require(meshCount > 0, "Imported model contains no source meshes: " + key + ".");
                Require(hasDetailedMesh,
                    "Imported model must include a real mesh with more than 64 vertices: " + key + ".");
                Require(triangleCount <= Mathf.CeilToInt(triangleBudgets[i] * 1.01f),
                    "Imported model exceeds its mobile triangle budget of " +
                    triangleBudgets[i] + " triangles (+1% tolerance): " + key +
                    " has " + triangleCount + ".");
            }

            bool foundImportedRenderer = false;
            foreach (MeshRenderer renderer in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material == null || !material.name.StartsWith("New Gaza • imported ",
                    StringComparison.Ordinal)) continue;

                foundImportedRenderer = true;
                Require(material.shader == lit.shader,
                    "Imported model batch must use the retained URP Lit shader.");
                Require(material.enableInstancing,
                    "Imported model material must permit instancing.");
                int modelIndex = -1;
                for (int i = 0; i < keys.Length; i++)
                    if (material.name.Contains("imported " + keys[i] + " albedo",
                        StringComparison.Ordinal))
                        modelIndex = i;
                Require(modelIndex >= 0 && albedos[modelIndex] != null &&
                    material.GetTexture("_BaseMap") == albedos[modelIndex],
                    "Imported model material must bind its matching Resources albedo texture.");
                if (renderer.transform.parent != null &&
                    renderer.transform.parent.name.StartsWith("District plot batch",
                        StringComparison.Ordinal))
                    Require(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On,
                        "Imported district plot batches must cast shadows.");
            }
            Require(foundImportedRenderer,
                "CityWorld did not create a runtime renderer using an imported model material.");
            Require(albedos[3] == albedos[0] && albedos[4] == albedos[1],
                "Context LOD models must reuse the apartment and ruined-building primary albedo PNG resources.");
        }

        private static void CheckSourcedCityBasemap()
        {
            TextAsset source = Resources.Load<TextAsset>("GazaBasemap");
            Require(source != null, "Missing authentic GazaBasemap JSON Resources asset.");
            if (source == null) return;

            CityBasemap map = JsonUtility.FromJson<CityBasemap>(source.text);
            Require(map != null && (map.schemaVersion == 1 || map.schema == 1),
                "GazaBasemap must parse with the supported versioned city schema.");
            if (map == null) return;
            Require(map.roads != null && map.roads.Length >= 20 &&
                map.buildings != null && map.buildings.Length >= 100 &&
                map.areas != null && map.areas.Length >= 5,
                "GazaBasemap must include the sourced city roads, building footprints and landuse areas.");
            if (map.roads == null || map.buildings == null || map.areas == null) return;

            int roadPoints = 0, buildingPoints = 0, areaPoints = 0;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < map.roads.Length; i++)
            {
                CityBasemapRoad road = map.roads[i];
                Require(road != null && road.points != null && road.points.Length >= 2,
                    "Every sourced road must contain a valid point sequence.");
                if (road == null || road.points == null) continue;
                for (int p = 0; p < road.points.Length; p++)
                    CheckFinitePoint(road.points[p], ref roadPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            for (int i = 0; i < map.buildings.Length; i++)
            {
                CityBasemapBuilding building = map.buildings[i];
                Require(building != null && building.center != null &&
                    building.outline != null && building.outline.Length >= 3,
                    "Every sourced building footprint must contain a center and polygon outline.");
                if (building == null) continue;
                CheckFinitePoint(building.center, ref buildingPoints,
                    ref minX, ref maxX, ref minZ, ref maxZ);
                if (building.outline == null) continue;
                for (int p = 0; p < building.outline.Length; p++)
                    CheckFinitePoint(building.outline[p], ref buildingPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            for (int i = 0; i < map.areas.Length; i++)
            {
                CityBasemapArea area = map.areas[i];
                Require(area != null && area.points != null && area.points.Length >= 3,
                    "Every sourced landuse area must contain a polygon outline.");
                if (area == null || area.points == null) continue;
                for (int p = 0; p < area.points.Length; p++)
                    CheckFinitePoint(area.points[p], ref areaPoints,
                        ref minX, ref maxX, ref minZ, ref maxZ);
            }
            Require(roadPoints > 0 && buildingPoints > 0 && areaPoints > 0 &&
                maxX - minX > 150f && maxZ - minZ > 150f,
                "GazaBasemap must provide finite sourced coordinates spanning a contiguous urban area.");
        }

        private static void CheckFinitePoint(CityBasemapPoint point, ref int count,
            ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            bool finite = point != null &&
                !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);
            Require(finite, "GazaBasemap feature coordinates must be finite.");
            if (!finite) return;
            count++;
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.z);
            maxZ = Mathf.Max(maxZ, point.z);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(PendingKey, false);
            if (success) Debug.Log("New Gaza smoke test PASS: " + message);
            else Debug.LogError("New Gaza smoke test FAIL: " + message);
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(success ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}