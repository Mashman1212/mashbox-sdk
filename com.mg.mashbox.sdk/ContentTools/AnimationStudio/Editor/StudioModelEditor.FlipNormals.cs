using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void FlipSelectedNormals()=>ChooseSelectionAction("Flip Normals",FlipSelectionNormals,"Select mesh objects, faces, edges or vertices to reverse face orientation.");
        private void FlipSelectionNormals()
        {
            PrepareMeshSelection(ComponentMode.Face,()=>Document&&(mode==ComponentMode.Object?NormalTargets().Any():ComponentTargetNodes(true).Count>0),"Select editable mesh objects, faces, edges or vertices to flip.",ComponentMode.Object,ComponentMode.Face,ComponentMode.Edge,ComponentMode.Vertex);
            FlushModifierEdits();var selected=new Dictionary<int,HashSet<int>>();var modes=new Dictionary<int,ComponentMode>();var ids=new Dictionary<int,int[]>();var edgePairs=new Dictionary<int,(int a,int b)[]>();
            bool wholeObjects=mode==ComponentMode.Object;
            var nodes=(wholeObjects?NormalTargets():ComponentTargetNodes(true)).ToArray();
            foreach(int i in nodes)using(new ComponentTargetScope(this,i))
            {
                var node=Document.nodes[i];if(Document.IsBone(i)||!string.IsNullOrEmpty(node.anchorType))throw new InvalidOperationException("Flip Normals requires ordinary static meshes.");
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History before flipping normals.");
                var faces=wholeObjects?new HashSet<int>(Enumerable.Range(0,Faces().Count)):SplitSelectionFaces();if(faces.Count==0)continue;
                selected.Add(i,faces);modes.Add(i,wholeObjects?ComponentMode.Object:mode);ids.Add(i,Components.ToArray());if(mode==ComponentMode.Edge){var edges=Edges();edgePairs.Add(i,Components.Where(e=>e>=0&&e<edges.Count).Select(e=>edges[e]).ToArray());}
            }
            if(selected.Count==0)throw new InvalidOperationException("Select faces, or edges or vertices directly attached to faces, to flip.");
            PrepareIsolationTopologyEdit();var before=CaptureSelection();var regions=selected.GroupBy(p=>Document.nodes[p.Key].mesh).ToDictionary(g=>g.Key,g=>new HashSet<int>(g.SelectMany(p=>p.Value)));var maps=new Dictionary<Mesh,Dictionary<int,int>>();
            using(var preview=new StudioMeshToolPreview(Document,selected.Keys))
            {
                foreach(var target in preview.Targets)maps[target.Live]=target.Topology.FlipNormals(target.Work,regions[target.Live]);
                preview.HasChanges=true;writingModifiers=true;
                try{preview.Commit("Flip normals",()=>{
                    foreach(var target in preview.Targets)
                    {
                        var node=Document.nodes.First(n=>n.mesh==target.Live);var source=target.ModifierSourceNode();var operation=new StudioModifierOperation{kind="FlipNormals",name="Flip Normals",faces=regions[target.Live].OrderBy(f=>f).ToArray()};operation.Bind(source);AppendModifier(node,operation,StudioModifierSource.Capture(source));
                    }
                    Rebuild();Changed();
                    foreach(int i in selected.Keys)using(new ComponentTargetScope(this,i))
                    {
                        var map=maps[Document.nodes[i].mesh];int Remap(int v)=>map.TryGetValue(v,out int value)?value:v;
                        Components.Clear();
                        if(modes[i]==ComponentMode.Face)Components.UnionWith(ids[i]);
                        else if(modes[i]==ComponentMode.Vertex)Components.UnionWith(ids[i].Select(Remap));
                        else if(modes[i]==ComponentMode.Edge)
                        {
                            var topology=new StudioMeshCuts(Document.nodes[i]);(int,int) Key(int a,int b)=>weld?topology.NormalEdgeKey(a,b):(Math.Min(a,b),Math.Max(a,b));
                            var keys=new HashSet<(int,int)>(edgePairs[i].Select(e=>Key(Remap(e.a),Remap(e.b))));var edges=Edges();for(int e=0;e<edges.Count;e++)if(keys.Contains(Key(edges[e].a,edges[e].b)))Components.Add(e);
                        }
                    }
                    RecordSelectionChange(before,false,"Flip normals");
                });}finally{writingModifiers=false;}
            }
            EstablishSelectionBaseline();RememberAction(RepeatKind.FlipNormals);repaint();
        }
    }
}
