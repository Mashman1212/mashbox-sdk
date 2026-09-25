using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MashBoxSDK.MapTools;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using MashBoxSDK.Maps.TerrainSystem.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Roads.Editor
{
    [InitializeOnLoad]
    public static class MGRoadLayerService
    {
        static readonly Dictionary<MGRoad, string> fingerprints = new Dictionary<MGRoad, string>();
        static readonly HashSet<MGTerrain> sculpted = new HashSet<MGTerrain>();
        public static double LastApplyMilliseconds { get; private set; }

        static readonly Unity.Profiling.ProfilerMarker sampleMarker = new Unity.Profiling.ProfilerMarker("MGRoadLayer.Sample");
        static readonly Unity.Profiling.ProfilerMarker evaluateMarker = new Unity.Profiling.ProfilerMarker("MGRoadLayer.Evaluate");
        static readonly Unity.Profiling.ProfilerMarker composeMarker = new Unity.Profiling.ProfilerMarker("MGRoadLayer.Compose");
        static bool busy;
        static double nextUpdate;
        static MGRoadLayerService()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += Restored;
            EditorSceneManager.sceneSaving += Saving;
            MeshSculptModifier.PrepareTerrainEdit += modifier =>
            {
                if (!busy && modifier.Target != null)
                {
                    var tile = modifier.Target.GetComponentInParent<MGTerrain>();
                    if (tile != null && tile.GetComponent<MGRoadTerrainLayers>() != null) sculpted.Add(tile);
                }
            };
        }
        static bool Editable(Component c) => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded
            && !EditorSceneManager.IsPreviewScene(c.gameObject.scene) && !EditorUtility.IsPersistent(c)
            && PrefabStageUtility.GetPrefabStage(c.gameObject) == null;
        static MGRoadTerrainLayers[] Stores() => Resources.FindObjectsOfTypeAll<MGRoadTerrainLayers>()
            .Where(s => s != null && s.gameObject.scene.IsValid() && s.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(s)).ToArray();
        static bool Active(MGRoad r) => r != null && r.isActiveAndEnabled && r.terrainLayerEnabled
            && r.TerrainSettings.mode == RoadTerrainMode.TerrainFollowsRoad && r.Network != null && r.Network.isActiveAndEnabled && r.Network.terrainWorld != null && r.Network.terrainWorld.isActiveAndEnabled;

        internal static string Fingerprint(MGRoad road)
        {
            var b = new StringBuilder();
            b.Append(road.transform.localToWorldMatrix.ToString("R"));
            b.Append(JsonUtility.ToJson(road.TerrainSettings));
            foreach (float v in new[] { road.width, road.shoulderWidth, road.shoulderDrop, road.crown, road.bankAngle, road.sampleSpacing }) b.Append('|').Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            b.Append(road.terrainLayerEnabled).Append(road.isActiveAndEnabled).Append(Order(road));
            if (road.Network != null) b.Append(road.Network.isActiveAndEnabled);
            var spline = road.Container.Spline; b.Append(spline.Closed);
            for (int i = 0; i < spline.Count; i++)
            {
                var k = spline[i]; b.Append(k.Position).Append(k.Rotation).Append(k.TangentIn).Append(k.TangentOut).Append(MGRoadKnotShape.GetScale(spline, i).ToString("R"));
            }
            b.Append(road.Network != null && road.Network.terrainWorld != null ? road.Network.terrainWorld.GetInstanceID() : 0);
            return Hash128.Compute(b.ToString()).ToString();
        }
        static string Order(MGRoad road)
        {
            string key = "";
            for (var t = road.transform; t != null; t = t.parent) key = t.GetSiblingIndex().ToString("D8") + "/" + key;
            return road.gameObject.scene.path + "/" + key;
        }
        static int Topology(Mesh mesh)
        {
            unchecked { int hash = mesh.vertexCount; foreach (int i in mesh.triangles) hash = hash * 31 + i; return hash; }
        }
        static void Validate(MGRoadTerrainLayers store)
        {
            var mesh = store.terrain != null && store.terrain.MeshFilter != null ? store.terrain.MeshFilter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || store.baseline == null || mesh.vertexCount != store.baseline.Length || Topology(mesh) != store.topology)
                throw new InvalidOperationException("Terrain topology changed under road layers on " + store.name + ". Undo the topology edit, then bake or remove road layers before changing resolution. Mesh=" + (mesh != null ? mesh.vertexCount : -1) + " base=" + (store.baseline != null ? store.baseline.Length : -1) + " stored=" + store.topology + " actual=" + (mesh != null ? Topology(mesh) : 0));
        }
        static MGRoadTerrainLayers Prepare(MGTerrain tile)
        {
            var store = tile.GetComponent<MGRoadTerrainLayers>();
            if (store != null) { Validate(store); return store; }
            var filter = tile.MeshFilter;
            var source = filter.sharedMesh;
            if (!source.isReadable) throw new InvalidOperationException("Terrain mesh must be readable: " + tile.name);
            Undo.RegisterCompleteObjectUndo(filter, "Create Road Terrain Layers");
            Undo.RegisterCompleteObjectUndo(tile, "Create Road Terrain Layers");
            foreach (var collider in tile.GetComponentsInChildren<MeshCollider>(true)) Undo.RegisterCompleteObjectUndo(collider, "Create Road Terrain Layers");
            var output = Object.Instantiate(source); output.hideFlags = HideFlags.None;
            output.name = tile.name + " Road Terrain";
            // Live editing stays in memory; Save/Bake persists the mesh.

            Undo.RegisterCreatedObjectUndo(output, "Create Road Terrain Mesh");
            filter.sharedMesh = output;
            using (var so = new SerializedObject(tile))
            {
                so.FindProperty("m_EditableSculptMesh").objectReferenceValue = output;
                so.ApplyModifiedProperties();
            }
            store = Undo.AddComponent<MGRoadTerrainLayers>(tile.gameObject);
            store.terrain = tile; store.output = output;
            store.baseline = source.vertices; store.topology = Topology(source);
            return store;
        }
        static void CaptureEdits(MGRoadTerrainLayers store)
        {
            Validate(store);
            Undo.RegisterCompleteObjectUndo(store, "Update Road Terrain Layer");
            var filter = store.terrain.MeshFilter;
            // Duplicating a tile or scene must never make one layer write into another tile's mesh.
            if (Resources.FindObjectsOfTypeAll<MGTerrain>().Any(t => t != store.terrain && t.MeshFilter != null && t.MeshFilter.sharedMesh == filter.sharedMesh))
            {
                Undo.RegisterCompleteObjectUndo(filter, "Isolate Road Terrain Mesh");
                Undo.RegisterCompleteObjectUndo(store.terrain, "Isolate Road Terrain Mesh");
                var copy = Object.Instantiate(filter.sharedMesh); copy.hideFlags = HideFlags.None;

                Undo.RegisterCreatedObjectUndo(copy, "Isolate Road Terrain Mesh");
                filter.sharedMesh = copy;
                using (var so = new SerializedObject(store.terrain)) { so.FindProperty("m_EditableSculptMesh").objectReferenceValue = copy; so.ApplyModifiedProperties(); }
            }
            var current = filter.sharedMesh.vertices;
            // Keep direct sculpt edits as changes to the underlying terrain, not road history.
            var expected = Evaluate(store);
            for (int i = 0; i < current.Length; i++) store.baseline[i] += current[i] - expected[i];
        }
        // Commit one-shot terrain edits in their own Undo group, before an editor tick
        // can recomposite them separately. Existing road footprints remain authoritative.
        internal static void CompleteTerrainEdit(MGTerrain tile)
        {
            var store = tile.GetComponent<MGRoadTerrainLayers>();
            if (store == null || busy) return;
            busy = true;
            try
            {
                CaptureEdits(store);
                store.layers.RemoveAll(layer => !Active(layer.road));
                Compose(store);
                sculpted.Remove(tile);
            }
            finally { busy = false; }
        }
        static Vector3[] Evaluate(MGRoadTerrainLayers store)
        {
            using var profile = evaluateMarker.Auto();
            var filter = store.terrain.MeshFilter;
            var vertices = (Vector3[])store.baseline.Clone();
            var toWorld = filter.transform.localToWorldMatrix;
            var toLocal = filter.transform.worldToLocalMatrix;
            foreach (var layer in store.layers.OrderBy(l => l.order, StringComparer.Ordinal))
            {
                var before = layer.smoothingPasses > 0 ? (Vector3[])vertices.Clone() : null;
                foreach (var sample in layer.samples)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(vertices[sample.index]);
                    float height = sample.height;
                    if (layer.heightMode == RoadHeightMode.RaiseOnly) height = Mathf.Max(p.y, height);
                    if (layer.heightMode == RoadHeightMode.LowerOnly) height = Mathf.Min(p.y, height);
                    p.y = Mathf.Lerp(p.y, height, sample.weight);
                    vertices[sample.index] = toLocal.MultiplyPoint3x4(p);
                }
                if (before != null) MGRoadFalloffSmoothing.Apply(filter.sharedMesh, filter.transform, before, vertices, layer, store.topology);
            }
            return vertices;
        }
        static void Compose(MGRoadTerrainLayers store)
        {
            using var profile = composeMarker.Auto();
            var filter = store.terrain.MeshFilter;
            var mesh = filter.sharedMesh;
            Undo.RegisterCompleteObjectUndo(mesh, "Update Road Terrain Layer");
            Undo.RegisterCompleteObjectUndo(store.terrain, "Update Road Terrain Instances");
            foreach (var collider in store.terrain.GetComponentsInChildren<MeshCollider>(true)) Undo.RegisterCompleteObjectUndo(collider, "Update Road Terrain Collider");
            var vertices = Evaluate(store);
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
            store.output = mesh;
            store.terrain.NotifySurfaceMeshChanged();
            RefreshCollision(store);
            store.terrain.ConformInstancesToSurface();
            store.terrain.RefreshSurfaceTiles();
            EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(filter); EditorUtility.SetDirty(store); EditorUtility.SetDirty(store.terrain);
            EditorSceneManager.MarkSceneDirty(store.gameObject.scene);
            foreach (var road in Object.FindObjectsByType<MGRoad>(FindObjectsSortMode.None))
                if (road.TerrainSettings.mode == RoadTerrainMode.RoadFollowsTerrain) road.RequestRebuild();
        }
        static void RefreshCollision(MGRoadTerrainLayers store)
        {
            var tile = store.terrain;
            tile.RefreshSurfaceCollidersFromMesh();
        }
        static MGRoadTerrainLayers.Layer Sample(MGRoad road, MGTerrain tile, LoftSurface surface)
        {
            using var profile = sampleMarker.Auto();
            var filter = tile.MeshFilter; var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var supports = LoftTerrainConformer.TerrainCellSupports(mesh, filter.transform, vertices);
            var settings = road.TerrainSettings;
            var samples = new List<MGRoadTerrainLayers.Sample>();
            var toWorld = filter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toWorld.MultiplyPoint3x4(vertices[i]);
                float support = supports[i] * 2, falloff = MGRoadFalloffSmoothing.Distance(settings, supports[i]);
                if (!surface.Sample(p, support + falloff, out float height, out float distance)) continue;
                distance = Mathf.Max(0, distance - support);
                float weight = distance <= .00001f ? 1 : falloff <= 0 ? 0 : settings.falloff != null
                    ? Mathf.Clamp01(settings.falloff.Evaluate(distance / falloff)) : 1 - Mathf.SmoothStep(0, 1, distance / falloff);
                weight *= Mathf.Clamp01(settings.strength);
                if (weight <= 0) continue;
                float transition = falloff > 0 ? Mathf.Clamp01(distance / falloff) : 0;
                float smoothing = settings.smoothFalloff ? Mathf.Clamp01(settings.falloffSmoothing) * 4 * transition * (1 - transition) : 0;
                samples.Add(new MGRoadTerrainLayers.Sample { index = i, height = height + settings.terrainOffset, weight = weight, smoothing = smoothing });
            }
            return new MGRoadTerrainLayers.Layer { road = road, fingerprint = Fingerprint(road), order = Order(road), heightMode = settings.heightMode, samples = samples.ToArray(), smoothingPasses = settings.smoothFalloff ? Mathf.Clamp(settings.falloffSmoothingPasses, 1, 12) : 0 };
        }
        public static string Apply(IEnumerable<MGRoad> roads, bool automatic = false)
        {
            if (Application.isPlaying || busy) return "Terrain layers only update in edit mode.";
            var requested = roads.Where(r => r != null).Distinct().ToArray();
            if (!automatic) Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            if (!automatic) Undo.SetCurrentGroupName("Apply Road Terrain Layers");
            var applyClock = System.Diagnostics.Stopwatch.StartNew();
            busy = true;
            try
            {
                var affected = new HashSet<MGRoadTerrainLayers>();
                var additions = new List<(MGTerrain tile, MGRoadTerrainLayers.Layer layer)>();
                // Compute every replacement before touching the terrain. Old layer samples remain
                // independent of the currently deformed surface, including overlapping roads.
                foreach (var road in requested)
                {
                    if (!road.gameObject.scene.IsValid() || PrefabStageUtility.GetPrefabStage(road.gameObject) != null) throw new InvalidOperationException("Apply roads in a loaded scene outside Prefab Mode.");
                    if (!automatic) { Undo.RecordObject(road, "Enable Road Terrain Layer"); road.terrainLayerEnabled = true; EditorUtility.SetDirty(road); }
                    if (road.isActiveAndEnabled && road.terrainLayerEnabled && road.TerrainSettings.mode == RoadTerrainMode.TerrainFollowsRoad && (road.Network == null || road.Network.terrainWorld == null))
                        throw new InvalidOperationException("Assign a Terrain World on the road network first.");
                    if (!Active(road)) continue;
                    var settings = road.TerrainSettings;
                    foreach (float number in new[] { settings.terrainOffset, settings.strength, settings.falloffDistance })
                        if (float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidOperationException("Enter finite terrain offset, strength and falloff values.");
                    var world = road.Network.terrainWorld; world.RefreshChunks();
                    road.Rebuild();
                    if (road.GeneratedMesh == null || road.GeneratedMesh.vertexCount == 0) continue;
                    var surface = new LoftSurface(new[] { road.GetComponent<MeshFilter>() });
                    foreach (var tile in world.Chunks)
                    {
                        if (tile == null || !tile.isActiveAndEnabled || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null) continue;
                        var layer = Sample(road, tile, surface);
                        if (layer.samples.Length > 0) additions.Add((tile, layer));
                    }
                }
                foreach (var store in Stores()) if (store.layers.Any(l => requested.Contains(l.road))) affected.Add(store);
                foreach (var entry in additions) affected.Add(Prepare(entry.tile));
                foreach (var store in affected) Validate(store);
                foreach (var store in affected) CaptureEdits(store);
                foreach (var store in affected) store.layers.RemoveAll(l => requested.Contains(l.road));
                foreach (var entry in additions) entry.tile.GetComponent<MGRoadTerrainLayers>().layers.Add(entry.layer);
                foreach (var store in affected) Compose(store);
                foreach (var road in requested) fingerprints[road] = Fingerprint(road);
                Undo.FlushUndoRecordObjects();
                SceneView.RepaintAll();
                if (additions.Count == 0 && affected.Count == 0) return "No layers applied. Check Terrain Follows Road mode, enabled roads, and overlap with the assigned Terrain World.";
                return $"Updated {additions.Count} road/tile layers across {affected.Count} terrain tiles. Previous footprints restored; Undo supported.";
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { LastApplyMilliseconds = applyClock.Elapsed.TotalMilliseconds; busy = false; }
        }
        public static string Remove(IEnumerable<MGRoad> roads)
        {
            if (Application.isPlaying || busy) return "Terrain layers only update in edit mode.";
            var requested = roads.Where(r => r != null).Distinct().ToArray();
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Remove Road Terrain Layers");
            foreach (var road in requested) { Undo.RecordObject(road, "Disable Road Terrain Layer"); road.terrainLayerEnabled = false; EditorUtility.SetDirty(road); }
            Apply(requested, true);
            return "Road layers removed and underlying terrain restored. Apply re-enables them.";
        }
        public static string Bake(MGTerrainWorld world)
        {
            if (Application.isPlaying || busy) return "Terrain layers only update in edit mode.";
            if (world == null) throw new InvalidOperationException("Assign a Terrain World first.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Bake Road Terrain Layers");
            try
            {
                world.RefreshChunks();
                var stores = Stores().Where(s => world.Chunks.Contains(s.terrain)).ToArray();
                foreach (var store in stores) Validate(store);
                foreach (var store in stores)
                {
                    CaptureEdits(store); Compose(store); Persist(store);
                    foreach (var layer in store.layers)
                        if (layer.road != null) { Undo.RecordObject(layer.road, "Disable Baked Road Layer"); layer.road.terrainLayerEnabled = false; EditorUtility.SetDirty(layer.road); }
                    Undo.DestroyObjectImmediate(store);
                }
                Undo.FlushUndoRecordObjects();
                return "Baked road layers into terrain. Layer history removed; live updates paused for baked roads. Undo restores layers.";
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }
        static void Restored()
        {
            fingerprints.Clear(); sculpted.Clear(); MGRoadFalloffSmoothing.ClearCache();
            foreach (var road in Object.FindObjectsByType<MGRoad>(FindObjectsSortMode.None)) if (Editable(road)) fingerprints[road] = Fingerprint(road);
            foreach (var store in Stores()) if (Editable(store))
            {
                store.terrain.NotifySurfaceMeshChanged(); RefreshCollision(store);
            }
            nextUpdate = EditorApplication.timeSinceStartup + .3;
        }
        static void Tick()
        {
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextUpdate) return;
            double started = EditorApplication.timeSinceStartup;
            try { UpdateLive(); nextUpdate = EditorApplication.timeSinceStartup + Math.Max(.05, EditorApplication.timeSinceStartup - started); }
            catch (Exception e) { Debug.LogError("Road terrain layer update stopped: " + e.Message); nextUpdate = EditorApplication.timeSinceStartup + 5; }
        }
        internal static void UpdateLive(Scene? testScene = null)
        {
            if (busy || Application.isPlaying) return;
            var roads = testScene.HasValue ? testScene.Value.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MGRoad>(true)).ToArray()
                : Object.FindObjectsByType<MGRoad>(FindObjectsSortMode.None).Where(Editable).ToArray();
            var changed = new List<MGRoad>();
            foreach (var road in roads)
            {
                string hash = Fingerprint(road);
                if (!fingerprints.TryGetValue(road, out string old) || old != hash)
                {
                    if (Active(road) && road.TerrainSettings.autoApplyTerrain) changed.Add(road);
                    fingerprints[road] = hash;
                }
            }
            if (changed.Count > 0) Apply(changed, true);
            foreach (var store in Stores().Where(s => testScene.HasValue ? s.gameObject.scene == testScene.Value : Editable(s)))
            {
                bool removed = store.layers.Any(l => !Active(l.road));
                if (!removed && (!sculpted.Contains(store.terrain) || GUIUtility.hotControl != 0)) continue;
                CaptureEdits(store); store.layers.RemoveAll(l => !Active(l.road)); Compose(store);
            }
            if (GUIUtility.hotControl == 0) sculpted.Clear();
            foreach (var road in fingerprints.Keys.Where(r => r == null).ToArray()) fingerprints.Remove(road);
        }
        internal static void Persist(MGRoadTerrainLayers store)
        {
            if (EditorSceneManager.IsPreviewScene(store.gameObject.scene)) return;
            var mesh = store.terrain.MeshFilter.sharedMesh;
            if (!AssetDatabase.Contains(mesh)) MGTerrainSceneAssets.Create(mesh, store.terrain, "RoadTerrain");
            AssetDatabase.SaveAssetIfDirty(mesh);
            EditorUtility.SetDirty(store);
            EditorUtility.SetDirty(store.terrain);
            EditorUtility.SetDirty(store.terrain.MeshFilter);
        }
        static void Saving(Scene scene, string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            UpdateLive();
            foreach (var store in Stores().Where(s => s.gameObject.scene == scene))
            {
                Validate(store);
                Persist(store);
                foreach (var collider in store.terrain.SurfaceColliderChunks)
                    if (collider != null && collider.sharedMesh != null) AssetDatabase.SaveAssetIfDirty(collider.sharedMesh);
            }
        }
    }

    [CustomEditor(typeof(MGRoadTerrainLayers))]
    public sealed class MGRoadTerrainLayersEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var store = (MGRoadTerrainLayers)target;
            EditorGUILayout.HelpBox("Reversible road contributions for this terrain tile. Use the Road Tool to update, remove or bake them. Gameplay uses the saved terrain mesh.", MessageType.Info);
            EditorGUILayout.LabelField("Road Layers", store.layers.Count.ToString());
            foreach (var layer in store.layers)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(layer.road, typeof(MGRoad), true);
                    using (new EditorGUI.DisabledScope(layer.road == null))
                        if (GUILayout.Button("Edit", GUILayout.Width(45))) MGRoadTool.Open(layer.road.Network, layer.road);
                }
            }
        }
    }
    public sealed class MGRoadLayerBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 1000;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var store in root.GetComponentsInChildren<MGRoadTerrainLayers>(true)) Object.DestroyImmediate(store);
        }
    }
}

