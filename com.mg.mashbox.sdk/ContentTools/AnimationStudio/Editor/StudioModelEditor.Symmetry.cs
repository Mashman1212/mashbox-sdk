using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool symmetryEnabled;
        private bool symmetryWorld;
        private int symmetryAxis;
        private StudioMeshSymmetry rotationSymmetry;
        private int rotationSymmetryControl;
        private Mesh rotationSymmetryMesh;
        private void RequireSingleSymmetryObject()
        {
            if (!symmetryEnabled || mode == ComponentMode.Object) return;
            if (!Document || Active < 0 || objects.Count != 1 || ComponentTargetNodes(true).Any(i => i != Active))
                throw new InvalidOperationException("Symmetry supports one mesh object at a time. Select one object or turn Symmetry off.");
            if (Document.nodes.Count(n => n.mesh == ActiveMesh) > 1 || Document.nodes[Active].skinned)
                throw new InvalidOperationException("Symmetry requires an independent static mesh. Make linked instances independent first.");
        }
        private StudioMeshSymmetry CaptureSymmetry(Vector3[] vertices = null)
        {
            if (!symmetryEnabled || pivot || mode == ComponentMode.Object || !ActiveMesh) return null;
            RequireSingleSymmetryObject();
            return new StudioMeshSymmetry(vertices ?? ActiveMesh.vertices, Document.nodes[Active].geometryVertices,
                Transforms[Active].localToWorldMatrix, ActiveMesh.bounds, symmetryWorld, symmetryAxis);
        }
        private StudioMeshSymmetry.Deformation CaptureSymmetryDeformation(Vector3[] vertices, IEnumerable<int> selected)
            => CaptureSymmetry(vertices)?.Capture(selected);
        private void ExpandSymmetrySelection()
        {
            if (symmetrySeparateSelection) { RequireSingleSymmetryObject(); return; }
            if (!symmetryEnabled || mode == ComponentMode.Object || Components.Count == 0) { RequireSingleSymmetryObject(); return; }
            var symmetry = CaptureSymmetry(); if (symmetry == null) return;
            var result = SymmetryComponentSet(Components,symmetry);
            Components.UnionWith(result); componentFrameValid = false;
        }
        private HashSet<int> SymmetryComponentSet(HashSet<int> selection, StudioMeshSymmetry symmetry)
        {
            var result = new HashSet<int>(selection);
            if (mode == ComponentMode.Vertex) result = symmetry.ExpandVertices(selection);
            else if (mode == ComponentMode.Edge)
            {
                var edges=Edges();
                foreach(var pair in MirroredSymmetryEdges(symmetry,new StudioMeshCuts(Document.nodes[Active]),selection.Select(i=>edges[i])))
                {
                    int index=edges.FindIndex(e=>e.a==pair.a&&e.b==pair.b || e.a==pair.b&&e.b==pair.a);
                    if(index<0)throw new InvalidOperationException("The mirrored authored edge is unavailable.");
                    result.Add(index);
                }
            }
            else
            {
                IEnumerable<int> Vertices(int i) => mode == ComponentMode.Edge ? new[] { Edges()[i].a, Edges()[i].b } : Faces()[i].Vertices;
                // Match authored boundary cycles, never render-triangle diagonals.
                string Key(IEnumerable<int> ids)
                {
                    var values = ids.ToArray(); string best = null;
                    for (int r = 0; r < values.Length; r++)
                    {
                        string key = string.Join(",", Enumerable.Range(0, values.Length).Select(i => values[(i+r)%values.Length]));
                        if (best == null || string.CompareOrdinal(key,best) < 0) best = key;
                    }
                    return best;
                }
                var lookup = new Dictionary<string,List<int>>();
                for (int i = 0; i < ComponentCount(); i++)
                {
                    var key = Key(Vertices(i).Select(v => symmetry.Geometry[v]));
                    if (!lookup.TryGetValue(key,out var list)) lookup[key] = list = new List<int>(); list.Add(i);
                }
                foreach (int i in selection)
                {
                    string key = Key(Vertices(i).Reverse().Select(symmetry.MirroredGeometry));
                    if (!lookup.TryGetValue(key,out var matches) || matches.Count != 1)
                        throw new InvalidOperationException("Symmetry needs a matching authored edge or face on the opposite side of this mesh.");
                    result.Add(matches[0]);
                }
            }
            if (result.Any(i => !IsolationAllowsComponent(i)))
                throw new InvalidOperationException("Show both sides of the mesh before editing with Symmetry.");
            return result;
        }
        private void SetSymmetry(bool? enabled = null, bool? world = null, int? axis = null)
        {
            FinishToolsForSwitch();
            if (GUIUtility.hotControl != 0) throw new InvalidOperationException("Finish the current drag before changing Symmetry.");
            if (enabled == true && Document && objects.Count > 1)
                throw new InvalidOperationException("Select one object before enabling Symmetry.");
            symmetryEnabled = enabled ?? symmetryEnabled; symmetryWorld = world ?? symmetryWorld; symmetryAxis = axis ?? symmetryAxis;
            rotationSymmetry = null; rotationSymmetryMesh = null; InvalidateSymmetryDisplay();
            // A session mode: opening another Studio starts safely disabled.
            componentFrameValid = false; repaint();
        }
        private void PopulateSymmetryMenu(StudioMenu menu)
        {
            menu.AddItem(new GUIContent("Symmetry/Enabled"), symmetryEnabled, () => Run(() => SetSymmetry(enabled: !symmetryEnabled)));
            menu.AddItem(new GUIContent("Symmetry/World or Object"), symmetryWorld, () => Run(() => SetSymmetry(world: !symmetryWorld)), commandKey: "Symmetry/Space");
            menu.AddItem(new GUIContent("Symmetry/World"), symmetryWorld, () => Run(() => SetSymmetry(world: true)));
            menu.AddItem(new GUIContent("Symmetry/Object"), !symmetryWorld, () => Run(() => SetSymmetry(world: false)));
            foreach (int axis in new[] {0,1,2})
            { int captured = axis; menu.AddItem(new GUIContent("Symmetry/"+"XYZ"[axis]), symmetryAxis == axis, () => Run(() => SetSymmetry(axis: captured))); }
        }
        private StudioMeshSymmetry.Deformation CaptureRotationSymmetry(IEnumerable<int> indices)
        {
            if (!symmetryEnabled) return null;
            if (rotationSymmetry == null || rotationSymmetryControl != GUIUtility.hotControl || rotationSymmetryMesh != ActiveMesh || GUIUtility.hotControl == 0)
            { rotationSymmetry = CaptureSymmetry(); rotationSymmetryControl = GUIUtility.hotControl; rotationSymmetryMesh = ActiveMesh; }
            return rotationSymmetry?.Capture(indices);
        }
        private void ApplyRotationSymmetry(Vector3[] values, IEnumerable<int> indices) => CaptureRotationSymmetry(indices)?.Apply(values);
        private void RequireSymmetryOff(string operation)
        {
            if (symmetryEnabled) throw new InvalidOperationException("Turn Symmetry off before using " + operation + ".");
        }
        private bool symmetrySeparateSelection;
        private void PrepareSymmetryBridgeSelection(Func<bool> ready,string guidance)
        {
            symmetrySeparateSelection=true;
            try { PrepareMeshSelection(ComponentMode.Edge,ready,guidance,ComponentMode.Edge); }
            finally { symmetrySeparateSelection=false; }
        }
        private (int a,int b)[] liveMirroredBridgeEdges;
        private int[] liveSymmetryMergePrimary,liveSymmetryMergeMirror;
        private StudioSymmetryDeformation liveSymmetryDeformation;
        private void CaptureSymmetryMerge()
        {
            liveSymmetryMergePrimary=liveSymmetryMergeMirror=null;
            var symmetry=CaptureSymmetry();if(symmetry==null)return;
            var positions=ActiveMesh.vertices;
            // A seam shared by both merge regions must contract as one region.
            if(liveSelection.Any(i=>Mathf.Abs(symmetry.Side(positions[i]))<=symmetry.Tolerance))return;
            liveSymmetryMergePrimary=liveSelection.Where(i=>symmetry.Side(positions[i])>0).ToArray();
            liveSymmetryMergeMirror=liveSelection.Where(i=>symmetry.Side(positions[i])<0).ToArray();
        }
        private IEnumerable<(int a,int b)> MirroredSymmetryEdges(StudioMeshSymmetry symmetry, StudioMeshCuts topology,IEnumerable<(int a,int b)> edges)
        {
            var result=new HashSet<(int,int)>();
            foreach(var edge in edges)
            {
                int a=symmetry.MirroredGeometry(edge.a),b=symmetry.MirroredGeometry(edge.b);bool found=false;
                for(int f=0;f<topology.Faces.Count;f++)
                {
                    var v=topology.Faces[f].Vertices;
                    if(!Enumerable.Range(0,v.Count).Any(i=>v[i]==edge.a&&v[(i+1)%v.Count]==edge.b || v[i]==edge.b&&v[(i+1)%v.Count]==edge.a))continue;
                    var mirrored=topology.Faces[MirroredSymmetryFace(symmetry,topology,f)].Vertices;
                    for(int i=0;i<mirrored.Count;i++)
                    {
                        int x=mirrored[i],y=mirrored[(i+1)%mirrored.Count];
                        if(symmetry.Geometry[x]==a&&symmetry.Geometry[y]==b || symmetry.Geometry[x]==b&&symmetry.Geometry[y]==a)
                        {result.Add((Math.Min(x,y),Math.Max(x,y)));found=true;}
                    }
                }
                if(!found)throw new InvalidOperationException("Symmetry needs a matching authored edge.");
            }
            return result;
        }
        private (int a,int b)[] CaptureSymmetryBridgeEdges((int a,int b)[] edges)
        {
            var symmetry=CaptureSymmetry();if(symmetry==null)return null;
            var mirrored=MirroredSymmetryEdges(symmetry,new StudioMeshCuts(Document.nodes[Active]),edges).ToArray();
            var original=new HashSet<(int,int)>(edges.Select(e=>(Math.Min(e.a,e.b),Math.Max(e.a,e.b))));
            if(original.SetEquals(mirrored))return null;
            if(mirrored.Any(original.Contains))throw new InvalidOperationException("Symmetric bridges must use separate matching borders, or one complete self-symmetric border pair.");
            return mirrored;
        }
        private StudioMeshCuts.MergeResult SymmetryTargetWeld(StudioMeshToolPreview.Target target,int source,int destination)
        {
            var symmetry=CaptureSymmetry();
            int mirroredSource=-1,mirroredDestination=-1;
            if(symmetry!=null)
            {
                mirroredSource=symmetry.MirroredGeometry(source);mirroredDestination=symmetry.MirroredGeometry(destination);
                var own=new HashSet<int>{symmetry.Geometry[source],symmetry.Geometry[destination]};
                if(own.Contains(mirroredSource)||own.Contains(mirroredDestination))
                {
                    if(mirroredSource!=symmetry.Geometry[source]||mirroredDestination!=symmetry.Geometry[destination])
                        throw new InvalidOperationException("Symmetric Target Weld requires separate source/destination pairs, or two vertices on the symmetry plane.");
                    symmetry=null;
                }
            }
            var result=target.Topology.MergeVertices(target.Work,new[]{source,destination},true,0,Matrix4x4.identity,target:destination);
            if(symmetry==null||result==null)return result;
            var geometry=StudioSubdivisionData.Geometry(target.Work);
            var selected=new HashSet<int>(result.Selection.Select(v=>geometry[v]));
            int a=Array.FindIndex(geometry,g=>g==mirroredSource),b=Array.FindIndex(geometry,g=>g==mirroredDestination);
            if(a<0||b<0)throw new InvalidOperationException("The first weld invalidated the mirrored target.");
            var second=new StudioMeshCuts(target.Work).MergeVertices(target.Work,new[]{a,b},true,0,Matrix4x4.identity,target:b);
            geometry=StudioSubdivisionData.Geometry(target.Work);
            if(second!=null)selected.UnionWith(second.Selection.Select(v=>geometry[v]));
            result.Selection=Enumerable.Range(0,geometry.Length).Where(i=>selected.Contains(geometry[i])).ToArray();
            return result;
        }
        private int MirroredSymmetryFace(StudioMeshSymmetry symmetry, StudioMeshCuts topology, int face)
        {
            var wanted = topology.Faces[face].Vertices.Select(symmetry.MirroredGeometry).Reverse().ToArray();
            int result = -1;
            for (int f = 0; f < topology.Faces.Count; f++)
            {
                var vertices = topology.Faces[f].Vertices; if (vertices.Count != wanted.Length) continue;
                bool match = false;
                for (int offset = 0; offset < vertices.Count && !match; offset++)
                    match = Enumerable.Range(0,vertices.Count).All(i => symmetry.Geometry[vertices[(i+offset)%vertices.Count]] == wanted[i]);
                if (!match) continue;
                if (result >= 0) throw new InvalidOperationException("Symmetry found ambiguous coincident faces.");
                result = f;
            }
            if (result < 0) throw new InvalidOperationException("Symmetry needs a matching authored face for this cut.");
            return result;
        }
        private IList<StudioMeshCuts.FaceStroke> SymmetryStrokes(StudioMeshCuts topology, IList<StudioMeshCuts.FaceStroke> strokes)
        {
            var symmetry = CaptureSymmetry(); if (symmetry == null) return strokes;
            var result = strokes.ToList();
            foreach (var stroke in strokes)
            {
                var mirrored = new StudioMeshCuts.FaceStroke(MirroredSymmetryFace(symmetry,topology,stroke.Face), stroke.Points.Select(symmetry.Reflect).ToArray());
                if (!result.Any(s => s.Face == mirrored.Face && s.Points.Count == mirrored.Points.Count &&
                    (s.Points.Zip(mirrored.Points,(a,b)=>(a-b).sqrMagnitude).All(d=>d<=symmetry.Tolerance*symmetry.Tolerance) ||
                     s.Points.Zip(mirrored.Points.AsEnumerable().Reverse(),(a,b)=>(a-b).sqrMagnitude).All(d=>d<=symmetry.Tolerance*symmetry.Tolerance)))) result.Add(mirrored);
            }
            return result;
        }
        private IList<StudioMeshCuts.Cut> SymmetryCuts(IList<StudioMeshCuts.Cut> cuts)
        {
            if (!symmetryEnabled) return cuts;
            var strokes = cuts.Select(c => new StudioMeshCuts.FaceStroke(c.Face,cutTopology.Position(c.A),cutTopology.Position(c.B))).ToList();
            return SymmetryStrokes(cutTopology,strokes).Select(s => {
                if (!cutTopology.OnCutBoundary(s.Face,s.Points[0],out var a) || !cutTopology.OnCutBoundary(s.Face,s.Points[1],out var b))
                    throw new InvalidOperationException("The mirrored cut endpoints must lie on their matching face boundary.");
                return new StudioMeshCuts.Cut { Face=s.Face,A=a,B=b };
            }).ToList();
        }
    }
}
