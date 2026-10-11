using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    // Unity invokes this on its scene copy for Editor Play Mode, players and scene
    // bundles. Keep local-space authoring meshes intact before static batching.
    [BuildCallbackVersion(1)]
    public sealed class MapEditLoftBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => -2100;
        public void OnProcessScene(Scene scene, BuildReport report) => PreserveLoftSources(scene);
        public static void PreserveLoftSources(Scene scene)
        {
            if (!scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var loft in root.GetComponentsInChildren<MultiSplineLoft>(true))
                {
                    Exclude(loft.GetComponent<MeshRenderer>());
                    var overlay = loft.HeightOverlayModifier;
                    if (!overlay) continue;
                    foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))
                        if (!renderer.GetComponentInParent<LoftVisualChunks>()) Exclude(renderer);
                }
        }
        static void Exclude(MeshRenderer renderer)
        {
            if (!renderer) return;
            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
        }
    }
}
