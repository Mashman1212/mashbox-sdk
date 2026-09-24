using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Roads.Editor
{
    [InitializeOnLoad]
    public static class MGRoadTerrain
    {
        static MGRoadTerrain()
        {
            MGRoad.CreateTerrainProjector = CreateProjector;
            Undo.undoRedoPerformed += RebuildAll;
            EditorSceneManager.sceneSaving += SaveMeshes;
        }
        static void RebuildAll()
        {
            foreach (var road in Object.FindObjectsByType<MGRoad>()) road.RequestRebuild();
        }
        static Func<Vector3, Vector3> CreateProjector(MGRoad road)
        {
            if (road.Network == null || road.Network.terrainWorld == null) return null;
            var world = road.Network.terrainWorld;
            world.RefreshChunks();
            var bounds = new Bounds(road.transform.position, Vector3.zero);
            var spline = road.Container.Spline;
            if (spline.Count > 0) bounds = new Bounds(road.transform.TransformPoint((Vector3)spline[0].Position), Vector3.zero);
            for (int i = 0; i < spline.Count; i++)
            {
                var curve = spline.GetCurve(i);
                bounds.Encapsulate(road.transform.TransformPoint((Vector3)curve.P0));
                bounds.Encapsulate(road.transform.TransformPoint((Vector3)curve.P1));
                bounds.Encapsulate(road.transform.TransformPoint((Vector3)curve.P2));
                bounds.Encapsulate(road.transform.TransformPoint((Vector3)curve.P3));
            }
            float maxWidthScale = 1f;
            for (int i = 0; i < spline.Count; i++) maxWidthScale = Mathf.Max(maxWidthScale, MGRoadKnotShape.GetScale(spline, i).x);
            bounds.Expand(((road.width + road.shoulderWidth * 2) * maxWidthScale + 2) * 2);
            var filters = world.Chunks.Where(t => t != null && t.isActiveAndEnabled && t.MeshFilter != null && t.MeshFilter.sharedMesh != null)
                .Select(t => t.MeshFilter).Where(f => OverlapsXZ(bounds, WorldBounds(f))).ToArray();
            if (filters.Length == 0) return null;
            var surface = new LoftSurface(filters, true);
            float clearance = road.TerrainSettings.clearance;
            return p => { if (surface.Sample(p, 0, out float height, out _)) p.y = height + clearance; return p; };
        }
        // Build spatial indexes lazily for tiles touched by this Shift gesture. No scene objects or mesh edits.
        internal static Func<Vector3, Vector3> CreatePreviewProjector(MGRoad road)
        {
            if (road.Network == null || road.Network.terrainWorld == null) return null;
            var world = road.Network.terrainWorld;
            world.RefreshChunks();
            var filters = world.Chunks.Where(t => t != null && t.isActiveAndEnabled && t.MeshFilter != null && t.MeshFilter.sharedMesh != null && t.MeshFilter.sharedMesh.isReadable)
                .Select(t => t.MeshFilter).ToArray();
            var bounds = filters.Select(WorldBounds).ToArray();
            var surfaces = new LoftSurface[filters.Length];
            float clearance = road.TerrainSettings.clearance;
            return point =>
            {
                float highest = float.NegativeInfinity;
                for (int i = 0; i < filters.Length; i++)
                {
                    var b = bounds[i];
                    if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) continue;
                    if (surfaces[i] == null) surfaces[i] = new LoftSurface(new[] { filters[i] }, true);
                    if (surfaces[i].Sample(point, 0, out float height, out _)) highest = Mathf.Max(highest, height);
                }
                if (!float.IsNegativeInfinity(highest)) point.y = highest + clearance;
                return point;
            };
        }
        static Bounds WorldBounds(MeshFilter filter)
        {
            var b = filter.sharedMesh.bounds;
            var result = new Bounds(filter.transform.TransformPoint(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(filter.transform.TransformPoint(new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return result;
        }
        static bool OverlapsXZ(Bounds a, Bounds b) => a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.z <= b.max.z && a.max.z >= b.min.z;

        public static string Apply(IEnumerable<MGRoad> roads) => MGRoadLayerService.Apply(roads);

        static void SaveMeshes(Scene scene, string path)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var road in root.GetComponentsInChildren<MGRoad>(true)) Persist(road);
        }
        public static void Persist(MGRoad road)
        {
            road.Rebuild();
            if (road.GeneratedMesh == null) return;
            const string folder = "Assets/MashBox Roads/Generated";
            if (!AssetDatabase.IsValidFolder("Assets/MashBox Roads")) AssetDatabase.CreateFolder("Assets", "MashBox Roads");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/MashBox Roads", "Generated");
            var storage = road.GetComponent<MGRoadMeshStorage>();
            if (storage == null) storage = road.gameObject.AddComponent<MGRoadMeshStorage>();
            string owner = GlobalObjectId.GetGlobalObjectIdSlow(road).ToString();
            bool shared = storage.mesh != null && Resources.FindObjectsOfTypeAll<MGRoadMeshStorage>().Any(s => s != storage && s.mesh == storage.mesh);
            if (storage.mesh == null || storage.owner != owner || shared)
            {
                storage.mesh = Object.Instantiate(road.GeneratedMesh);
                storage.owner = owner;
                AssetDatabase.CreateAsset(storage.mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/Road.asset"));
            }
            else EditorUtility.CopySerialized(road.GeneratedMesh, storage.mesh);
            EditorUtility.SetDirty(storage.mesh);
            road.GetComponent<MeshFilter>().sharedMesh = storage.mesh;
            var collider = road.GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = road.generateCollider ? storage.mesh : null;
            EditorUtility.SetDirty(road.GetComponent<MeshFilter>());
            if (collider != null) EditorUtility.SetDirty(collider);
            EditorUtility.SetDirty(storage);
            AssetDatabase.SaveAssetIfDirty(storage.mesh);
        }
    }
}
