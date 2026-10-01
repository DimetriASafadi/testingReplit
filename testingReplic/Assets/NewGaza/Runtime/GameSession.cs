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
        public long Now => Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), State == null ? 0 : State.lastSeenUtc);
        public event Action Changed;
        public event Action<string> Notification;
        public event Action<int> PlotSelected;
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
                Economy.Tick(Now);
                ConfigureLighting();
                world = new GameObject("City diorama").AddComponent<CityWorld>();
                world.Initialize(this);
                var cameraObject = new GameObject("City camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                var cam = cameraObject.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.48f, 0.72f, 0.76f);
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 1600f;
                cam.allowHDR = false;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                cityCamera = cameraObject.AddComponent<CityCamera>();
                cityCamera.Initialize(this, world);
                var hud = new GameObject("Arabic mobile HUD").AddComponent<CityHud>();
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
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.73f, 0.79f);
            RenderSettings.fog = false;
            var sunObject = new GameObject("Mediterranean sunlight");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.92f, 0.79f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sunObject.transform.rotation = Quaternion.Euler(50, -35, 0);
            RenderSettings.sun = sun;
            QualitySettings.shadowDistance = 100;
        }

        private void Update()
        {
            if (!Ready) return;
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + 1;
                Economy.Tick(Now);
                Changed?.Invoke();
            }
            if (Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 30;
                Save();
            }
        }

        public void Perform(Func<EconomyService, ActionResult> action)
        {
            if (!Ready) return;
            Economy.Tick(Now);
            var result = action(Economy);
            if (result.success) { Save(); Changed?.Invoke(); }
            Notify(result.message);
        }

        public void ChooseDistrict(int index)
        {
            if (!Ready) return;
            var result = Economy.SelectDistrict(index);
            if (!result.success) { Notify(result.message); return; }
            world.FocusDistrict(index);
            cityCamera.Focus(world.DistrictPosition(index));
            Save();
            Changed?.Invoke();
            PlotSelected?.Invoke(-1);
        }

        public void SelectPlot(int index)
        {
            if (!Ready) return;
            world.SetSelectedPlot(index);
            PlotSelected?.Invoke(index);
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
            if (State == null) return;
            try
            {
                GameSaveStore.Save(State);
                SaveError = null;
            }
            catch (Exception e)
            {
                SaveError = "تعذّر حفظ التقدم. تحقق من مساحة التخزين.";
                Debug.LogError("New Gaza save failed: " + e.Message);
                Notify(SaveError);
            }
        }

        private void OnApplicationPause(bool paused) { if (paused && Ready) Save(); }
        private void OnApplicationFocus(bool focused)
        {
            if (!Ready) return;
            if (focused) { Economy.Tick(Now); Changed?.Invoke(); }
            else Save();
        }
        private void OnApplicationQuit() { if (Ready) Save(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }

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