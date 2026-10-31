using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MG.GodSurface
{
    public sealed partial class MGSurfaceWorld
    {
        static readonly Dictionary<int, MGSurfaceWorld> sceneManagers = new Dictionary<int, MGSurfaceWorld>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSceneManagers() => sceneManagers.Clear();
        public static MGSurfaceWorld ForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            if (sceneManagers.TryGetValue(scene.handle, out var current) && current && current.gameObject.scene == scene) return current;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<MGSurfaceWorld>(true))
                {
                    sceneManagers[scene.handle] = candidate;
                    return candidate;
                }
            sceneManagers.Remove(scene.handle);
            return null;
        }
        public bool IsSceneManager => ForScene(gameObject.scene) == this;
        public bool Contains(Transform item)
        {
            if (!item || item.gameObject.scene != gameObject.scene || roots == null) return false;
            foreach (var root in roots)
                if (root && root.gameObject.scene == gameObject.scene && (item == root || item.IsChildOf(root))) return true;
            return false;
        }
        public static MGSurfaceWorld ForProp(Transform item)
        {
            if (!item) return null;
            var world = ForScene(item.gameObject.scene);
            return world && world.Contains(item) ? world : null;
        }
        void Awake()
        {
            if (Application.isPlaying && gameObject.scene.isLoaded && !IsSceneManager)
            {
                enabled = false;
                Debug.LogWarning("Only one God Prop Root is allowed per scene. The extra component is blocked; use the existing manager's Prop Roots list.", this);
            }
        }
    }
}
