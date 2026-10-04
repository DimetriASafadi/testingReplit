using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NewGaza.Editor
{
    public static class NewGazaProjectSetup
    {
        public const string ScenePath = "Assets/NewGaza/Scenes/NewGaza.unity";

        [MenuItem("New Gaza/Open game scene", priority = 0)]
        public static void OpenGame()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("New Gaza/Apply mobile settings", priority = 1)]
        public static void ConfigureMobile()
        {
            PlayerSettings.productName = "New Gaza";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            int mobileQuality = System.Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobileQuality >= 0) QualitySettings.SetQualityLevel(mobileQuality, true);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (pipeline != null)
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
            }
            var serialized = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = serialized.FindProperty("activeInputHandler");
            bool restartNeeded = input != null && input.intValue != 1;
            if (input != null)
            {
                input.intValue = 1;
                serialized.ApplyModifiedProperties();
            }
            // Only the game scene is in mobile builds; the original racing scene stays on disk.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("New Gaza",
                "Mobile settings applied. Set your own bundle identifier and signing credentials before publishing."
                + (restartNeeded ? "\nRestart Unity to finish enabling the Input System." : ""), "OK");
        }

        [MenuItem("New Gaza/Validate project assets", priority = 2)]
        public static void Validate()
        {
            bool valid = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                && Resources.Load<Font>("NewGazaArabic") != null
                && Resources.Load<Material>("NewGazaLit") != null;
            Debug.Log(valid
                ? "New Gaza: scene, Arabic font, and retained URP material are present. Next: run Play Mode tests and device testing."
                : "New Gaza: missing scene, font, or URP material. Import the entire project folder.");
            if (!valid) EditorUtility.DisplayDialog("New Gaza", "Required assets are missing. Check the Console.", "OK");
        }

        [MenuItem("New Gaza/Open save folder", priority = 3)]
        public static void OpenSaveFolder() { EditorUtility.RevealInFinder(Application.persistentDataPath); }

        [MenuItem("New Gaza/Reset game (file save and PlayerPrefs)", priority = 6)]
        public static void ResetGame()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("New Gaza", "أوقف Play أولاً حتى لا تعيد اللعبة كتابة الحفظ بعد مسحه.", "حسنًا");
                return;
            }
            if (!EditorUtility.DisplayDialog("إعادة اللعبة من البداية",
                "سيُمسح التقدم والمباني والمعدات والعملات المحفوظة، وتبدأ بمليون عملة وخريطة مدمرة. سيتم أرشفة ملفات الحفظ أولاً. ClearPlayerPrefs وحده لا يمسح هذه الملفات. هل تريد المتابعة؟",
                "أرشفة الحفظ وإعادة البداية", "إلغاء")) return;
            try
            {
                string archive = GameSaveStore.ArchiveAndClear();
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                EditorUtility.DisplayDialog("New Gaza", "تمت إعادة الضبط. اضغط Play لتبدأ بمليون عملة. النسخة السابقة محفوظة في:\n" + archive, "حسنًا");
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("New Gaza", "لم تكتمل إعادة الضبط:\n" + error.Message, "حسنًا");
            }
        }
    }
}