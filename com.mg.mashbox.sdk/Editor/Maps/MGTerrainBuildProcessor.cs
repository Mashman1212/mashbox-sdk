#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using MashBoxSDK.Maps.TerrainSystem;

namespace MashBoxSDK.MapTools
{
    // MG Terrain reads its source mesh as a local-space height grid at runtime.
    // Unity static batching replaces MeshFilter.sharedMesh with a combined,
    // world-space mesh, breaking both detail placement and GPU generation.
    // Run before UnityBuildPostprocessor (order 0), on the processed scene copy.
    // MG Terrain already batches its surface chunks and detail instances itself.
    [BuildCallbackVersion(1)]
    internal sealed class MGTerrainBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => -1000;
        public void OnProcessScene(Scene scene, BuildReport report) => PreserveTerrainSourceMeshes(scene);

        internal static void PreserveTerrainSourceMeshes(Scene scene)
        {
            if (!scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var terrain in root.GetComponentsInChildren<MGTerrain>(true))
                {
                    var target = terrain.gameObject;
                    var flags = GameObjectUtility.GetStaticEditorFlags(target);
                    if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                        GameObjectUtility.SetStaticEditorFlags(target, flags & ~StaticEditorFlags.BatchingStatic);
                }
        }
    }
}
#endif
