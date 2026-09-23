using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.TerrainSystem.Editor
{
    public static class LoftTerrainConformerValidation
    {
        static int checks;
        static void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception(message);
        }
        static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .002f, message + $" ({actual} vs {expected})");

        // Run with Unity -executeMethod MashBoxSDK.Maps.TerrainSystem.Editor.LoftTerrainConformerValidation.Run.
        [MenuItem("MashBox/Validation/Validate Loft Terrain Conform")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var cleanup = new List<Object>();
            var window = ScriptableObject.CreateInstance<LoftTerrainConformer>();
            try
            {
                checks = 0;
                var go = new GameObject("Test loft");
                SceneManager.MoveGameObjectToScene(go, scene);
                var filter = go.AddComponent<MeshFilter>();
                var mesh = new Mesh { vertices = new[] { new Vector3(0, 2, 0), new Vector3(20, 6, 0), new Vector3(0, 2, 10), new Vector3(20, 6, 10) },
                    triangles = new[] { 0, 2, 1, 1, 2, 3 } };
                cleanup.Add(mesh); filter.sharedMesh = mesh;
                var surface = new LoftSurface(new[] { filter });
                Check(surface.Sample(new Vector3(10, -100, 5), 0, out float y, out float distance), "Interior hit without collider");
                Near(y, 4, "Slope interpolation"); Near(distance, 0, "Interior distance");
                Check(!surface.Sample(new Vector3(10, 0, 12), 0, out _, out _), "No changes outside footprint without blend");
                Check(surface.Sample(new Vector3(10, 0, 12), 3, out y, out distance), "Blend reaches outside footprint");
                Near(y, 4, "Closest edge height"); Near(distance, 2, "Edge distance");
                go.transform.SetPositionAndRotation(new Vector3(100, 7, -30), Quaternion.Euler(0, 90, 0));
                go.transform.localScale = new Vector3(2, 3, 1);
                var moved = new LoftSurface(new[] { filter });
                var world = go.transform.TransformPoint(new Vector3(10, 4, 5));
                Check(moved.Sample(world, 0, out y, out _), "Transformed loft hit"); Near(y, world.y, "World transform height");
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;

                var settings = new SerializedObject(window);
                settings.FindProperty("offset").floatValue = -.25f;
                settings.FindProperty("blendDistance").floatValue = 4;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Check(window.Target(surface, new Vector3(10, 0, 12), out y), "Blend modifies height"); Near(y, 1.875f, "Halfway smooth blend with offset");
                settings.FindProperty("mode").enumValueIndex = (int)LoftTerrainConformer.HeightMode.LowerOnly;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Check(!window.Target(surface, new Vector3(10, 0, 5), out _), "Lower only preserves lower terrain");
                settings.FindProperty("mode").enumValueIndex = (int)LoftTerrainConformer.HeightMode.RaiseOnly;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Check(!window.Target(surface, new Vector3(10, 10, 5), out _), "Raise only preserves higher terrain");
                settings.FindProperty("mode").enumValueIndex = 0;
                settings.FindProperty("offset").floatValue = 0;
                settings.FindProperty("blendDistance").floatValue = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();

                var a = UnityTile(scene, 0, cleanup); var b = UnityTile(scene, 10, cleanup);
                var edits = window.BuildEdits(surface, Array.Empty<MGTerrain>(), new[] { a, b });
                Check(edits.Count == 2, "Both Unity terrain tiles discovered");
                Near(a.terrainData.GetHeights(0, 0, 1, 1)[0, 0], 0, "Preview does not mutate");
                LoftTerrainConformer.Apply(edits);
                Near(a.terrainData.GetHeights(32, 16, 1, 1)[0, 0] * 20, 4, "First tile edge conforms");
                Near(b.terrainData.GetHeights(0, 16, 1, 1)[0, 0] * 20, 4, "Second tile edge agrees");
                Undo.PerformUndo();
                Near(a.terrainData.GetHeights(32, 16, 1, 1)[0, 0], 0, "Undo first tile");
                Near(b.terrainData.GetHeights(0, 16, 1, 1)[0, 0], 0, "Undo second tile");
                Undo.PerformRedo();
                Near(b.terrainData.GetHeights(0, 16, 1, 1)[0, 0] * 20, 4, "Redo tile conform");

                var ma = MeshTile(scene, 0, cleanup); var mb = MeshTile(scene, 10, cleanup);
                edits = window.BuildEdits(surface, new[] { ma, mb }, Array.Empty<Terrain>());
                Check(edits.Count == 2, "Both MG tiles discovered");
                LoftTerrainConformer.Apply(edits);
                Near(ma.MeshFilter.sharedMesh.vertices[1].y, 4, "MG first shared edge");
                Near(mb.MeshFilter.sharedMesh.vertices[0].y, 4, "MG second shared edge");
                Undo.PerformUndo();
                Near(ma.MeshFilter.sharedMesh.vertices[1].y, 0, "MG undo first tile");
                Near(mb.MeshFilter.sharedMesh.vertices[0].y, 0, "MG undo second tile");
                Undo.PerformRedo();
                Near(mb.MeshFilter.sharedMesh.vertices[0].y, 4, "MG redo");
                Debug.Log($"LOFT_TERRAIN_VALIDATION_PASS: {checks} checks");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Object.DestroyImmediate(window);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var item in cleanup) if (item != null) Object.DestroyImmediate(item);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static Terrain UnityTile(Scene scene, float x, List<Object> cleanup)
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(10, 20, 10) };
            cleanup.Add(data);
            var go = Terrain.CreateTerrainGameObject(data);
            SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = new Vector3(x, 0, 0);
            return go.GetComponent<Terrain>();
        }

        static MGTerrain MeshTile(Scene scene, float x, List<Object> cleanup)
        {
            var go = new GameObject("MG test tile"); SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(x, 0, 0);
            var filter = go.AddComponent<MeshFilter>(); var renderer = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, new Vector3(10, 0, 0), new Vector3(0, 0, 10), new Vector3(10, 0, 10) }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            mesh.RecalculateNormals(); cleanup.Add(mesh); filter.sharedMesh = mesh;
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            var tile = go.AddComponent<MGTerrain>(); tile.Configure(filter, renderer, collider);
            return tile;
        }
    }
}
