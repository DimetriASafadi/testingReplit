using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Opt-in player build. Never runs setup, changes signing, or enters Play Mode.</summary>
    public static class NewGazaPlayerBuildGate
    {
        [Serializable]
        private sealed class Result
        {
            public bool success;
            public string status = "error";
            public string message;
            public string nonce;
            public string unityVersion;
            public string runtimeAssembly;
            public string editorAssembly;
            public string target;
            public string scene;
            public string scope;
            public string buildPath;
            public string buildResult;
            public int totalErrors;
            public int totalWarnings;
            public string[] buildErrors;
        }

        public static void Run()
        {
            var result = new Result();
            string resultPath = null;
            try
            {
                resultPath = Argument("-newGazaCompilationResult");
                result.nonce = Argument("-newGazaCompilationNonce");
                result.unityVersion = Application.unityVersion;
                if (result.unityVersion != Argument("-newGazaRequiredVersion"))
                    throw new InvalidOperationException("Exact project Unity version required.");
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Editor compilation incomplete or failed.");
                result.runtimeAssembly = typeof(GameSession).Assembly.GetName().Name;
                result.editorAssembly = typeof(NewGazaSmokeTest).Assembly.GetName().Name;
                var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                if (result.runtimeAssembly != "Assembly-CSharp" ||
                    result.editorAssembly != "Assembly-CSharp-Editor" ||
                    !assemblies.Any(a => a.name == result.runtimeAssembly && File.Exists(a.outputPath)) ||
                    !assemblies.Any(a => a.name == result.editorAssembly && File.Exists(a.outputPath)))
                    throw new InvalidOperationException("Compiled runtime/editor assembly boundary changed.");

                result.target = Argument("-newGazaPlayerTarget");
                BuildTarget target;
                BuildTargetGroup group;
                if (result.target == "Android")
                {
                    target = BuildTarget.Android;
                    group = BuildTargetGroup.Android;
                    result.scope = "android-player";
                }
                else if (result.target == "iOS")
                {
                    target = BuildTarget.iOS;
                    group = BuildTargetGroup.iOS;
                    result.scope = "xcode-export";
                    if (Application.platform != RuntimePlatform.OSXEditor)
                        Missing(result, "iOS export requires macOS. Xcode/signing is not checked here.");
                }
                else throw new ArgumentException("Only explicitly approved Android or iOS targets are allowed.");
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                    Missing(result, "Required " + result.target + " Build Support module is missing.");
                if (EditorUserBuildSettings.activeBuildTarget != target)
                    Missing(result, "Editor did not activate requested target. Check installed Build Support.");
                if (target == BuildTarget.Android && EditorUserBuildSettings.exportAsGoogleAndroidProject)
                    Missing(result, "Android Export Project is enabled. Disable it manually to check an actual APK/AAB build.");

                result.scene = Argument("-newGazaPlayerScene");
                if (!result.scene.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !result.scene.EndsWith(".unity", StringComparison.Ordinal) ||
                    result.scene.Split('/').Contains("..") ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(result.scene) == null)
                    Missing(result, "Build scene missing/invalid. Run approved setup manually; this gate never creates it.");
                string directory = Argument("-newGazaPlayerDirectory");
                // The Python runner provides a unique, external directory for each invocation.
                if (Directory.Exists(directory) || File.Exists(directory))
                    throw new InvalidOperationException("Build output already exists; refusing to overwrite it.");
                Directory.CreateDirectory(directory);
                result.buildPath = target == BuildTarget.iOS ? Path.Combine(directory, "Xcode") :
                    Path.Combine(directory, EditorUserBuildSettings.buildAppBundle ? "NewGaza.aab" : "NewGaza.apk");
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { result.scene },
                    locationPathName = result.buildPath,
                    target = target,
                    options = BuildOptions.None
                });
                if (build == null)
                    throw new InvalidOperationException("Unity returned no BuildReport.");
                result.buildResult = build.summary.result.ToString();
                result.totalErrors = build.summary.totalErrors;
                result.totalWarnings = build.summary.totalWarnings;
                result.buildErrors = build.steps.SelectMany(step => step.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content).ToArray();
                if (build.summary.result != BuildResult.Succeeded || result.totalErrors != 0)
                    throw new InvalidOperationException("Player build " + result.buildResult +
                        ". See retained compiler log/buildErrors for SDK/NDK/JDK, IL2CPP, signing or compiler failures.");
                result.success = true;
                result.status = "passed";
                result.message = target == BuildTarget.iOS ?
                    "Xcode export only; native Xcode compilation, provisioning and signing NOT verified." :
                    "Android player built with existing project settings; not installed or store-validated.";
                Debug.Log("NEW_GAZA_PLAYER_BUILD_PASS: " + result.target + " " + result.message);
            }
            catch (Exception exception)
            {
                result.message = exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                if (resultPath != null)
                    File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
                EditorApplication.Exit(result.success ? 0 : 1);
            }
        }

        private static void Missing(Result result, string message)
        {
            result.status = "prerequisite-missing";
            throw new InvalidOperationException(message);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing batch argument " + name);
            return args[index + 1];
        }
    }
}