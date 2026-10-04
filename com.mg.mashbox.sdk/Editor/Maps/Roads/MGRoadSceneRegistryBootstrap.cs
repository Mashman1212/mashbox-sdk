using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.Maps.Roads.Editor
{
    [InitializeOnLoad]
    internal static class MGRoadSceneRegistryBootstrap
    {
        static MGRoadSceneRegistryBootstrap()
        {
            // OnEnable registers active objects. This scene-scoped bootstrap
            // also covers payloads saved inactive and domain reloads.
            EditorApplication.delayCall += BootstrapLoadedScenes;
            EditorSceneManager.sceneOpened += SceneOpened;
            EditorSceneManager.sceneClosed += MGRoadSceneRegistry.RemoveScene;
            EditorApplication.hierarchyChanged += MGRoadSceneRegistry.NotifyChanged;
            ObjectChangeEvents.changesPublished += Changed;
        }
        static void BootstrapLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) RegisterScene(SceneManager.GetSceneAt(i));
        }
        static void SceneOpened(Scene scene, OpenSceneMode mode) => RegisterScene(scene);
        static void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var road in root.GetComponentsInChildren<MGRoad>(true)) MGRoadSceneRegistry.Register(road);
                foreach (var store in root.GetComponentsInChildren<MGRoadTerrainLayers>(true)) MGRoadSceneRegistry.Register(store);
                foreach (var store in root.GetComponentsInChildren<MGRoadDetailLayers>(true)) MGRoadSceneRegistry.Register(store);
                foreach (var terrain in root.GetComponentsInChildren<MashBoxSDK.Maps.TerrainSystem.MGTerrain>(true)) MGRoadSceneRegistry.Register(terrain);
            }
        }
        static void Changed(ref ObjectChangeEventStream stream)
        {
            if (stream.length != 0 && (MGRoadSceneRegistry.HasLayerWork || MGRoadSceneRegistry.HasDetailWork))
                MGRoadSceneRegistry.NotifyChanged();
        }
    }
}
