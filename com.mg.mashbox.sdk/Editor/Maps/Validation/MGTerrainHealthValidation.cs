#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MashBoxSDK.Maps.TerrainSystem;
using MashBoxSDK.MapTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class MGTerrainHealthValidation
{
    static string Folder => Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/DetailHealth/");
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static MGTerrainHealthValidation() { EditorApplication.delayCall += Requested; }
    static void Requested()
    {
        if (!File.Exists(Folder + "validate.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Folder + "validate.request"); Run();
    }
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static object Runtime(string n, params object[] a) => typeof(MGTerrain).GetMethod(n, F).Invoke(null, a);
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static float Value(Texture2D t, int i) => t.format == TextureFormat.R16 ? t.GetPixelData<ushort>(0)[i] : Mathf.HalfToFloat(t.GetPixelData<ushort>(0)[i]);

    [MenuItem("Tools/MashBox/MG Terrain/Validate Health and Multi Painting")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var assets = new List<Object>(); var tiles = new List<MGTerrain>();
        var reports = new List<string>(); Editor editor = null;
        float radius = MBEditorToolState.BrushRadius, strength = MBEditorToolState.BrushStrength;
        bool editing = MBEditorToolState.ActiveEditing; Tool tool = Tools.current;
        GameObject Go(string n) { var g = new GameObject(n); SceneManager.MoveGameObjectToScene(g, scene); return g; }
        Texture2D Map(TextureFormat f, float initial)
        {
            var t = new Texture2D(16, 16, f, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var data = new ushort[256]; Array.Fill(data, f == TextureFormat.R16 ? (ushort)initial : Mathf.FloatToHalf(initial));
            t.SetPixelData(data, 0); t.Apply(); assets.Add(t); return t;
        }
        try
        {
            var world = Go("Health validation world").AddComponent<MGTerrainWorld>(); world.enabled = false;
            var surface = new Mesh { vertices = new[] { Vector3.zero, new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(16, 0, 16) }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            surface.RecalculateBounds(); assets.Add(surface);
            var prefab = Go("Health validation detail"); prefab.AddComponent<MeshFilter>().sharedMesh = surface;
            var material = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Hidden/InternalErrorShader")); assets.Add(material);
            prefab.AddComponent<MeshRenderer>().sharedMaterial = material;
            for (int tile = 0; tile < 2; tile++)
            {
                var host = Go("Health fixture " + tile); host.SetActive(false);
                host.transform.position = new Vector3(tile * 16, 0, 0);
                var terrain = host.AddComponent<MGTerrain>(); tiles.Add(terrain);
                terrain.GetComponent<MeshFilter>().sharedMesh = surface; Call(terrain, "ResolveComponents");
                int prototype = terrain.FindOrAddPrototype(prefab, MGTerrain.InstanceKind.Detail);
                Set(terrain, "m_CombineDenseDetailMeshes", false); Set(terrain, "m_UseBatchRendererGroup", false);
                var layer = (MGTerrain.DensityDetailLayer)Activator.CreateInstance(typeof(MGTerrain.DensityDetailLayer), F, null,
                    new object[] { prototype, Map(TextureFormat.R16, 1), 1f, 1f, 1f, 1f, 17, 256L, 0f }, null);
                Set(layer, "m_SizeMap", Map(TextureFormat.RHalf, 1)); Set(layer, "m_HealthMap", Map(TextureFormat.RHalf, 1));
                ((IList)Get(terrain, "m_DensityDetailLayers")).Add(layer);
                host.SetActive(true); Set(terrain, "m_World", world);
                ((IList)Get(world, "m_Chunks")).Add(terrain);
                var owner = Editor.CreateEditor(terrain, typeof(MGTerrainEditor));
                var copies = (HashSet<Texture2D>)Get(owner, "m_PaintCopies");
                copies.Add(layer.DensityMap); copies.Add(layer.SizeMap); copies.Add(layer.HealthMap);
                Object.DestroyImmediate(owner);
            }
            var target = tiles[0]; editor = Editor.CreateEditor(target, typeof(MGTerrainEditor));
            var first = target.DensityDetailLayers[0]; var second = tiles[1].DensityDetailLayers[0];
            MBEditorToolState.BrushRadius = 4; MBEditorToolState.BrushStrength = 1;
            Set(editor, "m_PaintChannel", 2); Set(editor, "m_PaintHealth", 0f);
            void Stroke(Vector3 p, bool erase = false, bool all = false)
            {
                Call(editor, "BeginMultiDetailStroke");
                Call(editor, "PaintSelectedDetailDab", target, p, erase, all);
                Call(editor, "FinishDetailStroke"); Undo.FlushUndoRecordObjects();
            }
            int center = 8 * 16 + 8;
            Stroke(new Vector3(8.5f, 0, 8.5f));
            Check(Value(first.HealthMap, center) == 0 && Value(first.HealthMap, center + 1) > 0 && Value(first.HealthMap, center + 1) < 1, "Health brush lacks 0 endpoint or soft falloff");
            Check(Value(first.HealthMap, 0) == 1 && Value(first.DensityMap, center) == 1 && Value(first.SizeMap, center) == 1, "Health brush changed another channel or outside radius");
            Stroke(new Vector3(8.5f, 0, 8.5f), true);
            Check(Value(first.HealthMap, center) == 1, "Shift did not restore health 1");
            reports.Add("PASS: Health-only brush has soft falloff, preserves density/size and untouched areas, and Shift restores 1.");
            Set(editor, "m_PaintChannel", 3); Set(editor, "m_PaintDensity", 7f); Set(editor, "m_PaintSize", 2f); Set(editor, "m_RandomPaintSize", false);
            var before = tiles.SelectMany(t => new[] { t.DensityDetailLayers[0].DensityMap, t.DensityDetailLayers[0].SizeMap, t.DensityDetailLayers[0].HealthMap }).ToArray();
            var bytes = before.Select(t => t.GetRawTextureData<byte>().ToArray()).ToArray();
            Stroke(new Vector3(16, 0, 8.5f));
            int left = 8 * 16 + 15, right = 8 * 16;
            foreach (var pair in new[] { (first, left), (second, right) })
                Check(Value(pair.Item1.DensityMap, pair.Item2) > 1 && Value(pair.Item1.SizeMap, pair.Item2) > 1.8f && Value(pair.Item1.HealthMap, pair.Item2) < .1f, "Multi did not change all three channels across seam");
            Check(Mathf.Abs(Value(first.HealthMap, left) - Value(second.HealthMap, right)) < .001, "Health seam discontinuity");
            var painted = before.Select(t => t.GetRawTextureData<byte>().ToArray()).ToArray();
            Undo.PerformUndo();
            for (int i = 0; i < before.Length; i++) Check(before[i].GetRawTextureData<byte>().ToArray().SequenceEqual(bytes[i]), "One Undo did not restore all maps on both tiles");
            Undo.PerformRedo();
            for (int i = 0; i < before.Length; i++) Check(before[i].GetRawTextureData<byte>().ToArray().SequenceEqual(painted[i]), "One Redo did not restore all maps on both tiles");
            reports.Add("PASS: Multi paints density, size and health across a two-tile seam; one Undo/Redo restores all six maps byte-for-byte.");
            Set(editor, "m_MultiDensity", false); Set(editor, "m_MultiSize", false); Set(editor, "m_PaintHealth", .25f);
            var densityBefore = first.DensityMap.GetRawTextureData<byte>().ToArray(); var sizeBefore = first.SizeMap.GetRawTextureData<byte>().ToArray();
            Stroke(new Vector3(8.5f, 0, 8.5f));
            Check(Value(first.HealthMap, center) == .25f && first.DensityMap.GetRawTextureData<byte>().ToArray().SequenceEqual(densityBefore) && first.SizeMap.GetRawTextureData<byte>().ToArray().SequenceEqual(sizeBefore), "Multi channel toggles are ignored");
            Stroke(new Vector3(8.5f, 0, 8.5f), true, true);
            Check(Value(first.DensityMap, center) == 0 && Value(first.HealthMap, center) == .25f, "Ctrl+Shift must erase only density even in Health/Multi");
            reports.Add("PASS: Disabled Multi channels stay untouched; Ctrl+Shift erases density without resetting health.");

            // Verify CPU instance generation keeps population and transforms stable.
            Object.DestroyImmediate(editor); editor = null;
            Set(target, "m_World", null);
            Set(first, "m_DensityMap", Map(TextureFormat.R16, 1)); Set(first, "m_SizeMap", Map(TextureFormat.RHalf, 1));
            var healthMap = first.HealthMap; Set(first, "m_HealthMap", null);
            Check((uint)Call(first, "GetPaintedHealth", 0, 0) == uint.MaxValue, "Missing health map must mean 1");
            object Build() => Call(target, "BuildDensityDetailChunk", 0, first, target.Prototypes[0], surface.bounds, 16, 0, 0, 0, 1f);
            var baseline = Build(); Set(first, "m_HealthMap", Map(TextureFormat.RHalf, .25f)); var colored = Build();
            Check((int)Get(baseline, "instanceCount") == 256 && (int)Get(colored, "instanceCount") == 256, "Health changed population");
            var a = ((IList)Get(baseline, "batches"))[0]; var b = ((IList)Get(colored, "batches"))[0];
            var am = (IList)Get(a, "matrixChunks"); var bm = (IList)Get(b, "matrixChunks"); var bh = (IList)Get(b, "healthChunks");
            for (int i = 0; i < am.Count; i++)
            {
                Check(((Matrix4x4[])am[i]).SequenceEqual((Matrix4x4[])bm[i]), "Health altered placement or size");
                Check(((byte[])bh[i]).All(v => v == 64), "CPU health not attached to every instance");
            }
            string serialized = EditorJsonUtility.ToJson(target);
            Check(serialized.Contains("m_HealthMap"), "Health not serialized with terrain");
            reports.Add("PASS: CPU generation retains all 256 transforms/population and carries quantized health per instance; no map defaults to 1.");
            ValidateGpu(reports);
            foreach (string shaderName in new[] { "Shader Graphs/MG_GodGrass", "Shader Graphs/MG_GodGrass_V2" })
            {
                var shader = Shader.Find(shaderName); Check(shader != null, "Missing " + shaderName);
                Check(!ShaderUtil.ShaderHasError(shader), "Shader compilation failed: " + shaderName);
                var m = new Material(shader); assets.Add(m);
                for (int i = 0; i < 8; i++) Check(m.HasProperty($"_GrassLayer{i:00}_GrassPaintHealthStrength") && m.GetFloat($"_GrassLayer{i:00}_GrassPaintHealthHighBrightness") == 1f, "Missing per-index health controls or non-neutral default");
            }
            reports.Add("PASS: Both GodGrass shaders compile and expose health controls for all eight indices with neutral health-1 defaults.");
            File.WriteAllLines(Folder + "validation.txt", reports); Debug.Log("[MG Health Validation] " + string.Join("\n", reports));
        }
        catch (Exception e) { File.WriteAllText(Folder + "validation.txt", string.Join("\n", reports) + "\nFAIL: " + e); Debug.LogException(e); }
        finally
        {
            if (editor != null) Object.DestroyImmediate(editor);
            foreach (var t in tiles) { Undo.ClearUndo(t); Call(t, "ReleaseDetailRenderCache"); }
            foreach (var a in assets) if (a != null) Undo.ClearUndo(a);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var a in assets) if (a != null) Object.DestroyImmediate(a);
            MBEditorToolState.BrushRadius = radius; MBEditorToolState.BrushStrength = strength;
            MBEditorToolState.ActiveEditing = editing; Tools.current = tool;
        }
    }

    static void ValidateGpu(List<string> reports)
    {
        var compute = Resources.Load<ComputeShader>("MGTerrainDetailInstances"); Check(compute != null, "Missing GPU generator");
        var type = typeof(MGTerrain).GetNestedType("GpuDetailSpawnCommand", F);
        var layerType = typeof(MGTerrain).GetNestedType("GpuDetailLayerParameters", F);
        Check(Marshal.SizeOf(type) == 48, "GPU command stride changed");
        var command = Activator.CreateInstance(type); Set(command, "count", 64u); Set(command, "padding0", 64u); Set(command, "padding1", 8u); Set(command, "sizes", Vector4.one);
        var layer = Activator.CreateInstance(layerType);
        Set(layer, "width", 1u); Set(layer, "height", 1u); Set(layer, "seed", 17u);
        foreach (string n in new[] { "minWidth", "maxWidth", "minHeight", "maxHeight" }) Set(layer, n, 1f);
        Set(layer, "padding1", 1.75f);
        var commands = Array.CreateInstance(type, 1); var layers = Array.CreateInstance(layerType, 1); layers.SetValue(layer, 0);
        using var cb = new ComputeBuffer(1, 48); using var lb = new ComputeBuffer(1, Marshal.SizeOf(layerType));
        using var heights = new ComputeBuffer(4, 4); using var output = new ComputeBuffer(64 * 28, 4, ComputeBufferType.Raw);
        lb.SetData(layers); heights.SetData(new float[4]);
        int k = compute.FindKernel("GenerateDetailInstances");
        compute.SetBuffer(k, "_Commands", cb); compute.SetBuffer(k, "_Layers", lb); compute.SetBuffer(k, "_SurfaceHeights", heights); compute.SetBuffer(k, "_InstanceData", output);
        compute.SetInt("_CommandOffset", 0); compute.SetInt("_GroupOutputBase", 0);
        compute.SetInt("_ObjectToWorldAddress", 0); compute.SetInt("_WorldToObjectAddress", 64 * 48); compute.SetInt("_DetailDataAddress", 64 * 96);
        compute.SetInt("_SurfaceGridWidth", 2); compute.SetInt("_SurfaceGridHeight", 2);
        compute.SetVector("_SurfaceMinMax", new Vector4(0, 0, 1, 1)); compute.SetMatrix("_TerrainLocalToWorld", Matrix4x4.identity); compute.SetMatrix("_PartRelative", Matrix4x4.identity);
        float[] baseline = null;
        foreach (uint packed in new[] { uint.MaxValue, 0u, 0xFFAA5500u })
        {
            Set(command, "health", packed); commands.SetValue(command, 0); cb.SetData(commands); compute.Dispatch(k, 1, 1, 1);
            var data = new float[64 * 28]; output.GetData(data);
            for (int i = 0; i < 64; i++)
            {
                float x = data[i * 12 + 9], z = data[i * 12 + 11];
                float expected = (float)Runtime("EncodeDetailLayerHealth", 0, (float)Runtime("InterpolateDetailHealth", packed, x, z));
                int offset = 64 * 24 + i * 4;
                Check(Mathf.Abs(expected - data[offset]) < .00001f, "GPU/CPU health interpolation mismatch");
                Check(Mathf.FloorToInt(data[offset + 1]) == 8 && data[offset + 3] == 1.75f, "Health corrupted slice or fade");
                if (baseline != null) for (int j = 0; j < 24; j++) Check(data[i * 12 + (j % 12) + (j >= 12 ? 64 * 12 : 0)] == baseline[i * 12 + (j % 12) + (j >= 12 ? 64 * 12 : 0)], "Health changed GPU transform");
            }
            baseline = data;
        }
        reports.Add("PASS: Actual GPU dispatch matches CPU interpolation at 64 jittered positions for healthy, dead and gradient maps; transforms, slice and fade remain unchanged; spawn command stays 48 bytes.");
        var probe = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HealthPaintProbe.compute");
        if (probe != null)
        {
            using var result = new ComputeBuffer(8, 16); probe.SetBuffer(0, "_Result", result); probe.Dispatch(0, 1, 1, 1);
            var colors = new Vector4[8]; result.GetData(colors);
            Check((colors[0] - new Vector4(.2f, .4f, .6f, 1)).sqrMagnitude < .000001f, "Health 1 changes default color");
            Check((colors[1] - colors[0]).sqrMagnitude > .02f, "Health 0 has no visual effect");
            Check((colors[2] - colors[0]).sqrMagnitude < .000001f, "Strength 0 is not neutral");
            Check((colors[3] - Vector4.one).sqrMagnitude < .000001f, "Legacy/new health metadata decode incorrect");
            Check(colors[4].x == 7 && colors[4].y == 3, "Layer selection/shared fallback failed");
            reports.Add("PASS: GPU shader color probe confirms neutral health 1, visible low-health tint, strength-0 bypass, legacy metadata compatibility and per-index/shared selection.");
        }
    }
}
#endif
