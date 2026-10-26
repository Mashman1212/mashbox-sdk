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
                if (normals.Length != points.Length) throw new InvalidOperationException("Stitching requires imported vertex normals.");
                weights = new StudioSkinWeights(mesh);
                var binds = mesh.bindposes;
                var skin = rest.Select((m,i) => i < binds.Length ? m * binds[i] : Matrix4x4.identity).ToArray();
                matrices = new Matrix4x4[points.Length]; groups = new int[points.Length];
                var weld = new Dictionary<Vector3Int,int>();
                for (int i=0;i<points.Length;i++)
                {
                    var m = part.bound ? weights.PreviewSkinMatrix(i,skin) : Matrix4x4.identity;
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
        internal bool CanStitchSelectedBody => Part != null && !Part.referenceOnly && Part.bound && Part.role == StudioSkinningDocument.MeshRole.Body;
        internal bool CanMatchDefaultBody => HasEditableMesh && Part.bound && Part.role == StudioSkinningDocument.MeshRole.Body;

        internal void MatchDefaultBody()
        {
            if (!CanMatchDefaultBody) throw new InvalidOperationException("Select a skinned mesh marked as Body first.");
            EndStroke(); FinishPoseGesture();
            var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
            if(!defaults || !defaults.body) throw new InvalidOperationException("Assign a default body first.");
            var defaultMeshes=new HashSet<Mesh>(defaults.body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh));
            var references=Document.parts.Where(p=>p.referenceOnly && defaultMeshes.Contains(p.sourceMesh)).ToArray();
            if(references.Length!=1) throw new InvalidOperationException("Match Default Body requires one loaded default body mesh.");
            if(!previewClip || previewClip.frameRate<=0 || previewClip.length<2f/previewClip.frameRate)
                throw new InvalidOperationException("Match Default Body requires the SDK Skinning Test A-pose.");
            var part=Part; var reference=references[0];
            JumpToPreviewFrame(2);
            var pose=bones.Select(b=>b.localToWorldMatrix).ToArray();
            var source=new StitchSurface(part,pose);
            var target=new StitchSurface(reference,pose);
            var bounds=new Bounds(target.points[0],Vector3.zero);
            foreach(var point in target.points) bounds.Encapsulate(point);
            float tolerance=bounds.size.magnitude*.01f;
            var index=new StudioSkinSpatialIndex(target.points,tolerance);
            var nearby=new List<int>(); var mapping=new Dictionary<int,int>(); var used=new HashSet<int>();
            bool exact=true;
            foreach(int vertex in source.groups.Distinct().OrderBy(v=>v))
            {
                index.Query(source.points[vertex],tolerance,nearby);
                int closest=-1; float best=tolerance*tolerance;
                foreach(int candidate in nearby)
                {
                    float distance=(source.points[vertex]-target.points[candidate]).sqrMagnitude;
                    if(distance<best || (distance==best && (closest<0 || candidate<closest)))
                    {best=distance;closest=candidate;}
                }
                if(closest<0 || (source.boundary.Contains(vertex) && !target.boundary.Contains(target.groups[closest])) || !used.Add(target.groups[closest]))
                {exact=false;break;}
                mapping.Add(vertex,closest);
            }
            float reach=bounds.size.magnitude*.025f;
            var boundaryTargets=new Dictionary<int,Tuple<Vector3,Vector3>>();
            var boundaryCorners=new Dictionary<int,Tuple<StitchSurface,int>>();
            MatchStitchCorners(source,new[]{target},reach,boundaryTargets,boundaryCorners);
            FitBodySurface surface=null;
            if(!exact)
            {
                var triangles=reference.mesh.triangles;var faces=new List<FitBodyTriangle>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    if(Vector3.Cross(target.points[b]-target.points[a],target.points[c]-target.points[a]).sqrMagnitude<1e-16f)continue;
                    var face=new FitBodyTriangle {a=target.points[a],b=target.points[b],c=target.points[c],
                        na=target.normals[a],nb=target.normals[b],nc=target.normals[c],
                        wa=target.weights.Values[a],wb=target.weights.Values[b],wc=target.weights.Values[c],
                        bounds=new Bounds(target.points[a],Vector3.zero)};
                    face.bounds.Encapsulate(face.b);face.bounds.Encapsulate(face.c);faces.Add(face);
                }
                if(faces.Count==0)throw new InvalidOperationException("Default Body has no valid surface to match.");
                surface=new FitBodySurface(faces.ToArray());
            }
            var working=UnityEngine.Object.Instantiate(part.mesh);
            try
            {
                // Move blend-shape data into the reference bind space as well.
                RebaseStitchMesh(working,reference.mesh.bindposes,pose);
                var points=working.vertices; var normals=working.normals;
                var referencePoints=reference.mesh.vertices; var referenceNormals=reference.mesh.normals;
                var matchedWeights=new StudioSkinWeights(working);
                var targetBinds=working.bindposes;
                var targetSkin=pose.Select((m,i)=>m*targetBinds[i]).ToArray();
                int matched=0,unmatched=0,unmatchedBoundary=0;
                for(int v=0;v<points.Length;v++)
                {
                    bool boundary=source.boundary.Contains(source.groups[v]);
                    if(boundary && boundaryCorners.TryGetValue(source.groups[v],out var corner))
                    {
                        // A corresponding edge-loop corner must inherit the exact
                        // reference bind-space point and weights in every pose.
                        int donor=corner.Item2;
                        points[v]=referencePoints[donor];normals[v]=referenceNormals[donor];
                        matchedWeights.Values[v]=new List<BoneWeight1>(target.weights.Values[donor]);
                    }
                    else if(exact)
                    {
                        int donor=mapping[source.groups[v]];
                        points[v]=referencePoints[donor]; normals[v]=referenceNormals[donor];
                        matchedWeights.Values[v]=new List<BoneWeight1>(target.weights.Values[donor]);
                    }
                    else
                    {
                        Vector3 point,normal;
                        BoneWeight1[] sampled;
                        if(boundary)
                        {
                            if(!MatchBodyBoundaryEdge(target,source.points[v],reach,out point,out normal,out sampled))
                            {unmatched++;unmatchedBoundary++;continue;}
                        }
                        else
                        {
                            if(!surface.Closest(source.points[v],reach,out point,out normal))
                            {unmatched++;continue;}
                            sampled=surface.InterpolatedWeights(point);
                        }
                        // Project to the triangle surface, not its nearest corner:
                        // different mesh densities must not collapse onto one vertex.
                        var influences=sampled.OrderByDescending(w=>w.weight).ThenBy(w=>w.boneIndex).Take(4).ToList();
                        float sum=influences.Sum(w=>w.weight);
                        for(int k=0;k<influences.Count;k++){var w=influences[k];w.weight/=sum;influences[k]=w;}
                        matchedWeights.Values[v]=influences;
                        var matrix=matchedWeights.PreviewSkinMatrix(v,targetSkin);
                        if(Mathf.Abs(matrix.determinant)<1e-8f)throw new InvalidOperationException("Cannot match a singular skin transform. No mesh was changed.");
                        points[v]=matrix.inverse.MultiplyPoint3x4(point);
                        normals[v]=matrix.transpose.MultiplyVector(normal).normalized;
                    }
                    matched++;
                }
                if(matched==0)throw new InvalidOperationException("No vertices are near Default Body. Align the body to the reference and try again. No mesh was changed.");
                working.vertices=points; working.normals=normals;
                matchedWeights.Apply(working); working.RecalculateTangents(); working.RecalculateBounds();
                Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Match Default Body");
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{Document,part.mesh},"Match Default Body");
                EditorUtility.CopySerialized(working,part.mesh);
                part.preserveStitchedBindPose=true;
                // Matching a whole body does not freeze it as a bust seam.
                part.stitchedVertices=null; part.stitchedWeights=null; part.stitchedVertexCount=0;
                EditorUtility.SetDirty(part.mesh); Undo.CollapseUndoOperations(group);
                Rebuild(); Changed(); JumpToPreviewFrame(2);
                string message="Matched "+matched+" vertices and skin weights to Default Body"+(exact?" (vertex match).":" (surface match).")+
                    (unmatched>0?" "+unmatched+" vertices outside range were left in place.":"")+
                    (unmatchedBoundary>0?" "+unmatchedBoundary+" boundary vertices had no nearby matching open edge.":"");
                Debug.Log("[MashBox Skinning] "+message+" Workspace copy only.");
                if(lastView) lastView.ShowNotification(new GUIContent(message));
            }
            finally { UnityEngine.Object.DestroyImmediate(working); }
        }

        private static bool MatchBodyBoundaryEdge(StitchSurface target,Vector3 source,float reach,
            out Vector3 point,out Vector3 normal,out BoneWeight1[] weights)
        {
            point=normal=default;weights=null;
            float best=reach*reach,blend=0;Vector2Int closest=default;bool found=false;
            foreach(var edge in target.edges)
            {
                var a=target.points[edge.x];var delta=target.points[edge.y]-a;
                if(delta.sqrMagnitude<1e-16f)continue;
                float t=Mathf.Clamp01(Vector3.Dot(source-a,delta)/delta.sqrMagnitude);
                var candidate=a+t*delta;float distance=(candidate-source).sqrMagnitude;
                if(distance>=best)continue;
                best=distance;point=candidate;blend=t;closest=edge;found=true;
            }
            if(!found)return false;
            normal=Vector3.Lerp(target.normals[closest.x],target.normals[closest.y],blend).normalized;
            var merged=new Dictionary<int,float>();
            void Add(int vertex,float amount)
            {
                float total=target.weights.Values[vertex].Sum(w=>w.weight);
                if(total<=1e-8f)return;
                foreach(var w in target.weights.Values[vertex])
                    merged[w.boneIndex]=(merged.TryGetValue(w.boneIndex,out float value)?value:0)+amount*w.weight/total;
            }
            Add(closest.x,1-blend);Add(closest.y,blend);
            weights=merged.Where(p=>p.Value>1e-8f).Select(p=>new BoneWeight1{boneIndex=p.Key,weight=p.Value}).ToArray();
            return weights.Length>0;
        }

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

        internal void StitchBody()
        {
            if (!CanStitchSelectedBody) throw new InvalidOperationException("Select a bound mesh marked as Body first.");
            StitchBodyParts(new[] { Part });
        }

        private void StitchBodyParts(StudioSkinningDocument.Part[] bodies)
        {
            // Prefer the user's visible bust; hidden alternatives must not silently
            // become seam donors. With no visible user bust, use Default Bust.
            var busts=Document.parts.Where(p=>!p.referenceOnly && p.visible && p.bound && p.mesh && p.role==StudioSkinningDocument.MeshRole.Bust).ToArray();
            if(busts.Length>1) throw new InvalidOperationException("Leave only the bust you want to stitch to visible, then run Stitch to Bust again.");
            if(busts.Length==0)
            {
                var defaults=StudioDefaultReferences.LoadSkinning("Packages/com.mg.mashbox.sdk/ContentTools/AnimationStudio/Defaults/Default Skinning Body.asset");
                if(!defaults || !defaults.bust) throw new InvalidOperationException("Load a skinned bust or assign Default Bust first.");
                var meshes=new HashSet<Mesh>(defaults.bust.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh));
                busts=Document.parts.Where(p=>p.referenceOnly && p.bound && p.mesh && meshes.Contains(p.sourceMesh)).ToArray();
            }
            if(busts.Length==0) throw new InvalidOperationException("Load Default Bust or show a skinned mesh marked as Bust first.");
            StitchPartsToReference(bodies,busts,"Stitch to Bust");
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
                var target = weights.PreviewSkinMatrix(i,newSkin);
                if (Mathf.Abs(target.determinant)<1e-8f)
                    throw new InvalidOperationException("Cannot stitch a singular skin transform.");
                transforms[i] = target.inverse * weights.PreviewSkinMatrix(i,oldSkin);
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
            StitchPartsToReference(busts,bodies,"Stitch to Body");
        }

        private void StitchPartsToReference(StudioSkinningDocument.Part[] busts,StudioSkinningDocument.Part[] bodies,string action)
        {
            EndStroke(); FinishPoseGesture();
            if(busts.Any(p=>bodies.Contains(p))) throw new InvalidOperationException("The bust must be a separate mesh from the body.");
            if (!previewClip || previewClip.frameRate <= 0 || previewClip.length < 2f / previewClip.frameRate)
                throw new InvalidOperationException(action+" needs frame 2 of the SDK Skinning Test clip (A-pose). No geometry was changed.");
            // Sample the same A-pose used by the timeline, then solve in that
            // shared posed space. Inverse skin matrices return edits to mesh space.
            JumpToPreviewFrame(2);
            var stitchPose = bones.Select(b => b.localToWorldMatrix).ToArray();
            var surfaces=bodies.Select(p=>new StitchSurface(p,stitchPose)).ToArray();
            var bounds=new Bounds(surfaces[0].points[0],Vector3.zero);
            foreach(var s in surfaces) foreach(var p in s.points) bounds.Encapsulate(p);
            if(action=="Stitch to Bust")
                foreach(var part in busts) foreach(var point in new StitchSurface(part,stitchPose).points) bounds.Encapsulate(point);
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
                    var edgeWeights = new Dictionary<int,BoneWeight1[]>();
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
                        if(found)
                        {
                            targets[v]=Tuple.Create(point,normal);
                            if(action=="Stitch to Bust")
                            {
                                // A different-density body seam also needs the
                                // bust edge's weights, not just its A-pose position.
                                float nearest=tolerance;
                                foreach(var donor in surfaces)
                                    if(MatchBodyBoundaryEdge(donor,source.points[v],nearest,out var p,out var n,out var w))
                                    {
                                        nearest=Vector3.Distance(source.points[v],p);
                                        targets[v]=Tuple.Create(p,n);edgeWeights[v]=w;
                                    }
                            }
                        }
                        else unmatched++;
                    }
                    if(targets.Count==0) continue;
                    var stitchedVertices = new List<int>();
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
                        else if(edgeWeights.TryGetValue(source.groups[i],out var sampled))
                        {
                            var kept=sampled.OrderByDescending(w=>w.weight).ThenBy(w=>w.boneIndex).Take(4).ToList();
                            float sum=kept.Sum(w=>w.weight);
                            for(int k=0;k<kept.Count;k++){var w=kept[k];w.weight/=sum;kept[k]=w;}
                            source.weights.Values[i]=kept;
                        }
                        var matrix = source.weights.PreviewSkinMatrix(i,bustSkin);
                        if (Mathf.Abs(matrix.determinant)<1e-8f)
                            throw new InvalidOperationException("Cannot stitch a singular seam skin transform. No geometry was changed.");
                        positions[i]=matrix.inverse.MultiplyPoint3x4(target.Item1);
                        normals[i]=matrix.transpose.MultiplyVector(target.Item2).normalized;
                        stitchedVertices.Add(i);
                        matched++;
                    }
                    changes.Add(()=> { Undo.RegisterCompleteObjectUndo(bust.mesh,action); working.vertices=positions; working.normals=normals; source.weights.Apply(working); working.RecalculateTangents(); working.RecalculateBounds(); EditorUtility.CopySerialized(working,bust.mesh); bust.preserveStitchedBindPose=true; bust.stitchedVertices=stitchedVertices.ToArray(); bust.stitchedVertexCount=positions.Length; bust.CaptureStitchWeights(); EditorUtility.SetDirty(bust.mesh); });
                }
                if(changes.Count==0) throw new InvalidOperationException("No nearby compatible open body/bust edges found. Stitching leaves distant or overlapping surfaces unchanged.");
                Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(action);
                Undo.RegisterCompleteObjectUndo(Document,action);
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
