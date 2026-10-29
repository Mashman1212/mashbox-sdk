using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void RotateSelectedObjects(Quaternion rotation, Quaternion orientation, Vector3 center)
        {
            if (!local)
            {
                var turn = rotation * Quaternion.Inverse(orientation);
                foreach (int i in objects.Where(i => !HasSelectedParent(i)))
                {
                    var target = Transforms[i];
                    target.position = center + turn * (target.position - center);
                    target.rotation = turn * target.rotation; Document.ReadTransform(i, target);
                }
                return;
            }
            var localTurn = Quaternion.Inverse(orientation) * rotation;
            int Depth(int i) { int d=0; for (int p=Document.nodes[i].parent; p>=0; p=Document.nodes[p].parent) d++; return d; }
            var nodes = objects.OrderBy(Depth).ThenBy(i => i).ToArray();
            var positions = nodes.Select(i => Transforms[i].position).ToArray();
            var rotations = nodes.Select(i => Transforms[i].rotation).ToArray();
            var frames = nodes.Select(i => Transforms[i].rotation * Document.nodes[i].pivotRotation).ToArray();
            // Capture all world poses before touching a selected ancestor.
            for (int k=0; k<nodes.Length; k++)
            {
                var target = Transforms[nodes[k]];
                target.position = positions[k];
                target.rotation = frames[k] * localTurn * Quaternion.Inverse(frames[k]) * rotations[k];
                Document.ReadTransform(nodes[k], target);
            }
        }
        private static Vector3 RotateComponentTarget(MultiComponentGesture gesture, MultiTransformTarget target, Vector3 world, Quaternion rotation)
        {
            var center = gesture.individualRotation ? target.center : gesture.center;
            var turn = gesture.individualRotation
                ? target.orientation * (Quaternion.Inverse(gesture.orientation) * rotation) * Quaternion.Inverse(target.orientation)
                : rotation * Quaternion.Inverse(gesture.orientation);
            return center + turn * (world-center);
        }
    }
}
