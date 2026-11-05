using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private void PokeSelectedFaces()
        {
            ChooseSelectionAction("Poke",PokeFaces,"Select faces to add a center vertex and a connected triangle fan.");
        }
        private void PokeFaces()
        {
            PrepareMeshSelection(ComponentMode.Face,()=>HasFaceToolSelection,"Select faces to poke.",ComponentMode.Face);
            FlushModifierEdits();
            var selection=new Dictionary<int,HashSet<int>>();
            foreach(int i in ComponentTargetNodes(true))using(new ComponentTargetScope(this,i))
            {
                if(mode!=ComponentMode.Face)continue;
                var node=Document.nodes[i];
                if(Document.IsBone(i)||!string.IsNullOrEmpty(node.anchorType))throw new InvalidOperationException("Poke requires ordinary static mesh faces.");
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History before poking faces.");
                if(Components.Count>0)selection.Add(i,new HashSet<int>(Components));
            }
            if(selection.Count==0)throw new InvalidOperationException("Select faces to poke.");
            PrepareIsolationTopologyEdit();var before=CaptureSelection();
            var regions=selection.GroupBy(p=>Document.nodes[p.Key].mesh).ToDictionary(g=>g.Key,g=>new HashSet<int>(g.SelectMany(p=>p.Value)));
            var centers=new Dictionary<Mesh,int[]>();
            using(var preview=new StudioMeshToolPreview(Document,selection.Keys))
            {
                foreach(var target in preview.Targets)centers[target.Live]=target.Topology.PokeFaces(target.Work,regions[target.Live]);
                preview.HasChanges=true;writingModifiers=true;
                try{preview.Commit("Poke faces",()=>{
                    foreach(var target in preview.Targets)
                    {
                        var node=Document.nodes.First(n=>n.mesh==target.Live);var source=target.ModifierSourceNode();
                        var operation=new StudioModifierOperation{kind="Poke",name="Poke",faces=regions[target.Live].OrderBy(i=>i).ToArray()};
                        operation.Bind(source);AppendModifier(node,operation,StudioModifierSource.Capture(source));
                    }
                    ClearAllComponentSelections();mode=ComponentMode.Vertex;
                    foreach(int i in selection.Keys){var target=ComponentTargetAt(i);target.mode=ComponentMode.Vertex;target.selection.UnionWith(centers[Document.nodes[i].mesh]);}
                    if(Active>=0)Components=ComponentTargetAt(Active).selection;
                    RecordSelectionChange(before,false,"Poke faces");
                });}finally{writingModifiers=false;}
            }
            tool=Tool.Move;pivot=false;Rebuild();Changed();EstablishSelectionBaseline();RememberAction(RepeatKind.Poke);repaint();
        }
    }
}
