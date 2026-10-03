using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace NewGaza.Editor
{
    /// <summary>Compilation only: no scene changes, Play Mode, save access or setup menus.</summary>
    public static class NewGazaCompilationGate
    {
        [Serializable]
        private sealed class Result
        {
            public string nonce;
            public string unityVersion;
            public string runtimeAssembly;
            public string editorAssembly;
            public bool success;
        }

        // Unity executes this only after importing and compiling the actual project.
        public static void Run()
        {
            try
            {
                string expected = Argument("-newGazaRequiredVersion");
                if (Application.unityVersion != expected)
                    throw new InvalidOperationException("Required Unity " + expected +
                        ", running " + Application.unityVersion);
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Script compilation did not complete successfully.");

                string runtime = typeof(GameSession).Assembly.GetName().Name;
                string editor = typeof(NewGazaSmokeTest).Assembly.GetName().Name;
                if (runtime != "Assembly-CSharp" || editor != "Assembly-CSharp-Editor" ||
                    runtime == editor)
                    throw new InvalidOperationException("Runtime/Editor assembly boundary changed.");
                var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
                if (!assemblies.Any(a => a.name == runtime && File.Exists(a.outputPath)) ||
                    !assemblies.Any(a => a.name == editor && File.Exists(a.outputPath)))
                    throw new InvalidOperationException("Compiled runtime/editor assemblies are missing.");

                File.WriteAllText(Argument("-newGazaCompilationResult"), JsonUtility.ToJson(
                    new Result {
                        nonce = Argument("-newGazaCompilationNonce"),
                        unityVersion = Application.unityVersion,
                        runtimeAssembly = runtime,
                        editorAssembly = editor,
                        success = true
                    }, true));
                Debug.Log("NEW_GAZA_COMPILATION_PASS: actual Unity runtime and editor assemblies loaded.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
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