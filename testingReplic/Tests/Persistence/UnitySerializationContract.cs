using System.Text.Json;
using System.Text.Json.Serialization;

// Filesystem/domain fixture only. This is NOT Unity's JsonUtility, nor engine evidence.
namespace UnityEngine
{
    internal static class Application
    {
        public static string persistentDataPath;
    }

    internal static class JsonUtility
    {
        // Explicitly model a compatibility hazard of Unity's inline serialized
        // classes. This switch is a fixture, not a claim to emulate the engine.
        internal static bool MaterializeAbsentCamera;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string value)
        {
            var result = JsonSerializer.Deserialize<T>(value, Options);
            if (MaterializeAbsentCamera && result is NewGaza.Core.GameState state && state.camera == null)
                state.camera = new NewGaza.Core.CameraSaveState();
            return result;
        }
    }
}