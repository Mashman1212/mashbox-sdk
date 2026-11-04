using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool CanSplitSelection => Document && mode != ComponentMode.Object && ComponentTargetNodes(true).Count > 0;
        private HashSet<int> SplitSelectionFaces()
        {
            var faces = Faces();
            if (mode == ComponentMode.Face) return new HashSet<int>(Components.Where(f => f >= 0 && f < faces.Count));
            var result = new HashSet<int>();
            if (mode == ComponentMode.Vertex)
            {
                // Match vertex isolation: capture once, then only direct incident faces.
                var selected = SelectedVertices();
                for (int f = 0; f < faces.Count; f++) if (faces[f].Vertices.Any(selected.Contains)) result.Add(f);
            }
            else if (mode == ComponentMode.Edge)
            {
                var topology = new StudioMeshCuts(Document.nodes[Active]); var edges = Edges();
                (int,int) Key(int a,int b) => weld ? topology.NormalEdgeKey(a,b) : (Math.Min(a,b),Math.Max(a,b));
                var selected = new HashSet<(int,int)>(Components.Where(e => e >= 0 && e < edges.Count).Select(e => Key(edges[e].a,edges[e].b)));
                for (int f = 0; f < faces.Count; f++)
                {
                    var vertices = faces[f].Vertices;
                    for (int e = 0; e < vertices.Count; e++) if (selected.Contains(Key(vertices[e],vertices[(e+1)%vertices.Count]))) { result.Add(f); break; }
                }
            }
            return result;
        }
        private void SplitSelectedComponents()
        {
            PrepareMeshSelection(ComponentMode.Face, () => CanSplitSelection,
                "Select faces, edges or vertices to split into a new mesh.", ComponentMode.Vertex, ComponentMode.Edge, ComponentMode.Face);
            var selection = new Dictionary<int,HashSet<int>>();
            foreach (int node in ComponentTargetNodes(true)) using (new ComponentTargetScope(this,node))
            {
                var faces = SplitSelectionFaces(); if (faces.Count > 0) selection.Add(node,faces);
            }
            if (selection.Count == 0) throw new InvalidOperationException("Select faces, or vertices or edges directly attached to faces, to split.");
            var split = Document.SplitMeshes(selection);
            if (split.Length == 0) return;
            CancelMarkingMenu(); EndPivotSnap(); StopCutTool(); pivot = false; marquee = false; gestureUndo = -1;
            ClearAllComponentSelections(); objects.Clear();
            foreach (int node in split) objects.Add(node);
            Active = split[split.Length-1]; mode = ComponentMode.Object;
            foreach (var target in componentTargets.Values) target.mode = ComponentMode.Object;
            search = ""; UnityEngine.GUIUtility.hotControl = 0; Rebuild(); Changed(); RememberAction(RepeatKind.Split);
            // The shared Run command transaction snapshots source/result selection
            // together with this document action. No separate history stack or pane.
        }
    }
}

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private const string DuplicateFaceTool="Duplicate Face";
        private void DuplicateSelectedFaces(bool asObject)
        {
            ChooseSelectionAction(asObject?"Duplicate Face as New Object":DuplicateFaceTool,
                ()=>EditSelectedFaces(true,asObject),"Select faces to duplicate.");
        }
        private bool HasFaceToolSelection => Document && mode==ComponentMode.Face && ComponentTargetNodes(true).Any(i=>ComponentTargetAt(i).mode==ComponentMode.Face);
        private void EditSelectedFaces(bool duplicate,bool asObject=false)
        {
            var previousTool=tool;
            string label=duplicate?(asObject?"Duplicate Face as New Object":DuplicateFaceTool):"Detach Face";
            PrepareMeshSelection(ComponentMode.Face,()=>HasFaceToolSelection,"Select faces to "+(duplicate?"duplicate.":"detach."),ComponentMode.Face);
            FlushModifierEdits();
            var selection=new Dictionary<int,HashSet<int>>();
            foreach(int index in ComponentTargetNodes(true))using(new ComponentTargetScope(this,index))
            {
                if(mode!=ComponentMode.Face)continue;
                var node=Document.nodes[index];var topology=new StudioMeshCuts(node);topology.ValidateSplitSource(node);
                if(Document.IsBone(index)||!string.IsNullOrEmpty(node.anchorType))throw new InvalidOperationException(label+" requires ordinary static mesh objects.");
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History before editing this mesh.");
                var faces=new HashSet<int>(Components.Where(f=>f>=0&&f<topology.Faces.Count));
                if(faces.Count>0)selection.Add(index,faces);
            }
            if(selection.Count==0)throw new InvalidOperationException("Select faces to "+(duplicate?"duplicate.":"detach."));
            PrepareIsolationTopologyEdit();
            var before=CaptureSelection();
            if(duplicate&&asObject)
            {
                var added=Document.SplitMeshes(selection,duplicate:true);
                ClearAllComponentSelections();objects.Clear();foreach(int i in added)objects.Add(i);
                Active=added.Last();mode=ComponentMode.Object;foreach(var target in componentTargets.Values)target.mode=mode;
                Rebuild();Changed();
            }
            else
            {
                // Linked mesh selections form one region and publish once per mesh.
                var regions=selection.GroupBy(p=>Document.nodes[p.Key].mesh).ToDictionary(g=>g.Key,g=>new HashSet<int>(g.SelectMany(p=>p.Value)));
                var results=new Dictionary<UnityEngine.Mesh,int[]>();
                using(var preview=new StudioMeshToolPreview(Document,selection.Keys))
                {
                    foreach(var target in preview.Targets)results.Add(target.Live,target.Topology.CopyFaceRegion(target.Work,regions[target.Live],duplicate));
                    preview.HasChanges=true;writingModifiers=true;
                    try {preview.Commit(label,()=> {
                        RecordDirectPreviewModifiers(preview,label);
                        foreach(var pair in selection)using(new ComponentTargetScope(this,pair.Key))
                        {Components.Clear();Components.UnionWith(results[Document.nodes[pair.Key].mesh]);}
                        RecordSelectionChange(before,false,label);
                    });}
                    finally{writingModifiers=false;}
                }
                Rebuild();Changed();EstablishSelectionBaseline();
            }
            tool=previousTool==Tool.Rotate||previousTool==Tool.Scale?previousTool:Tool.Move;
            repaint();
            RememberAction(duplicate?RepeatKind.DuplicateFace:RepeatKind.DetachFace,new UnityEngine.Vector4(asObject?1:0,0,0,0));
        }
    }
}
