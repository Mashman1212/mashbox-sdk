#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainTreeDitherValidation
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        [MenuItem("Tools/MashBox/MG Terrain/Validate Tree Density Dither")]
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.NewPreviewScene();
            Material material = null;
            Mesh mesh = null;
            try
            {
                var shader = Shader.Find("Shader Graphs/MG_GodGrass_V2") ?? Shader.Find("Shader Graphs/MG_GodGrass");
                Check(shader != null, "Import GodGrass first.");
                material = new Material(shader);
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up * 10 }, triangles = new[] { 0, 1, 2 } };
                mesh.RecalculateBounds();
                var go = new GameObject("Tree dither validation");
                SceneManager.MoveGameObjectToScene(go, scene);
                var terrain = go.AddComponent<MGTerrain>();
                int index = terrain.FindOrAddPrototype(mesh, material, MGTerrain.InstanceKind.Tree);
                var prototype = terrain.Prototypes[index];
                Set(prototype, "m_TreeLodCount", 1);
                Set(prototype, "m_TreeLod1Distance", 60f); Set(prototype, "m_TreeLod2Distance", 200f);
                Set(prototype, "m_TreeMidDensity", .5f); Set(prototype, "m_TreeFarDensity", .25f);
                Set(prototype, "m_TreeDensityTransitionWidth", 10f);
                Set(terrain, "m_UseDetailDensityLod", false);
                Set(terrain, "m_OverallDetailDensity", .1f);
                Set(terrain, "m_DetailStreamingRefreshDistance", .1f);
                Check((bool)Call(terrain, "EvaluateDetailFadeEligibility", prototype), "Trees should fade independently of grass density LOD.");
                Check((Vector4)Call(terrain, "DensityFadeRanges", prototype) == new Vector4(60, 200, 10, 0), "Tree-specific fade ranges.");
                Check((float)Call(terrain, "DensityFadeOverall", prototype) == 1f, "Grass density must not reduce trees.");
                Check((int)Call(terrain, "DensityFadePopulation", prototype, 1000, 64f) == 1000, "Retain close trees through the first transition.");
                Check((int)Call(terrain, "DensityFadePopulation", prototype, 1000, 100f) == 500, "Mid tree population.");
                Check((int)Call(terrain, "DensityFadePopulation", prototype, 1000, 204f) == 500, "Retain mid trees through the far transition.");
                Check((int)Call(terrain, "DensityFadePopulation", prototype, 1000, 220f) == 250, "Far tree population.");
                Set(prototype, "m_TreeDensityTransitionWidth", 0f);
                Check(!(bool)Call(terrain, "EvaluateDetailFadeEligibility", prototype), "Zero width disables dither.");
                Set(prototype, "m_TreeDensityTransitionWidth", 10f);
                terrain.AddInstanceLocal(index, Vector3.zero, Quaternion.identity, Vector3.one);
                terrain.AddInstanceLocal(index, Vector3.right * 3, Quaternion.identity, Vector3.one);
                Call(terrain, "BuildTreeInstanceCells", Matrix4x4.identity);
                var cells = (IList)Get(terrain, "m_TreeInstanceCells");
                Check(cells.Count == 1, "Expected one cached tree cell.");
                var batches = (IList)Get(cells[0], "batches");
                var chunks = (IList)Get(batches[0], "treeFadeChunks");
                var data = (Vector4[])chunks[0];
                Check(data.Length == 2 && data[0].y == 0 && data[1].y == 0, "Cached metadata preserves material Sub-ID selection.");
                Check(data[0].w == 2 && data[1].w == 2, "Serialized trees enable density dithering.");
                Check(data[0].z >= 0 && data[0].z <= 1 && data[1].z >= 0 && data[1].z <= 1, "Stable ranks stay in range.");
                Debug.Log("Tree density dither validation passed: per-tree settings; grass independence; retained transition populations; zero-width opt-out; cached serialized-tree metadata.");
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif