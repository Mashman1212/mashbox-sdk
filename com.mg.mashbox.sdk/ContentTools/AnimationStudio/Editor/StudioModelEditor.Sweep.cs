using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo=MashBoxSDK.AnimationStudio.StudioUndo;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane sweepPane;
        private bool sweepAlignmentExpanded=true;
        private StudioSweepSettings sweepParameters;
        private StudioModelDocument.Node sweepTarget;
        private StudioModifierHistory sweepBefore;
        private bool sweepCreating, sweepPending, evaluatingSweeps, sweepPreviewValid, sweepPickingProfile;
        private readonly Dictionary<string,string> sweepRevisions=new Dictionary<string,string>();
        private readonly Dictionary<StudioSweepSettings,StudioSweep.TopologyCache> sweepTopologies=new Dictionary<StudioSweepSettings,StudioSweep.TopologyCache>();
        
        private void CaptureSweepProfile(StudioSweepSettings settings)
        {
            if(!ActiveMesh||Active<0||(mode!=ComponentMode.Edge&&mode!=ComponentMode.Face)||Components.Count==0)
                throw new InvalidOperationException("Select an edge chain, edge loop, or connected face region on one static mesh.");
            var node=Document.nodes[Active];
            if(node==sweepTarget||node.skinned||ActiveMesh.blendShapeCount>0)
                throw new InvalidOperationException("Choose a separate static mesh for the sweep profile.");
            var geometry=StudioSubdivisionData.Geometry(node);var cuts=new StudioMeshCuts(node);
            var edges=new Dictionary<(int,int),int>();
            void Add(int a,int b){a=geometry[a];b=geometry[b];if(a==b)return;var key=(Math.Min(a,b),Math.Max(a,b));edges.TryGetValue(key,out int count);edges[key]=count+1;}
            if(mode==ComponentMode.Edge)
            {var selected=Edges();foreach(int i in Components){if(i<0||i>=selected.Count)throw new InvalidOperationException("Profile edge selection is invalid.");Add(selected[i].a,selected[i].b);}}
            else foreach(int i in Components)
            {if(i<0||i>=cuts.Faces.Count)throw new InvalidOperationException("Profile face selection is invalid.");var face=cuts.Faces[i].Vertices;for(int j=0;j<face.Count;j++)Add(face[j],face[(j+1)%face.Count]);}
            if(edges.Values.Any(v=>v>2))throw new InvalidOperationException("Profile has non-manifold edges.");
            var boundary=edges.Where(e=>mode==ComponentMode.Edge||e.Value==1).Select(e=>e.Key).ToList();
            var adjacency=new Dictionary<int,List<int>>();
            foreach(var e in boundary){if(!adjacency.ContainsKey(e.Item1))adjacency[e.Item1]=new List<int>();if(!adjacency.ContainsKey(e.Item2))adjacency[e.Item2]=new List<int>();adjacency[e.Item1].Add(e.Item2);adjacency[e.Item2].Add(e.Item1);}
            if(adjacency.Count<2||adjacency.Values.Any(v=>v.Count>2))throw new InvalidOperationException("Profile must be one unbranched chain or loop.");
            var ends=adjacency.Where(p=>p.Value.Count==1).Select(p=>p.Key).OrderBy(i=>i).ToArray();
            bool closed=ends.Length==0;if(!closed&&ends.Length!=2||mode==ComponentMode.Face&&!closed)throw new InvalidOperationException("Face profiles require one closed perimeter.");
            int first=closed?adjacency.Keys.Min():ends[0],previous=-1,current=first;var ordered=new List<int>();
            do{ordered.Add(current);int next=adjacency[current].Where(i=>i!=previous).DefaultIfEmpty(-1).First();previous=current;current=next;}while(current>=0&&current!=first&&ordered.Count<=adjacency.Count);
            if(ordered.Count!=adjacency.Count||ordered.Distinct().Count()!=ordered.Count)throw new InvalidOperationException("Choose one connected profile without holes or separate islands.");
            var positions=ActiveMesh.vertices;var lookup=geometry.Select((id,i)=>(id,i)).GroupBy(p=>p.id).ToDictionary(g=>g.Key,g=>positions[g.First().i]);
            var x=(lookup[ordered[1]]-lookup[first]).normalized;var normal=Vector3.zero;
            for(int i=2;i<ordered.Count;i++)normal+=Vector3.Cross(lookup[ordered[i-1]]-lookup[first],lookup[ordered[i]]-lookup[first]);
            if(normal.sqrMagnitude<1e-12f){normal=Vector3.Cross(x,Mathf.Abs(x.y)<.9f?Vector3.up:Vector3.forward);}
            var y=Vector3.Cross(normal.normalized,x).normalized;
            float extent=ordered.Max(i=>(lookup[i]-lookup[first]).magnitude);
            if(ordered.Any(i=>Mathf.Abs(Vector3.Dot(lookup[i]-lookup[first],normal.normalized))>Mathf.Max(1e-5f,extent*1e-4f)))throw new InvalidOperationException("Sweep profile must be planar.");
            settings.profileId=node.constraintId;settings.profileAncestorId=null;settings.profileFrame=settings.profileAncestorBind=settings.profileAncestorLast=Matrix4x4.identity;settings.profileShape=3;settings.profileClosed=closed;
            settings.profileVertices=ordered.ToArray();settings.profileTopology=SweepProfileTopology(node);settings.profileX=x;settings.profileY=y;
            var matrix=Transforms[Active].localToWorldMatrix;
            // The first perimeter edge is not an orientation reference: it can run down a wall.
            var worldNormal=matrix.inverse.transpose.MultiplyVector(normal).normalized;
            int dominant=Mathf.Abs(worldNormal.x)>Mathf.Abs(worldNormal.y)?0:1;
            if(Mathf.Abs(worldNormal.z)>Mathf.Abs(worldNormal[dominant]))dominant=2;
            if(worldNormal[dominant]<0)worldNormal=-worldNormal;
            var up=Vector3.ProjectOnPlane(Vector3.up,worldNormal);
            if(up.sqrMagnitude<1e-6f)up=Vector3.ProjectOnPlane(Vector3.forward,worldNormal);
            settings.profileWorldY=up.normalized;
            settings.profileWorldX=Vector3.Cross(settings.profileWorldY,worldNormal).normalized;
        }
        private static int[] SweepProfileTopology(StudioModelDocument.Node node)
        {
            var ids=StudioSubdivisionData.Geometry(node);
            return new StudioMeshCuts(node).Faces.SelectMany(f=>new[]{f.Vertices.Count}.Concat(f.Vertices.Select(v=>ids[v]))).ToArray();
        }
        private Vector3[] ResolveSweepProfile(StudioSweepSettings settings)
        {
            if(settings.profileShape!=3)return null;
            int index=Document.nodes.FindIndex(n=>n.constraintId==settings.profileId);
            if(index<0||!Document.nodes[index].mesh)throw new InvalidOperationException("Sweep profile source is missing. Restore it or assign a new edge/face selection.");
            var node=Document.nodes[index];
            if(settings.profileTopology==null||!settings.profileTopology.SequenceEqual(SweepProfileTopology(node)))throw new InvalidOperationException("Profile topology changed. Reassign the intended edges or faces; the sweep will not silently bind different components.");
            var ids=StudioSubdivisionData.Geometry(node);var vertices=node.mesh.vertices;
            var matrix=settings.profileFrame*settings.profileAncestorBind*settings.profileAncestorLast.inverse*Transforms[index].localToWorldMatrix;
            var positions=ids.Select((id,i)=>(id,i)).GroupBy(p=>p.id).ToDictionary(g=>g.Key,g=>matrix.MultiplyPoint3x4(vertices[g.First().i]));
            if(settings.profileVertices==null||settings.profileVertices.Any(i=>!positions.ContainsKey(i)))throw new InvalidOperationException("Sweep profile components are missing.");
            var points=settings.profileVertices.Select(i=>positions[i]).ToArray();var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
            if(settings.profileWorldX.sqrMagnitude<.5f||settings.profileWorldY.sqrMagnitude<.5f)
            {
                settings.profileWorldX=matrix.MultiplyVector(settings.profileX).normalized;
                var initialY=matrix.MultiplyVector(settings.profileY);settings.profileWorldY=(initialY-settings.profileWorldX*Vector3.Dot(initialY,settings.profileWorldX)).normalized;
            }
            var x=settings.profileWorldX;var y=settings.profileWorldY;var normal=Vector3.Cross(x,y).normalized;
            if(normal.sqrMagnitude<.5f||points.Any(p=>!StudioSpline.Finite(p)))throw new InvalidOperationException("Live profile must remain finite and non-collapsed.");
            return points.Select(p=>new Vector3(Vector3.Dot(p-center,x),Vector3.Dot(p-center,y),Vector3.Dot(p-center,normal))).ToArray();
        }
        private string SweepProfileRevision(StudioSweepSettings settings)
        {
            if(settings.profileShape!=3)return "";
            int index=Document.nodes.FindIndex(n=>n.constraintId==settings.profileId);
            if(index<0||!Document.nodes[index].mesh)return "missing-profile";
            var node=Document.nodes[index];
            // Called only by existing scene-change events, never an idle poll.
            return JsonUtility.ToJson(new SweepProfileStamp{positions=node.mesh.vertices,topology=SweepProfileTopology(node),world=Transforms[index].localToWorldMatrix});
        }
        [Serializable] private sealed class SweepProfileStamp{public Vector3[] positions;public int[] topology;public Matrix4x4 world;}
        private StudioModelDocument.Node SelectedSweep => Active>=0&&Active<Document.nodes.Count&&Document.nodes[Active].modifiers?.operations.Any(o=>o.kind=="Sweep")==true?Document.nodes[Active]:null;
        private string sweepProfileDraftOwnerId;
        private void CaptureSweepSource(StudioSweepSettings settings,StudioModelDocument.Node owner,int source)
        {
            if(source<0||source>=Document.nodes.Count||Document.nodes[source]==owner||!Document.nodes[source].mesh)
                throw new InvalidOperationException("Choose a separate static mesh for Profile Source.");
            using(new ComponentTargetScope(this,source))
            {
                var previousMode=mode;var previousSelection=Components;
                try
                {
                    if((mode!=ComponentMode.Edge&&mode!=ComponentMode.Face)||Components.Count==0)
                    {mode=ComponentMode.Face;Components=new HashSet<int>(Enumerable.Range(0,new StudioMeshCuts(Document.nodes[source]).Faces.Count));}
                    CaptureSweepProfile(settings);
                }
                finally{mode=previousMode;Components=previousSelection;}
            }
        }
        private bool TrySweepProfileDrop(Rect rect,StudioSweepSettings settings,StudioModelDocument.Node owner)
        {
            var evt=Event.current;
            if((evt.type!=EventType.DragUpdated&&evt.type!=EventType.DragPerform)||!rect.Contains(evt.mousePosition))return false;
            if(!(DragAndDrop.GetGenericData(HierarchyDragKey) is HierarchyDragPayload payload))return false;
            bool valid=payload.owner==this&&payload.document==Document&&payload.nodes.Length==1&&payload.nodes[0]!=owner&&payload.nodes[0].mesh&&!payload.nodes[0].skinned;
            DragAndDrop.visualMode=valid?DragAndDropVisualMode.Link:DragAndDropVisualMode.Rejected;
            if(valid&&evt.type==EventType.DragPerform)
            {
                Run(()=>{CaptureSweepSource(settings,owner,Document.nodes.IndexOf(payload.nodes[0]));GUI.changed=true;},false);
                DragAndDrop.AcceptDrag();CancelHierarchyDrag();
            }
            evt.Use();return true;
        }
        private void DrawSweepProfileSource(StudioSweepSettings settings,StudioModelDocument.Node owner)
        {
            var sources=Document.nodes.Where(n=>n!=owner&&n.mesh&&!n.skinned).ToArray();
            int selected=Array.FindIndex(sources,n=>n.constraintId==settings.profileId)+1;
            var rect=EditorGUILayout.GetControlRect();
            if(!TrySweepProfileDrop(rect,settings,owner))
            {
                int next=EditorGUI.Popup(rect,"Profile Source",selected,new[]{"Drag hierarchy mesh here / choose"}.Concat(sources.Select(n=>n.name)).ToArray());
                if(next>0&&next!=selected)Run(()=>CaptureSweepSource(settings,owner,Document.nodes.IndexOf(sources[next-1])),false);
            }
            if(settings.profileShape==3)EditorGUILayout.HelpBox("Drag a mesh from the hierarchy or choose it above. Uses its retained edge/face selection, otherwise the whole face perimeter. Source edits remain live.",MessageType.Info);
        }
        private void BindSweepAncestor(StudioModelDocument.Node output,StudioSweepSettings settings,int source)
        {
            var ancestors=new HashSet<int>();
            for(int i=source;i>=0;i=Document.nodes[i].parent)ancestors.Add(i);
            int common=-1;for(int i=output.parent;i>=0;i=Document.nodes[i].parent)if(ancestors.Contains(i)){common=i;break;}
            string id=common<0?null:Document.nodes[common].constraintId;
            var matrix=common<0?Matrix4x4.identity:Transforms[common].localToWorldMatrix;
            if((settings.ancestorId??"")!=(id??""))
            {
                settings.sourceFrame=settings.sourceFrame*settings.ancestorBind*settings.ancestorLast.inverse;
                settings.ancestorId=id;settings.ancestorBind=matrix;
            }
            settings.ancestorLast=matrix;
        }
        private void BindSweepProfileAncestor(StudioModelDocument.Node output,StudioSweepSettings settings,int source)
        {
            var ancestors=new HashSet<int>();
            for(int i=source;i>=0;i=Document.nodes[i].parent)ancestors.Add(i);
            int common=-1;for(int i=output.parent;i>=0;i=Document.nodes[i].parent)if(ancestors.Contains(i)){common=i;break;}
            string id=common<0?null:Document.nodes[common].constraintId;
            var matrix=common<0?Matrix4x4.identity:Transforms[common].localToWorldMatrix;
            if((settings.profileAncestorId??"")!=(id??""))
            {
                settings.profileFrame=settings.profileFrame*settings.profileAncestorBind*settings.profileAncestorLast.inverse;
                settings.profileAncestorId=id;settings.profileAncestorBind=matrix;
            }
            settings.profileAncestorLast=matrix;
        }
        private StudioModelDocument.Node GenerateSweep(StudioSweepSettings settings)
        {
            int index=Document.nodes.FindIndex(n=>n.constraintId==settings.sourceId);
            if(index<0||Document.nodes[index].spline==null)
                throw new InvalidOperationException("Sweep source is missing or is not a spline. Restore the source with Undo or repair its link in the Sweep modifier.");
            if(index>=Transforms.Length||!Transforms[index])throw new InvalidOperationException("Sweep source transform is unavailable.");
            if(!sweepTopologies.TryGetValue(settings,out var topology))sweepTopologies[settings]=topology=new StudioSweep.TopologyCache();
            return StudioSweep.Generate(Document.nodes[index].spline,settings.sourceFrame*settings.ancestorBind*settings.ancestorLast.inverse*Transforms[index].localToWorldMatrix,settings,topology,ResolveSweepProfile(settings));
        }
        private IEnumerable<StudioModelDocument.Node> SweepEvaluationOrder()
        {
            var result=new List<StudioModelDocument.Node>();var done=new HashSet<int>();var visiting=new HashSet<int>();
            void Visit(int index)
            {
                if(index<0||done.Contains(index))return;
                if(!visiting.Add(index))throw new InvalidOperationException("Sweep dependency cycle. Repair the source links.");
                var node=Document.nodes[index];Visit(node.parent);
                foreach(var op in node.modifiers?.operations??new List<StudioModifierOperation>())if(op.sweep!=null)
                {Visit(Document.nodes.FindIndex(n=>n.constraintId==op.sweep.sourceId));if(op.sweep.profileShape==3)Visit(Document.nodes.FindIndex(n=>n.constraintId==op.sweep.profileId));}
                visiting.Remove(index);done.Add(index);result.Add(node);
            }
            for(int i=0;i<Document.nodes.Count;i++)Visit(i);return result;
        }
        private void UpdateSweeps(bool force=false)
        {
            if(evaluatingSweeps||!Document)return;
            evaluatingSweeps=true;
            try
            {
                foreach(var node in SweepEvaluationOrder())
                {
                    var generator=node.modifiers?.operations?.FirstOrDefault(o=>o.kind=="Sweep");if(generator?.sweep==null||!node.mesh)continue;
                    var settings=generator.sweep;int source=Document.nodes.FindIndex(n=>n.constraintId==settings.sourceId);
                    if(source>=0)BindSweepAncestor(node,settings,source);
                    int profile=Document.nodes.FindIndex(n=>n.constraintId==settings.profileId);if(settings.profileShape==3&&profile>=0)BindSweepProfileAncestor(node,settings,profile);
                    string revision=JsonUtility.ToJson(settings)+":"+(source<0?"missing":JsonUtility.ToJson(Document.nodes[source].spline)+":"+(source<Transforms.Length&&Transforms[source]?JsonUtility.ToJson(Transforms[source].localToWorldMatrix):"missing"));
                    revision+=SweepProfileRevision(settings);
                    if(!force&&sweepRevisions.TryGetValue(node.constraintId,out var old)&&revision==old)continue;
                    sweepRevisions[node.constraintId]=revision;
                    var result=StudioModifierEvaluator.Evaluate(node.modifiers,GenerateSweep);
                    try
                    {
                        bool topology=node.mesh.vertexCount!=result.mesh.vertexCount||!(node.cutPolygons??Array.Empty<int>()).SequenceEqual(result.cutPolygons??Array.Empty<int>());
                        if(topology)StudioModifierEvaluator.Publish(result,node);
                        else {node.mesh.vertices=result.mesh.vertices;node.mesh.normals=result.mesh.normals;node.mesh.tangents=result.mesh.tangents;node.mesh.uv=result.mesh.uv;node.mesh.bounds=result.mesh.bounds;}
                        node.subdivision=node.modifiers.displaySettings?.Copy()??new StudioSubdivisionSettings();
                        if(topology){edgeMesh=null;cachedEdges=null;faceNode=null;cachedFaces=null;cachedFaceTriangles=null;cutTopology=null;cutMesh=null;if(Active>=0&&Document.nodes[Active]==node)Components.Clear();}
                        if(selectionWireframes.TryGetValue(node,out var wire))
                        {
                            if(topology||wire.baked){if(wire.baked)Object.DestroyImmediate(wire.baked);ReleaseWireBatch(wire);selectionWireframes.Remove(node);}
                            else{node.mesh.GetVertices(wire.vertices);wire.batchPositionsDirty=true;wire.linesDirty=true;wire.vertexUpdateStart=0;if(wire.quadOverlay!=null)wire.quadOverlay.dirty=true;}
                        }
                        if(componentTargets.ContainsKey(node)||Active>=0&&Document.nodes[Active]==node)
                            using(new ComponentTargetScope(this,Document.nodes.IndexOf(node)))
                            {if(topology){Components.Clear();ClearComponentDrawing();}else{RefreshComponentPositions();RefreshIsolatedPositions(null);}}
                        InvalidatePositionStatistics(node.mesh,false);DirtySubdivision(node.mesh);EditorUtility.SetDirty(node.mesh);
                        var failure=node.modifiers.operations.FirstOrDefault(o=>!string.IsNullOrEmpty(o.failure));
                        if(failure!=null)error=node.name+": "+failure.failure;
                    }
                    finally{Object.DestroyImmediate(result.mesh);}
                }
            }
            finally{evaluatingSweeps=false;}
            var retained=new HashSet<StudioSweepSettings>(Document.nodes.SelectMany(n=>n.modifiers?.operations??new List<StudioModifierOperation>()).Where(o=>o.kind=="Sweep").Select(o=>o.sweep));
            foreach(var key in sweepTopologies.Keys.Where(k=>!retained.Contains(k)).ToArray())sweepTopologies.Remove(key);
        }
        private void OpenSweepTool(StudioModelDocument.Node edit=null,StudioSweepSettings repeat=null)
        {
            FinishToolsForSwitch();
            if(edit==null&&SelectedSweep!=null)edit=SelectedSweep;
            if(edit!=null){SelectObject(Document.nodes.IndexOf(edit));modifiersExpanded=true;repaint();return;}
            StudioSweepSettings captured=null;
            if(SelectedSpline==null){captured=new StudioSweepSettings();CaptureSweepProfile(captured);if(!Document.nodes.Any(n=>n.spline!=null))throw new InvalidOperationException("Create a spline before sweeping a profile.");}
            sweepParameters=captured??repeat?.Copy()??new StudioSweepSettings();
            sweepParameters.sourceId=(SelectedSpline!=null?Document.nodes[Active]:Document.nodes.First(n=>n.spline!=null)).constraintId;
            sweepParameters.worldToCreation=Matrix4x4.identity;sweepParameters.sourceFrame=sweepParameters.ancestorBind=sweepParameters.ancestorLast=Matrix4x4.identity;sweepParameters.ancestorId=null;
            sweepTarget=null;sweepCreating=true;sweepBefore=null;sweepPending=false;
            try{PreviewSweep();ApplySweep();modifiersExpanded=true;}
            finally{CancelSweep();}
            repaint();
        }
        private void DrawSweepModifier(StudioModelDocument.Node node,StudioModifierOperation operation)
        {
            var settings=operation.sweep.Copy();
            if(sweepProfileDraftOwnerId==node.constraintId)settings.profileShape=3;
            EditorGUI.BeginChangeCheck();DrawSweepFields(settings,node);
            if(EditorGUI.EndChangeCheck())
            {
                if(settings.profileShape==3&&string.IsNullOrEmpty(settings.profileId))sweepProfileDraftOwnerId=node.constraintId;
                else {sweepProfileDraftOwnerId=null;Run(()=>ChangeModifier(node,operation.id,o=>o.sweep=settings),false);}
            }
        }
        private void DrawSweepFields(StudioSweepSettings settings,StudioModelDocument.Node owner)
        {
            settings.profileShape=EditorGUILayout.Popup("Profile",settings.profileShape,new[]{"Tube","Ribbon","Square","Live Mesh Selection"});
            if(settings.profileShape==0){settings.sides=ModifierIntegerField("Profile Sides",settings.sides,4,256);settings.radius=StudioSceneUnits.Field("Radius",settings.radius);}
            if(settings.profileShape==1||settings.profileShape==2)settings.profileWidth=StudioSceneUnits.Field("Width",settings.profileWidth);
            if(settings.profileShape==2)settings.profileHeight=StudioSceneUnits.Field("Height",settings.profileHeight);
            DrawSweepProfileSource(settings,owner);
            bool changedBeforeFoldout=GUI.changed;
            sweepAlignmentExpanded=EditorGUILayout.Foldout(sweepAlignmentExpanded,"Alignment",true);
            GUI.changed=changedBeforeFoldout;
            if(sweepAlignmentExpanded)
            {
                EditorGUI.indentLevel++;
                settings.align=EditorGUILayout.Toggle("Align",settings.align);
                using(new EditorGUI.DisabledScope(!settings.align))
                {
                    settings.horizontalAlignment=EditorGUILayout.Popup("Horizontal",settings.horizontalAlignment,new[]{"Center","Left","Right"});
                    settings.verticalAlignment=EditorGUILayout.Popup("Vertical",settings.verticalAlignment,new[]{"Center","Top","Bottom"});
                    settings.horizontalOffset=StudioSceneUnits.Field("Horizontal Offset",settings.horizontalOffset);
                    settings.verticalOffset=StudioSceneUnits.Field("Vertical Offset",settings.verticalOffset);
                }
                EditorGUI.indentLevel--;
            }
            settings.subdivisions=ModifierIntegerField("Length Subdivisions",settings.subdivisions,1,4096);
            settings.rotation=EditorGUILayout.FloatField("Profile Rotation",settings.rotation);
            settings.twist=EditorGUILayout.FloatField("Total Twist",settings.twist);
            settings.taper=EditorGUILayout.CurveField("Taper",settings.taper,Color.green,new Rect(0,.000001f,1,10));
            var source=Document.nodes.FirstOrDefault(n=>n.constraintId==settings.sourceId);
            bool caps=source?.spline?.closed!=true&&settings.profileShape!=1&&(settings.profileShape!=3||settings.profileClosed);
            using(new EditorGUI.DisabledScope(!caps)){settings.capStart=EditorGUILayout.Toggle("Cap Start",settings.capStart);settings.capEnd=EditorGUILayout.Toggle("Cap End",settings.capEnd);}
            var sources=Document.nodes.Where(n=>n.spline!=null).ToArray();
            int selected=Array.FindIndex(sources,n=>n.constraintId==settings.sourceId)+1;
            int next=EditorGUILayout.Popup("Source Spline",selected,new[]{"Missing / choose source"}.Concat(sources.Select(n=>n.name)).ToArray());
            if(next>0&&next!=selected)settings.sourceId=sources[next-1].constraintId;
            settings.ClampFields();
            if(source?.spline?.closed==true){settings.subdivisions=Math.Max(3,settings.subdivisions);settings.twist=Mathf.Round(settings.twist/360)*360;
                var keys=settings.taper.keys.ToList();float start=settings.taper.Evaluate(0);int end=keys.FindIndex(k=>k.time==1);var key=end>=0?keys[end]:new Keyframe(1,start);key.value=start;if(end>=0)keys[end]=key;else keys.Add(key);settings.taper.keys=keys.ToArray();}
        }
        private void PreviewSweep()
        {
            sweepPreviewValid=false;
            try
            {
                if(sweepTarget==null)
                {
                    var generated=GenerateSweep(sweepParameters);
                    generated.name=ObjectNames.GetUniqueName(Document.nodes.Select(n=>n.name).ToArray(),"Sweep Mesh");
                    generated.materials=new[]{StudioMeshMaterialPreview.DefaultMaterial};
                    generated.modifiers=new StudioModifierHistory();
                    generated.modifiers.operations.Add(new StudioModifierOperation{kind="Sweep",name="Sweep Mesh",sweep=sweepParameters.Copy()});
                    sweepTarget=generated;Document.nodes.Add(generated);
                    try{StudioSweepDependencies.Validate(Document);}catch{Document.nodes.Remove(generated);Object.DestroyImmediate(generated.mesh);sweepTarget=null;throw;}
                    sweepPending=true;Rebuild();
                }
                else
                {
                    var op=sweepTarget.modifiers.operations.First(o=>o.kind=="Sweep");var before=op.sweep;
                    op.sweep=sweepParameters.Copy();
                    try{StudioSweepDependencies.Validate(Document);}catch{op.sweep=before;throw;}
                    sweepPending=true;UpdateSweeps();
                }
                Changed();sweepPreviewValid=true;
            }
            catch(Exception ex){error=ex.Message;}
        }
        private void ApplySweep()
        {
            if(!sweepPreviewValid)throw new InvalidOperationException(error??"Repair the sweep preview before applying.");
            if(!sweepPending)return;
            if(sweepTarget==null)throw new InvalidOperationException("Sweep has no valid preview.");
            var failure=sweepTarget.modifiers.operations.FirstOrDefault(o=>!string.IsNullOrEmpty(o.failure));
            if(failure!=null)throw new InvalidOperationException(failure.failure);
            var final=sweepTarget.modifiers.Copy();
            writingModifiers=true;Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            try
            {
                if(sweepCreating)Document.nodes.Remove(sweepTarget);else sweepTarget.modifiers=sweepBefore;
                Undo.RegisterCompleteObjectUndo(Document,sweepCreating?"Create Sweep Mesh":"Edit Sweep Mesh");
                if(sweepCreating){Document.nodes.Add(sweepTarget);Document.OwnSweepMesh(sweepTarget.mesh);}
                sweepTarget.modifiers=final;sweepBefore=final.Copy();sweepCreating=false;sweepPending=false;
                if(repeatingAction==null)lastRepeatAction=new RepeatAction{kind=RepeatKind.Sweep,sweep=sweepParameters.Copy()};
                EditorUtility.SetDirty(Document);Rebuild();SelectObject(Document.nodes.IndexOf(sweepTarget));Changed();
            }
            finally{Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();writingModifiers=false;}
        }
        private void CancelSweep()
        {
            if(sweepPending&&sweepTarget!=null&&Document)
            {
                if(sweepCreating){Document.nodes.Remove(sweepTarget);Object.DestroyImmediate(sweepTarget.mesh);}
                else sweepTarget.modifiers=sweepBefore;
                sweepPending=false;Rebuild();Changed();
            }
            sweepTarget=null;sweepBefore=null;sweepCreating=false;sweepPickingProfile=false;
        }
        private void CloseSweepTool(){if(sweepPane)sweepPane.Close();sweepPane=null;}
    }
    public sealed partial class StudioModelDocument
    {
        [NonSerialized] internal bool writingSweepFrame;
        internal void OwnSweepMesh(Mesh mesh)=>AttachOwnedMesh(mesh);
    }
    internal static class StudioSweepDependencies
    {
        internal static void Validate(StudioModelDocument document,IReadOnlyDictionary<int,int> parents=null)
        {
            var ids=document.nodes.Select((n,i)=>(n.constraintId,i)).Where(p=>!string.IsNullOrEmpty(p.constraintId)).ToDictionary(p=>p.constraintId,p=>p.i);
            var visiting=new HashSet<int>();var visited=new HashSet<int>();
            void Visit(int index)
            {
                if(visited.Contains(index))return;
                if(!visiting.Add(index))throw new InvalidOperationException("Sweep source links cannot form a dependency cycle through hierarchy or constraints.");
                var node=document.nodes[index];int parent=parents!=null&&parents.TryGetValue(index,out var changed)?changed:node.parent;
                if(parent>=0)Visit(parent);
                foreach(var op in node.modifiers?.operations??new List<StudioModifierOperation>())
                    if(op.sweep!=null){if(!string.IsNullOrEmpty(op.sweep.sourceId)&&ids.TryGetValue(op.sweep.sourceId,out int source))Visit(source);if(op.sweep.profileShape==3&&!string.IsNullOrEmpty(op.sweep.profileId)&&ids.TryGetValue(op.sweep.profileId,out int profile))Visit(profile);}
                foreach(var c in node.constraints??new List<StudioModelConstraintData>())
                {
                    foreach(var s in c.sources)if(!string.IsNullOrEmpty(s.nodeId)&&ids.TryGetValue(s.nodeId,out int source))Visit(source);
                    if(!string.IsNullOrEmpty(c.worldUpId)&&ids.TryGetValue(c.worldUpId,out int up))Visit(up);
                }
                visiting.Remove(index);visited.Add(index);
            }
            for(int i=0;i<document.nodes.Count;i++)Visit(i);
        }
    }
}
