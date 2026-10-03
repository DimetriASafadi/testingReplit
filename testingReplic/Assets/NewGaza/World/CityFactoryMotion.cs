using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Shared low-poly rollers/cleats. Idle recyclers have no mechanical motion.</summary>
    public sealed class CityFactoryMotion : MonoBehaviour
    {
        private GameState state;
        private string depotId;
        private bool producer;
        private readonly Transform[] rollers = new Transform[2];
        private readonly Transform[] cleats = new Transform[4];
        private Renderer visibility;
        private float distance, angle;

        internal static CityFactoryMotion Create(Transform parent, CityGeometry geometry, GameState state,
            Vector3 position, float scale, string depotId, bool producer)
        {
            var root = new GameObject("Working factory conveyor");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localScale = Vector3.one * scale;
            var motion = root.AddComponent<CityFactoryMotion>();
            motion.state = state; motion.depotId = depotId; motion.producer = producer;
            var steel = geometry.Material("factory moving steel", new Color(.32f, .38f, .42f));
            var gold = geometry.Material("factory moving safety", new Color(.93f, .68f, .15f));
            for (int i = 0; i < motion.rollers.Length; i++)
            {
                var part = new GameObject("Conveyor roller " + i);
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = new Vector3(0, .08f, -1.2f + i * 2.4f);
                Part("Roller drum", part.transform, geometry.Cylinder, steel, Vector3.zero, new Vector3(.25f, .6f, .25f));
                Part("Roller safety mark", part.transform, geometry.Box, gold,
                    new Vector3(.125f, 0, 0), new Vector3(.02f, .5f, .06f));
                motion.rollers[i] = part.transform;
                part.transform.localRotation = Quaternion.Euler(0, 0, 90);
                if (i == 0) motion.visibility = part.GetComponentInChildren<Renderer>();
            }
            for (int i = 0; i < motion.cleats.Length; i++)
            {
                motion.cleats[i] = Part("Conveyor cleat " + i, root.transform, geometry.Box, gold,
                    new Vector3(0, .21f, -1.6f + i * .8f), new Vector3(.55f, .035f, .045f));
            }
            return motion;
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return part.transform;
        }

        private void Update()
        {
            if (state == null || (visibility != null && !visibility.isVisible)) return;
            bool active = producer || (state.jobStage == JobStage.Recycling &&
                (state.development == null || !state.development.initialized ||
                    state.development.dispatchDepotId == depotId ||
                    (depotId == "central" && state.development.dispatchDepotId == null)));
            if (!active) return;
            float delta = Mathf.Min(Time.deltaTime, .05f);
            distance = Mathf.Repeat(distance + delta * .24f, 3.2f);
            // Surface speed equals roller radius * angular speed, not an arbitrary spin.
            angle = Mathf.Repeat(angle + delta * (.24f / .125f) * Mathf.Rad2Deg, 360);
            for (int i = 0; i < rollers.Length; i++)
                rollers[i].localRotation = Quaternion.Euler(angle, 0, 0) * Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < cleats.Length; i++)
                cleats[i].localPosition = new Vector3(0, .21f, Mathf.Repeat(i * .8f + distance, 3.2f) - 1.6f);
        }
    }
}