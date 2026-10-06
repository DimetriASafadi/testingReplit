#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NewGaza.Editor
{
    internal static class ExcavatorAppearanceMenu
    {
        private const string Folder = "Assets/NewGaza/Resources/Equipment";

        [MenuItem("New Gaza/Equipment/Use original excavator")]
        private static void Original() { Select("original"); }

        [MenuItem("New Gaza/Equipment/Use Meshy excavator")]
        private static void Meshy()
        {
            if (!File.Exists(Folder + "/MeshyExcavator.bytes"))
                throw new FileNotFoundException("Install the Meshy excavator art package first.",
                    Folder + "/MeshyExcavator.bytes");
            Select("meshy");
        }

        private static void Select(string model)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/ExcavatorAppearance.json",
                "{\n  \"model\": \"" + model + "\"\n}\n");
            AssetDatabase.Refresh();
            Debug.Log("Excavator appearance set to " + model + ". Restart Play mode to apply. " +
                "Original model and gameplay rig have not been deleted.");
        }
    }
}
#endif
