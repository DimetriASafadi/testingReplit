using UnityEngine;

namespace NewGaza
{
    /// <summary>A telescoping barrel, exposed rod and pinned eyes following two articulated anchors.</summary>
    internal sealed class EquipmentHydraulicLink
    {
        private readonly Transform parent;
        private readonly Transform barrel;
        private readonly Transform rod;
        private readonly Transform basePin;
        private readonly Transform rodPin;
        private readonly float diameter;
        private readonly float barrelFraction;

        internal EquipmentHydraulicLink(CityGeometry geometry, Transform parent, Material steel,
            string name, float diameter, float barrelFraction = .62f)
        {
            this.parent = parent;
            this.diameter = diameter;
            this.barrelFraction = Mathf.Clamp(barrelFraction, .35f, .82f);
            barrel = Part(geometry, parent, name + " cylinder barrel", geometry.Cylinder, steel,
                Vector3.zero, new Vector3(diameter, .1f, diameter));
            rod = Part(geometry, parent, name + " polished piston rod", geometry.Cylinder, steel,
                Vector3.zero, new Vector3(diameter * .44f, .1f, diameter * .44f));
            basePin = Part(geometry, parent, name + " base eye", geometry.Cylinder, steel,
                Vector3.zero, new Vector3(diameter * 1.3f, diameter * .42f, diameter * 1.3f));
            rodPin = Part(geometry, parent, name + " rod eye", geometry.Cylinder, steel,
                Vector3.zero, new Vector3(diameter * 1.15f, diameter * .38f, diameter * 1.15f));
        }

        internal void Follow(Transform baseAnchor, Transform movingAnchor)
        {
            Vector3 start = parent.InverseTransformPoint(baseAnchor.position);
            Vector3 end = parent.InverseTransformPoint(movingAnchor.position);
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length < .025f) return;
            Quaternion axis = Quaternion.FromToRotation(Vector3.up, direction / length);
            float barrelLength = Mathf.Max(.04f, length * barrelFraction);
            float rodLength = Mathf.Max(.025f, length - barrelLength);
            barrel.localPosition = start + direction.normalized * (barrelLength * .5f);
            barrel.localRotation = axis;
            barrel.localScale = new Vector3(diameter, barrelLength, diameter);
            rod.localPosition = start + direction.normalized * (barrelLength + rodLength * .5f);
            rod.localRotation = axis;
            rod.localScale = new Vector3(diameter * .44f, rodLength, diameter * .44f);
            basePin.localPosition = start;
            basePin.localRotation = axis * Quaternion.Euler(0f, 0f, 90f);
            rodPin.localPosition = end;
            rodPin.localRotation = axis * Quaternion.Euler(0f, 0f, 90f);
        }

        private static Transform Part(CityGeometry geometry, Transform parent, string name, Mesh mesh,
            Material material, Vector3 position, Vector3 scale)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localScale = scale;
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return root;
        }
    }
}