using System;
using System.Collections.Generic;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    internal sealed class CitySampleBuildings : IDisposable
    {
        private sealed class View
        {
            internal GameObject root, visual;
            internal CityConstructionCrew crew;
            internal int phase = -1;
            internal bool generated;
        }
        private readonly Dictionary<string, View> views = new Dictionary<string, View>();
        private readonly CityGeometry geometry;
        private readonly Transform parent;
        private readonly Material stone, roof, glass, grass, soil, steel, yellow, white;

        internal CitySampleBuildings(CityGeometry geometry, Transform parent)
        {
            this.geometry = geometry; this.parent = parent;
            stone = geometry.Material("sample building stone", new Color(.81f, .73f, .58f));
            roof = geometry.Material("sample terracotta roofs", new Color(.62f, .32f, .23f));
            glass = geometry.Material("sample blue windows", new Color(.24f, .48f, .58f));
            grass = geometry.Material("sample farmland", new Color(.36f, .53f, .24f));
            soil = geometry.Material("sample earth rows", new Color(.41f, .30f, .19f));
            steel = geometry.Material("sample industrial steel", new Color(.34f, .40f, .43f));
            yellow = geometry.Material("sample work safety", new Color(.91f, .66f, .13f));
            white = geometry.Material("sample plaster", new Color(.9f, .88f, .8f));
        }

        internal void Refresh(PlacedBuildingState[] buildings, long now, GameState state)
        {
            foreach (var building in buildings)
            {
                var definition = CityBuildingCatalog.Find(building.definitionId);
                if (!views.TryGetValue(building.id, out var view))
                {
                    var root = new GameObject("Placed • " + definition.name);
                    root.transform.SetParent(parent, false);
                    root.transform.position = new Vector3(building.x, 0, building.z);
                    root.transform.rotation = Quaternion.Euler(0, building.quarterTurn * 90, 0);
                    var hit = root.AddComponent<BoxCollider>();
                    hit.center = new Vector3(0, .06f, 0);
                    hit.size = new Vector3(definition.widthMeters / 20f, .12f, definition.depthMeters / 20f);
                    view = new View { root = root }; views.Add(building.id, view);
                }
                float progress = Mathf.Clamp01((float)(now - building.startedUtc) / definition.duration);
                int phase = building.completed ? 4 : progress < .25f ? 1 : progress < .7f ? 2 : 3;
                if (phase != view.phase)
                {
                    if (view.visual != null) Release(view.visual, view.generated);
                    view.phase = phase;
                    GameObject prefab = phase == 4 ? Resources.Load<GameObject>("NewGazaBuildings/" + definition.id) : null;
                    view.generated = prefab == null;
                    view.visual = prefab != null ? UnityEngine.Object.Instantiate(prefab, view.root.transform) :
                        MakeSample(definition, phase, view.root.transform);
                    if (prefab != null) FitPrefab(view.visual, definition);
                    var hit = view.root.GetComponent<BoxCollider>();
                    hit.center = new Vector3(0, phase == 1 ? .02f : definition.floors * .075f, 0);
                    hit.size = new Vector3(definition.widthMeters / 20f,
                        phase == 1 ? .04f : Mathf.Max(.12f, definition.floors * .15f), definition.depthMeters / 20f);
                }
                if (!building.completed)
                {
                    if (view.crew == null)
                        view.crew = new CityConstructionCrew(view.root.transform,
                            new Vector3(definition.widthMeters / 20f, 0, definition.depthMeters / 20f),
                            geometry, white, yellow, grass, yellow, steel);
                    view.crew.SetPhase(phase == 1 ? CityConstructionPhase.Foundation :
                        phase == 2 ? CityConstructionPhase.Frame : CityConstructionPhase.Finishing,
                        state.districts[building.district].unlocked);
                }
                else view.crew?.SetPhase(CityConstructionPhase.Complete, false);
            }
        }

        private GameObject MakeSample(CityBuildingDefinition definition, int phase, Transform root)
        {
            var batch = new CityMeshBatch(geometry);
            float width = definition.widthMeters / 20f, depth = definition.depthMeters / 20f;
            float height = definition.floors * .15f;
            batch.Box(phase == 1 ? soil : stone, new Vector3(0, .008f, 0), new Vector3(width, .016f, depth));
            if (phase == 1)
            {
                batch.Box(stone, new Vector3(0, .022f, 0), new Vector3(width * .85f, .026f, depth * .85f));
            }
            else if (phase == 2)
            {
                for (int floor = 0; floor <= definition.floors; floor++)
                    batch.Box(stone, new Vector3(0, .02f + floor * .15f, 0), new Vector3(width * .82f, .016f, depth * .8f));
                for (int corner = 0; corner < 4; corner++)
                    batch.Box(steel, new Vector3((corner % 2 == 0 ? -1 : 1) * width * .36f, height * .5f,
                        (corner < 2 ? -1 : 1) * depth * .36f), new Vector3(.018f, height, .018f));
            }
            else if (definition.category == BuildingCategory.Agricultural)
            {
                batch.Box(grass, new Vector3(0, .017f, 0), new Vector3(width * .96f, .02f, depth * .96f));
                bool trees = definition.id == "citrus" || definition.id == "olive" || definition.id == "palms" ||
                    definition.id == "protective_trees" || definition.id == "ornamental_trees";
                for (int row = 0; row < 5; row++)
                {
                    float z = (row - 2) * depth / 6f;
                    if (!trees) batch.Box(soil, new Vector3(0, .032f, z), new Vector3(width * .9f, .02f, depth / 12f));
                    for (int column = 0; column < 4; column++)
                    {
                        var position = new Vector3((column - 1.5f) * width / 5f, .06f, z);
                        if (trees)
                        {
                            batch.Box(roof, position + Vector3.up * .03f, new Vector3(.025f, .12f, .025f));
                            batch.Round(grass, position + Vector3.up * .13f, new Vector3(.12f, .16f, .12f));
                        }
                        else batch.Box(definition.id == "wheat" || definition.id == "corn" ? yellow : grass,
                            position, new Vector3(.025f, .055f, .025f));
                    }
                }
                if (definition.id == "cattle")
                {
                    batch.Box(white, new Vector3(0, .1f, 0), new Vector3(width * .35f, .18f, depth * .4f));
                    batch.Box(roof, new Vector3(0, .2f, 0), new Vector3(width * .4f, .04f, depth * .46f));
                    for (int cow = 0; cow < 4; cow++)
                    {
                        Vector3 point = new Vector3(width * .25f, .07f, (cow - 1.5f) * depth / 6f);
                        batch.Box(white, point, new Vector3(.065f, .04f, .035f));
                        batch.Box(steel, point + new Vector3(.035f, .01f, 0), new Vector3(.025f, .03f, .03f));
                    }
                }
            }
            else if (definition.category == BuildingCategory.Economic || definition.category == BuildingCategory.Recycling ||
                definition.category == BuildingCategory.Equipment)
            {
                batch.Box(white, new Vector3(-width * .15f, height * .45f, 0), new Vector3(width * .6f, height * .9f, depth * .75f));
                batch.Box(steel, new Vector3(-width * .15f, height * .94f, 0), new Vector3(width * .65f, .035f, depth * .8f));
                batch.Box(glass, new Vector3(-width * .15f, .055f, depth * .378f), new Vector3(width * .32f, .1f, .012f));
                if (definition.category != BuildingCategory.Equipment)
                {
                    batch.Round(steel, new Vector3(width * .3f, .14f, 0), new Vector3(width * .2f, .28f, depth * .2f));
                    batch.Box(roof, new Vector3(width * .32f, .27f, -depth * .24f), new Vector3(.055f, .54f, .055f));
                }
                else
                    for (int bay = 0; bay < 3; bay++)
                        batch.Box(yellow, new Vector3(-width * .3f + bay * width * .15f, .018f, depth * .4f),
                            new Vector3(width * .1f, .014f, depth * .18f));
            }
            else if (definition.id == "zoo")
            {
                batch.Box(grass, new Vector3(0, .025f, 0), new Vector3(width * .93f, .03f, depth * .93f));
                for (int enclosure = 0; enclosure < 4; enclosure++)
                {
                    float x = (enclosure % 2 == 0 ? -1 : 1) * width * .24f;
                    float z = (enclosure < 2 ? -1 : 1) * depth * .24f;
                    batch.Box(soil, new Vector3(x, .05f, z), new Vector3(width * .35f, .04f, depth * .35f));
                    batch.Round(grass, new Vector3(x, .15f, z), new Vector3(.2f, .25f, .2f));
                    batch.Box(white, new Vector3(x + .1f, .08f, z + .1f), new Vector3(.08f, .07f, .04f));
                }
            }
            else
            {
                batch.Box(phase == 3 ? stone : white, new Vector3(0, height * .5f + .02f, 0), new Vector3(width * .8f, height, depth * .78f));
                for (int floor = 0; floor < definition.floors; floor++)
                {
                    batch.Box(roof, new Vector3(0, .02f + (floor + 1) * .15f, 0), new Vector3(width * .84f, .015f, depth * .82f));
                    for (int window = 0; window < 3; window++)
                    {
                        float x = (window - 1) * width * .22f;
                        batch.Box(glass, new Vector3(x, .085f + floor * .15f, depth * .395f), new Vector3(width * .13f, .065f, .012f));
                        batch.Box(glass, new Vector3(x, .085f + floor * .15f, -depth * .395f), new Vector3(width * .13f, .065f, .012f));
                    }
                }
                batch.Box(steel, new Vector3(0, .06f, depth * .4f), new Vector3(width * .12f, .09f, .015f));
                if (definition.category == BuildingCategory.Commercial || definition.category == BuildingCategory.Recreation)
                    batch.Box(yellow, new Vector3(0, .14f, depth * .46f), new Vector3(width * .75f, .035f, depth * .18f));
                if (definition.id == "housing_complex")
                    batch.Box(stone, new Vector3(width * .3f, height * .65f, depth * .3f), new Vector3(width * .25f, height * 1.3f, depth * .35f));
                if (definition.id == "tourist_villa" || definition.id == "luxury_restaurant")
                    batch.Box(glass, new Vector3(width * .32f, .025f, 0), new Vector3(width * .18f, .02f, depth * .58f));
            }
            return batch.Build("Editable sample • " + definition.id + " • phase " + phase, root, Vector3.zero, true);
        }

        private static void FitPrefab(GameObject root, CityBuildingDefinition definition)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Building prefab has no renderer: " + definition.id);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = Mathf.Min(definition.widthMeters / 20f / Mathf.Max(.001f, bounds.size.x),
                definition.depthMeters / 20f / Mathf.Max(.001f, bounds.size.z));
            root.transform.localScale *= scale;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            root.transform.position += new Vector3(root.transform.parent.position.x - bounds.center.x,
                -bounds.min.y, root.transform.parent.position.z - bounds.center.z);
        }

        internal GameObject Preview(CityBuildingDefinition definition, Vector3 position, int turn, bool valid)
        {
            var batch = new CityMeshBatch(geometry);
            var material = valid ? grass : roof;
            float width = definition.widthMeters / 20f, depth = definition.depthMeters / 20f;
            batch.Box(material, new Vector3(0, .018f, 0), new Vector3(width, .018f, depth));
            batch.Box(material, new Vector3(0, .13f, -depth * .5f), new Vector3(width, .24f, .02f));
            var result = batch.Build(valid ? "Valid build footprint" : "Blocked build footprint", parent, position);
            result.transform.rotation = Quaternion.Euler(0, turn * 90, 0);
            return result;
        }

        internal bool OwnsHit(string id, Transform hit)
        {
            return views.TryGetValue(id, out var view) && (hit == view.root.transform || hit.IsChildOf(view.root.transform));
        }

        internal void Animate()
        {
            foreach (var view in views.Values) view.crew?.Update(Time.deltaTime);
        }
        internal void ReleasePreview(GameObject root) => Release(root, true);

        private void Release(GameObject root, bool generated)
        {
            root.SetActive(false);
            if (generated)
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null) geometry.Release(filter.sharedMesh);
            UnityEngine.Object.Destroy(root);
        }

        public void Dispose()
        {
            foreach (var view in views.Values)
            {
                view.crew?.Dispose();
                if (view.visual != null) Release(view.visual, view.generated);
                if (view.root != null) UnityEngine.Object.Destroy(view.root);
            }
            views.Clear();
        }
    }
}