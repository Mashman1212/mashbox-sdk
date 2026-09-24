#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainSeamRepair
    {
        static bool repairing;
        static MGTerrainSeamRepair() => EditorSceneManager.sceneSaving += OnSaving;

        [MenuItem("Tools/MashBox/MG Terrain/Repair Loaded Tile Seams")]
        internal static void RepairLoaded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Repair Terrain Seams");
            try { for (int i = 0; i < SceneManager.sceneCount; i++) Repair(SceneManager.GetSceneAt(i)); }
            finally { Undo.CollapseUndoOperations(group); }
        }

        static void OnSaving(Scene scene, string path)
        {
            if (!CanRepair(scene) || repairing) return;
            Repair(scene);
            // Scene references alone do not serialize dirty mesh assets.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var tile in root.GetComponentsInChildren<MGTerrain>(true))
                    if (tile.MeshFilter != null && tile.MeshFilter.sharedMesh != null)
                        AssetDatabase.SaveAssetIfDirty(tile.MeshFilter.sharedMesh);
        }

        static bool CanRepair(Scene scene) => scene.IsValid() && scene.isLoaded
            && !EditorSceneManager.IsPreviewScene(scene) && !EditorApplication.isPlayingOrWillChangePlaymode;

        internal static void Repair(Scene scene)
        {
            if (!CanRepair(scene) || repairing) return;
            repairing = true;
            try
            {
                var tiles = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MGTerrain>(true))
                    .Where(tile => tile.MeshFilter != null && tile.MeshFilter.sharedMesh != null && tile.MeshFilter.sharedMesh.isReadable).ToList();
                foreach (var tile in tiles) MGTerrainGridRepair.Repair(tile);
                foreach (var world in tiles.GroupBy(tile => tile.GetComponentInParent<MGTerrainWorld>(true)))
                {
                    if (world.Key == null) continue;
                    // Do not weld independent worlds or unsupported rotated terrain.
                    var valid = new List<MGTerrain>();
                    foreach (var tile in world)
                    {
                        try { MGTerrainTileAuthoring.Validate(tile); valid.Add(tile); }
                        catch (InvalidOperationException error) { Debug.LogWarning(error.Message, tile); }
                    }
                    MGTerrainSeams.Join(valid, Vector3.zero, float.PositiveInfinity, false, tile =>
                    {
                        tile.RefreshSurfaceCollidersFromMesh();
                        EditorSceneManager.MarkSceneDirty(scene);
                    });
                }
            }
            finally { repairing = false; }
        }
    }
}
#endif
