using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Synthetic preview-scene checks: never edits a user's scene or prefab assets.
[InitializeOnLoad]
static class MGTerrainTreeLodValidation
{
    static string Folder => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/TerrainTreeLod/");
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static MGTerrainTreeLodValidation() { EditorApplication.delayCall += RunRequested; }
    static void RunRequested()
    {
        if (!File.Exists(Folder + "validate.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "validate.request");
        Run();
    }
    static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static object StaticCall(string name, params object[] args) => typeof(MGTerrain).GetMethod(name, Flags).Invoke(null, args);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/MashBox/MG Terrain/Validate Terrain Tree LODs")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.NewPreviewScene();
        var assets = new List<Object>();
        var results = new List<string>();
        MGTerrain terrain = null;
        GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
        try
        {
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader");
            var material = new Material(shader); assets.Add(material);
            var prefab = NewObject("Synthetic tree");
            var renderers = new MeshRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var child = NewObject("LOD " + i); child.transform.SetParent(prefab.transform, false);
                var mesh = new Mesh { name = "Tree level " + i };
                mesh.vertices = new[] { new Vector3(-4, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 20, 0) };
                mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateBounds(); assets.Add(mesh);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderers[i] = child.AddComponent<MeshRenderer>(); renderers[i].sharedMaterial = material;
            }
            prefab.AddComponent<LODGroup>().SetLODs(new[] { new LOD(.5f, new[] { renderers[0] }),
                new LOD(.15f, new[] { renderers[1] }), new LOD(.01f, new[] { renderers[2] }), new LOD(0, Array.Empty<Renderer>()) });
            var terrainObject = NewObject("Validation terrain"); terrainObject.SetActive(false);
            terrain = terrainObject.AddComponent<MGTerrain>();
            var surface = new Mesh { vertices = new[] { Vector3.zero, new Vector3(32, 0, 0), new Vector3(0, 0, 32), new Vector3(32, 0, 32) },
                triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            surface.RecalculateBounds(); assets.Add(surface);
            terrainObject.GetComponent<MeshFilter>().sharedMesh = surface;
            Call(terrain, "ResolveComponents");
            int prototypeIndex = terrain.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Tree);
            var prototype = ((IList)Get(terrain, "m_Prototypes"))[prototypeIndex];
            Set(prototype, "m_TreeLodCount", 0);
            Set(prototype, "m_TreeLod1Distance", 0f);
            ((ISerializationCallbackReceiver)prototype).OnAfterDeserialize();
            Check(((MGTerrain.Prototype)prototype).TreeLodCount == 3 && ((MGTerrain.Prototype)prototype).TreeLod1Distance == 60f, "Existing prototype migration");
            int Lod(float distance, int previous = -1) => (int)StaticCall("SelectTreeLod", prototype, distance, previous);
            Check(Lod(0) == 0 && Lod(100) == 1 && Lod(1000) == 2, "Three distance bands");
            Check(Lod(63, 0) == 0 && Lod(66, 0) == 1 && Lod(57, 1) == 1 && Lod(54, 1) == 0, "Near boundary hysteresis");
            Check(Lod(204, 1) == 1 && Lod(206, 1) == 2 && Lod(196, 2) == 2 && Lod(194, 2) == 1, "Far boundary hysteresis");
            Set(prototype, "m_TreeLodCount", 2); Check(Lod(10000) == 1, "Two-level cap");
            Set(prototype, "m_TreeLodCount", 1); Check(Lod(10000) == 0, "Single-level cap");
            Set(prototype, "m_TreeLodCount", 3);
            results.Add("PASS: LOD selection, 1/2/3-level caps, hysteresis and camera teleports.");
            var sizeShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/MapiX/MashBox/ImpostorBaker/Resources/ImpostorRuntime/OctahedralImpostor.shadergraph");
            var sizeMaterial = new Material(sizeShader); assets.Add(sizeMaterial);
            var sizeMesh = new Mesh(); assets.Add(sizeMesh);
            sizeMesh.bounds = new Bounds(new Vector3(4,4,4), Vector3.one*2);
            Check(((Bounds)StaticCall("TreeDistanceBounds", sizeMesh, sizeMaterial)).Equals(sizeMesh.bounds), "Neutral size does not inflate culling bounds");
            sizeMaterial.SetFloat("_ImpostorFarWidth",2); sizeMaterial.SetFloat("_ImpostorFarHeight",.5f);
            var sizeBounds = (Bounds)StaticCall("TreeDistanceBounds",sizeMesh,sizeMaterial);
            Check(sizeBounds.Contains(new Vector3(10,1.5f,10)) && sizeBounds.Contains(sizeMesh.bounds.min), "Bounds cover both original and scaled endpoints including off-origin shrink");
            results.Add("PASS: Impostor bounds remain unchanged at neutral size and contain enlarged/shrunk endpoints.");

            // Simulate an already-cached prefab whose LOD renderer assignments changed.
            var sourceMeshes = new List<Mesh>();
            terrain.GetTreeLodSourceMeshes(prototypeIndex, 0, sourceMeshes);
            Check(sourceMeshes.Count == 1 && sourceMeshes[0] == renderers[0].GetComponent<MeshFilter>().sharedMesh, "Prime close LOD cache");
            var group = prefab.GetComponent<LODGroup>();
            var originalLods = group.GetLODs();
            var changedLods = group.GetLODs();
            changedLods[0].renderers = new Renderer[] { renderers[1] };
            changedLods[1].renderers = new Renderer[] { renderers[0] };
            group.SetLODs(changedLods);
            string paintedState = EditorJsonUtility.ToJson(terrain);
            terrain.RefreshPrototypeAssets();
            terrain.GetTreeLodSourceMeshes(prototypeIndex, 0, sourceMeshes);
            Check(sourceMeshes.Count == 1 && sourceMeshes[0] == renderers[1].GetComponent<MeshFilter>().sharedMesh,
                "Refresh exposes new close mesh before any camera render");
            terrain.GetTreeLodSourceMeshes(prototypeIndex, 1, sourceMeshes);
            Check(sourceMeshes.Count == 1 && sourceMeshes[0] == renderers[0].GetComponent<MeshFilter>().sharedMesh,
                "Refresh exposes new medium mesh before any camera render");
            Check(EditorJsonUtility.ToJson(terrain) == paintedState, "Refresh preserves serialized terrain data");
            group.SetLODs(originalLods);
            terrain.RefreshPrototypeAssets();
            results.Add("PASS: Cached prefab LOD edits refresh before rendering and preserve serialized terrain data.");

            var parts = (IList)Get(Call(terrain, "GetDenseDetailRenderParts", prototype), "parts");
            Check(parts.Count == 3, "Three distinct mesh levels (terminal cull level skipped)");
            for (int lod = 0; lod < 3; lod++)
            {
                var selected = (IList)Call(terrain, "GetRenderParts", prototype, lod);
                Check(selected.Count == 1 && (Mesh)Get(selected[0], "mesh") == renderers[lod].GetComponent<MeshFilter>().sharedMesh, "Correct LOD mesh");
            }
            results.Add("PASS: Prefab LODGroup extraction selects the requested mesh and ignores terminal cull-only LOD.");
            // Match a two-level tree prefab with an old, disabled impostor left
            // outside its LODGroup. Medium and far must contain only the voxel LOD.
            var hiddenImpostor = NewObject("Disabled old impostor");
            hiddenImpostor.transform.SetParent(prefab.transform, false);
            hiddenImpostor.AddComponent<MeshFilter>().sharedMesh = renderers[2].GetComponent<MeshFilter>().sharedMesh;
            hiddenImpostor.AddComponent<MeshRenderer>().sharedMaterial = material;
            hiddenImpostor.SetActive(false);
            var hiddenParent = NewObject("Disabled old impostor parent");
            hiddenParent.transform.SetParent(prefab.transform, false);
            hiddenParent.SetActive(false);
            var hiddenChild = NewObject("Enabled renderer under disabled parent");
            hiddenChild.transform.SetParent(hiddenParent.transform, false);
            hiddenChild.AddComponent<MeshFilter>().sharedMesh = renderers[2].GetComponent<MeshFilter>().sharedMesh;
            hiddenChild.AddComponent<MeshRenderer>().sharedMaterial = material;
            renderers[2].gameObject.SetActive(false);
            group.SetLODs(new[] { originalLods[0], originalLods[1] });
            terrain.RefreshPrototypeAssets();
            for (int lod = 0; lod < 3; lod++)
            {
                terrain.GetTreeLodSourceMeshes(prototypeIndex, lod, sourceMeshes);
                Check(sourceMeshes.Count == 1 && sourceMeshes[0] == renderers[Math.Min(lod, 1)].GetComponent<MeshFilter>().sharedMesh,
                    "Two-level tree excludes inactive impostors and reuses medium mesh for far");
            }
            // An active, ungrouped attachment must still be included normally.
            hiddenImpostor.SetActive(true);
            terrain.RefreshPrototypeAssets();
            terrain.GetTreeLodSourceMeshes(prototypeIndex, 1, sourceMeshes);
            Check(sourceMeshes.Count == 2, "Active ungrouped attachments remain supported");
            Object.DestroyImmediate(hiddenImpostor);
            Object.DestroyImmediate(hiddenParent);
            renderers[2].gameObject.SetActive(true);
            group.SetLODs(originalLods);
            terrain.RefreshPrototypeAssets();
            results.Add("PASS: Inactive children/ancestors excluded; two-level trees reuse only LOD 1 at medium/far; active attachments retained.");

            var missingOverride = NewObject("Removed LOD override");
            Set(prototype, "m_TreeLod1Prefab", missingOverride);
            Set(prototype, "m_TreeLod2Prefab", missingOverride);
            Object.DestroyImmediate(missingOverride);
            for (int lod = 1; lod <= 2; lod++)
            {
                var selected = (IList)Call(terrain, "GetRenderParts", prototype, lod);
                Check(selected.Count == 1 && (Mesh)Get(selected[0], "mesh") == renderers[lod].GetComponent<MeshFilter>().sharedMesh,
                    "Unity null override must use source prefab LOD, not fall back to previous mesh");
            }
            Set(prototype, "m_TreeLod1Prefab", null); Set(prototype, "m_TreeLod2Prefab", null);
            results.Add("PASS: Unity null override references resolve the source prefab's medium/far meshes.");
            terrain.AddInstance(prefab, MGTerrain.InstanceKind.Tree, new Vector3(8, 0, 8), Quaternion.identity, Vector3.one);
            terrain.AddInstance(prefab, MGTerrain.InstanceKind.Tree, new Vector3(48, 0, 8), Quaternion.identity, Vector3.one);
            Call(terrain, "RebuildRenderCache", Matrix4x4.identity);
            var cells = (IList)Get(terrain, "m_TreeInstanceCells");
            Check(cells.Count == 2, "Serialized placements are spatially partitioned");
            Check(((IList)Get(terrain, "m_DrawBatches")).Count == 0, "Trees are not also submitted as regular LOD0 batches");
            foreach (var cell in cells)
            {
                var batches = (IList)Get(cell, "batches"); Check(batches.Count == 3, "Three cached variants");
                object matrices = ((IList)Get(batches[0], "matrixChunks"))[0];
                Check(ReferenceEquals(matrices, ((IList)Get(batches[1], "matrixChunks"))[0])
                    && ReferenceEquals(matrices, ((IList)Get(batches[2], "matrixChunks"))[0]), "Serialized LODs share transform arrays");
                Check(((Bounds)Get(cell, "bounds")).size.y >= 20, "Tree canopy included in cell bounds");
            }
            results.Add("PASS: Serialized trees use separate spatial cells, shared LOD matrix arrays, and full canopy bounds.");

            var density = new Texture2D(2, 2, TextureFormat.R16, false, true); assets.Add(density);
            density.SetPixelData(new ushort[] { 1, 1, 1, 1 }, 0); density.Apply();
            var layer = Activator.CreateInstance(typeof(MGTerrain.DensityDetailLayer), Flags, null,
                new object[] { prototypeIndex, density, 1f, 1f, 1f, 1f, 17, 4L, 0f }, null);
            ((IList)Get(terrain, "m_DensityDetailLayers")).Add(layer);
            var chunk = Call(terrain, "BuildDensityDetailChunk", 0, layer, prototype, surface.bounds, 2, 0, 0, 2, .01f);
            Check((int)Get(chunk, "instanceCount") == 4, "Trees ignore grass generation thinning");
            var treeBatches = (IList)Get(chunk, "batches");
            Check(treeBatches.Count == 3, "Painted cells cache all LODs");
            Check(ReferenceEquals(((IList)Get(treeBatches[0], "matrixChunks"))[0], ((IList)Get(treeBatches[2], "matrixChunks"))[0]), "Painted LODs share matrices");
            var visibleType = typeof(MGTerrain).GetNestedType("VisibleDensityDetail", Flags);
            for (int lod = 0; lod < 3; lod++)
            {
                var visible = Activator.CreateInstance(visibleType, Flags, null, new object[] { chunk, prototype, 1000f, lod }, null);
                Check((int)Call(terrain, "GetVisibleDensityDetailInstanceCount", visible) == 4, "Population stable across LODs");
                int selectedCount = 0;
                foreach (var batch in treeBatches) if ((bool)StaticCall("TreeBatchVisible", batch, lod)) selectedCount++;
                Check(selectedCount == 1, "Exactly one tree representation selected");
            }
            Check(((Bounds)Get(chunk, "worldBounds")).max.y >= 20, "Painted canopy bounds");
            Set(prototype, "m_TreeMidDensity", .5f); Set(prototype, "m_TreeFarDensity", .25f);
            foreach (int levels in new[] { 1, 2, 3 })
            {
                Set(prototype, "m_TreeLodCount", levels);
                foreach (var sample in new[] { (10f, 4), (100f, 2), (300f, 1) })
                {
                    var visible = Activator.CreateInstance(visibleType, Flags, null, new object[] { chunk, prototype, sample.Item1, 0 }, null);
                    Check((int)Call(terrain, "GetVisibleDensityDetailInstanceCount", visible) == sample.Item2, "Density bands independent of mesh LOD count");
                }
            }
            Set(prototype, "m_TreeFarDensity", 0f);
            var emptyFar = Activator.CreateInstance(visibleType, Flags, null, new object[] { chunk, prototype, 300f, 2 }, null);
            Check((int)Call(terrain, "GetVisibleDensityDetailInstanceCount", emptyFar) == 0, "Zero far density");
            ((ISerializationCallbackReceiver)prototype).OnAfterDeserialize();
            Check(((MGTerrain.Prototype)prototype).TreeFarDensity == 0f, "Explicit zero density survives deserialization");
            Set(prototype, "m_TreeDensityInitialized", false);
            ((ISerializationCallbackReceiver)prototype).OnAfterDeserialize();
            Check(((MGTerrain.Prototype)prototype).TreeMidDensity == 1f && ((MGTerrain.Prototype)prototype).TreeFarDensity == 1f, "Legacy density migration retains trees");
            Set(prototype, "m_TreeLodCount", 3);
            Check((int)Get(chunk, "instanceCount") == 4 && density.GetPixelData<ushort>(0)[0] == 1, "Density controls preserve resident population and painted map");
            results.Add("PASS: Independent near/mid/far density with 1/2/3 mesh LODs, zero density, migration, unchanged painted maps and resident transforms.");
            results.Add("PASS: Painted trees preserve population across all distance bands and share matrix arrays; exactly one LOD is selected.");
            var cameraObject = NewObject("Validation camera"); var camera = cameraObject.AddComponent<Camera>();
            Set(terrain, "m_UseDetailDensityLod", false);
            int CellLod(float distance) => (int)Call(terrain, "GetFixedDetailCellDensityLod", camera, 0, 0, 0, 2, distance);
            Check(CellLod(0) == 0 && CellLod(1000) == 2 && CellLod(0) == 0, "Tree LOD independent of grass density switch");
            results.Add("PASS: Fixed-cell tree LOD switching works with grass density LOD disabled.");

            // Exercise actual compute dispatch, resident aliasing and BRG draw selection
            // synchronously in the preview scene, without entering the user's Play Mode.
            Check((bool)Call(terrain, "CanUseGpuGeneratedDensityDetailBrg"), "Compute generator available");
            Set(chunk, "gpuProcedural", true);
            var spawnType = typeof(MGTerrain).GetNestedType("DensityDetailSpawn", Flags);
            var spawns = (IList)Get(chunk, "proceduralSpawns");
            for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
                spawns.Add(Activator.CreateInstance(spawnType, Flags, null, new object[] { x, z, 1, Vector4.one, 0 }, null));
            var visibleCells = (IList)Get(terrain, "m_VisibleDensityDetails");
            var residentCells = (IList)Get(terrain, "m_ResidentDensityDetails");
            object Visible(int lod) => Activator.CreateInstance(visibleType, Flags, null, new object[] { chunk, prototype, lod * 150f, lod }, null);
            visibleCells.Clear(); residentCells.Clear(); visibleCells.Add(Visible(0)); residentCells.Add(Visible(0));
            Check((bool)Call(terrain, "TryPrepareGpuGeneratedDensityDetailBrg", camera, 4, 1f, 1f), "GPU preparation succeeded");
            var gpuCells = (IList)Get(terrain, "m_ResidentGpuCells");
            Check(gpuCells.Count == 3, "Three resident GPU variants");
            Check((int)Get(terrain, "m_ResidentGpuEnd") == 4, "All three GPU variants use one four-instance range");
            Check(terrain.LastRegeneratedDetailInstances == 4, "Only one set of transforms generated");
            int gpuStart = (int)Get(gpuCells[0], "start");
            foreach (var gpuCell in gpuCells) Check((int)Get(gpuCell, "start") == gpuStart, "Shared GPU range address");
            var gpuBuffer = (GraphicsBuffer)Get(terrain, "m_DetailBrgInstanceBuffer");
            var before = new float[4 * 12]; gpuBuffer.GetData(before, 0, 24 + gpuStart * 12, before.Length);
            for (int lod = 1; lod <= 2; lod++)
            {
                visibleCells.Clear(); visibleCells.Add(Visible(lod));
                Check((bool)Call(terrain, "UpdateResidentGpuVisibility", 4, 1f, 1f), "GPU LOD visibility update");
                Check(terrain.LastRegeneratedDetailInstances == 0, "LOD change regenerated no transforms");
                var prepared = (IList)Get(terrain, "m_DetailBrgPreparedGroups");
                Check(prepared.Count == 1, "Only selected GPU LOD submitted");
                object expectedMeshId = Call(terrain, "GetOrRegisterBrgMesh", renderers[lod].GetComponent<MeshFilter>().sharedMesh);
                Check(Get(prepared[0], "meshId").Equals(expectedMeshId), "Correct GPU mesh selected");
                Check(ReferenceEquals(gpuBuffer, Get(terrain, "m_DetailBrgInstanceBuffer")), "GPU buffer retained");
                var after = new float[before.Length]; gpuBuffer.GetData(after, 0, 24 + gpuStart * 12, after.Length);
                for (int i = 0; i < before.Length; i++) Check(before[i].Equals(after[i]), "GPU transforms unchanged");
            }
            Set(prototype, "m_TreeMidDensity", .5f); Set(prototype, "m_TreeFarDensity", .25f);
            foreach (int lod in new[] { 1, 2, 0 })
            {
                visibleCells.Clear(); visibleCells.Add(Visible(lod));
                Check((bool)Call(terrain, "UpdateResidentGpuVisibility", 4, 1f, 1f), "Density GPU visibility update");
                var prepared = (IList)Get(terrain, "m_DetailBrgPreparedGroups");
                Check(prepared.Count == 1 && (uint)Get(prepared[0], "visibleCount") == (lod == 1 ? 2u : lod == 2 ? 1u : 4u), "GPU density submits exact retained population");
                Check(ReferenceEquals(gpuBuffer, Get(terrain, "m_DetailBrgInstanceBuffer")), "Density preserves GPU buffer");
                var after = new float[before.Length]; gpuBuffer.GetData(after, 0, 24 + gpuStart * 12, after.Length);
                for (int i = 0; i < before.Length; i++) Check(before[i].Equals(after[i]), "Density leaves GPU transforms unchanged");
            }
            Set(prototype, "m_TreeMidDensity", 1f); Set(prototype, "m_TreeFarDensity", 1f);
            results.Add("PASS: Actual GPU density updates submit 2/1/4 trees at mid/far/near while retaining the same transform buffer.");
            visibleCells.Clear(); residentCells.Clear();
            Check((bool)Call(terrain, "TryPrepareGpuGeneratedDensityDetailBrg", camera, 4, 1f, 1f), "GPU cell release succeeded");
            Check(((IList)Get(terrain, "m_ResidentGpuCells")).Count == 0, "GPU aliases evicted");
            var free = (IList)Get(terrain, "m_FreeGpuRanges");
            Check(free.Count == 1 && (int)Get(free[0], "count") == 4, "Shared range freed exactly once");
            Call(terrain, "ReleaseDensityDetailBrg");
            results.Add("PASS: Real GPU dispatch creates four transforms once, shares them across three LODs, switches mesh without regeneration or buffer replacement, and frees the shared range once.");
            prefab.GetComponent<LODGroup>().SetLODs(new[] { new LOD(.01f, new[] { renderers[0] }) });
            Object.DestroyImmediate(renderers[1].gameObject); Object.DestroyImmediate(renderers[2].gameObject);
            Call(terrain, "ReleaseDetailRenderCache");
            parts = (IList)Get(Call(terrain, "GetDenseDetailRenderParts", prototype), "parts");
            Check(parts.Count == 1 && (int)Get(parts[0], "treeLodMask") == 7, "Missing levels reuse one draw part");
            results.Add("PASS: Single-mesh prefabs safely reuse one part for all LOD bands.");
            var bakedTree = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MapiX/MashBox/ImpostorBakes/RFMP_Connifer_06/RFMP_Connifer_06.prefab");
            Check(bakedTree != null && bakedTree.GetComponent<LODGroup>().lodCount == 2, "Baked conifer exposes mesh and impostor LODs");
            int bakedIndex = terrain.FindOrAddPrototype(bakedTree, MGTerrain.InstanceKind.Tree);
            var closeMeshes = new List<Mesh>(); var mediumMeshes = new List<Mesh>(); var farMeshes = new List<Mesh>();
            terrain.GetTreeLodSourceMeshes(bakedIndex, 0, closeMeshes);
            terrain.GetTreeLodSourceMeshes(bakedIndex, 1, mediumMeshes);
            terrain.GetTreeLodSourceMeshes(bakedIndex, 2, farMeshes);
            Check(closeMeshes.Count == 1 && mediumMeshes.Count == 1 && farMeshes.Count == 1,
                "Baked conifer selects one representation per band");
            Check(closeMeshes[0] != mediumMeshes[0] && mediumMeshes[0] == farMeshes[0], "Automatic conifer mesh/impostor selection");
            results.Add("PASS: RFMP_Connifer_06 automatically resolves its full tree for Close and its impostor for Medium/Far, without overlapping representations.");
            foreach (var loadedTerrain in Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorSceneManager.IsPreviewScene(loadedTerrain.gameObject.scene)) continue;
                foreach (MGTerrain.Prototype entry in (IList)Get(loadedTerrain, "m_Prototypes"))
                    if (entry != null && entry.Kind == MGTerrain.InstanceKind.Tree && entry.Prefab != null && entry.Prefab.name == "RFMP_Connifer_06")
                    {
                        string diagnostic = "Scene conifer source: " + AssetDatabase.GetAssetPath(entry.Prefab)
                            + " | direct mesh=" + (entry.Mesh != null ? entry.Mesh.name : "null")
                            + " | direct material=" + (entry.Material != null ? entry.Material.name : "null")
                            + " | children=" + entry.Prefab.transform.childCount
                            + " | groups=" + entry.Prefab.GetComponentsInChildren<LODGroup>(true).Length
                            + " | parent=" + (entry.Prefab.transform.parent != null ? entry.Prefab.transform.parent.name : "null");
                        if (!results.Contains(diagnostic)) results.Add(diagnostic);
                        for (int lod = 0; lod < entry.TreeLodCount; lod++)
                        {
                            var actual = (IList)Call(loadedTerrain, "GetRenderParts", entry, lod);
                            var description = new List<string>();
                            foreach (var part in actual) description.Add(((Mesh)Get(part, "mesh")).name);
                            results.Add(loadedTerrain.name + " raw LOD " + lod + ": " + string.Join(",", description));
                        }
                    }
            }
            File.WriteAllLines(Folder + "validation.txt", results);
            Debug.Log("MG Terrain tree LOD validation passed (" + results.Count + " groups).");
        }
        catch (Exception exception)
        {
            results.Add("FAIL: " + exception); File.WriteAllLines(Folder + "validation.txt", results);
            Debug.LogException(exception);
        }
        finally
        {
            if (terrain != null) { Call(terrain, "ReleaseDetailRenderCache"); Call(terrain, "ReleaseInstancedMaterials"); }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        }
    }
}
