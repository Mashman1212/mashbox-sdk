using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public static class MGRoadDetailService
    {
        static readonly Dictionary<MGRoad, string> fingerprints = new Dictionary<MGRoad, string>();
        static readonly Unity.Profiling.ProfilerMarker marker = new Unity.Profiling.ProfilerMarker("MGRoadDetails.Apply");
        static bool busy;
        static double nextUpdate;
        static MGRoadDetailService()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += Restored;
            EditorSceneManager.sceneSaving += (scene, path) => { if (!Application.isPlaying) UpdateLive(); };
        }
        static bool Editable(Component c) => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded
            && !EditorSceneManager.IsPreviewScene(c.gameObject.scene) && !EditorUtility.IsPersistent(c)
            && PrefabStageUtility.GetPrefabStage(c.gameObject) == null;
        static bool Active(MGRoad r) => r != null && r.isActiveAndEnabled && r.detailMaskEnabled && r.DetailSettings.clearDetails
            && r.Network != null && r.Network.isActiveAndEnabled && r.Network.terrainWorld != null && r.Network.terrainWorld.isActiveAndEnabled;
        static MGRoadDetailLayers[] Stores() => Resources.FindObjectsOfTypeAll<MGRoadDetailLayers>()
            .Where(s => s != null && s.gameObject.scene.IsValid() && s.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(s)).ToArray();
        static string Fingerprint(MGRoad r)
        {
            var b = new StringBuilder(MGRoadLayerService.Fingerprint(r));
            b.Append(JsonUtility.ToJson(r.DetailSettings)).Append(r.detailMaskEnabled);
            var world = r.Network != null ? r.Network.terrainWorld : null;
            if (world != null && r.DetailSettings.clearDetails && r.detailMaskEnabled)
            {
                world.RefreshChunks(); b.Append(world.isActiveAndEnabled);
                foreach (var tile in world.Chunks)
                {
                    if (tile == null) continue;
                    b.Append(tile.GetInstanceID()).Append(tile.isActiveAndEnabled).Append(tile.transform.localToWorldMatrix.ToString("R"));
                    var mesh = tile.MeshFilter != null ? tile.MeshFilter.sharedMesh : null;
                    if (mesh != null) b.Append(mesh.bounds.min.x).Append(mesh.bounds.min.z).Append(mesh.bounds.size.x).Append(mesh.bounds.size.z);
                    foreach (var layer in tile.DensityDetailLayers)
                        if (layer?.DensityMap != null) b.Append(layer.DensityMap.width).Append('x').Append(layer.DensityMap.height).Append(';');
                }
            }
            return Hash128.Compute(b.ToString()).ToString();
        }
        static void Tick()
        {
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.timeSinceStartup < nextUpdate) return;
            double start = EditorApplication.timeSinceStartup;
            try { UpdateLive(); nextUpdate = EditorApplication.timeSinceStartup + Math.Max(.15, EditorApplication.timeSinceStartup - start); }
            catch (Exception e) { Debug.LogException(e); nextUpdate = EditorApplication.timeSinceStartup + 5; }
        }
        internal static void UpdateLive(Scene? scene = null)
        {
            if (busy || Application.isPlaying) return;
            var roads = scene.HasValue ? scene.Value.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MGRoad>(true)).ToArray()
                : Object.FindObjectsByType<MGRoad>(FindObjectsSortMode.None).Where(Editable).ToArray();
            var changed = new List<MGRoad>();
            foreach (var road in roads)
            {
                var hash = Fingerprint(road);
                if (!fingerprints.TryGetValue(road, out var previous) || hash != previous)
                {
                    if (Active(road) && road.DetailSettings.autoUpdate) changed.Add(road);
                    fingerprints[road] = hash;
                }
            }
            if (changed.Count > 0) Apply(changed, true);
            foreach (var store in Stores().Where(s => scene.HasValue ? s.gameObject.scene == scene.Value : Editable(s)))
            {
                var tile = store.GetComponent<MGTerrain>();
                bool Valid(MGRoadDetailLayers.Layer layer) => Active(layer.road) && tile != null && tile.isActiveAndEnabled
                    && layer.road.Network.terrainWorld.Chunks.Contains(tile);
                if (!store.layers.Any(l => !Valid(l))) continue;
                Undo.RegisterCompleteObjectUndo(store, "Restore Road Details");
                store.layers.RemoveAll(l => !Valid(l)); Compose(store);
            }
            foreach (var road in fingerprints.Keys.Where(r => r == null).ToArray()) fingerprints.Remove(road);
        }
        public static string Apply(IEnumerable<MGRoad> source, bool automatic = false)
        {
            if (busy || Application.isPlaying) return "Detail clearing is edited outside Play Mode.";
            using var profile = marker.Auto();
            var roads = source.Where(r => r != null).Distinct().ToArray();
            if (!automatic) { Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Clear Road Details"); }
            int group = Undo.GetCurrentGroup(); busy = true;
            try
            {
                var additions = new List<(MGTerrain tile, MGRoadDetailLayers.Layer layer)>();
                foreach (var road in roads)
                {
                    if (PrefabStageUtility.GetPrefabStage(road.gameObject) != null || !road.gameObject.scene.IsValid())
                        throw new InvalidOperationException("Apply road detail clearing in a loaded scene outside Prefab Mode.");
                    if (!automatic)
                    {
                        if (road.Network == null || road.Network.terrainWorld == null) throw new InvalidOperationException("Assign a Terrain World on the road network first.");
                        Undo.RecordObject(road, "Enable Road Detail Clearing"); road.detailMaskEnabled = true;
                        if (!road.DetailSettings.clearDetails)
                        {
                            road.details = JsonUtility.FromJson<RoadDetailSettings>(JsonUtility.ToJson(road.DetailSettings));
                            road.overrideDetails = true; road.details.clearDetails = true;
                        }
                        EditorUtility.SetDirty(road);
                    }
                    if (!Active(road)) continue;
                    if (float.IsNaN(road.DetailSettings.extraWidth) || float.IsInfinity(road.DetailSettings.extraWidth))
                        throw new InvalidOperationException("Enter a finite extra clearing width.");
                    var world = road.Network.terrainWorld; world.RefreshChunks(); road.Rebuild();
                    if (road.GeneratedMesh == null || road.GeneratedMesh.vertexCount == 0) continue;
                    var surface = new LoftSurface(new[] { road.GetComponent<MeshFilter>() });
                    foreach (var tile in world.Chunks)
                    {
                        if (tile == null || !tile.isActiveAndEnabled || tile.MeshFilter == null || tile.MeshFilter.sharedMesh == null) continue;
                        var layer = Sample(road, tile, surface);
                        if (layer.masks.Count > 0) additions.Add((tile, layer));
                    }
                }
                var affected = new HashSet<MGRoadDetailLayers>(Stores().Where(s => s.layers.Any(l => roads.Contains(l.road))));
                foreach (var entry in additions)
                {
                    var store = entry.tile.GetComponent<MGRoadDetailLayers>();
                    if (store == null) store = Undo.AddComponent<MGRoadDetailLayers>(entry.tile.gameObject);
                    affected.Add(store);
                }
                foreach (var store in affected)
                {
                    Undo.RegisterCompleteObjectUndo(store, "Update Road Detail Mask");
                    store.layers.RemoveAll(l => roads.Contains(l.road));
                }
                foreach (var entry in additions) entry.tile.GetComponent<MGRoadDetailLayers>().layers.Add(entry.layer);
                foreach (var store in affected) Compose(store);
                foreach (var road in roads) fingerprints[road] = Fingerprint(road);
                Undo.FlushUndoRecordObjects(); SceneView.RepaintAll();
                return additions.Count == 0 && affected.Count == 0
                    ? "No painted detail cells overlap these roads in the assigned Terrain World."
                    : $"Updated detail clearing on {affected.Count} terrain tiles. Original detail paint is preserved.";
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { busy = false; }
        }
        public static string Remove(IEnumerable<MGRoad> source)
        {
            if (Application.isPlaying || busy) return "Detail clearing is edited outside Play Mode.";
            var roads = source.Where(r => r != null).Distinct().ToArray();
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Restore Road Details");
            foreach (var road in roads) { Undo.RecordObject(road, "Pause Road Detail Clearing"); road.detailMaskEnabled = false; EditorUtility.SetDirty(road); }
            Apply(roads, true);
            return "Restored details where no other road clears them. Clearing paused; Apply resumes it.";
        }
        internal static MGRoadDetailLayers.Layer Sample(MGRoad road, MGTerrain tile, LoftSurface surface)
        {
            var result = new MGRoadDetailLayers.Layer { road = road };
            var bounds = tile.MeshFilter.sharedMesh.bounds;
            var matrix = tile.transform.localToWorldMatrix;
            var origin = matrix.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.center.y, bounds.min.z));
            var axisX = matrix.MultiplyVector(new Vector3(bounds.size.x, 0, 0));
            var axisZ = matrix.MultiplyVector(new Vector3(0, 0, bounds.size.z));
            float det = axisX.x * axisZ.z - axisX.z * axisZ.x;
            if (Mathf.Abs(det) < 1e-8f) return result;
            var sizes = tile.DensityDetailLayers.Where(l => l?.DensityMap != null)
                .Select(l => (width: l.DensityMap.width, height: l.DensityMap.height)).Distinct();
            foreach (var size in sizes)
            {
                var dx = axisX / size.width; var dz = axisZ / size.height; dx.y = dz.y = 0;
                // Whole density cells are suppressed, including random blade positions inside them.
                float radius = Mathf.Max(0, road.DetailSettings.extraWidth) + .5f * Mathf.Max((dx + dz).magnitude, (dx - dz).magnitude);
                var roadBounds = surface.Bounds; var min = roadBounds.min; var max = roadBounds.max;
                float u0 = float.PositiveInfinity, u1 = float.NegativeInfinity, v0 = u0, v1 = u1;
                for (int i = 0; i < 4; i++)
                {
                    float x = ((i & 1) == 0 ? min.x - radius : max.x + radius) - origin.x;
                    float z = ((i & 2) == 0 ? min.z - radius : max.z + radius) - origin.z;
                    float u = (x * axisZ.z - z * axisZ.x) / det, v = (axisX.x * z - axisX.z * x) / det;
                    u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
                }
                if (u1 < 0 || v1 < 0 || u0 > 1 || v0 > 1) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(u0 * size.width), 0, size.width - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(u1 * size.width), 0, size.width - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(v0 * size.height), 0, size.height - 1), z1 = Mathf.Clamp(Mathf.FloorToInt(v1 * size.height), 0, size.height - 1);
                var cells = new List<int>();
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                {
                    var p = origin + axisX * ((x + .5f) / size.width) + axisZ * ((z + .5f) / size.height);
                    if (surface.Sample(p, radius, out _, out _)) cells.Add(z * size.width + x);
                }
                if (cells.Count > 0) result.masks.Add(new MGRoadDetailLayers.Cells { width = size.width, height = size.height, indices = cells.ToArray() });
            }
            return result;
        }
        static void Compose(MGRoadDetailLayers store)
        {
            var tile = store.GetComponent<MGTerrain>();
            if (tile == null) return;
            var masks = new List<MGTerrain.DetailExclusionMask>();
            foreach (var layer in store.layers) foreach (var cells in layer.masks)
            {
                var mask = masks.Find(m => m.width == cells.width && m.height == cells.height);
                if (mask == null) { mask = new MGTerrain.DetailExclusionMask { width = cells.width, height = cells.height, cells = new byte[cells.width * cells.height] }; masks.Add(mask); }
                foreach (int index in cells.indices) if ((uint)index < mask.cells.Length) mask.cells[index] = 1;
            }
            Undo.RegisterCompleteObjectUndo(tile, "Update Road Detail Clearing");
            tile.SetRoadDetailMasks(masks);
            EditorUtility.SetDirty(tile); EditorUtility.SetDirty(store); EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
        }
        static void Restored()
        {
            fingerprints.Clear();
            foreach (var road in Object.FindObjectsByType<MGRoad>(FindObjectsSortMode.None)) if (Editable(road)) fingerprints[road] = Fingerprint(road);
            foreach (var store in Stores()) if (Editable(store)) store.GetComponent<MGTerrain>()?.RefreshRoadDetailMasks();
            nextUpdate = EditorApplication.timeSinceStartup + .3;
        }
    }
    public sealed class MGRoadDetailBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 1001;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var store in root.GetComponentsInChildren<MGRoadDetailLayers>(true)) Object.DestroyImmediate(store);
        }
    }
    [CustomEditor(typeof(MGRoadDetailLayers))]
    public sealed class MGRoadDetailLayersEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Reversible road detail masks. Original painted density maps remain untouched. Use the Road Tool's Details tab to apply or restore. The saved combined mask is used in gameplay.", MessageType.Info);
            EditorGUILayout.LabelField("Road Masks", ((MGRoadDetailLayers)target).layers.Count.ToString());
        }
    }
}
