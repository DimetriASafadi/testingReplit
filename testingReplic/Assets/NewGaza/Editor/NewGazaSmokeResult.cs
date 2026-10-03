using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewGaza.Editor
{
    /// <summary>Fresh batch evidence, separate from the compilation gate.</summary>
    internal static class NewGazaSmokeResult
    {
        [Serializable]
        private sealed class Result
        {
            public bool success;
            public bool unavailable;
            public string message;
            public string nonce;
            public string unityVersion;
            public bool playMode;
            public string graphicsDeviceType;
            public string graphicsDeviceName;
            public string savePath;
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        internal static void ValidateBatch()
        {
            if (Argument("-newGazaSmokeVersion") != Application.unityVersion)
                throw new InvalidOperationException("Smoke runner requires the exact project Unity version.");
            if (string.IsNullOrEmpty(Argument("-newGazaSmokeNonce")) ||
                string.IsNullOrEmpty(Argument("-newGazaSmokeResult")))
                throw new InvalidOperationException("Use tools/unity_smoke.py to retain fresh batch evidence.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                string.IsNullOrEmpty(SystemInfo.graphicsDeviceName))
                throw new InvalidOperationException("Graphics unavailable; real Play Mode smoke test NOT RUN.");
        }

        internal static void Write(bool success, string message)
        {
            string path = Argument("-newGazaSmokeResult");
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Missing smoke result path.");
            var result = new Result
            {
                success = success, message = message,
                unavailable = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                    string.IsNullOrEmpty(SystemInfo.graphicsDeviceName),
                nonce = Argument("-newGazaSmokeNonce"),
                unityVersion = Application.unityVersion,
                playMode = Application.isPlaying,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                savePath = GameSaveStore.SavePath
            };
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
        }
    }
}