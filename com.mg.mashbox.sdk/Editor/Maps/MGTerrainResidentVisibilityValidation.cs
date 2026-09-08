using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainResidentVisibilityValidation
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static Type Nested(string name) => typeof(MGTerrain).GetNestedType(name, BindingFlags.NonPublic | BindingFlags.Public);
        static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        static object New(string type, params object[] args) => Activator.CreateInstance(Nested(type), Flags, null, args, null);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        [MenuItem("MashBox/Validation/Validate Terrain Resident Visibility")]
        public static void Run()
        {
            var go = new GameObject("Resident visibility validation");
            go.SetActive(false);
            var cameraObject = new GameObject("Resident visibility camera");
            try
            {
                var terrain = go.AddComponent<MGTerrain>();
                Check(!(bool)Get(terrain, "m_AmortizeResidentVisibility"), "Expanded visibility must be opt-in.");
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                Set(terrain, "m_UseDetailDensityLod", true);
                object prototype = New("Prototype", (GameObject)null, (MGTerrain.InstanceKind)0, 500f);
                object sector = New("ResidentDetailSector");
                Set(sector, "prototype", prototype);
                Set(sector, "bounds", new Bounds(new Vector3(0, 0, 60), new Vector3(210, 4, 120)));
                IList cells = (IList)Get(sector, "cells");
                for (int i = 0; i < 20000; i++)
                {
                    var bounds = new Bounds(new Vector3(i % 200 - 100, 0, i / 200 + 5), Vector3.one);
                    object chunk = New("DensityDetailChunk");
                    Set(chunk, "instanceCount", 64);
                    Set(chunk, "worldBounds", bounds);
                    cells.Add(New("DetailCandidateChunk", i % 200, i / 200, 1, 0, bounds, 0f, true, chunk));
                }
                ((IList)Get(terrain, "m_FullResidentSectors")).Add(sector);
                var planes = (Plane[])Get(terrain, "m_ResidentScanPlanes");
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                Array bands = (Array)Get(terrain, "m_ResidentPendingBands");
                Action reset = () =>
                {
                    Set(terrain, "m_ResidentSectorCursor", 0); Set(terrain, "m_ResidentCellCursor", 0);
                    foreach (IList band in bands) band.Clear();
                };
                var advance = (Func<Camera, bool, bool>)typeof(MGTerrain).GetMethod("AdvanceResidentVisibilityScan", Flags)
                    .CreateDelegate(typeof(Func<Camera, bool, bool>), terrain);
                advance(camera, true); // Warm JIT, LOD dictionary and list capacities.
                reset();
                var timer = Stopwatch.StartNew();
                Check(advance(camera, true), "Immediate camera-cut scan did not complete.");
                double fullMilliseconds = timer.Elapsed.TotalMilliseconds;
                var expected = new HashSet<object>();
                foreach (IList band in bands) foreach (object visible in band) expected.Add(Get(visible, "chunk"));
                Check(expected.Count > 1000, "Fixture did not exercise visible cells.");
                // Independent renderer-work baseline: no cell outside the real frustum and
                // no 12-metre density promotion may leak into the default draw selection.
                var precise = new HashSet<object>();
                foreach (object cell in cells)
                    if (GeometryUtility.TestPlanesAABB(planes, (Bounds)Get(cell, "bounds")))
                        precise.Add(Get(cell, "cachedChunk"));
                Check(expected.SetEquals(precise), "Default selection widened the camera frustum.");
                foreach (IList band in bands) foreach (object visible in band)
                {
                    Bounds bounds = (Bounds)Get(Get(visible, "chunk"), "worldBounds");
                    float distance = Mathf.Sqrt(bounds.SqrDistance(camera.transform.position));
                    Check(Mathf.Abs((float)Get(visible, "distance") - distance) < 0.001f,
                        "Default selection promoted density by reducing camera distance.");
                }
                reset();
                var timings = new List<double>();
                bool done;
                do
                {
                    timer.Restart(); done = advance(camera, false); timings.Add(timer.Elapsed.TotalMilliseconds);
                    Check(timings.Count < 1000, "Incremental scan never completed.");
                } while (!done);
                Check(timings.Count > 1, "Motion scan was not spread across frames.");
                var actual = new HashSet<object>();
                foreach (IList band in bands) foreach (object visible in band)
                    Check(actual.Add(Get(visible, "chunk")), "Incremental scan duplicated a cell.");
                Check(expected.SetEquals(actual), "Incremental scan lost visible cells.");
                timings.Sort();
                ValidateUnchangedSubmission(terrain, bands, prototype);
                ValidateRangeCache(terrain);
                ValidateMotionScheduling(terrain, camera, reset);
                ValidateLocalHeightBounds(terrain, camera);
                typeof(MGTerrain).GetMethod("ResetFullDetailResidency", Flags).Invoke(terrain, null);
                Check(!terrain.ResidentVisibilityUpdatePending, "Cache invalidation retained pending scan state.");
                Debug.Log($"TERRAIN_RESIDENT_VALIDATION_PASS: 20000 cells, {expected.Count} selected; full scan {fullMilliseconds:F3} ms; " +
                    $"{timings.Count} slices, median {timings[timings.Count / 2]:F3} ms, max {timings[timings.Count - 1]:F3} ms. " +
                    "Exact selection parity, unchanged GPU submission reuse and cached prefix/generation checks passed.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(cameraObject); }
        }

        static void ValidateUnchangedSubmission(MGTerrain terrain, Array bands, object prototype)
        {
            object visible = ((IList)bands.GetValue(0))[0];
            ((IList)Get(terrain, "m_VisibleDensityDetails")).Add(visible);
            int count = (int)typeof(MGTerrain).GetMethod("GetVisibleDensityDetailInstanceCount", Flags).Invoke(terrain, new[] { visible });
            ((IDictionary)Get(terrain, "m_GpuPrototypeGroups")).Add(prototype, New("GpuPrototypeGroups"));
            ((IDictionary)Get(terrain, "m_ResidentVisibleCounts")).Add(Get(visible, "chunk"), count);
            Set(terrain, "m_UseIndirectDetailDraws", false);
            Set(terrain, "m_DetailBrgHasSignature", true);
            Set(terrain, "m_DetailBrgUsesGpuGeneration", true);
            Set(terrain, "m_DetailBrgVisibleCount", 123);
            bool result = (bool)typeof(MGTerrain).GetMethod("UpdateResidentGpuVisibility", Flags).Invoke(terrain, new object[] { 1000, 1f, 1f });
            Check(result && (int)Get(terrain, "m_DetailBrgVisibleCount") == 123, "Unchanged selection rebuilt GPU visibility.");
            Check((int)Get(terrain, "m_LastSubmittedDensityDetailInstances") == count, "Reused submission statistics are wrong.");
            MethodInfo compare = typeof(MGTerrain).GetMethod("ResidentGpuSelectionUnchanged", Flags);
            object[] compareArgs = { 1000, 1f, 1f, 0 };
            var counts = (IDictionary)Get(terrain, "m_ResidentVisibleCounts");
            object chunk = Get(visible, "chunk");
            counts[chunk] = count + 1;
            Check(!(bool)compare.Invoke(terrain, compareArgs) && (int)compareArgs[3] == 0,
                "Changed first cell did not stop comparison immediately.");
            counts[chunk] = count;
            counts.Add(New("DensityDetailChunk"), 1);
            Check(!(bool)compare.Invoke(terrain, compareArgs), "Removed cell was treated as unchanged.");
            counts.Clear(); counts.Add(chunk, count);
        }

        static void ValidateMotionScheduling(MGTerrain terrain, Camera camera, Action reset)
        {
            Set(terrain, "m_AmortizeResidentVisibility", true);
            Set(terrain, "m_ResidentSelectionAmortized", true);
            reset();
            Set(terrain, "m_ResidentSelectionValid", true);
            Set(terrain, "m_ResidentSelectionCamera", camera);
            Set(terrain, "m_ResidentSelectedPosition", Vector3.zero);
            Set(terrain, "m_ResidentSelectedRotation", Quaternion.identity);
            Matrix4x4 projection = camera.projectionMatrix;
            Set(terrain, "m_ResidentProjection", projection);
            camera.nonJitteredProjectionMatrix = projection;
            Matrix4x4 jittered = projection; jittered.m02 += 0.001f;
            camera.projectionMatrix = jittered;
            camera.transform.position = Vector3.right;
            var update = (Action<Camera, Plane[]>)typeof(MGTerrain).GetMethod("UpdateFullResidentVisibility", Flags)
                .CreateDelegate(typeof(Action<Camera, Plane[]>), terrain);
            Set(terrain, "m_ResidentLastFrame", -1);
            update(camera, GeometryUtility.CalculateFrustumPlanes(camera));
            Check(terrain.LastResidentVisibilityCellsTested == 0 && !terrain.ResidentVisibilityUpdatePending,
                "Small movement or projection jitter triggered a visibility rebuild.");
            camera.transform.position = Vector3.right * 3f;
            int previousVisibleCount = ((IList)Get(terrain, "m_VisibleDensityDetails")).Count;
            Set(terrain, "m_ResidentLastFrame", -1);
            update(camera, GeometryUtility.CalculateFrustumPlanes(camera));
            Check(terrain.ResidentVisibilityUpdatePending, "Movement scan did not yield.");
            int cursor = (int)Get(terrain, "m_ResidentCellCursor");
            Set(terrain, "m_ResidentLastFrame", -1);
            update(camera, GeometryUtility.CalculateFrustumPlanes(camera));
            Check((int)Get(terrain, "m_ResidentCellCursor") > cursor, "Pending scan restarted instead of advancing.");
            Check(((IList)Get(terrain, "m_VisibleDensityDetails")).Count == previousVisibleCount,
                "A partial scan replaced the active render set.");
        }

        static void ValidateLocalHeightBounds(MGTerrain terrain, Camera camera)
        {
            var mesh = new Mesh();
            try
            {
                var vertices = new Vector3[81];
                for (int z = 0; z < 9; z++) for (int x = 0; x < 9; x++)
                    vertices[z * 9 + x] = new Vector3(x, x == 8 ? 100f : (x == 1 && z == 1 ? 3f : 0f), z);
                mesh.vertices = vertices; mesh.RecalculateBounds();
                terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
                Set(terrain, "m_SurfaceGridWidth", 9); Set(terrain, "m_SurfaceGridHeight", 9);
                MethodInfo calculate = typeof(MGTerrain).GetMethod("CalculateDetailChunkBounds", Flags);
                Bounds local = (Bounds)calculate.Invoke(terrain,
                    new object[] { mesh.bounds, 8, 8, 2, 0, 0, 1f, 0f, (Matrix4x4?)Matrix4x4.identity });
                Check(Mathf.Abs(local.min.y) < 0.001f && Mathf.Abs(local.max.y - 4f) < 0.001f,
                    "Cell bounds missed the interior ridge or included the distant mountain.");
                Bounds flat = (Bounds)calculate.Invoke(terrain,
                    new object[] { mesh.bounds, 8, 8, 1, 0, 4, 1f, 0f, (Matrix4x4?)Matrix4x4.identity });
                Check(Mathf.Abs(flat.size.y - 1f) < 0.001f, "Flat cell retained terrain-wide height.");
                camera.projectionMatrix = camera.nonJitteredProjectionMatrix;
                camera.transform.position = new Vector3(0.5f, 2f, 6f);
                camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                Bounds old = flat; old.SetMinMax(new Vector3(flat.min.x, 0, flat.min.z), new Vector3(flat.max.x, 101, flat.max.z));
                Check(!GeometryUtility.TestPlanesAABB(planes, flat), "Downward view retained a cell behind the camera.");
                Check(GeometryUtility.TestPlanesAABB(planes, old), "Fixture did not reproduce oversized-bounds visibility.");
            }
            finally { terrain.GetComponent<MeshFilter>().sharedMesh = null; Object.DestroyImmediate(mesh); }
        }

        static void ValidateRangeCache(MGTerrain terrain)
        {
            object chunk = New("DensityDetailChunk"); Set(chunk, "instanceCount", 10);
            IList spawns = (IList)Get(chunk, "proceduralSpawns");
            spawns.Add(New("DensityDetailSpawn", 0, 0, 3, Vector4.one));
            spawns.Add(New("DensityDetailSpawn", 1, 0, 7, Vector4.one));
            object cell = New("ResidentGpuCell");
            Set(cell, "chunk", chunk); Set(cell, "visible", 5); Set(cell, "population", 10); Set(cell, "generation", 1);
            Set(terrain, "m_UseIndirectDetailDraws", true);
            IList ranges = (IList)Get(terrain, "m_IndirectVisibilityRanges");
            MethodInfo append = typeof(MGTerrain).GetMethod("AppendResidentVisibleIndices", Flags);
            Func<string> run = () =>
            {
                ranges.Clear(); object[] args = { cell, 0, 0 }; append.Invoke(terrain, args);
                Check((int)args[1] == 5, "Prefix selection changed population.");
                var result = new List<string>();
                foreach (object range in ranges) result.Add(Get(range, "source") + ":" + Get(range, "count"));
                return string.Join(",", result);
            };
            string first = run();
            Check(first == run(), "Cached visibility ranges differ from original prefix selection.");
            spawns.Clear();
            spawns.Add(New("DensityDetailSpawn", 0, 0, 5, Vector4.one));
            spawns.Add(New("DensityDetailSpawn", 1, 0, 5, Vector4.one));
            Set(cell, "generation", 2);
            Check(first != run(), "Regenerated density data reused stale prefix ranges.");
            string rebuilt = run(); Set(cell, "start", 200);
            Check(rebuilt != run(), "Relocated GPU storage reused stale offsets.");
        }
    }
}
