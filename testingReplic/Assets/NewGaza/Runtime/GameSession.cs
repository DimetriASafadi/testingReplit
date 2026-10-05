using System;
using NewGaza.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace NewGaza
{
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public EconomyService Economy { get; private set; }
        public GameState State => Economy == null ? null : Economy.State;
        public bool Ready { get; private set; }
        public string SaveError { get; private set; }
        public CityAudio Audio { get; private set; }
        public CityDevelopment Development { get; private set; }
        public CityHud Hud { get; private set; }
        public long Now => Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), State == null ? 0 : State.lastSeenUtc);
        public event Action Changed;
        public event Action<string> Notification;
        public event Action<int> PlotSelected;
        public event Action<string> RoadSelected;
        public string SelectedRoadId { get; private set; }
        private float nextTick;
        private float nextSave;
        private string startupError;
        private CityWorld world;
        private CityCamera cityCamera;

        // NewGaza is delivered without script .meta files. Attach by type at runtime
        // instead of storing a MonoScript GUID that will differ on each computer.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartEntryScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/NewGaza/Scenes/NewGaza.unity" ||
                FindFirstObjectByType<GameSession>() != null) return;
            GameObject host = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "New Gaza - Reconstruction Game") { host = root; break; }
            if (host == null) host = new GameObject("New Gaza - Reconstruction Game");
            host.AddComponent<GameSession>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            try
            {
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.SystemSetting;
                QualitySettings.vSyncCount = 0;
                // Runtime fallback also supports opening directly without using the setup menu.
                InputSystemBootstrap.EnsureInputEnabled();
                var loaded = GameSaveStore.Load(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var warning);
                Economy = new EconomyService(loaded);
                if (GameCatalog.ApplyMillionOpeningBalance(State)) GameSaveStore.Save(State);
                ConfigureLighting();
                world = new GameObject("City diorama").AddComponent<CityWorld>();
                world.Initialize(this);
                var cameraObject = new GameObject("City camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                var cam = cameraObject.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.76f, 0.83f, 0.88f);
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 1600f;
                cam.allowHDR = false;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                cityCamera = cameraObject.AddComponent<CityCamera>();
                cityCamera.Initialize(this, world);
                Development = new GameObject("Free building and local rubble work").AddComponent<CityDevelopment>();
                Development.transform.SetParent(transform, false);
                Development.Initialize(this, world, cityCamera);
                Economy.Tick(Now);
                world.Refresh();
                world.Fleet.RestoreSave(State);
                Audio = new GameObject("Camera-focused city soundscape").AddComponent<CityAudio>();
                Audio.transform.SetParent(transform, false);
                Audio.Initialize(this, world, cityCamera, cam);
                var hud = new GameObject("Arabic mobile HUD").AddComponent<CityHud>();
                Hud = hud;
                hud.Initialize(this, world, cityCamera);
                Ready = true;
                Save();
                if (!string.IsNullOrEmpty(warning)) Notify(warning);
            }
            catch (Exception e)
            {
                startupError = e.Message;
                Debug.LogException(e);
                // Keep the behaviour enabled so OnGUI can display the startup error.
            }
        }

        private void ConfigureLighting()
        {
            // Neutral Mediterranean daylight, with darker sheltered walls/undersides.
            // District access haze is local geometry, never distance fog over playable work.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.76f, 0.82f, 0.89f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.62f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.39f, 0.37f);
            RenderSettings.fog = false;
            var sunObject = new GameObject("Mediterranean sunlight");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.985f, 0.96f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.62f;
            sun.shadowBias = 0.025f;
            sun.shadowNormalBias = 0.08f;
            // URP otherwise replaces the Light bias with the pipeline asset defaults.
            sun.GetUniversalAdditionalLightData().usePipelineSettings = false;
            sunObject.transform.rotation = Quaternion.Euler(50, -35, 0);
            RenderSettings.sun = sun;
            QualitySettings.shadowDistance = 100;
        }

        private void Update()
        {
            var saveFailure = GameSaveStore.TakeSaveFailure();
            if (saveFailure != null) ReportSaveFailure(saveFailure);
            if (!Ready) return;
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + 1;
                var stage = State.jobStage;
                bool arrived = State.development != null && State.development.crewArrived;
                int completed = CompletedBuildingCount();
                Economy.Tick(Now);
                Changed?.Invoke();
                if (stage != State.jobStage ||
                    arrived != (State.development != null && State.development.crewArrived) ||
                    completed != CompletedBuildingCount())
                    Save();
            }
            if (Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 5;
                Save();
            }
        }

        public void Perform(Func<EconomyService, ActionResult> action)
        {
            if (!Ready) return;
            Economy.Tick(Now);
            var result = action(Economy);
            if (result.success)
            {
                Save();
                Changed?.Invoke();
                if (string.IsNullOrEmpty(SaveError)) Audio?.PlayConfirmation();
            }
            else Audio?.PlayFailure();
            Notify(result.message);
        }

        public void ChooseDistrict(int index)
        {
            if (!Ready) return;
            var result = Economy.SelectDistrict(index);
            if (!result.success) { Audio?.PlayFailure(); Notify(result.message); return; }
            ClearRoadSelection();
            world.FocusDistrict(index);
            cityCamera.Focus(world.DistrictPosition(index));
            Save();
            Changed?.Invoke();
            PlotSelected?.Invoke(-1);
        }

        public void SelectPlot(int index)
        {
            if (!Ready) return;
            ClearRoadSelection();
            world.SetSelectedPlot(index);
            PlotSelected?.Invoke(index);
        }

        public void SelectRoad(string id)
        {
            if (!Ready || world.Roads == null || world.Roads.FindSegment(id) == null) return;
            world.SetSelectedPlot(-1);
            PlotSelected?.Invoke(-1);
            SelectedRoadId = id;
            world.SetSelectedRoad(id);
            RoadSelected?.Invoke(id);
        }

        public void ImproveRoad(string id, int targetLevel)
        {
            if (string.IsNullOrEmpty(id)) return;
            Perform(e => e.ImproveRoad(id, targetLevel));
        }

        public void ClearRoadSelection()
        {
            if (SelectedRoadId == null) return;
            SelectedRoadId = null;
            world?.SetSelectedRoad(null);
            RoadSelected?.Invoke(null);
        }

        public void SetPlayerName(string value)
        {
            if (!Ready || string.IsNullOrWhiteSpace(value)) return;
            State.playerName = value.Trim().Substring(0, Math.Min(24, value.Trim().Length));
            Save();
            Changed?.Invoke();
        }

        public void Notify(string message)
        {
            if (!string.IsNullOrEmpty(message)) Notification?.Invoke(message);
        }

        public void Save()
        {
            SaveCheckpoint(false);
        }

        private void SaveCheckpoint(bool flush)
        {
            if (State == null) return;
            try
            {
                // Record wall-clock advancement before any lifecycle checkpoint.
                Economy.Tick(Now);
                if (cityCamera != null) cityCamera.CaptureSave();
                if (world != null && world.Fleet != null) world.Fleet.CaptureSave(State);
                GameSaveStore.Enqueue(State);
                if (flush) GameSaveStore.FlushPending();
                nextSave = Time.unscaledTime + 5;
                SaveError = null;
            }
            catch (Exception e)
            {
                ReportSaveFailure(e);
            }
        }

        private void ReportSaveFailure(Exception error)
        {
            SaveError = "تعذّر حفظ التقدم. تحقق من مساحة التخزين.";
            Debug.LogError("New Gaza save failed: " + error.Message);
            Audio?.PlayFailure();
            Notify(SaveError);
        }

        private int CompletedBuildingCount()
        {
            int count = 0;
            if (State.development != null)
                foreach (var building in State.development.buildings)
                    if (building.completed) count++;
            return count;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!Ready) return;
            SaveCheckpoint(paused);
            if (!paused) Changed?.Invoke();
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!Ready) return;
            SaveCheckpoint(!focused);
            if (focused) Changed?.Invoke();
        }
        private void OnApplicationQuit() { if (Ready) SaveCheckpoint(true); }
        private void OnDisable()
        {
            // Checkpoint before teardown, while the live world/view can still be captured.
            if (Ready && Instance == this) SaveCheckpoint(true);
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(startupError)) return;
            // An initialization problem should never be only a black screen.
            GUI.color = Color.white;
            GUI.Box(new Rect(20, 20, Mathf.Min(Screen.width - 40, 850), 190),
                "NEW GAZA — STARTUP ERROR\n\n" + startupError +
                "\n\nCheck the Unity Console. Saved progress has NOT been reset.");
        }
    }

    internal static class InputSystemBootstrap
    {
        public static void EnsureInputEnabled()
        {
            // Project-wide Active Input Handling is set by the explicit mobile setup menu.
            // InputSystem devices used directly by the camera; no legacy Input calls.
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
        }
    }
}