#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainWorldValidation
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static object Get(object target, string field) => target.GetType().GetField(field, Fields).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }



        public static void RunColliderSpeedBenchmark()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            string folder = "Assets/MGColliderSpeed_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var meshes = new System.Collections.Generic.List<Mesh>();
            try
            {
                var root = new GameObject("Collider speed benchmark"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGTerrainWorld>();
                var tiles = new MGTerrain[2];
                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    const int size = 129;
                    var vertices = new Vector3[size * size];
                    var triangles = new int[(size - 1) * (size - 1) * 6];
                    for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
                        vertices[z * size + x] = new Vector3(x * 4, Mathf.Sin(x * .1f) * Mathf.Cos(z * .1f) * 5, z * 4);
                    int index = 0;
                    for (int z = 0; z < size - 1; z++) for (int x = 0; x < size - 1; x++)
                    {
                        int a = z * size + x;
                        triangles[index++] = a; triangles[index++] = a + size; triangles[index++] = a + 1;
                        triangles[index++] = a + 1; triangles[index++] = a + size; triangles[index++] = a + size + 1;
                    }
                    var mesh = new Mesh { vertices = vertices, triangles = triangles }; mesh.RecalculateBounds(); meshes.Add(mesh);
                    var go = new GameObject("Tile " + tileIndex); go.transform.SetParent(root.transform, false); go.transform.localPosition = new Vector3(tileIndex * 512, 0, 0);
                    var tile = tiles[tileIndex] = go.AddComponent<MGTerrain>();
                    tile.MeshFilter.sharedMesh = mesh;
                    var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                    tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                }
                var timer = System.Diagnostics.Stopwatch.StartNew();
                MGTerrainWorldEditor.BuildWorldColliders(world, 50, folder);
                double first = timer.Elapsed.TotalMilliseconds;
                var initial = tiles.Select(tile => tile.SurfaceColliderChunks[0]).ToArray();
                timer.Restart();
                MGTerrainWorldEditor.BuildWorldColliders(world, 50, folder);
                double repeat = timer.Elapsed.TotalMilliseconds;
                Check(MGTerrainWorldEditor.LastColliderTilesBuilt == 0 && MGTerrainWorldEditor.LastColliderTilesSkipped == 2,
                    "Unchanged world must skip all collider rebuilds");
                Check(initial[0] == tiles[0].SurfaceColliderChunks[0] && initial[1] == tiles[1].SurfaceColliderChunks[0],
                    "Skipped tiles must preserve collider objects and assets");
                var changed = meshes[0].vertices; for (int i = 0; i < changed.Length; i++) changed[i].y += 3;
                meshes[0].vertices = changed; meshes[0].RecalculateBounds();
                timer.Restart();
                MGTerrainWorldEditor.BuildWorldColliders(world, 50, folder);
                double changedTime = timer.Elapsed.TotalMilliseconds;
                Check(MGTerrainWorldEditor.LastColliderTilesBuilt == 1 && MGTerrainWorldEditor.LastColliderTilesSkipped == 1,
                    "Editing one mesh must rebuild only that tile, even without manually marking it dirty");
                Check(initial[1] == tiles[1].SurfaceColliderChunks[0], "An unrelated tile must retain its colliders");
                // An Undo clears the optimization cache so restored data is never assumed current.
                Undo.PerformUndo();
                MGTerrainWorldEditor.BuildWorldColliders(world, 50, folder);
                Check(MGTerrainWorldEditor.LastColliderTilesBuilt == 2, "Undo must invalidate build cache");
                Debug.Log($"COLLIDER_SPEED_VALIDATION_PASS: first={first:F1}ms unchanged={repeat:F1}ms oneChanged={changedTime:F1}ms; 2 tiles, 65536 triangles, 242 chunks");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                AssetDatabase.DeleteAsset(folder);
            }
        }


        [MenuItem("Tools/MashBox/MG Terrain/Validate World Collider Controls")]
        public static void RunColliders()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            string folder = "Assets/MGWorldColliderValidation_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var mesh = new Mesh { name = "World collider validation surface" };
            int checks = 0;
            void Verify(bool condition, string message) { checks++; Check(condition, message); }
            try
            {
                var vertices = new Vector3[9];
                for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) vertices[z * 3 + x] = new Vector3(x * 50, 0, z * 50);
                var triangles = new System.Collections.Generic.List<int>();
                for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
                {
                    int corner = z * 3 + x;
                    triangles.AddRange(new[] { corner, corner + 3, corner + 1, corner + 1, corner + 3, corner + 4 });
                }
                mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateBounds();
                var root = new GameObject("Collider world"); SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGTerrainWorld>();
                MGTerrain Tile(Transform parent, string name)
                {
                    var go = new GameObject(name); go.transform.SetParent(parent, false);
                    var tile = go.AddComponent<MGTerrain>();
                    tile.MeshFilter.sharedMesh = mesh;
                    var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                    tile.Configure(tile.MeshFilter, tile.MeshRenderer, collider);
                    return tile;
                }
                var a = Tile(root.transform, "Active terrain");
                var b = Tile(root.transform, "Inactive terrain"); b.gameObject.SetActive(false);
                var nestedRoot = new GameObject("Nested world"); nestedRoot.transform.SetParent(root.transform, false);
                nestedRoot.AddComponent<MGTerrainWorld>();
                var nested = Tile(nestedRoot.transform, "Nested terrain");
                Verify(MGTerrainWorldEditor.CollisionTiles(world).Length == 2, "World collision scope includes inactive tiles and excludes nested worlds");
                MGTerrainWorldEditor.BuildWorldColliders(world, 50, folder);
                Verify(a.SurfaceColliderChunks.Count == 4 && b.SurfaceColliderChunks.Count == 4, "Build creates spatial collider chunks for every owned tile");
                Verify(nested.SurfaceColliderChunks.Count == 0, "Nested world collision remains untouched");
                Verify(a.SurfaceColliderChunks.All(c => AssetDatabase.Contains(c.sharedMesh)), "Chunk meshes are saved assets");
                Verify(!a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => c.enabled), "Build activates chunks without duplicate master collision");
                Undo.PerformUndo();
                Verify(a.SurfaceColliderChunks.Count == 0 && b.SurfaceColliderChunks.Count == 0 && a.MeshCollider.enabled, "One Undo restores all pre-build colliders");
                Undo.PerformRedo();
                Verify(a.SurfaceColliderChunks.Count == 4 && b.SurfaceColliderChunks.Count == 4, "Redo restores world collider chunks");
                MGTerrainWorldEditor.BuildWorldColliders(world, 100, folder);
                Verify(a.SurfaceColliderChunks.Count == 1 && b.SurfaceColliderChunks.Count == 1 && world.ColliderCellSize == 100, "Rebuild uses and saves the requested cell size");
                Undo.PerformUndo();
                Verify(a.SurfaceColliderChunks.Count == 4 && world.ColliderCellSize == 50, "Rebuild Undo restores chunks and cell size");

                MGTerrainWorldEditor.SetWorldCollision(world, false);
                Verify(a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled), "World master mode disables chunks");
                a.MeshCollider.enabled = false;
                foreach (var c in a.SurfaceColliderChunks) c.enabled = true;
                a.ApplyRuntimeSurfaceCollision();
                Verify(a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled), "Runtime honors saved master preference");
                MGTerrainWorldEditor.SetWorldCollision(world, true);
                a.MeshCollider.enabled = true;
                foreach (var c in a.SurfaceColliderChunks) c.enabled = false;
                a.ApplyRuntimeSurfaceCollision();
                Verify(!a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => c.enabled), "Runtime honors saved chunk preference");
                for (int i = 0; i < vertices.Length; i++) vertices[i].y = 12;
                mesh.vertices = vertices; mesh.RecalculateBounds();
                MGTerrainWorldEditor.SetWorldCollision(world, null);
                var ray = new Ray(new Vector3(10, 50, 10), Vector3.down);
                Verify(a.RaycastSurface(ray, out var hit, 100) && Mathf.Abs(hit.point.y - 12) < .001f, "World refresh updates actual chunk physics");
                MGTerrainWorldEditor.SetWorldCollision(world, false);
                MGTerrainWorldEditor.SetWorldCollision(world, null);
                Verify(a.MeshCollider.enabled && a.MeshCollider.Raycast(ray, out hit, 100) && Mathf.Abs(hit.point.y - 12) < .001f, "Refresh preserves master mode and current physics");
                b.MeshFilter.sharedMesh = null;
                bool rejected = false;
                try { MGTerrainWorldEditor.BuildWorldColliders(world, 25, folder); }
                catch (InvalidOperationException) { rejected = true; }
                Verify(rejected && a.SurfaceColliderChunks.Count == 4, "Invalid tile rejects world rebuild before changing other tiles");
                Verify(a.NeedsCollision && b.NeedsCollision, "Existing terrain defaults to collision enabled");
                Set(a, "m_NeedsCollision", false);
                Set(b, "m_NeedsCollision", false);
                a.DisableSurfaceCollision(); b.DisableSurfaceCollision();
                var chunk = a.SurfaceColliderChunks[0];
                var oldChunkVertices = chunk.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].y = 20;
                mesh.vertices = vertices; mesh.RecalculateBounds();
                a.NotifySurfaceMeshChanged();
                a.RefreshSurfaceCollidersFromMesh();
                a.ApplyRuntimeSurfaceCollision();
                Verify(!a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled),
                    "Background geometry updates and runtime handoff keep all collision disabled");
                Verify(chunk.sharedMesh.vertices.SequenceEqual(oldChunkVertices),
                    "Background updates do not synchronize chunk meshes");
                Verify(!a.HasSurfaceCollider && !a.RaycastSurface(ray, out _, 100),
                    "Background terrain is excluded from gameplay collision queries");
                Verify(a.RaycastEditingSurface(ray, out hit, 100) && Mathf.Abs(hit.point.y - 20) < .001f,
                    "Editor picking hits the current background surface after sculpting");
                Verify(!a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled),
                    "Editor picking leaves background collision disabled");
                MGTerrainWorldEditor.BuildWorldColliders(world, 25, folder);
                MGTerrainWorldEditor.SetWorldCollision(world, true);
                Verify(a.SurfaceColliderChunks[0] == chunk && !a.MeshCollider.enabled && !chunk.enabled,
                    "World actions skip background tiles, including invalid background meshes");
                Set(b, "m_NeedsCollision", true);
                b.MeshFilter.sharedMesh = mesh;
                MGTerrainWorldEditor.BuildWorldColliders(world, 100, folder);
                Verify(MGTerrainWorldEditor.LastColliderTilesBuilt == 1 && a.SurfaceColliderChunks[0] == chunk,
                    "Mixed world builds only participating tiles");
                Set(nested, "m_NeedsCollision", false);
                MGTerrainEditor.BuildSurfaceColliders(nested, 50, folder);
                Verify(nested.SurfaceColliderChunks.Count == 0 && !nested.MeshCollider.enabled,
                    "Direct tile builds skip background terrain");
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(a, "Enable terrain collision");
                foreach (var collider in a.GetComponentsInChildren<MeshCollider>(true))
                    Undo.RegisterCompleteObjectUndo(collider, "Enable terrain collision");
                using (var participation = new SerializedObject(a))
                {
                    participation.FindProperty("m_NeedsCollision").boolValue = true;
                    participation.ApplyModifiedProperties();
                }
                a.ApplySurfaceHoles();
                a.RefreshSurfaceCollidersFromMesh();
                a.ApplyRuntimeSurfaceCollision();
                Verify(a.RaycastSurface(ray, out hit, 100) && Mathf.Abs(hit.point.y - 20) < .001f,
                    "Re-enabling collision restores current geometry");
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Verify(!a.NeedsCollision && !a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled),
                    "Undo re-enabling collision restores the background opt-out");
                Undo.PerformRedo();
                Verify(a.NeedsCollision && a.RaycastSurface(ray, out hit, 100) && Mathf.Abs(hit.point.y - 20) < .001f,
                    "Redo restores current collision: needs=" + a.NeedsCollision + " masterEnabled=" + a.MeshCollider.enabled + " masterMesh=" + a.MeshCollider.sharedMesh + " chunks=" + string.Join(",", a.SurfaceColliderChunks.Select(c => c.enabled + ":" + c.sharedMesh)) + " y=" + hit.point.y);
                Verify(a.NeedsCollisionChunks && b.NeedsCollisionChunks, "Chunk participation defaults on for existing terrain");
                Set(a, "m_NeedsCollisionChunks", false);
                Set(b, "m_NeedsCollisionChunks", false);
                var retainedChunk = a.SurfaceColliderChunks[0];
                var retainedVertices = retainedChunk.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].y = 24;
                mesh.vertices = vertices; mesh.RecalculateBounds();
                a.NotifySurfaceMeshChanged();
                a.RefreshSurfaceCollidersFromMesh();
                Verify(a.NeedsCollision && a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled)
                    && a.RaycastSurface(ray, out hit, 100) && Mathf.Abs(hit.point.y - 24) < .001f,
                    "Main-only terrain keeps current player-blocking collision after edits");
                Verify(retainedChunk.sharedMesh.vertices.SequenceEqual(retainedVertices),
                    "Main-only terrain skips chunk mesh synchronization");
                MGTerrainWorldEditor.BuildWorldColliders(world, 25, folder);
                MGTerrainEditor.BuildSurfaceColliders(a, 25, folder);
                Verify(MGTerrainWorldEditor.LastColliderTilesBuilt == 0 && a.SurfaceColliderChunks[0] == retainedChunk,
                    "World and direct tile builds skip main-only terrain chunks");
                MGTerrainWorldEditor.SetWorldCollision(world, true);
                Verify(a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => !c.enabled),
                    "World chunk activation respects per-tile main-only selection");
                Set(a, "m_NeedsCollisionChunks", true);
                a.ApplySurfaceHoles();
                a.RefreshSurfaceCollidersFromMesh();
                a.ApplyRuntimeSurfaceCollision();
                Verify(!a.MeshCollider.enabled && a.SurfaceColliderChunks.All(c => c.enabled)
                    && a.RaycastSurface(ray, out hit, 100) && Mathf.Abs(hit.point.y - 24) < .001f,
                    "Opting back into chunks refreshes retained geometry before use");
                Debug.Log($"WORLD_COLLIDER_VALIDATION_PASS: {checks} checks");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(mesh);
                AssetDatabase.DeleteAsset(folder);
            }
        }


        [MenuItem("MashBox/Validation/Validate Terrain World")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run terrain world validation outside Play Mode.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh { name = "Validation terrain" };
            var density = new Texture2D(4, 4, TextureFormat.R16, false, true);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                mesh.vertices = new[] { Vector3.zero, new Vector3(512, 0, 0), new Vector3(0, 0, 512), new Vector3(512, 0, 512) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; mesh.RecalculateBounds();
                var pixels = density.GetPixelData<ushort>(0);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i * 7);
                density.Apply(false, false);
                var parent = new GameObject("Existing parent");
                SceneManager.MoveGameObjectToScene(parent, scene);
                var chunks = new MGTerrain[4];
                var snapshots = new string[4];
                var matrices = new Matrix4x4[4];
                for (int i = 0; i < chunks.Length; i++)
                {
                    var go = new GameObject("Chunk " + i);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetParent(parent.transform);
                    go.transform.position = new Vector3((i % 2) * 512, -250, (i / 2) * 512);
                    var terrain = chunks[i] = go.AddComponent<MGTerrain>();
                    Set(terrain, "m_UseSurfaceTiles", false);
                    terrain.MeshFilter.sharedMesh = mesh;
                    var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                    terrain.Configure(terrain.MeshFilter, terrain.MeshRenderer, collider);
                    terrain.ConfigureSurfaceGrid(2, 2);
                    int prototype = terrain.FindOrAddPrototype(mesh, material, MGTerrain.InstanceKind.Detail);
                    terrain.AddDensityDetailLayer(prototype, density, 1, 2, 1, 3, 123 + i, 840);
                    terrain.AddInstanceLocal(prototype, new Vector3(20, 0, 30), Quaternion.identity, Vector3.one, .2f);
                    terrain.SetControlMaps(density, null);
                    snapshots[i] = EditorJsonUtility.ToJson(terrain);
                    matrices[i] = go.transform.localToWorldMatrix;
                }
                var world = MGTerrainWorldEditor.Adopt(chunks);
                Check(world.Chunks.Count == 4, "All four chunks must register.");
                for (int i = 0; i < chunks.Length; i++)
                {
                    Check(chunks[i].World == world, "Chunk ownership failed.");
                    Check(chunks[i].transform.localToWorldMatrix == matrices[i], "Adoption moved a chunk.");
                    Check(EditorJsonUtility.ToJson(chunks[i]) == snapshots[i], "Adoption changed serialized terrain data.");
                    Check(chunks[i].DensityDetailLayers[0].DensityMap == density, "Density asset reference changed.");
                }
                Check(density.GetPixelData<ushort>(0)[15] == 105, "Paint data changed.");
                Physics.SyncTransforms();
                Check(world.RaycastSurface(new Ray(new Vector3(600, 0, 600), Vector3.down), out _, out var hit, 1000)
                    && hit == chunks[3], "World picking did not hit the correct chunk.");
                Check(MGTerrainEditor.FindWorldPaintLayer(chunks[0], 0, chunks[1]) == 0, "Matching detail layer not found.");
                chunks[1].AddDensityDetailLayer(0, density, 1, 1, 1, 1, 999, 840);
                Check(MGTerrainEditor.FindWorldPaintLayer(chunks[0], 0, chunks[1]) == -1, "Ambiguous detail layers must not be painted.");

                world.enabled = false;
                Check(chunks.All(chunk => chunk.World == null), "Disabling a world must restore standalone ownership.");
                world.enabled = true;
                Check(chunks.All(chunk => chunk.World == world), "Re-enabling a world must register children.");
                chunks[0].gameObject.SetActive(false);
                Check(world.Chunks.Count == 3, "Unloaded chunk was not unregistered.");
                chunks[0].gameObject.SetActive(true);
                Check(world.Chunks.Count == 4, "Reloaded chunk was not registered.");
                chunks[0].transform.SetParent(parent.transform, true);
                Check(chunks[0].World == null && world.Chunks.Count == 3, "Detached chunk did not become standalone.");
                chunks[0].transform.SetParent(world.transform, true);
                Check(chunks[0].World == world, "Reattached chunk did not register.");

                ValidateAllocation();
                ValidateCommands(world, chunks);
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) ValidateRendererOwnership(world, chunks, mesh, material);
                // Undo/redo uses fresh chunks to avoid unrelated validation mutations in the adoption undo group.
                var undoChunk = new GameObject("Undo chunk").AddComponent<MGTerrain>();
                SceneManager.MoveGameObjectToScene(undoChunk.gameObject, scene);
                undoChunk.transform.SetParent(parent.transform);
                var undoWorld = MGTerrainWorldEditor.Adopt(new[] { undoChunk });
                Undo.PerformUndo();
                Check(undoWorld == null && undoChunk.transform.parent == parent.transform && undoChunk.World == null,
                    "Adoption undo failed.");
                Undo.PerformRedo();
                Check(undoChunk.World != null, "Adoption redo failed.");
                Debug.Log("MG Terrain World validation PASS: four-chunk adoption, unchanged serialized paint/assets/transforms, picking, layer matching, lifecycle, undo/redo, global budget allocation, shared camera/shadow command offsets.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(density); Object.DestroyImmediate(material);
            }
        }

        static void ValidateAllocation()
        {
            var random = new System.Random(73);
            for (int sample = 0; sample < 1000; sample++)
            {
                long[] demands = Enumerable.Range(0, 4).Select(_ => (long)random.Next(0, 1000000)).ToArray();
                int budget = random.Next(1, 200000), remaining = budget;
                long total = demands.Sum(), left = total;
                foreach (long demand in demands)
                {
                    int allocated = MGTerrainWorld.AllocateDetailBudget(demand, left, remaining);
                    Check(allocated >= 0 && allocated <= demand && allocated <= remaining, "Invalid world allocation.");
                    remaining -= allocated; left -= demand;
                }
                Check(budget - remaining == Math.Min(total, budget), "Shared budget was lost or exceeded.");
            }
        }

        static void ValidateRendererOwnership(MGTerrainWorld world, MGTerrain[] chunks, Mesh mesh, Material material)
        {
            foreach (var chunk in chunks)
            {
                Check((bool)typeof(MGTerrain).GetMethod("EnsureDensityDetailBrg", Fields).Invoke(chunk, null), "BRG creation failed.");
                Check((bool)typeof(MGTerrain).GetMethod("EnsureDensityDetailBrgBuffer", Fields).Invoke(chunk, new object[] { 32, false }), "BRG buffer creation failed.");
                typeof(MGTerrain).GetMethod("GetOrRegisterBrgMesh", Fields).Invoke(chunk, new object[] { mesh });
                typeof(MGTerrain).GetMethod("GetOrRegisterBrgMaterial", Fields).Invoke(chunk, new object[] { material });
            }
            Check(world.SharedRendererCount == 1 && world.RegisteredMeshCount == 1 && world.RegisteredMaterialCount == 1,
                "World did not share its renderer and asset registrations.");
            object renderer = Get(chunks[0], "m_DetailBrg");
            Check(chunks.All(chunk => ReferenceEquals(Get(chunk, "m_DetailBrg"), renderer)), "Chunks own different renderer groups.");
            chunks[0].enabled = false;
            Check(world.SharedRendererCount == 1 && world.RegisteredMeshCount == 1 && world.RegisteredMaterialCount == 1,
                "Unloading one chunk released a neighbour's assets.");
            chunks[0].enabled = true;
            world.enabled = false;
            Check(world.SharedRendererCount == 0 && chunks.All(chunk => Get(chunk, "m_DetailBrg") == null), "Disabling the world leaked rendering resources.");
            world.enabled = true;
            Debug.Log("MG Terrain World GPU ownership PASS: shared BRG, shared mesh/material registration, per-chunk buffer cleanup, neighbour retention and world disposal.");
        }

        static void ValidateCommands(MGTerrainWorld world, MGTerrain[] chunks)
        {
            var registered = (IList)Get(world, "m_BrgChunks");
            Type groupType = typeof(MGTerrain).GetNestedType("BrgPreparedGroup", BindingFlags.NonPublic);
            for (int i = 0; i < chunks.Length; i++)
            {
                var indices = new NativeArray<int>(3, Allocator.Persistent);
                for (int j = 0; j < 3; j++) indices[j] = j;
                Set(chunks[i], "m_DetailBrgSequentialVisibleIndices", indices);
                Set(chunks[i], "m_DetailBrgVisibleCount", 3);
                var group = Activator.CreateInstance(groupType, Fields, null,
                    new object[] { default(BatchMeshID), default(BatchMaterialID), 0, 0, 3,
                        i % 2 == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off, true }, null);
                ((IList)Get(chunks[i], "m_DetailBrgPreparedGroups")).Add(group);
                registered.Add(chunks[i]);
            }
            foreach (var view in new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light })
            {
                object context = default(BatchCullingContext);
                Set(context, "viewType", view);
                var commands = new NativeArray<BatchCullingOutputDrawCommands>(1, Allocator.Temp);
                try
                {
                    var output = new BatchCullingOutput { drawCommands = commands };
                    typeof(MGTerrainWorld).GetMethod("Cull", Fields).Invoke(world, new object[] { null, context, output, IntPtr.Zero });
                    MGTerrainCommandValidation.ValidateAndRelease(commands[0], view == BatchCullingViewType.Camera ? 4 : 2);
                }
                finally { commands.Dispose(); }
            }
            registered.Clear();
        }
    }
}
#endif
