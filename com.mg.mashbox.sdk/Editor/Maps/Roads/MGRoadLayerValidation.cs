using System;
using MashBoxSDK.Maps.TerrainSystem;
using MashBoxSDK.Maps.TerrainSystem.Editor;
using MashBoxSDK.Maps.Sculpting;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Roads.Editor
{
    public static class MGRoadLayerValidation
    {
        static int checks;
        static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
        static void Near(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < .002f, label + $" ({actual} vs {expected})");
        [MenuItem("MashBox/Validation/Validate Road Terrain Layers")]
        public static void Run() => RunInScene(true);
        internal static void RunInScene(bool preview)
        {
            checks = 0;
            var activeScene = SceneManager.GetActiveScene();
            var scene = preview ? EditorSceneManager.NewPreviewScene() : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (!preview) SceneManager.SetActiveScene(activeScene);
            Mesh original = null;
            var meshes = new System.Collections.Generic.HashSet<Mesh>();
            try
            {
                var root = new GameObject("Layer test"); SceneManager.MoveGameObjectToScene(root, scene);
                var network = root.AddComponent<MGRoadNetwork>();
                var worldGo = new GameObject("World"); worldGo.transform.SetParent(root.transform, false);
                var world = worldGo.AddComponent<MGTerrainWorld>(); network.terrainWorld = world;
                network.terrain.mode = RoadTerrainMode.TerrainFollowsRoad;
                network.terrain.terrainOffset = 0; network.terrain.falloffDistance = 6;
                var tileGo = new GameObject("Tile"); tileGo.transform.SetParent(worldGo.transform, false);
                var filter = tileGo.AddComponent<MeshFilter>(); var renderer = tileGo.AddComponent<MeshRenderer>(); var collider = tileGo.AddComponent<MeshCollider>();
                var vertices = new Vector3[18]; var triangles = new int[48];
                for (int x = 0; x < 9; x++) { vertices[x * 2] = new Vector3((x - 4) * 8, 2, 0); vertices[x * 2 + 1] = new Vector3((x - 4) * 8, 2, 8); }
                for (int x = 0; x < 8; x++) { int a = x * 2, t = x * 6; triangles[t] = a; triangles[t + 1] = a + 1; triangles[t + 2] = a + 2; triangles[t + 3] = a + 2; triangles[t + 4] = a + 1; triangles[t + 5] = a + 3; }
                original = new Mesh { vertices = vertices, triangles = triangles }; original.RecalculateNormals(); original.RecalculateBounds();
                filter.sharedMesh = original; collider.sharedMesh = original;
                var tile = tileGo.AddComponent<MGTerrain>(); tile.Configure(filter, renderer, collider); world.RefreshChunks();
                MGRoad Road(string name, float y)
                {
                    var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                    var r = go.AddComponent<MGRoad>(); r.width = 2; r.shoulderWidth = 0; r.crown = 0; r.generateCollider = false;
                    r.Container.Spline.Add(new BezierKnot(new float3(0, y, 0)), TangentMode.Linear);
                    r.Container.Spline.Add(new BezierKnot(new float3(0, y, 8)), TangentMode.Linear);
                    return r;
                }
                // A saved chunk plus a separate master reproduces the stale binding
                // when the road service replaces the original terrain mesh.
                var chunkGo = new GameObject("Collider chunk"); chunkGo.transform.SetParent(tileGo.transform, false);
                var chunk = chunkGo.AddComponent<MeshCollider>();
                chunk.sharedMesh = Object.Instantiate(original); meshes.Add(chunk.sharedMesh);
                using (var data = new SerializedObject(tile))
                {
                    var chunks = data.FindProperty("m_SurfaceColliderChunks");
                    chunks.arraySize = 1; chunks.GetArrayElementAtIndex(0).objectReferenceValue = chunk;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var indices = new int[vertices.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
                tile.SetSurfaceColliderVertexMaps(new[] { new MGTerrain.SurfaceColliderVertexMap(chunk, indices) }, vertices.Length);
                var aRoad = Road("A", 5);
                MGRoadLayerService.Apply(new[] { aRoad }); meshes.Add(filter.sharedMesh);
                Near(filter.sharedMesh.vertices[8].y, 5, "Initial layer reaches road");
                Near(original.vertices[8].y, 2, "Source mesh untouched");
                Check(!AssetDatabase.Contains(filter.sharedMesh), "Applying a road creates no imported terrain asset");
                Near(filter.sharedMesh.vertices[12].y, 5, "Extra cell apron retained");
                Check(collider.sharedMesh == filter.sharedMesh, "Road replacement updates master even with chunks");
                Near(chunk.sharedMesh.vertices[8].y, 5, "Road replacement refreshes chunk heights");
                Check(chunk.enabled && !collider.enabled, "Chunks retain collision ownership");

                float beforeLoftHeight = filter.sharedMesh.vertices[0].y;
                LoftTerrainConformer.Apply(new System.Collections.Generic.List<LoftTerrainConformer.Edit>
                {
                    new LoftTerrainConformer.Edit { mg = tile, deltas = new System.Collections.Generic.List<MeshSculptModifier.SeamVertex>
                    {
                        new MeshSculptModifier.SeamVertex { index = 0, delta = Vector3.up * 3 },
                        new MeshSculptModifier.SeamVertex { index = 1, delta = Vector3.up * 3 }
                    } }
                });
                meshes.Add(filter.sharedMesh);
                Near(filter.sharedMesh.vertices[0].y, 5, "Loft conform changes terrain outside road");
                Near(filter.sharedMesh.vertices[8].y, 5, "Loft conform preserves road footprint");
                Near(tile.GetComponent<MGRoadTerrainLayers>().baseline[0].y, 5, "Loft edit committed to road baseline immediately");
                Check(collider.sharedMesh == filter.sharedMesh, $"Loft keeps master on current mesh (master={collider.sharedMesh}, surface={filter.sharedMesh}, configured={tile.MeshCollider})");
                Near(chunk.sharedMesh.vertices[0].y, 5, "Loft refreshes collider chunk");
                MGRoadLayerService.UpdateLive(scene);
                Near(filter.sharedMesh.vertices[0].y, 5, "Editor tick does not replay loft delta");
                Undo.PerformUndo();
                Near(filter.sharedMesh.vertices[0].y, beforeLoftHeight, "One Undo restores pre-loft terrain");
                Near(filter.sharedMesh.vertices[8].y, 5, "Loft Undo preserves road");
                Near(chunk.sharedMesh.vertices[0].y, beforeLoftHeight, "Loft Undo restores chunk");
                Undo.PerformRedo();
                Near(filter.sharedMesh.vertices[0].y, 5, "Loft Redo restores terrain");
                Near(chunk.sharedMesh.vertices[0].y, 5, "Loft Redo restores chunk");
                Undo.IncrementCurrentGroup();
                // Simulate an old master binding left behind while chunks own collision.
                collider.sharedMesh = original;
                MashBoxSDK.MapTools.MeshSculptWindow.EnsureSeamMasterCollider(tile);
                Check(collider.enabled && !chunk.enabled && collider.sharedMesh == filter.sharedMesh,
                    "Painting handoff binds the latest loft-conformed surface");
                var conformedVertices = filter.sharedMesh.vertices;
                Vector3 point = (conformedVertices[0] + conformedVertices[1] + conformedVertices[2]) / 3;
                var ray = new Ray(point + Vector3.up * 100, Vector3.down);
                Check(collider.Raycast(ray, out var masterHit, 200), "Painting master raycast hits conformed terrain");
                Near(masterHit.point.y, point.y, "Painting master physics matches conformed geometry");
                tile.RefreshSurfaceCollidersFromMesh();
                Check(chunk.enabled && !collider.enabled, "Finishing painting hands collision back to chunks");
                Check(chunk.Raycast(ray, out var chunkHit, 200), "Chunk raycast hits conformed terrain");
                Near(chunkHit.point.y, point.y, "Chunk physics matches conformed geometry");
                MGRoadLayerService.Apply(new[] { aRoad });
                Near(filter.sharedMesh.vertices[0].y, 5, "Road reapply preserves loft");

                var bRoad = Road("B", 9);
                bRoad.overrideTerrain = true; bRoad.terrain.mode = RoadTerrainMode.TerrainFollowsRoad; bRoad.terrain.terrainOffset = 0; bRoad.terrain.strength = .5f;
                MGRoadLayerService.Apply(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 7, "Ordered overlapping layers compose");
                MGRoadLayerService.Apply(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 7, "Repeated Apply does not accumulate strength");
                aRoad.transform.position = new Vector3(100, 0, 0);
                MGRoadLayerService.Apply(new[] { aRoad });
                Near(filter.sharedMesh.vertices[8].y, 5.5f, "Moving road restores old footprint beneath another layer");
                MGRoadLayerService.Remove(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 2, "Remove restores base");
                Near(filter.sharedMesh.vertices[0].y, 5, "Road removal preserves loft terrain");
                Check(collider.sharedMesh == filter.sharedMesh, "Road removal keeps master synchronized");
                Undo.PerformUndo(); Near(filter.sharedMesh.vertices[8].y, 5.5f, "Remove Undo restores layer output");
                Undo.PerformRedo(); Near(filter.sharedMesh.vertices[8].y, 2, "Remove Redo restores base");
                MGRoadLayerService.Apply(new[] { bRoad });
                var sculpt = filter.sharedMesh.vertices; sculpt[8].y += 1; filter.sharedMesh.vertices = sculpt;
                MGRoadLayerService.Remove(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 3, "Independent sculpt delta survives removal");
                bRoad.terrain.autoApplyTerrain = true;
                MGRoadLayerService.UpdateLive(scene);
                Near(filter.sharedMesh.vertices[8].y, 3, "Removed layer stays paused with auto enabled");
                MGRoadLayerService.Apply(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 6, "Apply resumes from sculpted base");
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(bRoad.transform, "Move live road");
                bRoad.transform.position = new Vector3(100, 0, 0);
                MGRoadLayerService.UpdateLive(scene);
                Near(filter.sharedMesh.vertices[8].y, 3, "Live road movement restores footprint");
                Undo.PerformUndo(); Near(filter.sharedMesh.vertices[8].y, 6, "Live movement and terrain Undo together");
                Undo.PerformRedo(); Near(filter.sharedMesh.vertices[8].y, 3, "Live movement and terrain Redo together");
                bRoad.transform.position = Vector3.zero;
                MGRoadLayerService.Apply(new[] { bRoad });
                var store = tile.GetComponent<MGRoadTerrainLayers>();
                string saved = JsonUtility.ToJson(store);
                JsonUtility.FromJsonOverwrite(saved, store);
                MGRoadLayerService.Apply(new[] { bRoad });
                Near(filter.sharedMesh.vertices[8].y, 6, "Serialized layer data round-trips without accumulating changes");
                bRoad.enabled = false; MGRoadLayerService.UpdateLive(scene);
                Near(filter.sharedMesh.vertices[8].y, 3, "Disabled road restores base");
                bRoad.enabled = true; MGRoadLayerService.Apply(new[] { bRoad });
                MGRoadLayerService.Bake(world);
                Near(filter.sharedMesh.vertices[8].y, 6, "Bake preserves visible terrain");
                if (!preview) Check(AssetDatabase.Contains(filter.sharedMesh), "Bake persists the previously in-memory terrain mesh");
                Check(tile.GetComponent<MGRoadTerrainLayers>() == null && !bRoad.terrainLayerEnabled, "Bake removes history and pauses auto");
                Undo.PerformUndo(); Check(tile.GetComponent<MGRoadTerrainLayers>() != null, "Bake Undo restores editable layers");
                MGRoadLayerService.Remove(new[] { bRoad }); Near(filter.sharedMesh.vertices[8].y, 3, "Restored baked layer can be removed");
                MGRoadLayerService.Apply(new[] { bRoad });
                Object.DestroyImmediate(bRoad.gameObject); MGRoadLayerService.UpdateLive(scene);
                Near(filter.sharedMesh.vertices[8].y, 3, "Deleting road removes its contribution");
                aRoad.transform.position = Vector3.zero; MGRoadLayerService.Apply(new[] { aRoad });
                var beforeBuild = filter.sharedMesh.vertices;
                new MGRoadLayerBuildProcessor().OnProcessScene(scene, null);
                Check(tile.GetComponent<MGRoadTerrainLayers>() == null, "Build strips road authoring history");
                Near(filter.sharedMesh.vertices[8].y, beforeBuild[8].y, "Build keeps final geometry without replay");
                Check(collider.sharedMesh == filter.sharedMesh, "Playable collider uses final mesh");
                var replacement = Object.Instantiate(filter.sharedMesh); meshes.Add(replacement);
                filter.sharedMesh = replacement;
                tile.RefreshSurfaceCollidersFromMesh();
                Check(collider.sharedMesh == replacement, "Disabled master adopts replacement surface");
                var maps = new[] { new MGTerrain.SurfaceColliderVertexMap(chunk, indices) };
                tile.SetSurfaceColliderVertexMaps(maps, replacement.vertexCount + 1);
                tile.RefreshSurfaceCollidersFromMesh();
                Check(collider.enabled && collider.sharedMesh == replacement && !chunk.enabled, "Invalid chunk mapping falls back to current master");
                var fallbackVertices = replacement.vertices; fallbackVertices[0].y += 1;
                replacement.vertices = fallbackVertices;
                tile.SetSurfaceColliderVertexMaps(maps, replacement.vertexCount);
                tile.RefreshSurfaceCollidersFromMesh();
                Check(chunk.enabled && !collider.enabled && chunk.sharedMesh != null, "Repaired mapping restores disabled chunk binding");
                Near(chunk.sharedMesh.vertices[0].y, replacement.vertices[0].y, "Reactivated chunk uses latest surface");
                Debug.Log($"ROAD_LAYER_VALIDATION_PASS: {checks} checks");
            }
            finally
            {
                if (preview) EditorSceneManager.ClosePreviewScene(scene); else EditorSceneManager.CloseScene(scene, true);
                foreach (var mesh in meshes) if (mesh != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(mesh);
                    if (!string.IsNullOrEmpty(assetPath)) AssetDatabase.DeleteAsset(assetPath); else Object.DestroyImmediate(mesh);
                }
                if (original != null) Object.DestroyImmediate(original);
            }
        }
    }
}
