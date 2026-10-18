using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable]
    public sealed class StudioBoneCorrection
    {
        public string path;
        public Vector3 position, angles;
        public Vector3 axisX = Vector3.right, axisY = Vector3.up, axisZ = Vector3.forward;
        public Quaternion gizmoRotation = Quaternion.identity;
        public Quaternion GizmoRotation => Quaternion.Dot(gizmoRotation, gizmoRotation) > .000001f ? gizmoRotation.normalized : Quaternion.identity;
        public Quaternion Rotation => Quaternion.AngleAxis(angles.x, axisX) * Quaternion.AngleAxis(angles.y, axisY) * Quaternion.AngleAxis(angles.z, axisZ) * GizmoRotation;
        public StudioBoneCorrection Copy() => (StudioBoneCorrection)MemberwiseClone();
    }

    public sealed partial class AnimationTake
    {
        // These values track the controls, not an evaluation layer. Motion is updated in place.
        public List<StudioBoneCorrection> quickCorrections = new List<StudioBoneCorrection>();

        public void SetQuickCorrection(StudioBoneCorrection requested)
        {
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            int index = Array.IndexOf(bonePaths, requested.path);
            if (index < 0) throw new InvalidOperationException("The correction joint is not in this animation's skeleton.");
            if (keys.Count == 0) throw new InvalidOperationException("Open an animation or key a starting pose before correcting it.");
            if (!Finite(requested.position) || !Finite(requested.angles) || !Finite(requested.axisX) || !Finite(requested.axisY) || !Finite(requested.axisZ))
                throw new ArgumentException("Correction values must be finite.");
            if (!Finite(new Vector3(requested.gizmoRotation.x, requested.gizmoRotation.y, requested.gizmoRotation.z)) || float.IsNaN(requested.gizmoRotation.w) || float.IsInfinity(requested.gizmoRotation.w))
                throw new ArgumentException("Correction rotation must be finite.");
            if (keys.Any(k => k.bones == null || index >= k.bones.Length))
                throw new InvalidOperationException("The animation contains a key with a different skeleton.");
            var affectedLayers = (layers ?? new List<StudioAnimationLayer>()).Where(l => l.bones.Contains(requested.path)).ToArray();
            if (affectedLayers.Any(l => l.keys.Any(k => k.bones == null || index >= k.bones.Length)))
                throw new InvalidOperationException("A layer contains a key with a different skeleton.");

            quickCorrections = quickCorrections ?? new List<StudioBoneCorrection>();
            var previous = quickCorrections.Find(c => c.path == requested.path);
            Vector3 translation = requested.position - (previous?.position ?? Vector3.zero);
            Quaternion rotation = Quaternion.Inverse(previous?.Rotation ?? Quaternion.identity) * requested.Rotation;
            Quaternion inverse = Quaternion.Inverse(rotation);
            foreach (var key in keys)
            {
                // Include fallback values in sparse keys; channel masks and key times stay intact.
                key.bones[index].position += translation;
                key.bones[index].rotation *= rotation;
            }
            foreach (var layer in affectedLayers)
            {
                // Transform the existing stack so its final pose gets the same local offset,
                // including weighted, muted and range-limited layers. No layer is added.
                bool additive = layer.mode == StudioLayerMode.Additive;
                foreach (var key in layer.keys)
                {
                    if (!additive) key.bones[index].position += translation;
                    key.bones[index].rotation = additive
                        ? inverse * key.bones[index].rotation * rotation
                        : key.bones[index].rotation * rotation;
                }
                foreach (var offset in layer.offsets.Where(o => o.path == requested.path))
                {
                    offset.axisX = inverse * offset.axisX;
                    offset.axisY = inverse * offset.axisY;
                    offset.axisZ = inverse * offset.axisZ;
                }
            }
            if (previous != null) quickCorrections.Remove(previous);
            quickCorrections.Add(requested.Copy());
        }

        private static bool Finite(Vector3 value) =>
            !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) ||
              float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
