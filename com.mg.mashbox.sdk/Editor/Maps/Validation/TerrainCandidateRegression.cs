using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

static class TerrainCandidateRegression
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    [MenuItem("Tools/MashBox/MG Terrain/Check Terrain Candidate Selection")]
    static void Once()
    {
        if (!Application.isPlaying) { UnityEngine.Debug.LogWarning("Run the candidate regression in Play Mode."); return; }
        string name = typeof(MGTerrain).GetNestedType("FixedCandidateCache", BindingFlags.NonPublic)
            .GetField("geometryBounds", Flags) != null ? (typeof(MGTerrain).GetMethod("PrioritizeFixedDetailCandidates", Flags) != null ? "after-budget" : "after") : "before";
        string output = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MGTerrain).Assembly).resolvedPath, "Development~/MGTerrainImplementation~/TerrainCandidateFix/", name + ".txt");
        Run(output);
    }
    static void Run(string output)
    {
        var root = new GameObject("Candidate regression fixture"); root.SetActive(false);
        Mesh mesh = null; Texture2D density = null;
        try
        {
            const int size = 257, resolution = 1024;
            var vertices = new Vector3[size * size];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
                vertices[z * size + x] = new Vector3(x * 2, Mathf.Sin(x * .1f) * Mathf.Cos(z * .1f) * 30, z * 2);
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices };
            mesh.RecalculateBounds();
            var mf = root.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            var terrain = root.AddComponent<MGTerrain>(); terrain.Configure(mf, mr, null); terrain.ConfigureSurfaceGrid(size, size);
            density = new Texture2D(resolution, resolution, TextureFormat.R16, false);
            int prototype = terrain.FindOrAddPrototype(root, MGTerrain.InstanceKind.Detail);
            terrain.AddDensityDetailLayer(prototype, density, 1, 1, 1, 2, 1, 0);
            var type = typeof(MGTerrain);
            type.GetField("m_PrewarmFixedDetailCells", Flags).SetValue(terrain, false);
            type.GetField("m_UseBatchRendererGroup", Flags).SetValue(terrain, true);
            type.GetField("m_KeepAllDetailCellsResident", Flags).SetValue(terrain, false);
            var cacheType = type.GetNestedType("FixedCandidateCache", BindingFlags.NonPublic);
            var cache = Activator.CreateInstance(cacheType, true);
            ((IDictionary)type.GetField("m_FixedCandidateCaches", Flags).GetValue(terrain)).Add(0, cache);
            var candidates = (IList)cacheType.GetField("candidates", Flags).GetValue(cache);
            var camera = root.AddComponent<Camera>(); camera.enabled = false;
            var planes = new Plane[6]; for (int i = 0; i < 6; i++) planes[i] = new Plane(Vector3.up, 10000);
            var build = type.GetMethod("BuildFixedDetailCellCandidates", Flags);
            object[] args = { camera, planes, Vector3.zero, 0, terrain.DensityDetailLayers[0], mesh.bounds,
                resolution, resolution, 8, 220f, candidates };
            void Call(int i)
            {
                args[2] = new Vector3(250 + i % 8, 10, 250 + i % 5);
                build.Invoke(terrain, args);
            }
            for (int i = 0; i < 24; i++) Call(i);
            int beforeBounds = terrain.LastDetailCandidateBoundsBuilt;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 24; i++) Call(i);
            watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long fingerprint = 17;
            foreach (var candidate in candidates)
            {
                var ct = candidate.GetType();
                int x = (int)ct.GetField("firstX", Flags).GetValue(candidate);
                int z = (int)ct.GetField("firstZ", Flags).GetValue(candidate);
                var bounds = (Bounds)ct.GetField("bounds", Flags).GetValue(candidate);
                unchecked { fingerprint += x * 397L + z * 31L + Mathf.RoundToInt(bounds.center.y * 1000) * 17L + Mathf.RoundToInt(bounds.size.y * 1000); }
            }
            int rebuilt = terrain.LastDetailCandidateBoundsBuilt - beforeBounds;
            int count = candidates.Count;
            float previous = float.NegativeInfinity;
            for (int i = 0; i < 2; i++)
            {
                float selected = (float)candidates[i].GetType().GetField("distance", Flags).GetValue(candidates[i]);
                if (selected < previous) throw new Exception("Priority prefix out of order");
                for (int j = i + 1; j < candidates.Count; j++)
                    if ((float)candidates[j].GetType().GetField("distance", Flags).GetValue(candidates[j]) < selected)
                        throw new Exception("Priority prefix did not select the nearest candidate");
                previous = selected;
            }
            // Changing the terrain transform must discard world-space cached bounds.
            root.transform.position = new Vector3(30, 11, -15);
            Call(23);
            if (terrain.LastDetailCandidateBoundsBuilt <= beforeBounds + rebuilt) throw new Exception("Transform did not invalidate bounds");
            var calculate = type.GetMethod("CalculateDetailChunkBounds", Flags);
            for (int i = 0; i < candidates.Count; i += Math.Max(1, candidates.Count / 32))
            {
                var candidate = candidates[i]; var ct = candidate.GetType();
                int x = (int)ct.GetField("firstX", Flags).GetValue(candidate), z = (int)ct.GetField("firstZ", Flags).GetValue(candidate);
                var actual = (Bounds)ct.GetField("bounds", Flags).GetValue(candidate);
                var layer = terrain.DensityDetailLayers[0];
                var expected = (Bounds)calculate.Invoke(terrain, new object[] {mesh.bounds, resolution, resolution, 8, x, z, layer.MaximumPaintedHeight, layer.YOffset, root.transform.localToWorldMatrix, layer});
                if (actual != expected) throw new Exception("Cached bounds differ from uncached height scan");
            }
            terrain.RefreshDetailPaintRegion(0, new Rect(.4f, .4f, .1f, .1f));
            if (((IDictionary)type.GetField("m_FixedCandidateCaches", Flags).GetValue(terrain)).Count != 0)
                throw new Exception("Painting did not invalidate geometry cache");
            File.WriteAllText(output, $"Synthetic Play Mode fixed-cell selection, 24 moving-camera queries after 24 warmups.\nTime total ms: {watch.Elapsed.TotalMilliseconds:F3}\nBytes counter: {allocated} (Unity may not support this counter; not an allocation guarantee)\nBounds rebuilt: {rebuilt}\nCandidates: {count}\nFingerprint: {fingerprint}\nPASS: nearest build-budget prefix, terrain transform invalidation, bounds match uncached height scans, paint invalidation.\n");
        }
        catch(Exception e) { File.WriteAllText(output, e.ToString()); UnityEngine.Debug.LogException(e); }
        finally { Object.DestroyImmediate(root); if(mesh) Object.DestroyImmediate(mesh); if(density) Object.DestroyImmediate(density); }
    }
}
