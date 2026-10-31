using UnityEngine;

namespace MG.GodSurface
{
    /// <summary>Optional marker for a movable prefab. Static props are discovered automatically by the world.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("MashBox/God Surface/Prop")]
    public sealed class MGSurfaceProp : MonoBehaviour
    {
        Matrix4x4 lastMatrix;
        MGSurfaceWorld previousWorld;
        void OnEnable() { lastMatrix = transform.localToWorldMatrix; Notify(); }
        void OnDisable() => Notify();
        void OnTransformParentChanged() => Notify();
        void OnTransformChildrenChanged() => Notify();
        void OnValidate() => Notify();
        void LateUpdate()
        {
            var matrix = transform.localToWorldMatrix;
            if (matrix == lastMatrix) return;
            lastMatrix = matrix;
            var world = MGSurfaceWorld.ForProp(transform);
            if (world != previousWorld) { Notify(); return; }
            if (world) world.NotifyTransformChanged(transform);
        }
        public void Notify()
        {
            var world = MGSurfaceWorld.ForProp(transform);
            if (previousWorld && previousWorld != world) previousWorld.RequestRebuild();
            if (world) world.RequestRebuild();
            previousWorld = world;
        }
    }
}
