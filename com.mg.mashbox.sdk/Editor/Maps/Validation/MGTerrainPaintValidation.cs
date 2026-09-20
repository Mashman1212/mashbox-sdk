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

[InitializeOnLoad]
static class MGTerrainPaintValidation
{
    static string Folder => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/LocalizedPainting/");
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static MGTerrainPaintValidation() { EditorApplication.delayCall += Requested; }
    static void Requested()
    {
        if (!File.Exists(Folder + "validate.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "validate.request"); Run();
    }
    static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Flags).Invoke(o, args);
    static object New(string type, params object[] args) => Activator.CreateInstance(typeof(MGTerrain).GetNestedType(type, Flags), Flags, null, args, null);
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }

    [MenuItem("Tools/MashBox/MG Terrain/Validate Localized Density Painting")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.NewPreviewScene();
        var assets = new List<Object>();
        var results = new List<string>();
        MGTerrain terrain = null;
        Editor editor = null;
        var previousTool = Tools.current;
        GameObject Go(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
        }
        try
        {
            var host = Go("Paint validation"); host.SetActive(false); terrain = host.AddComponent<MGTerrain>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, new Vector3(32, 0, 0), new Vector3(0, 0, 32), new Vector3(32, 0, 32) }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            mesh.RecalculateBounds(); assets.Add(mesh); host.GetComponent<MeshFilter>().sharedMesh = mesh;
            Call(terrain, "ResolveComponents");
            var prefab = Go("Tree fixture"); prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Hidden/InternalErrorShader")); assets.Add(material);
            prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
            int tree = terrain.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Tree);
            int grass = terrain.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Detail);
            Set(terrain, "m_CombineDenseDetailMeshes", false);
            Set(terrain, "m_UseBatchRendererGroup", false);
            var layers = (IList)Get(terrain, "m_DensityDetailLayers");
            var maps = new Texture2D[2];
            for (int n = 0; n < 2; n++)
            {
                var map = new Texture2D(16, 16, TextureFormat.R16, false, true); assets.Add(map); maps[n] = map;
                var data = new ushort[256]; data[17] = data[221] = 1; map.SetPixelData(data, 0); map.Apply();
                layers.Add(Activator.CreateInstance(typeof(MGTerrain.DensityDetailLayer), Flags, null,
                    new object[] { n == 0 ? tree : grass, map, 1f, 1f, 1f, 1f, 17, 2L, 0f }, null));
            }
            var camera = Go("Validation camera").AddComponent<Camera>(); camera.enabled = false;
            var planes = new Plane[6];
            var caches = (IDictionary)Get(terrain, "m_FixedCandidateCaches");
            var chunks = (IDictionary)Get(terrain, "m_DensityDetailCache");
            void Candidates(int layer)
            {
                Call(terrain, "BuildFixedDetailCellCandidates", camera, planes, Vector3.zero, layer, layers[layer], mesh.bounds, 16, 16, 4, float.MaxValue, Get(caches[layer], "candidates"));
            }
            object Build(int layer, int x, int z)
            {
                var prototype = terrain.Prototypes[layer == 0 ? tree : grass];
                var chunk = Call(terrain, "BuildDensityDetailChunk", layer, layers[layer], prototype, mesh.bounds, 4, x, z, 0, 1f);
                chunks.Add(New("DetailChunkKey", layer, x, z, 4, 0), chunk); return chunk;
            }
            for (int n = 0; n < 2; n++) { caches.Add(n, New("FixedCandidateCache")); Candidates(n); }
            var close = Build(0, 0, 0); var distant = Build(0, 12, 12);
            var grassClose = Build(1, 0, 0); var grassFar = Build(1, 12, 12);
            var treeCache = caches[0]; var grassCache = caches[1];
            var grassOccupancy = Get(grassCache, "occupiedCells");
            var grassCandidates = Get(grassCache, "candidates");
            var treeLayer = (MGTerrain.DensityDetailLayer)layers[0];
            Set(terrain, "m_DetailRenderCacheDirty", false);
            var copy = Object.Instantiate(maps[0]); assets.Add(copy);
            Undo.RegisterCompleteObjectUndo(terrain, "Paint validation");
            treeLayer.AssignPaintMapCopy(copy, MGTerrain.DetailPaintMap.Density);
            using (var serialized = new SerializedObject(terrain)) serialized.Update();
            Check(!(bool)Get(terrain, "m_DetailRenderCacheDirty") && chunks.Count == 4, "First-stroke map copy globally invalidated rendering");
            results.Add("PASS: First-stroke map copy preserves existing cell caches without triggering full terrain validation.");
            var pixels = copy.GetPixelData<ushort>(0); pixels[17] = 0; copy.Apply();
            terrain.RefreshDetailPaintRegion(0, new Rect(1f / 16, 1f / 16, 1f / 16, 1f / 16));
            Check(!chunks.Contains(New("DetailChunkKey", 0, 0, 0, 4, 0)), "Painted tree cell retained stale instances");
            Check(ReferenceEquals(chunks[New("DetailChunkKey", 0, 12, 12, 4, 0)], distant), "Distant tree cell regenerated");
            Check(ReferenceEquals(chunks[New("DetailChunkKey", 1, 0, 0, 4, 0)], grassClose)
                && ReferenceEquals(chunks[New("DetailChunkKey", 1, 12, 12, 4, 0)], grassFar), "Grass regenerated while painting trees");
            Check(ReferenceEquals(caches[0], treeCache) && ReferenceEquals(caches[1], grassCache)
                && ReferenceEquals(Get(grassCache, "occupiedCells"), grassOccupancy)
                && ReferenceEquals(Get(grassCache, "candidates"), grassCandidates), "Unrelated candidate caches replaced");
            Check(!(bool)Get(terrain, "m_DetailRenderCacheDirty"), "Region refresh dirtied entire terrain");
            Candidates(0);
            Check(((IList)Get(treeCache, "candidates")).Count == 1, "Thinning did not remove empty candidate");
            results.Add("PASS: Thinning evicts only the touched tree cell; distant trees and overlapping grass retain the same cells and candidate caches.");
            pixels = copy.GetPixelData<ushort>(0); pixels[153] = 3; copy.Apply();
            terrain.RefreshDetailPaintRegion(0, new Rect(9f / 16, 9f / 16, 1f / 16, 1f / 16));
            Candidates(0);
            Check(((IList)Get(treeCache, "candidates")).Count == 2, "Newly occupied cell not discovered at stationary camera");
            var added = Build(0, 8, 8);
            Check((int)Get(added, "instanceCount") == 3, "Added density not rebuilt");
            terrain.RefreshDetailPaintRegion(0, default, true);
            Check(treeLayer.RepresentedInstanceCount == 4 && chunks.Count == 4, "Finishing stroke rebuilt cells or miscounted density");
            results.Add("PASS: Adding density discovers empty cells without moving the camera; stroke completion preserves caches and updates population.");

            // Exercise the actual brush's no-op path without writing assets or modifying user scenes.
            var editorType = typeof(MashBoxSDK.MapTools.MGTerrainEditor);
            editor = Editor.CreateEditor(terrain, editorType);
            Set(editor, "m_StrokeMap", copy); Set(editor, "m_PaintDetailIndex", 0); Set(editor, "m_PaintChannel", 0);
            Set(editor, "m_HasPendingPaint", false);
            Call(editor, "PaintDetailDab", terrain, new Vector3(-10000, 0, -10000), true);
            Check(!(bool)Get(editor, "m_HasPendingPaint"), "No-op brush scheduled regeneration");
            float oldRadius = MashBoxSDK.MapTools.MBEditorToolState.BrushRadius;
            float oldStrength = MashBoxSDK.MapTools.MBEditorToolState.BrushStrength;
            try
            {
                MashBoxSDK.MapTools.MBEditorToolState.BrushRadius = 100;
                MashBoxSDK.MapTools.MBEditorToolState.BrushStrength = 1;
                Set(editor, "m_PaintDensity", .1f);
                var data = copy.GetPixelData<ushort>(0);
                for (int i=0;i<data.Length;i++) data[i]=0;
                Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),false);
                var sparse = data.ToArray(); int count=0;
                foreach(var value in sparse) { Check(value<=1,"Sparse paint must store only zero or one");count+=value; }
                Check(count>10 && count<45,"0.1 target should cover approximately 10 percent of 256 texels");
                for(int dab=0;dab<20;dab++) Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),false);
                for(int i=0;i<data.Length;i++) Check(data[i]==sparse[i],"Repeated dabs must not fill sparse gaps");
                for(int i=0;i<data.Length;i++) data[i]=32;
                for(int dab=0;dab<20;dab++) Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),false);
                for(int i=0;i<data.Length;i++) Check(data[i]==sparse[i],"Repainting dense forest must converge to the same sparse pattern");
                Set(editor,"m_PaintDensity",.02f);
                for(int dab=0;dab<4;dab++) Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),false);
                for(int i=0;i<data.Length;i++) Check(data[i]<=sparse[i],"Lower targets retain a subset without reshuffling");
                Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),true);
                for(int i=0;i<data.Length;i++) Check(data[i]==0,"Erase clears sparse painting");
                Set(editor,"m_PaintDensity",1f);
                Call(editor,"PaintDetailDab",terrain,new Vector3(16,0,16),false);
                for(int i=0;i<data.Length;i++) Check(data[i]==1,"Integer one keeps original dense coverage");
                results.Add($"PASS: Fractional brush painted {count}/256 texels at 0.1; 20 overlapping dabs stayed stable; dense repaint thins to the same pattern; lower targets nest; erase and integer targets work.");
            }
            finally
            {
                MashBoxSDK.MapTools.MBEditorToolState.BrushRadius=oldRadius;
                MashBoxSDK.MapTools.MBEditorToolState.BrushStrength=oldStrength;
            }
            Set(editor, "m_StrokeMap", null);
            results.Add("PASS: A brush dab that changes no samples schedules no cell regeneration.");
            File.WriteAllLines(Folder + "validation.txt", results);
            Debug.Log("[MG Terrain Paint Validation] " + string.Join("\n", results));
        }
        catch (Exception e)
        {
            File.WriteAllText(Folder + "validation.txt", string.Join("\n", results) + "\nFAIL: " + e);
            Debug.LogException(e);
        }
        finally
        {
            if (editor != null) { Set(editor, "m_StrokeMap", null); Object.DestroyImmediate(editor); }
            Tools.current = previousTool;
            if (terrain != null) { Undo.ClearUndo(terrain); Call(terrain, "ReleaseDetailRenderCache"); }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        }
    }
}
