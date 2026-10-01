using System;
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
                Require(UnityEngine.Object.FindFirstObjectByType<CityWorld>() != null, "Missing city world.");
                Require(UnityEngine.Object.FindFirstObjectByType<CityHud>() != null, "Missing HUD.");
                Require(UnityEngine.Object.FindObjectsByType<CitySelectable>(FindObjectsSortMode.None).Length >= 11,
                    "Missing selectable city districts/plots.");
                Require(UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                    "Missing UI EventSystem.");
                Require(Resources.Load<Font>("NewGazaArabic") != null, "Arabic font did not import.");
                Require(Resources.Load<Material>("NewGazaLit") != null, "URP material did not import.");
                Require(string.IsNullOrEmpty(session.SaveError), "Save could not be written.");
                Require(session.State.districts.Length == 11, "Expected ten neighborhoods and final Rashid district.");
                Finish(true, "Startup, camera, city selection, HUD, font, material and local save passed in Unity Play Mode.");
            }
            catch (Exception e) { Finish(false, e.Message); }
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