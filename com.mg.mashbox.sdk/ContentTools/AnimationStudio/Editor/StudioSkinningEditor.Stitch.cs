using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        // Geometric edges ignore UV/normal splits, without welding the actual mesh.
        private sealed class StitchSurface
        {
            internal Vector3[] points, normals;
            internal Matrix4x4[] matrices;
            internal StudioSkinWeights weights;
            internal StudioSkinningDocument.Part part;
            internal int[] groups;
            internal readonly List<Vector2Int> edges = new List<Vector2Int>();
            internal readonly HashSet<int> boundary = new HashSet<int>();
            internal StitchSurface(StudioSkinningDocument.Part part, Matrix4x4[] rest)
            {
                this.part = part;
                var mesh = part.mesh;
                points = mesh.vertices; normals = mesh.normals;
                if (normals.Length != points.Length) throw new InvalidOperationException("Stitch Bust requires imported vertex normals.");
                weights = new StudioSkinWeights(mesh);
                var binds = mesh.bindposes;
                var skin = rest.Select((m,i) => i < binds.Length ? m * binds[i] : Matrix4x4.identity).ToArray();
                matrices = new Matrix4x4[points.Length]; groups = new int[points.Length];
                var weld = new Dictionary<Vector3Int,int>();
                for (int i=0;i<points.Length;i++)
                {
                    var m = part.bound ? weights.SkinMatrix(i,skin) : Matrix4x4.identity;
                    if (Mathf.Abs(m.determinant)<1e-8f) throw new InvalidOperationException("Cannot stitch a singular skin transform.");
                    matrices[i]=m; points[i]=m.MultiplyPoint3x4(points[i]);
                    normals[i]=m.inverse.transpose.MultiplyVector(normals[i]).normalized;
                    var key = new Vector3Int(Mathf.RoundToInt(points[i].x*100000),Mathf.RoundToInt(points[i].y*100000),Mathf.RoundToInt(points[i].z*100000));
                    if (!weld.TryGetValue(key,out int group)) { group=i; weld.Add(key,group); }
                    groups[i]=group;
                }
                var counts = new Dictionary<Vector2Int,int>();
                var triangles=mesh.triangles;
                for (int t=0;t<triangles.Length;t+=3)
                    for(int k=0;k<3;k++)
                    {
                        int a=groups[triangles[t+k]], b=groups[triangles[t+(k+1)%3]];
                        if(a==b) continue;
                        var edge=new Vector2Int(Mathf.Min(a,b),Mathf.Max(a,b));
                        counts.TryGetValue(edge,out int count); counts[edge]=count+1;
                    }
                foreach(var pair in counts) if(pair.Value==1)
                { edges.Add(pair.Key); boundary.Add(pair.Key.x); boundary.Add(pair.Key.y); }
            }
        }

        // A nearest point on an edge is not necessarily its matching corner.
        // For compatible closed seams, map whole loops and verify every edge.
        private static void MatchStitchCorners(StitchSurface source, StitchSurface[] bodies, float tolerance,
            Dictionary<int,Tuple<Vector3,Vector3>> targets, Dictionary<int,Tuple<StitchSurface,int>> matchedCorners)
        {
            var adjacent = source.boundary.ToDictionary(v => v, v => new List<int>());
            foreach (var edge in source.edges) { adjacent[edge.x].Add(edge.y); adjacent[edge.y].Add(edge.x); }
            var visited = new HashSet<int>();
            foreach (int seed in source.boundary.OrderBy(v => v))
            {
                if (!visited.Add(seed)) continue;
                var component = new List<int>(); var pending = new Queue<int>(); pending.Enqueue(seed);
                while (pending.Count > 0)
                {
                    int v = pending.Dequeue(); component.Add(v);
                    foreach (int next in adjacent[v]) if (visited.Add(next)) pending.Enqueue(next);
                }
                if (component.Any(v => adjacent[v].Count != 2)) continue;
                foreach (var body in bodies)
                {
                    var map = new Dictionary<int,int>(); var used = new HashSet<int>(); bool valid = true;
                    foreach (int v in component)
                    {
                        int closest = -1; float best = tolerance * tolerance;
                        foreach (int b in body.boundary.OrderBy(b => b))
                        {
                            float distance = (source.points[v] - body.points[b]).sqrMagnitude;
                            if (distance < best) { best = distance; closest = b; }
                        }
                        if (closest < 0 || !used.Add(closest)) { valid = false; break; }
                        map.Add(v,closest);
                    }
                    if (!valid) continue;
                    var bodyEdges = new HashSet<Vector2Int>(body.edges);
                    foreach (int v in component) foreach (int next in adjacent[v])
                    {
                        int a = map[v], b = map[next];
                        if (!bodyEdges.Contains(new Vector2Int(Mathf.Min(a,b),Mathf.Max(a,b)))) valid = false;
                    }
                    if (!valid) continue;
                    foreach (int v in component)
                    {
                        targets[v] = Tuple.Create(body.points[map[v]], body.normals[map[v]]);
                        matchedCorners[v] = Tuple.Create(body,map[v]);
                    }
                    break;
                }
            }
        }

        internal bool CanStitchSelectedBust => Part != null && !Part.referenceOnly && Part.bound && Part.role == StudioSkinningDocument.MeshRole.Bust;

        private void SetMeshRole(StudioSkinningDocument.Part part, StudioSkinningDocument.MeshRole role)
        {
            if (part.referenceOnly) return;
            EndStroke(); Undo.RegisterCompleteObjectUndo(Document,"Set skinning mesh role");
            part.role=role; Changed();
        }

        internal void StitchBust()
        {
            if (!CanStitchSelectedBust) throw new InvalidOperationException("Select a bound mesh marked as Bust first.");
            StitchBustParts(new[] { Part });
        }

