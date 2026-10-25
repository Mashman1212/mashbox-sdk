using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        internal bool CanMirrorSkin => HasEditableMesh && Part.bound;

        internal void MirrorSkinWeights(bool leftToRight)
        {
            if (!CanMirrorSkin) return;
            EndStroke(); PausePreviewAnimation();
            if (!previewClip || previewClip.frameRate <= 0 || previewClip.length < 2f / previewClip.frameRate)
                throw new InvalidOperationException("Mirror weights requires A-pose, frame 2 of Skinning Test.");
            var names = bones.Select(b => StudioSkinningDocument.BoneName(b.name)).ToArray();
            int left = Array.IndexOf(names, "LeftUpLeg"), right = Array.IndexOf(names, "RightUpLeg");
            if (left < 0 || right < 0) throw new InvalidOperationException("Mirror weights requires the SDK left and right hip joints.");
            var map = new int[bones.Length];
            for (int i = 0; i < map.Length; i++)
            {
                string name = names[i];
                string other = name.StartsWith("Left", StringComparison.Ordinal) ? "Right" + name.Substring(4) :
                    name.StartsWith("Right", StringComparison.Ordinal) ? "Left" + name.Substring(5) : name;
                var matches = Enumerable.Range(0,names.Length).Where(k => names[k] == other).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("No unique mirrored bone for " + name);
                map[i] = matches[0];
            }
            JumpToPreviewFrame(2);
            Vector3 center = (bones[left].position + bones[right].position) * .5f;
            Vector3 axis = (bones[left].position - bones[right].position).normalized;
            if (axis.sqrMagnitude < .5f) throw new InvalidOperationException("The mirror plane could not be determined.");
            var original = new StudioSkinWeights(Part.mesh);
            var output = new StudioSkinWeights(Part.mesh);
            var binds = Part.mesh.bindposes;
            if (binds.Length != bones.Length) throw new InvalidOperationException("Skin bone mapping does not match the SDK skeleton.");
            var matrices = bones.Select((b,i) => b.localToWorldMatrix * binds[i]).ToArray();
            var verts = Part.mesh.vertices;
            var points = verts.Select((v,i) => original.SkinMatrix(i,matrices).MultiplyPoint3x4(v)).ToArray();
            float sign = leftToRight ? 1 : -1;
            float Side(Vector3 p) => Vector3.Dot(p-center,axis) * sign;
            var triangles = new List<FitBodyTriangle>();
            var indices = Part.mesh.triangles;
            for (int i=0; i<indices.Length; i+=3)
            {
                int a=indices[i], b=indices[i+1], c=indices[i+2];
                if (Side(points[a]) < -1e-5f || Side(points[b]) < -1e-5f || Side(points[c]) < -1e-5f) continue;
                if (original.Values[a].Count==0 || original.Values[b].Count==0 || original.Values[c].Count==0) continue;
                if (Vector3.Cross(points[b]-points[a],points[c]-points[a]).sqrMagnitude < 1e-16f) continue;
                var t = new FitBodyTriangle { a=points[a],b=points[b],c=points[c],wa=original.Values[a],wb=original.Values[b],wc=original.Values[c],bounds=new Bounds(points[a],Vector3.zero) };
                t.bounds.Encapsulate(t.b); t.bounds.Encapsulate(t.c); triangles.Add(t);
            }
            if (triangles.Count==0) throw new InvalidOperationException("No skinned surface was found on the source side.");
            var surface = new FitBodySurface(triangles.ToArray());
            var cache = new Dictionary<Vector3,BoneWeight1[]>();
            int changed=0;
            for (int v=0;v<points.Length;v++)
            {
                if (Side(points[v]) >= -1e-5f) continue; // Source side and center seam remain intact.
                if (!cache.TryGetValue(points[v],out var mirrored))
                {
                    var reflected = points[v] - 2 * Vector3.Dot(points[v]-center,axis) * axis;
                    mirrored = surface.InterpolatedWeights(reflected);
                    for (int k=0;k<mirrored.Length;k++) mirrored[k].boneIndex=map[mirrored[k].boneIndex];
                    cache.Add(points[v],mirrored);
                }
                var fixedWeights = original.Values[v].Where(w=>locked[w.boneIndex]).ToList();
                float remaining = Mathf.Max(0,1-fixedWeights.Sum(w=>w.weight));
                float free = mirrored.Where(w=>!locked[w.boneIndex]).Sum(w=>w.weight);
                if (free <= 1e-8f || remaining <= 1e-8f) continue;
                var result = output.Values[v]; result.Clear(); result.AddRange(fixedWeights);
                foreach (var w in mirrored) if (!locked[w.boneIndex]) result.Add(new BoneWeight1 { boneIndex=w.boneIndex,weight=w.weight/free*remaining });
                changed++;
            }
            if (changed>0)
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,Part.mesh},"Mirror skin weights");
                output.Apply(Part.mesh); Rebuild(); Changed();
            }
            error=null;
            lastView?.ShowNotification(new GUIContent($"Mirrored {changed:N0} vertices {(leftToRight ? "left to right" : "right to left")} in A-pose"));
        }
    }
}
