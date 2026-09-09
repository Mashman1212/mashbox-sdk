using MashBoxSDK.Maps.Spline;
using System.Linq;
using UnityEngine;
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
                    try
                    {
                        // Older scenes saved the chunk objects but omitted their DontSave meshes.
                        // Repair on the build copy, before visuals mark the loft as baked.
                        Transform chunks = loft.transform.Find("Collider Chunks");
                        bool expectsCollision = (loft.GeneratedMesh != null && loft.GeneratedMesh.vertexCount > 0) ||
                            (chunks != null && chunks.childCount > 0);
                        if (loft.UpdateMeshCollider && expectsCollision && (chunks == null ||
                            chunks.GetComponentsInChildren<MeshCollider>(true).Length == 0 ||
                            chunks.GetComponentsInChildren<MeshCollider>(true).Any(c => c.sharedMesh == null || c.sharedMesh.vertexCount == 0)))
                        {
                            loft.Regenerate();
                            chunks = loft.transform.Find("Collider Chunks");
                            if (chunks == null || chunks.GetComponentsInChildren<MeshCollider>(true).Length == 0 ||
                                chunks.GetComponentsInChildren<MeshCollider>(true).Any(c => c.sharedMesh == null || c.sharedMesh.vertexCount == 0))
                                throw new System.InvalidOperationException("Loft collision is missing and could not be regenerated. Rebuild the loft in the source map before exporting.");
                        }
                        loft.BuildVisualChunks(true);
                    }
                    catch (System.Exception exception)
                    {
                        throw new BuildFailedException($"Visual chunk baking failed for loft '{loft.name}' in '{scene.path}': {exception.Message}");
                    }
                }
        }
    }
}
