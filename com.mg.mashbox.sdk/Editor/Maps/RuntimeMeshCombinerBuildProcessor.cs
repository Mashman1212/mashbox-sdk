using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs for player and scene AssetBundle builds before Unity static batching (order 0).
public sealed class RuntimeMeshCombinerBuildProcessor : IProcessSceneWithReport
{
    public int callbackOrder => -1000;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (RuntimeMeshCombiner combiner in root.GetComponentsInChildren<RuntimeMeshCombiner>(true))
        foreach (MeshFilter filter in combiner.GetMaterialPreviewMeshFilters())
        {
            // Build-time static batching replaces source meshes before Awake.
            // Keep the original meshes available to this runtime combiner.
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
            Mesh mesh = filter.sharedMesh;
            if (mesh.isReadable) continue;
            // Only change scene-owned data on Unity's temporary build scene.
            // Imported assets must be prepared/reimported before the build starts.
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)) &&
                RuntimeMeshCombiner.EnableReadWriteOnSerializedMesh(mesh)) continue;
            throw new BuildFailedException($"RuntimeMeshCombiner '{combiner.name}' in '{scene.path}' cannot combine unreadable mesh '{mesh.name}' on '{filter.name}'. Run Enable Read/Write On Child Mesh Assets on the combiner and save before building. Asset: {AssetDatabase.GetAssetPath(mesh)}");
        }
    }
}

