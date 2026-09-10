#if UNITY_EDITOR
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
