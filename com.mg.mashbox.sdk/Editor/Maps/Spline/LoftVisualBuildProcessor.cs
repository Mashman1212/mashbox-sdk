using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.MapTools
{
    // Applies to both player and scene AssetBundle builds. Unity provides a temporary
    // scene copy here; the author's scene and editable meshes are never overwritten.
    [InitializeOnLoad]
    public sealed class LoftVisualBuildProcessor : IProcessSceneWithReport
    {
        static LoftVisualBuildProcessor()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingPlayMode && state != PlayModeStateChange.EnteredEditMode) return;
                foreach (MultiSplineLoft loft in UnityEngine.Resources.FindObjectsOfTypeAll<MultiSplineLoft>())
                    if (loft.gameObject.scene.IsValid()) loft.ReleaseEditorVisualChunks();
            };
        }

        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (MultiSplineLoft loft in root.GetComponentsInChildren<MultiSplineLoft>(true))
                {
                    try { loft.BuildVisualChunks(true); }
                    catch (System.Exception exception)
                    {
                        throw new BuildFailedException($"Visual chunk baking failed for loft '{loft.name}' in '{scene.path}': {exception.Message}");
                    }
                }
        }
    }
}
