using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private bool modifiersExpanded=true,writingModifiers;
        private static readonly GUIContent ModifiersChannelHeader=new GUIContent("Modifiers",EditorGUIUtility.IconContent("MeshFilter Icon").image);
        private readonly StudioDragSensitivity.IntegerScrubber modifierIntegerScrubber=new StudioDragSensitivity.IntegerScrubber();
        private int ModifierIntegerField(string label,int value,int minimum,int maximum) => Mathf.Clamp(
            modifierIntegerScrubber.Draw(EditorGUILayout.GetControlRect(),new GUIContent(label),value,minimum,maximum,StudioSettings.Current.channelDragSensitivity),minimum,maximum);
        private readonly HashSet<string> closedModifiers=new HashSet<string>();
        private sealed class PendingModifier
        {internal Mesh Mesh;internal StudioModifierSource Source;internal string Name;internal int Group;}
        private readonly Dictionary<Mesh,PendingModifier> pendingModifiers=new Dictionary<Mesh,PendingModifier>();
        private void InitializeModifiers()
        {modifiersExpanded=StudioPreferenceStore.GetFloat("Model.ModifiersExpanded",1)>=.5f;Undo.PreparingObjects+=PrepareModifierObjects;}
        private void DisposeModifiers()
        {modifierIntegerScrubber.End();Undo.PreparingObjects-=PrepareModifierObjects;EditorApplication.update-=FinishPendingModifiers;pendingModifiers.Clear();}
        private Object[] PrepareModifierObjects(Object[] targets,string label)
        {
            if(writingModifiers||!Document||livePreview!=null||componentLiveTargets!=null)return targets;
            var meshes=targets.OfType<Mesh>().Where(m=>m&&Document.nodes.Any(n=>n.mesh==m&&!n.skinned&&n.blendShape==null)).ToArray();
            if(meshes.Length==0)return targets;
            foreach(var mesh in meshes)
            {
                if(pendingModifiers.ContainsKey(mesh))continue;
                var node=Document.nodes.First(n=>n.mesh==mesh);
                if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)
                    throw new InvalidOperationException("Repair or delete the broken modifier, or Clear History, before editing this mesh.");
                pendingModifiers.Add(mesh,new PendingModifier {Mesh=mesh,Source=StudioModifierSource.Capture(node),Name=label,Group=Undo.GetCurrentGroup()});
            }
            EditorApplication.update-=FinishPendingModifiers;EditorApplication.update+=FinishPendingModifiers;
            return targets.Concat(new Object[]{Document}).Distinct().ToArray();
        }
        private void FinishPendingModifiers()
        {
            // Installed only for an active edit. Never polls after the edit finishes.
            if(GUIUtility.hotControl!=0)return;
            EditorApplication.update-=FinishPendingModifiers;
            if(!Document){pendingModifiers.Clear();return;}
            foreach(var pending in pendingModifiers.Values.ToArray())
            {
                var node=Document.nodes.FirstOrDefault(n=>n.mesh==pending.Mesh);if(node==null||!node.mesh)continue;
                var after=StudioModifierSource.Capture(node);
                if(after.mesh==pending.Source.mesh && (after.polygons??Array.Empty<int>()).SequenceEqual(pending.Source.polygons??Array.Empty<int>()))continue;
                var before=pending.Source.Restore();
                try
                {
                    var operation=new StudioModifierOperation{kind="Direct",name=pending.Name,inputMesh=pending.Source.mesh,replacement=after};operation.Bind(before);
                    if(operation.Matches(node)&&(pending.Name.IndexOf("Transform",StringComparison.OrdinalIgnoreCase)>=0||pending.Name.IndexOf("channel",StringComparison.OrdinalIgnoreCase)>=0||pending.Name.IndexOf("Move",StringComparison.OrdinalIgnoreCase)>=0||pending.Name.IndexOf("Rotate",StringComparison.OrdinalIgnoreCase)>=0||pending.Name.IndexOf("Scale",StringComparison.OrdinalIgnoreCase)>=0))
                    {operation.kind="Transform";var positions=node.mesh.vertices;var original=before.mesh.vertices;operation.offsets=positions.Select((p,i)=>p-original[i]).ToArray();operation.inputMesh=null;operation.replacement=null;}
                    AppendModifier(node,operation,pending.Source);
                }
                finally{Object.DestroyImmediate(before.mesh);}
            }
            pendingModifiers.Clear();DirtySubdivision();UpdateSubdivision();
        }
        private void FlushModifierEdits()
        {if(pendingModifiers.Count>0)FinishPendingModifiers();}
        private void AppendModifier(StudioModelDocument.Node node,StudioModifierOperation operation,StudioModifierSource source)
        {
            var history=node.modifiers?.HasHistory==true?node.modifiers.Copy():new StudioModifierHistory{source=source};
            history.operations.Add(operation);history.displaySource=null;history.displaySettings=null;history.revision++;
            foreach(var shared in Document.nodes.Where(n=>n.mesh==node.mesh))
            {shared.modifiers=history;shared.subdivision=new StudioSubdivisionSettings();}
            EditorUtility.SetDirty(Document);
        }
        private void RecordDirectPreviewModifiers(StudioMeshToolPreview preview,string label)
        {
            foreach(var target in preview.Targets)
            {
                var node=Document.nodes.First(n=>n.mesh==target.Live);var source=target.ModifierSourceNode();var captured=StudioModifierSource.Capture(source);
                var operation=new StudioModifierOperation{kind="Direct",name=label,inputMesh=captured.mesh,replacement=StudioModifierSource.Capture(node)};
                operation.Bind(source);AppendModifier(node,operation,captured);
            }
        }
        private void RecordLiveModifiers()
        {
            foreach(var target in livePreview.Targets)
            {
                var node=Document.nodes.First(n=>n.mesh==target.Live);var source=target.ModifierSourceNode();
                var operation=new StudioModifierOperation{kind=liveTool.ToString(),name=liveTool==LiveTool.EdgeFlow?"Edit Edge Flow":liveTool.ToString(),
                    parameters=liveParameters,flow=liveFlowParameters,weld=weld,world=liveWorld,
                    faces=liveFaces?.ToArray(),edges=liveEdges?.SelectMany(e=>new[]{e.a,e.b}).ToArray(),vertices=liveSelection?.ToArray()};
                if(liveTool==LiveTool.Soften)
                {int index=Array.IndexOf(livePreview.Targets,target);operation.edges=liveNormalEdges[index].SelectMany(e=>new[]{e.Item1,e.Item2}).ToArray();}
                operation.Bind(source);AppendModifier(node,operation,StudioModifierSource.Capture(source));
            }
        }
        private void RefreshModifierResult()
        {
            edgeMesh=null;cachedEdges=null;faceNode=null;cachedFaces=null;cachedFaceTriangles=null;cutTopology=null;cutMesh=null;
            ClearComponentDrawing();Components.Clear();Changed();DirtySubdivision();UpdateSubdivision();
        }
        private void ChangeModifier(StudioModelDocument.Node node,string id,Action<StudioModifierOperation> edit,bool remove=false)
        {
            FlushModifierEdits();if(node.modifiers==null)return;
            var history=node.modifiers.Copy();var operation=history.operations.FirstOrDefault(o=>o.id==id);if(operation==null)return;
            if(remove)history.operations.Remove(operation);else edit(operation);
            var work=StudioModifierEvaluator.Evaluate(history);
            writingModifiers=true;
            try
            {
                Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();string label=remove?"Delete modifier":"Edit modifier";
                Undo.RegisterCompleteObjectUndo(new Object[]{Document,node.mesh},label);ClearSubdivision();
                foreach(var shared in Document.nodes.Where(n=>n.mesh==node.mesh))
                {StudioModifierEvaluator.Publish(work,shared);shared.modifiers=history;shared.subdivision=history.displaySettings?.Copy()??new StudioSubdivisionSettings();}
                EditorUtility.SetDirty(node.mesh);Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();RefreshModifierResult();
                var failed=history.operations.FirstOrDefault(o=>!string.IsNullOrEmpty(o.failure));
                if(failed!=null)error=failed.name+": "+failed.failure;
            }
            finally{writingModifiers=false;Object.DestroyImmediate(work.mesh);}
        }
        private void AddSubdivisionModifier()
        {
            FlushModifierEdits();var nodes=SubdivisionSelection().GroupBy(n=>n.mesh).Select(g=>g.First()).ToArray();
            if(nodes.Length==0)throw new InvalidOperationException("Select a static mesh to add Subdivision.");
            var prepared=new List<(StudioModelDocument.Node node,StudioModifierHistory history,StudioModelDocument.Node work)>();
            try
            {
                foreach(var node in nodes)
                {
                    if(node.skinned||node.blendShape!=null||node.mesh.blendShapeCount>0)throw new InvalidOperationException("Modifiers require a static mesh without blend shapes.");
                    if(node.modifiers?.operations.Any(o=>!string.IsNullOrEmpty(o.failure))==true)throw new InvalidOperationException("Repair the broken modifier or Clear History first.");
                    var history=node.modifiers?.HasHistory==true?node.modifiers.Copy():new StudioModifierHistory{source=StudioModifierSource.Capture(node)};
                    var operation=new StudioModifierOperation{kind="Subdivision",name="Subdivision",subdivision=new StudioSubdivisionSettings{preview=true}};operation.Bind(node);history.operations.Add(operation);
                    var work=StudioModifierEvaluator.Evaluate(history);
                    if(history.operations.Any(o=>!string.IsNullOrEmpty(o.failure))){Object.DestroyImmediate(work.mesh);throw new InvalidOperationException(history.operations.First(o=>!string.IsNullOrEmpty(o.failure)).failure);}
                    prepared.Add((node,history,work));
                }
                writingModifiers=true;Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(new Object[]{Document}.Concat(nodes.Select(n=>(Object)n.mesh)).ToArray(),"Add Subdivision");ClearSubdivision();
                foreach(var item in prepared)
                {
                    foreach(var node in Document.nodes.Where(n=>n.mesh==item.node.mesh))
                    {StudioModifierEvaluator.Publish(item.work,node);node.modifiers=item.history;node.subdivision=item.history.displaySettings.Copy();}
                    EditorUtility.SetDirty(item.node.mesh);
                }
                Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();RefreshModifierResult();
            }
            finally{writingModifiers=false;foreach(var item in prepared)Object.DestroyImmediate(item.work.mesh);}
        }
        private void ClearModifierHistory()
        {
            FlushModifierEdits();var selected=SubdivisionSelection().ToArray();if(selected.Length==0)throw new InvalidOperationException("Select a mesh to Clear History.");
            var meshes=new HashSet<Mesh>(selected.Select(n=>n.mesh));writingModifiers=true;
            try
            {
                Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Clear History");ClearSubdivision();
                foreach(var node in Document.nodes.Where(n=>meshes.Contains(n.mesh))){node.modifiers=null;node.subdivision=new StudioSubdivisionSettings();}
                EditorUtility.SetDirty(Document);Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();Changed(false);
            }
            finally{writingModifiers=false;}
        }
        private void DrawModifierChannels()
        {
            if(!Document||Active<0||!ActiveMesh)return;var node=Document.nodes[Active];var history=node.modifiers;
            GUILayout.Space(4);var rect=EditorGUILayout.GetControlRect(false,20);
            EditorGUI.DrawRect(new Rect(rect.x-4,rect.y,rect.width+8,rect.height),Color.Lerp(StudioTheme.Current.Panel,StudioTheme.Current.Button,.5f));
            bool expanded=EditorGUI.Foldout(rect,modifiersExpanded,ModifiersChannelHeader,true,StudioTheme.Styles.transformHeader);
            if(expanded!=modifiersExpanded){modifiersExpanded=expanded;StudioPreferenceStore.SetFloat("Model.ModifiersExpanded",expanded?1:0);repaint();}
            if(!expanded)return;
            using(new EditorGUILayout.VerticalScope(StudioTheme.Styles.channelTransformInset))
            {
                using(new EditorGUI.DisabledScope(history==null||history.operations.Count==0))
                    if(GUILayout.Button("Clear History")){Run(ClearModifierHistory);return;}
                if(history==null||history.operations.Count==0)return;
                using var sensitivity=new StudioDragSensitivity.DragScope(StudioSettings.Current.channelDragSensitivity);
                foreach(var operation in history.operations.AsEnumerable().Reverse().ToArray())
                {
                    bool hasFields=operation.kind is "Bevel" or "Extrude" or "Bridge" or "Soften" or "EdgeFlow" or "Subdivision";
                    bool open=hasFields&&!closedModifiers.Contains(operation.id);
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        if(hasFields)
                        {
                            bool next=EditorGUILayout.Foldout(open,operation.name,true);if(next)closedModifiers.Remove(operation.id);else closedModifiers.Add(operation.id);
                        }
                        else
                        {
                            GUILayout.Space(12);
                            EditorGUILayout.LabelField(operation.name);
                        }
                        if(GUILayout.Button(new GUIContent("×","Delete this operation"),GUILayout.Width(22))) {Run(()=>ChangeModifier(node,operation.id,null,true));return;}
                    }
                    if(!string.IsNullOrEmpty(operation.failure))EditorGUILayout.HelpBox(operation.failure,MessageType.Warning);
                    if(!open)continue;
                    EditorGUI.BeginChangeCheck();var p=operation.parameters;var flow=operation.flow;
                    int level=operation.subdivision?.level??1,boundary=operation.subdivision?.boundary??2;
                    switch(operation.kind)
                    {
                        case "Bevel":
                            p.x=Mathf.Max(0,EditorGUILayout.FloatField("Width",p.x));p.y=ModifierIntegerField("Segments",Mathf.RoundToInt(p.y),0,32);
                            p.z=Mathf.Clamp(EditorGUILayout.FloatField("Profile",p.z),-1,1);p.w=EditorGUILayout.Popup("Junction",Mathf.RoundToInt(p.w),new[]{"Grid","Cap"});
                            flow.x=EditorGUILayout.Toggle("Edge Flow",flow.x>=.5f)?1:0;
                            if(flow.x>=.5f){flow.y=EditorGUILayout.FloatField("Flow influence",flow.y);flow.z=ModifierIntegerField("Flow iterations",Mathf.RoundToInt(flow.z),1,64);}break;
                        case "Extrude":p.x=EditorGUILayout.FloatField("Distance",p.x);p.z=EditorGUILayout.FloatField("Inset",p.z);p.y=EditorGUILayout.Toggle("Together",p.y>=.5f)?1:0;break;
                        case "Bridge":p.x=ModifierIntegerField("Divisions",Mathf.RoundToInt(p.x),0,64);p.y=Mathf.Clamp(EditorGUILayout.FloatField("Curvature",p.y),-1,1);break;
                        case "Soften":p.x=Mathf.Clamp(EditorGUILayout.FloatField("Angle",p.x),0,180);break;
                        case "EdgeFlow":p.x=EditorGUILayout.FloatField("Influence",p.x);p.y=ModifierIntegerField("Iterations",Mathf.RoundToInt(p.y),1,64);break;
                        case "Subdivision":level=ModifierIntegerField("Level",level,0,6);boundary=EditorGUILayout.Popup("Boundary",boundary-1,new[]{"Smooth corners","Keep corners"})+1;break;
                    }
                    if(EditorGUI.EndChangeCheck())
                    {Run(()=>ChangeModifier(node,operation.id,o=>{o.parameters=p;o.flow=flow;if(o.subdivision!=null){o.subdivision.level=level;o.subdivision.boundary=boundary;}}));return;}
                    GUILayout.Space(3);
                }
            }
        }
    }
}
