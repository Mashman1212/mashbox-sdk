using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MeshSculptWindow
    {
        [MenuItem("MashBox/Validation/Validate Sculptable Selection")]
        public static void ValidateSculptableSelection()
        {
            var previous = new List<MeshFilter>(s_SculptableSurfaces);
            var first = new GameObject("Sculpt selection validation A") { hideFlags = HideFlags.HideAndDontSave };
            var second = new GameObject("Sculpt selection validation B") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                s_SculptableSurfaces.Clear();
                var firstFilter = first.AddComponent<MeshFilter>();
                var secondFilter = second.AddComponent<MeshFilter>();
                var modifier = second.AddComponent<MeshSculptModifier>();
                modifier.SetTarget(secondFilter);
                modifier.AddStroke(new MeshSculptModifier.Stroke());

                CheckSculptSelection(!IsSculptableSurface(secondFilter), "An existing modifier must not enroll its surface.");
                AddSculptableSurface(firstFilter);
                CheckSculptSelection(IsSculptableSurface(firstFilter) && !IsSculptableSurface(secondFilter),
                    "Selecting one surface must exclude other modified surfaces.");
                AddSculptableSurface(secondFilter);
                AddSculptableSurface(firstFilter);
                CheckSculptSelection(IsSculptableSurface(firstFilter) && IsSculptableSurface(secondFilter)
                    && s_SculptableSurfaces.Count == 2, "Selection must be additive and idempotent.");

                ClearSculptables();
                CheckSculptSelection(!IsSculptableSurface(firstFilter) && !IsSculptableSurface(secondFilter),
                    "Clear must remove every surface from the set.");
                CheckSculptSelection(modifier != null && modifier.Target == secondFilter && modifier.StrokeCount == 1,
                    "Clear must preserve modifiers, targets and recorded strokes.");
                AddSculptableSurface(firstFilter);
                CheckSculptSelection(IsSculptableSurface(firstFilter) && !IsSculptableSurface(secondFilter),
                    "Re-adding one surface must not restore previous selections.");
                UnityEngine.Object.DestroyImmediate(first);
                CheckSculptSelection(!IsSculptableSurface(firstFilter), "Destroyed selections must be ignored.");
                Debug.Log("Sculptable selection PASS: existing modifier exclusion, additive selection, clear, history preservation, re-selection, destroyed targets.");
            }
            finally
            {
                s_SculptableSurfaces.Clear();
                foreach (var surface in previous) AddSculptableSurface(surface);
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [MenuItem("MashBox/Validation/Validate Sculpt Terrain Activation")]
        public static void ValidateTerrainActivation()
        {
            var previous = new List<MeshFilter>(s_SculptableSurfaces);
            var previousOwner = s_ActiveSceneToolOwner;
            var owner = new GameObject("Terrain activation validation") { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh();
            MeshSculptWindow window = null;
            try
            {
                var filter = owner.AddComponent<MeshFilter>();
                owner.AddComponent<MeshRenderer>();
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
                mesh.triangles = new[] { 0, 2, 1 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
                var collider = owner.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                owner.AddComponent<MGTerrain>();
                var modifier = owner.AddComponent<MeshSculptModifier>();
                modifier.SetTarget(filter);
                modifier.AddStroke(modifier.CreateStroke(MeshSculptModifier.SculptMode.Displace,
                    MeshSculptModifier.StrokeSpace.World, Vector3.zero, Vector3.up, 10f, 1f, 1f));
                window = CreateInstance<MeshSculptWindow>();
                window.m_Mode = MeshSculptModifier.SculptMode.Displace;
                window.CreateOrActivateModifier(filter);
                CheckSculptSelection(IsSculptableSurface(filter), "Activation must enroll the terrain.");
                CheckSculptSelection(filter.sharedMesh == mesh && mesh.vertices[0] == Vector3.zero,
                    "Activation must not clone the mesh or replay existing strokes.");
                CheckSculptSelection(collider.sharedMesh == mesh && window.m_SculptPickingCollider == null,
                    "Activation must reuse terrain collision without a duplicate picking collider.");
                window.ClearActiveModifier();
                window.CreateOrActivateModifier(filter);
                CheckSculptSelection(filter.sharedMesh == mesh && window.m_SculptPickingCollider == null,
                    "Reactivation must remain free of mesh rebuilds and duplicate collision.");
                Debug.Log("Terrain activation PASS: enrollment, no history replay, mesh identity, collision reuse, reactivation.");
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(mesh);
                s_SculptableSurfaces.Clear();
                foreach (var surface in previous) AddSculptableSurface(surface);
                if (previousOwner != null) previousOwner.ActivateSceneTool();
                SculptablesChanged?.Invoke();
            }
        }

        [MenuItem("MashBox/Validation/Validate Terrain Smoothing Equivalence")]
        public static void ValidateTerrainSmoothing()
        {
            var owner = new GameObject("Terrain smoothing validation") { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh();
            try
            {
                var filter = owner.AddComponent<MeshFilter>();
                owner.AddComponent<MeshRenderer>();
                var source = new Vector3[81];
                var triangles = new List<int>();
                for (int z = 0; z < 9; z++)
                    for (int x = 0; x < 9; x++)
                    {
                        int i = z * 9 + x;
                        source[i] = new Vector3(x, Mathf.Sin(x * 1.3f + z) * 2f, z);
                        if (x == 8 || z == 8) continue;
                        triangles.AddRange(new[] { i, i + 9, i + 1, i + 1, i + 9, i + 10 });
                    }
                mesh.vertices = source;
                mesh.triangles = triangles.ToArray();
                filter.sharedMesh = mesh;
                owner.AddComponent<MGTerrain>().HeightOnlySculpt = true;
                var modifier = owner.AddComponent<MeshSculptModifier>();
                modifier.SetTarget(filter);
                owner.transform.position = new Vector3(12, 3, -8);
                owner.transform.rotation = Quaternion.Euler(12, 35, 5);
                owner.transform.localScale = new Vector3(2, .7f, 1.3f);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var optimized = typeof(MeshSculptModifier).GetMethod("ApplyStroke", flags);
                var reference = typeof(MeshSculptModifier).GetMethod("ApplyHeightOnlyStroke", flags);
                foreach (int passes in new[] { 1, 8, 128 })
                    foreach (float strength in new[] { 0f, .1f, 1f })
                        foreach (float radius in new[] { .01f, 4f, 100f })
                            foreach (var space in new[] { MeshSculptModifier.StrokeSpace.World, MeshSculptModifier.StrokeSpace.TargetLocal })
                            {
                                var stroke = modifier.CreateStroke(MeshSculptModifier.SculptMode.Smooth, space,
                                    owner.transform.TransformPoint(new Vector3(4, 0, 4)), Vector3.up, radius, strength, 2f);
                                stroke.smoothIterations = passes;
                                var expected = (Vector3[])source.Clone();
                                var actual = (Vector3[])source.Clone();
                                for (int pass = 0; pass < passes; pass++)
                                    reference.Invoke(modifier, new object[] { expected, mesh, stroke, owner.transform });
                                optimized.Invoke(modifier, new object[] { actual, mesh, stroke });
                                for (int i = 0; i < actual.Length; i++)
                                {
                                    CheckSculptSelection((actual[i] - expected[i]).sqrMagnitude < .00000001f,
                                        $"Smoothing differs at vertex {i}: {passes} passes, strength {strength}, radius {radius}, {space}.");
                                    CheckSculptSelection(actual[i].x == source[i].x && actual[i].z == source[i].z,
                                        "Smoothing must preserve terrain X/Z.");
                                }
                            }
                Debug.Log("Terrain smoothing equivalence PASS: 54 cases, 1/8/128 passes, zero/full strength, brush boundaries, world/local space, transformed terrain.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        static void CheckSculptSelection(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Sculptable selection validation failed: " + message);
        }
    }
}