#if MashBoxDev
        internal void StitchDefaultBust()
        {
            var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if (!defaults || !defaults.bust) throw new InvalidOperationException("Assign a default bust first.");
            var meshes=new HashSet<Mesh>(defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh));
            var parts=Document.parts.Where(p=>p.referenceOnly && meshes.Contains(p.sourceMesh)).ToArray();
            if (parts.Length==0) throw new InvalidOperationException("Load the default bust into this workspace first.");
            StitchBustParts(parts);
        }
#endif

        private static void RebaseStitchMesh(Mesh mesh, Matrix4x4[] binds, Matrix4x4[] pose)
        {
            var weights = new StudioSkinWeights(mesh);
            weights.LimitToFour(); weights.Apply(mesh);
            var previous = mesh.bindposes;
            var oldSkin = pose.Select((m,i) => m * previous[i]).ToArray();
            var newSkin = pose.Select((m,i) => m * binds[i]).ToArray();
            var transforms = new Matrix4x4[mesh.vertexCount];
            for (int i=0;i<transforms.Length;i++)
            {
                var target = weights.SkinMatrix(i,newSkin);
                if (Mathf.Abs(target.determinant)<1e-8f)
                    throw new InvalidOperationException("Cannot stitch a singular skin transform.");
                transforms[i] = target.inverse * weights.SkinMatrix(i,oldSkin);
            }
            TransformExportVertices(mesh,transforms);
            mesh.bindposes = binds;
        }
        private void StitchBustParts(StudioSkinningDocument.Part[] busts)
        {
            EndStroke(); FinishPoseGesture();
            var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if(!defaults || !defaults.body) throw new InvalidOperationException("Assign a body in Default Skinning Body first.");
            Func<GameObject,HashSet<Mesh>> meshes = source => new HashSet<Mesh>(source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh));
            var bodyMeshes=meshes(defaults.body);
            var bodies=Document.parts.Where(p=>p.referenceOnly && bodyMeshes.Contains(p.sourceMesh)).ToArray();
            if(bodies.Length==0) throw new InvalidOperationException("Load the default body into this workspace first.");
            if(busts.Any(p=>bodies.Contains(p))) throw new InvalidOperationException("The bust must be a separate mesh from the body.");
            if (!previewClip || previewClip.frameRate <= 0 || previewClip.length < 2f / previewClip.frameRate)
                throw new InvalidOperationException("Stitch Bust needs frame 2 of the SDK Skinning Test clip (A-pose). No geometry was changed.");
            // Sample the same A-pose used by the timeline, then solve in that
            // shared posed space. Inverse skin matrices return edits to mesh space.
            JumpToPreviewFrame(2);
            var stitchPose = bones.Select(b => b.localToWorldMatrix).ToArray();
            var surfaces=bodies.Select(p=>new StitchSurface(p,stitchPose)).ToArray();
            var bounds=new Bounds(surfaces[0].points[0],Vector3.zero);
            foreach(var s in surfaces) foreach(var p in s.points) bounds.Encapsulate(p);
            // Restrict snapping to nearby open edges (about 1.5 cm on a human).
            float tolerance=bounds.size.magnitude*.008f;
            var changes=new List<Action>(); int matched=0, unmatched=0;
            var checks = new List<Tuple<StudioSkinningDocument.Part,int,StudioSkinningDocument.Part,int>>();
            var temporaryMeshes = new List<Mesh>();
            try
            {
                foreach(var bust in busts)
                {
                    var working = UnityEngine.Object.Instantiate(bust.mesh);
                    working.name = bust.mesh.name;
                    temporaryMeshes.Add(working);
                    // Share the body's bone-local coordinates, not just a position
                    // in the current animation frame. Keep edits transactional.
                    RebaseStitchMesh(working,bodies[0].mesh.bindposes,stitchPose);
                    var source=new StitchSurface(new StudioSkinningDocument.Part { mesh=working, bound=true },stitchPose);
                    var positions=working.vertices; var normals=working.normals;
                    var targets=new Dictionary<int,Tuple<Vector3,Vector3>>();
                    var corners = new Dictionary<int,Tuple<StitchSurface,int>>();
                    MatchStitchCorners(source,surfaces,tolerance,targets,corners);
                    var bustBinds = working.bindposes;
                    var bustSkin = stitchPose.Select((m,i) => i < bustBinds.Length ? m * bustBinds[i] : Matrix4x4.identity).ToArray();
                    foreach(int v in source.boundary)
                    {
                        if (targets.ContainsKey(v)) continue;
                        float best=tolerance*tolerance; Vector3 point=default, normal=default; bool found=false;
                        foreach(var body in surfaces) foreach(var edge in body.edges)
                        {
                            Vector3 a=body.points[edge.x], delta=body.points[edge.y]-a;
                            float t=delta.sqrMagnitude>1e-12f ? Mathf.Clamp01(Vector3.Dot(source.points[v]-a,delta)/delta.sqrMagnitude) : 0;
                            Vector3 candidate=a+t*delta;
                            float distance=(candidate-source.points[v]).sqrMagnitude;
                            if(distance>best) continue;
                            var n=Vector3.Lerp(body.normals[edge.x],body.normals[edge.y],t).normalized;
                            // Authored seam normals can disagree sharply: fixing that
                            // disagreement is part of stitching, not a reason to skip it.
                            best=distance; point=candidate; normal=n; found=true;
                        }
                        if(found) targets[v]=Tuple.Create(point,normal);
                        else unmatched++;
                    }
                    if(targets.Count==0) continue;
                    for(int i=0;i<positions.Length;i++) if(targets.TryGetValue(source.groups[i],out var target))
                    {
                        // Matching positions in one pose is insufficient when both sides
                        // use different skin weights. Only exact matched seam corners
                        // inherit the body's influences, including their UV-split peers.
                        if (corners.TryGetValue(source.groups[i],out var corner))
                        {
                            source.weights.Values[i] = new List<BoneWeight1>(corner.Item1.weights.Values[corner.Item2]);
                            checks.Add(Tuple.Create(bust,i,corner.Item1.part,corner.Item2));
                        }
                        var matrix = source.weights.SkinMatrix(i,bustSkin);
                        if (Mathf.Abs(matrix.determinant)<1e-8f)
                            throw new InvalidOperationException("Cannot stitch a singular seam skin transform. No geometry was changed.");
                        positions[i]=matrix.inverse.MultiplyPoint3x4(target.Item1);
                        normals[i]=matrix.transpose.MultiplyVector(target.Item2).normalized;
                        matched++;
                    }
                    changes.Add(()=> { Undo.RegisterCompleteObjectUndo(bust.mesh,"Stitch Bust"); working.vertices=positions; working.normals=normals; source.weights.Apply(working); working.RecalculateTangents(); working.RecalculateBounds(); EditorUtility.CopySerialized(working,bust.mesh); bust.preserveStitchedBindPose=true; EditorUtility.SetDirty(bust.mesh); });
                }
                if(changes.Count==0) throw new InvalidOperationException("No nearby compatible open body/bust edges found. Stitch Bust leaves distant or overlapping surfaces unchanged.");
                Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Stitch Bust");
                Undo.RegisterCompleteObjectUndo(Document,"Stitch Bust");
                foreach(var apply in changes) apply();
                Undo.CollapseUndoOperations(group); Rebuild(); Changed();
                // Re-sample exactly as the A-pose button does and measure Unity's
                // rendered skin result, not only our CPU transform calculation.
                JumpToPreviewFrame(2);
                float maxGap = 0;
                var baked = new Dictionary<StudioSkinningDocument.Part,Vector3[]>();
                foreach (var part in checks.SelectMany(c => new[] {c.Item1,c.Item3}).Distinct())
                {
                    var mesh = new Mesh();
                    try
                    {
                        objects[Document.parts.IndexOf(part)].GetComponent<SkinnedMeshRenderer>().BakeMesh(mesh);
                        baked[part] = mesh.vertices;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                foreach (var check in checks)
                    maxGap = Mathf.Max(maxGap,Vector3.Distance(baked[check.Item1][check.Item2],baked[check.Item3][check.Item4]));
                string result = "A-pose: stitched "+matched+" vertices";
                if (checks.Count > 0) result += "; measured seam gap " + (maxGap*1000).ToString("0.000") + " mm";
                if (unmatched > 0) result += "; "+unmatched+" boundary vertices outside stitch range";
                Debug.Log("[MashBox Skinning] " + result + ". Workspace copy only.");
                if(lastView) lastView.ShowNotification(new GUIContent(result));
                }
            finally { foreach(var mesh in temporaryMeshes) UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
