using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void FillSelectedHoles()=>ChooseSelectionAction("Fill Hole",FillHoles,"Select complete closed loops of open boundary edges to create n-gon caps.");
        private void FillHoles()
        {
            PrepareMeshSelection(ComponentMode.Edge,()=>Document&&mode==ComponentMode.Edge&&ComponentTargetNodes(true).Count>0,"Select closed boundary edge loops to fill.",ComponentMode.Edge);
            FlushModifierEdits();var selection=new Dictionary<int,List<(int a,int b)>>();
            foreach(int i in ComponentTargetNodes(true))using(new ComponentTargetScope(this,i))
            {
                if(mode!=ComponentMode.Edge)continue;var node=Document.nodes[i];
                if(Document.IsBone(i)||!string.IsNullOrEmpty(node.anchorType))throw new InvalidOperationException("Fill Hole requires ordinary static mesh objects.");
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History before filling holes.");
                var edges=Edges();if(Components.Any(e=>e<0||e>=edges.Count))throw new InvalidOperationException("The edge selection changed. Select the hole border again.");
                if(Components.Count>0)selection[i]=Components.OrderBy(e=>e).Select(e=>edges[e]).ToList();
            }
            if(selection.Count==0)throw new InvalidOperationException("Select closed boundary edge loops to fill.");
            PrepareIsolationTopologyEdit();var before=CaptureSelection();
            var regions=selection.GroupBy(p=>Document.nodes[p.Key].mesh).ToDictionary(g=>g.Key,g=>g.SelectMany(p=>p.Value).Distinct().ToList());
            var caps=new Dictionary<Mesh,int[]>();
            using(var preview=new StudioMeshToolPreview(Document,selection.Keys))
            {
                foreach(var target in preview.Targets)caps[target.Live]=target.Topology.FillHoles(target.Work,regions[target.Live]);
                preview.HasChanges=true;writingModifiers=true;
                try{preview.Commit("Fill holes",()=>{
                    foreach(var target in preview.Targets)
                    {
                        var node=Document.nodes.First(n=>n.mesh==target.Live);var source=target.ModifierSourceNode();
                        var operation=new StudioModifierOperation{kind="FillHole",name="Fill Hole",edges=regions[target.Live].SelectMany(e=>new[]{e.a,e.b}).ToArray()};operation.Bind(source);AppendModifier(node,operation,StudioModifierSource.Capture(source));
                    }
                    ClearAllComponentSelections();mode=ComponentMode.Face;
                    foreach(int i in selection.Keys){var target=ComponentTargetAt(i);target.mode=ComponentMode.Face;target.selection.UnionWith(caps[Document.nodes[i].mesh]);}
                    if(Active>=0)Components=ComponentTargetAt(Active).selection;RecordSelectionChange(before,false,"Fill holes");
                });}finally{writingModifiers=false;}
            }
            tool=tool==Tool.Rotate||tool==Tool.Scale?tool:Tool.Move;pivot=false;Rebuild();Changed();EstablishSelectionBaseline();RememberAction(RepeatKind.FillHole);repaint();
        }
    }
}
