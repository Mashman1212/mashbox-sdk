using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainTileNames
    {
        static MGTerrainTileNames()
        {
            EditorApplication.hierarchyChanged += Queue;
            Undo.undoRedoPerformed += Queue;
            ObjectChangeEvents.changesPublished += OnChanges;
            Queue();
        }
        static void OnChanges(ref ObjectChangeEventStream stream) => Queue();
        static void Queue()
        {
            EditorApplication.delayCall -= Repair;
            EditorApplication.delayCall += Repair;
        }
        internal static bool TryCoordinates(MGTerrain tile, out Vector2Int coordinate)
        {
            coordinate = default;
            if (tile == null) return false;
            var world = tile.GetComponentInParent<MGTerrainWorld>(true);
            var filter = tile.MeshFilter;
            if (world == null || filter == null || filter.sharedMesh == null) return false;
            // Mesh footprint, not its pivot or its old label, defines its grid cell.
            var bounds = filter.sharedMesh.bounds;
            var matrix = world.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var min = matrix.MultiplyPoint3x4(bounds.min);
            var size = matrix.MultiplyVector(bounds.size);
            if (size.x <= .001f || size.z <= .001f) return false;
            coordinate = new Vector2Int(Mathf.RoundToInt(min.x / Mathf.Max(.001f, world.TileSize)), Mathf.RoundToInt(min.z / Mathf.Max(.001f, world.TileSize)));
            return true;
        }
        internal static void Repair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            foreach (var tile in Resources.FindObjectsOfTypeAll<MGTerrain>())
            {
                var scene = tile.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)
                    || EditorUtility.IsPersistent(tile) || !TryCoordinates(tile, out var coordinate)) continue;
                string expected = $"Terrain Tile ({coordinate.x}, {coordinate.y})";
                if (tile.name == expected) continue;
                tile.gameObject.name = expected;
                PrefabUtility.RecordPrefabInstancePropertyModifications(tile.gameObject);
                EditorUtility.SetDirty(tile.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
