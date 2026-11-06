using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private StudioSpline splineDraft;
        private bool splineEditing, splineInsert, splineExtend;
        private int splineOwner = -1, splineHandle, splineControl, splineUndo = -1;
        private readonly HashSet<int> splineKnots = new HashSet<int>();
        private StudioSpline splineBeforeDrag;
        private Vector3 splineDragStart;
        private Quaternion splineRotation = Quaternion.identity;
        private Vector3 splineScale = Vector3.one;
        private Quaternion splineFrame = Quaternion.identity;
        private bool splineMarquee;
        private Vector2 splineMarqueeStart, splineMarqueeEnd;
        private StudioSpline SelectedSpline => Document && Active >= 0 && Active < Document.nodes.Count ? Document.nodes[Active].spline : null;
        private StudioSpline EditingSpline => splineDraft ?? (splineEditing ? SelectedSpline : null);
        private bool SplineInputActive => splineDraft != null || splineEditing && SelectedSpline != null;
        private Matrix4x4 SplineMatrix => splineDraft != null ? Matrix4x4.identity : Transforms[Active].localToWorldMatrix;
        private void BeginSpline(SceneView view, Action<StudioModelDocument> loaded, StudioSplineKind kind)
        {
            using var camera = new CreationCameraScope(view);
            FinishToolsForSwitch(); CancelMarkingMenu(); EnsureWorkspace(loaded);
            splineDraft = new StudioSpline { kind=kind }; splineEditing=false; splineInsert=splineExtend=false;
            splineKnots.Clear(); splineHandle=0; polygonPlane=new Plane(-view.camera.transform.forward,view.pivot);
            tool=Tool.Move; mode=ComponentMode.Object; pivot=false; CameraOptionsVisible=LightingOptionsVisible=false;
            OpenToolOptions(); repaint();
        }
        private void EditSpline()
        {
            if (SelectedSpline==null) throw new InvalidOperationException("Select a spline object in the viewport or hierarchy first.");
            FinishToolsForSwitch(); splineEditing=true; splineOwner=Active; splineKnots.Clear(); splineHandle=0;
            mode=ComponentMode.Object; tool=Tool.Move; pivot=false; repaint();
        }
        private void FinishSpline()
        {
            if(splineDraft==null){StopSplineEditing();return;}
            splineDraft.Validate();
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
            try {
                int index=Document.AddSpline(splineDraft); RememberAction(RepeatKind.Spline,new Vector4((int)splineDraft.kind,0,0,0)); splineDraft=null;
                Rebuild(); SelectObject(index); splineEditing=true; splineOwner=index;
                splineKnots.Clear(); splineKnots.Add(Document.nodes[index].spline.knots.Count-1); tool=Tool.Move; Changed(false);
            } finally {Undo.CollapseUndoOperations(group);}
        }
        private void StopSplineEditing()
        {
            EndSplineGesture(false); splineDraft=null; splineEditing=splineInsert=splineExtend=false;
            splineKnots.Clear(); splineHandle=0; splineMarquee=false; polygonHover=null; EndPivotSnap(); ClearGridSnap(); repaint();
        }
        private void FinishSplineForSwitch()
        {
            if(splineDraft!=null){if(splineDraft.knots.Count>=2)Run(FinishSpline);else splineDraft=null;}
            splineInsert=splineExtend=false;
        }
        private void SplineEdit(string label, Action<StudioSpline> edit)
        {
            var source=EditingSpline ?? SelectedSpline;
            if(source==null)throw new InvalidOperationException("Select a spline object first.");
            var copy=source.Copy(); edit(copy); copy.RefreshAutomaticTangents(); copy.Validate();
            if(splineDraft!=null)splineDraft=copy;
            else {Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,label);Document.nodes[Active].spline=copy;Changed(false);}
            splineKnots.RemoveWhere(i=>i<0||i>=copy.knots.Count);
            int repeat=label=="Reverse Spline"?2:label=="Remove Spline Knots"?3:label=="Close Spline"||label=="Open Spline"||label=="Spline Closure"?(copy.closed?4:5):label=="Linear Spline"||label=="Bezier Spline"||label=="Spline Type"?6+(int)copy.kind:label=="Spline Tangent Mode"&&splineKnots.Count>0?8+(int)copy.knots[splineKnots.First()].tangent:label=="Smooth Spline Knots"?11:label=="Insert Spline Knot"?12:-1;
            if(splineDraft==null&&repeat>=0)RememberAction(RepeatKind.Spline,new Vector4(repeat,0,0,0));repaint();
        }
        private void RepeatSplineAction(int action)
        {
            if(action<2){BeginSpline(commandView,commandLoaded,(StudioSplineKind)action);return;}
            if(action==2)ReverseSpline();else if(action==3)RemoveSplineKnots();
            else if(action==4||action==5)SplineEdit("Spline Closure",s=>s.closed=action==4);
            else if(action==6||action==7)SplineEdit("Spline Type",s=>s.SetKind((StudioSplineKind)(action-6)));
            else if(action>=8&&action<=10)SetSplineTangent((StudioSplineTangent)(action-8));
            else if(action==11)SmoothSplineKnots();else if(action==12)SetSplineInsert(false);
        }
        private void RemoveSplineKnots() => SplineEdit("Remove Spline Knots",s=>s.Remove(splineKnots));
        private void ReverseSpline()
        {
            int count=(EditingSpline??SelectedSpline)?.knots.Count??0;
            var selection=splineKnots.Select(i=>count-1-i).ToArray();
            SplineEdit("Reverse Spline",s=>s.Reverse()); splineKnots.Clear();foreach(int i in selection)splineKnots.Add(i);splineHandle=-splineHandle;
        }
        private void SetSplineTangent(StudioSplineTangent tangent)
        {
            if(splineKnots.Count==0)throw new InvalidOperationException("Select spline knots first.");
            SplineEdit("Spline Tangent Mode",s=>{foreach(int i in splineKnots){var k=s.knots[i];k.automatic=false;k.tangent=tangent;var v=k.outgoing.sqrMagnitude>1e-20f?k.outgoing:-k.incoming;s.SetHandle(i,false,v);}});
        }
        private void SmoothSplineKnots()
        {
            if(splineKnots.Count==0)throw new InvalidOperationException("Select spline knots first.");
            SplineEdit("Smooth Spline Knots",s=>{foreach(int i in splineKnots){var k=s.knots[i];var before=s.knots[i>0?i-1:s.closed?s.knots.Count-1:i].position;var after=s.knots[i+1<s.knots.Count?i+1:s.closed?0:i].position;var direction=(after-before).normalized;k.incoming=-direction*Vector3.Distance(before,k.position)/3;k.outgoing=direction*Vector3.Distance(after,k.position)/3;k.tangent=StudioSplineTangent.Aligned;k.automatic=true;}});
        }
        private void SetSplineInsert(bool extend)
        {
            if(!SplineInputActive)EditSpline();
            if(extend){var s=EditingSpline;if(s.closed)throw new InvalidOperationException("Open the spline before extending it.");if(splineKnots.Count!=1||(splineKnots.First()!=0&&splineKnots.First()!=s.knots.Count-1))throw new InvalidOperationException("Select one endpoint to extend.");}
            splineExtend=extend;splineInsert=!extend;repaint();
        }
        private void PopulateSplineMenu(StudioMenu menu, SceneView view, Action<StudioModelDocument> loaded)
        {
            AddCommand(menu,"Linear Spline",true,()=>BeginSpline(view,loaded,StudioSplineKind.Linear),finishPreview:false);
            AddCommand(menu,"Bezier Spline",true,()=>BeginSpline(view,loaded,StudioSplineKind.Bezier),finishPreview:false);
            menu.AddSeparator("");
            AddCommand(menu,"Edit Knots",true,EditSpline,finishPreview:false);

            AddCommand(menu,"Finish Drawing",splineDraft!=null,FinishSpline,finishPreview:false);
            AddCommand(menu,"Insert Knot",true,()=>SetSplineInsert(false),finishPreview:false);
            AddCommand(menu,"Remove Knot",true,RemoveSplineKnots,finishPreview:false);
            AddCommand(menu,"Extend Endpoint",true,()=>SetSplineInsert(true),finishPreview:false);
            menu.AddSeparator("");
            AddCommand(menu,"Open Loop",true,()=>SplineEdit("Open Spline",s=>s.closed=false),finishPreview:false);
            AddCommand(menu,"Close Loop",true,()=>SplineEdit("Close Spline",s=>s.closed=true),finishPreview:false);
            AddCommand(menu,"Reverse Direction",true,ReverseSpline,finishPreview:false);
            menu.AddSeparator("");
            AddCommand(menu,"Convert to Linear",true,()=>SplineEdit("Linear Spline",s=>s.SetKind(StudioSplineKind.Linear)),finishPreview:false);
            AddCommand(menu,"Convert to Bezier",true,()=>SplineEdit("Bezier Spline",s=>s.SetKind(StudioSplineKind.Bezier)),finishPreview:false);
            AddCommand(menu,"Corner Tangents",true,()=>SetSplineTangent(StudioSplineTangent.Corner),finishPreview:false);
            AddCommand(menu,"Aligned Tangents",true,()=>SetSplineTangent(StudioSplineTangent.Aligned),finishPreview:false);
            AddCommand(menu,"Mirrored Tangents",true,()=>SetSplineTangent(StudioSplineTangent.Mirrored),finishPreview:false);
            AddCommand(menu,"Smooth Knots",true,SmoothSplineKnots,finishPreview:false);
        }
        private void DrawSplineCreationOptions()
        {
            EditorGUILayout.HelpBox("Click to place knots; Bezier tangents smooth automatically. Drag to author manual handles. Enter or W finishes; Backspace removes the last knot; Escape cancels. Hold configured vertex/edge/grid snap keys. Alt navigates.",MessageType.Info);
            GUILayout.Label(splineDraft.kind+" · "+splineDraft.knots.Count+" knots");
            if(GUILayout.Button("Finish Drawing"))Run(FinishSpline);
            if(GUILayout.Button("Cancel"))StopSplineEditing();
        }
        private bool SplineKey(Event evt)
        {
            if(!SplineInputActive)return false;
            if(StudioShortcuts.Pressed(StudioShortcut.Cancel,evt)){if(splineBeforeDrag!=null)EndSplineGesture(true);else StopSplineEditing();evt.Use();return true;}
            if(splineDraft!=null && (evt.keyCode==KeyCode.Return || StudioShortcuts.Pressed(StudioShortcut.Move,evt))){Run(FinishSpline);evt.Use();return true;}
            if(StudioShortcuts.Pressed(StudioShortcut.Delete,evt)||evt.keyCode==KeyCode.Backspace){if(splineDraft!=null){if(splineDraft.knots.Count>0)splineDraft.knots.RemoveAt(splineDraft.knots.Count-1);splineDraft.RefreshAutomaticTangents();splineKnots.Clear();}else Run(RemoveSplineKnots);evt.Use();repaint();return true;}
            if(StudioShortcuts.Pressed(StudioShortcut.SelectAll,evt)){splineKnots.Clear();for(int i=0;i<EditingSpline.knots.Count;i++)splineKnots.Add(i);splineHandle=0;evt.Use();repaint();return true;}
            if(StudioShortcuts.Pressed(StudioShortcut.Object,evt)){StopSplineEditing();evt.Use();return true;}
            return false;
        }
        private void BeginSplineGesture(Vector3 world)
        {
            if(splineBeforeDrag!=null)return;
            splineBeforeDrag=EditingSpline.Copy();splineDragStart=world;splineFrame=local&&!CenterMode&&splineDraft==null?(Transforms[Active].rotation*Document.nodes[Active].pivotRotation):Quaternion.identity;splineRotation=splineFrame;splineScale=Vector3.one;
            if(splineDraft==null){Undo.IncrementCurrentGroup();splineUndo=Undo.GetCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Edit Spline");}
        }
        private void EndSplineGesture(bool cancel)
        {
            if(splineBeforeDrag==null&&splineControl==0)return;
            if(splineBeforeDrag!=null&&cancel){if(splineDraft!=null)splineDraft=splineBeforeDrag;else if(splineUndo>=0)Undo.RevertAllDownToGroup(splineUndo);}
            if(cancel)GUIUtility.hotControl=0;
            if(splineUndo>=0&&!cancel)Undo.CollapseUndoOperations(splineUndo);
            splineUndo=-1;splineBeforeDrag=null;splineRotation=Quaternion.identity;splineScale=Vector3.one;
            if(splineControl!=0&&GUIUtility.hotControl==splineControl)GUIUtility.hotControl=0;
            splineControl=0;edgeSnapLatched=false;repaint();
        }
        private bool TrySplinePoint(SceneView view, Vector2 mouse, out Vector3 point)
        {
            if(pivotSnapHeld&&!edgeSnapHeld){float best=12;bool found=false;point=default;
                for(int n=-1;n<(Document?Document.nodes.Count:0);n++){
                    var s=n<0?splineDraft:Document.nodes[n].spline;if(s==null||n>=0&&!SplineVisible(n))continue;
                    var matrix=n<0?Matrix4x4.identity:Transforms[n].localToWorldMatrix;
                    for(int i=0;i<s.knots.Count;i++){if(splineBeforeDrag!=null&&splineKnots.Contains(i)&&(n<0&&splineDraft!=null||n==Active&&splineDraft==null))continue;var p=matrix.MultiplyPoint3x4(s.knots[i].position);if(view.camera.WorldToViewportPoint(p).z<=0)continue;float d=Vector2.Distance(mouse,HandleUtility.WorldToGUIPoint(p));if(d<best){best=d;point=p;found=true;}}
                }
                if(found)return true;
            }
            return TryPolygonPoint(view,mouse,out point);
        }
        private bool SplineVisible(int index) => Transforms!=null&&index<Transforms.Length&&Transforms[index]&&Transforms[index].gameObject.activeInHierarchy;
        private void DrawSpline(StudioSpline s, Matrix4x4 matrix, bool selected)
        {
            if(Event.current.type!=EventType.Repaint)return;
            using(new Handles.DrawingScope(selected?new Color(.35f,.9f,1f):new Color(.6f,.7f,.85f)))
            {
                var depth=Handles.zTest;Handles.zTest=displayXray?UnityEngine.Rendering.CompareFunction.Always:UnityEngine.Rendering.CompareFunction.LessEqual;
                try {for(int i=0;i<s.SegmentCount;i++){var a=s.knots[i];var b=s.knots[(i+1)%s.knots.Count];var p=matrix.MultiplyPoint3x4(a.position);var q=matrix.MultiplyPoint3x4(b.position);if(s.kind==StudioSplineKind.Bezier)Handles.DrawBezier(p,q,matrix.MultiplyPoint3x4(a.position+a.outgoing),matrix.MultiplyPoint3x4(b.position+b.incoming),Handles.color,null,2);else Handles.DrawAAPolyLine(2,p,q);}
                    if(selected&&s.SegmentCount>0){var at=matrix.MultiplyPoint3x4(s.Evaluate(0,.5f));var dir=matrix.MultiplyVector(s.Evaluate(0,.51f)-s.Evaluate(0,.49f));if(dir.sqrMagnitude>1e-15f)Handles.ConeHandleCap(0,at,Quaternion.LookRotation(dir),HandleUtility.GetHandleSize(at)*.07f,EventType.Repaint);}
                }finally{Handles.zTest=depth;}
            }
        }
        private bool PickSplineSegment(SceneView view, StudioSpline s, Matrix4x4 matrix, Vector2 mouse, out int segment, out float fraction, out float distance)
        {
            segment=-1;fraction=0;distance=9;OcclusionMesh[] occluders=null;
            for(int i=0;i<s.SegmentCount;i++){int steps=s.kind==StudioSplineKind.Linear?1:48;var prev=HandleUtility.WorldToGUIPoint(matrix.MultiplyPoint3x4(s.Evaluate(i,0)));
                for(int j=1;j<=steps;j++){var world=matrix.MultiplyPoint3x4(s.Evaluate(i,(float)j/steps));var next=HandleUtility.WorldToGUIPoint(world);var delta=next-prev;float u=delta.sqrMagnitude>1e-10f?Mathf.Clamp01(Vector2.Dot(mouse-prev,delta)/delta.sqrMagnitude):0;float d=Vector2.Distance(mouse,prev+u*delta);if(d<distance){float candidate=(j-1+u)/steps;var point=matrix.MultiplyPoint3x4(s.Evaluate(i,candidate));if(view.camera.WorldToViewportPoint(point).z>view.camera.nearClipPlane&&(xray||!Occluded(view,point,ref occluders))){distance=d;segment=i;fraction=candidate;}}prev=next;}}
            return segment>=0;
        }
        private bool SplineSceneGUI(SceneView view)
        {
            var evt=Event.current;
            bool over=!(view is MashBoxStudioViewport host)||host.ModelCanvasRect.Contains(evt.mousePosition)&&!host.IsOverViewportControls(evt.mousePosition);
            if(Document)for(int n=0;n<Document.nodes.Count;n++)if(Document.nodes[n].spline!=null&&SplineVisible(n))DrawSpline(Document.nodes[n].spline,Transforms[n].localToWorldMatrix,objects.Contains(n));
            if(splineEditing && splineOwner!=Active){splineKnots.Clear();splineHandle=0;splineOwner=Active;}
            var s=EditingSpline;
            if(s==null){
                int pick=GUIUtility.GetControlID("StudioSplineObjectPick".GetHashCode(),FocusType.Passive);
                if(!markingOpen&&!evt.alt&&over&&Document){int hit=-1;float best=9;for(int n=0;n<Document.nodes.Count;n++)if(Document.nodes[n].spline!=null&&SplineVisible(n)&&PickSplineSegment(view,Document.nodes[n].spline,Transforms[n].localToWorldMatrix,evt.mousePosition,out _,out _,out float d)&&d<best){hit=n;best=d;}
                    if(hit>=0){if(evt.type==EventType.Layout)HandleUtility.AddControl(pick,best);if(evt.type==EventType.MouseDown&&evt.button==0&&GUIUtility.hotControl==0&&HandleUtility.nearestControl==pick){SelectObject(hit,evt.shift);if(evt.clickCount==2)EditSpline();evt.Use();return true;}}}
                return false;
            }
            splineKnots.RemoveWhere(i=>i<0||i>=s.knots.Count);
            if(markingOpen)return false;
            if(HandleModelKeyboard(view))return true;
            if(evt.alt)return true;
            if(splineDraft==null&&!SplineVisible(Active))return false;
            var matrix=SplineMatrix;
            if(splineDraft!=null)DrawSpline(s,matrix,true);
            int control=GUIUtility.GetControlID("StudioSplineInput".GetHashCode(),FocusType.Passive);
            if(over&&evt.type==EventType.Layout)HandleUtility.AddDefaultControl(control);
            if(over&&evt.type==EventType.MouseMove&&(splineDraft!=null||splineExtend))polygonHover=TrySplinePoint(view,evt.mousePosition,out var hover)?hover:(Vector3?)null;
            if(evt.type==EventType.Repaint){
                using(new Handles.DrawingScope(Color.yellow)){
                    if(splineInsert&&PickSplineSegment(view,s,matrix,evt.mousePosition,out int seg,out float fraction,out _)){var p=matrix.MultiplyPoint3x4(s.Evaluate(seg,fraction));Handles.DotHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.06f,EventType.Repaint);}
                    if((splineDraft!=null||splineExtend)&&polygonHover.HasValue){var p=polygonHover.Value;if(s.knots.Count>0)Handles.DrawDottedLine(matrix.MultiplyPoint3x4(s.knots[splineExtend&&splineKnots.Count==1?splineKnots.First():s.knots.Count-1].position),p,4);Handles.DotHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.04f,EventType.Repaint);}
                }
                if(splineMarquee){Handles.BeginGUI();GUI.Box(Rect.MinMaxRect(Mathf.Min(splineMarqueeStart.x,splineMarqueeEnd.x),Mathf.Min(splineMarqueeStart.y,splineMarqueeEnd.y),Mathf.Max(splineMarqueeStart.x,splineMarqueeEnd.x),Mathf.Max(splineMarqueeStart.y,splineMarqueeEnd.y)),GUIContent.none);Handles.EndGUI();}
            }
            if(splineBeforeDrag!=null&&splineControl==0&&GUIUtility.hotControl==0)EndSplineGesture(false);
            if(splineDraft==null&&!splineInsert&&!splineExtend)DrawSplineGizmo(view);
            int knot=-1,handle=0;float nearest=10;
            for(int i=0;i<s.knots.Count;i++){
                var k=s.knots[i];var p=matrix.MultiplyPoint3x4(k.position);
                void Marker(Vector3 world,int part){float d=Vector2.Distance(evt.mousePosition,HandleUtility.WorldToGUIPoint(world));if(d<nearest){nearest=d;knot=i;handle=part;}if(evt.type==EventType.Repaint){Handles.color=splineKnots.Contains(i)&&(splineHandle==part||part==0)?Color.yellow:part==0?Color.cyan:new Color(1,.3f,.8f);Handles.DotHandleCap(0,world,Quaternion.identity,HandleUtility.GetHandleSize(world)*(part==0?.055f:.035f),EventType.Repaint);}}
                Marker(p,0);
                if(s.kind==StudioSplineKind.Bezier&&splineKnots.Contains(i)){
                    var a=matrix.MultiplyPoint3x4(k.position+k.incoming);var b=matrix.MultiplyPoint3x4(k.position+k.outgoing);
                    if(evt.type==EventType.Repaint){Handles.color=new Color(1,.3f,.8f);Handles.DrawAAPolyLine(1,a,p,b);}Marker(a,-1);Marker(b,1);
                }
            }
            if(evt.type==EventType.MouseDown&&evt.button==0&&over&&GUIUtility.hotControl==0){
                if(splineDraft!=null||splineExtend){
                    if(splineExtend)polygonPlane=new Plane(-view.camera.transform.forward,matrix.MultiplyPoint3x4(s.knots[splineKnots.First()].position));
                    if(TrySplinePoint(view,evt.mousePosition,out var point)){
                        BeginSplineGesture(point);var k=new StudioSpline.Knot{position=matrix.inverse.MultiplyPoint3x4(point),tangent=StudioSplineTangent.Aligned,automatic=s.kind==StudioSplineKind.Bezier};
                        int index=splineExtend&&splineKnots.First()==0?0:s.knots.Count;s.knots.Insert(index,k);s.RefreshAutomaticTangents();splineKnots.Clear();splineKnots.Add(index);splineHandle=0;splineControl=control;GUIUtility.hotControl=control;if(splineDraft==null)Changed(false);
                    }evt.Use();repaint();return true;
                }
                if((splineInsert||evt.clickCount==2)&&PickSplineSegment(view,s,matrix,evt.mousePosition,out int segment,out float t,out _)){
                    int added=-1;Run(()=>SplineEdit("Insert Spline Knot",c=>added=c.Insert(segment,Mathf.Clamp(t,.001f,.999f))));if(added>=0){splineKnots.Clear();splineKnots.Add(added);splineHandle=0;}evt.Use();return true;
                }
                if(knot>=0){if(!evt.shift)splineKnots.Clear();if(evt.shift&&splineKnots.Contains(knot))splineKnots.Remove(knot);else splineKnots.Add(knot);splineHandle=splineKnots.Count==1?handle:0;
                    if(splineKnots.Count>0){var k=s.knots[knot];var point=matrix.MultiplyPoint3x4(k.position+(handle<0?k.incoming:handle>0?k.outgoing:Vector3.zero));polygonPlane=new Plane(-view.camera.transform.forward,point);BeginSplineGesture(point);splineControl=control;GUIUtility.hotControl=control;}
                }else {if(!evt.shift){splineKnots.Clear();splineHandle=0;}splineMarquee=true;splineMarqueeStart=splineMarqueeEnd=evt.mousePosition;splineControl=control;GUIUtility.hotControl=control;}
                evt.Use();repaint();
            }
            if(splineControl!=0&&GUIUtility.hotControl==splineControl&&evt.type==EventType.MouseDrag){if(splineMarquee)splineMarqueeEnd=evt.mousePosition;else if(TrySplinePoint(view,evt.mousePosition,out var point)){if(splineDraft!=null||splineExtend){int i=splineKnots.First();s.knots[i].tangent=StudioSplineTangent.Mirrored;s.SetHandle(i,false,matrix.inverse.MultiplyPoint3x4(point)-s.knots[i].position);}else ApplySplineTranslation(point-splineDragStart);if(splineDraft==null)Changed(false);}evt.Use();repaint();}
            if(splineControl!=0&&evt.rawType==EventType.MouseUp){if(splineMarquee){var rect=Rect.MinMaxRect(Mathf.Min(splineMarqueeStart.x,splineMarqueeEnd.x),Mathf.Min(splineMarqueeStart.y,splineMarqueeEnd.y),Mathf.Max(splineMarqueeStart.x,splineMarqueeEnd.x),Mathf.Max(splineMarqueeStart.y,splineMarqueeEnd.y));for(int i=0;i<s.knots.Count;i++)if(VisiblePoint(view,matrix.MultiplyPoint3x4(s.knots[i].position),rect))splineKnots.Add(i);splineMarquee=false;}EndSplineGesture(false);evt.Use();}
            if(evt.type==EventType.MouseMove)repaint();
            return true;
        }
        private Vector3 SplineSelectionCenter()
        {
            var s=EditingSpline;var center=Vector3.zero;foreach(int i in splineKnots){var k=s.knots[i];center+=SplineMatrix.MultiplyPoint3x4(k.position+(splineHandle<0?k.incoming:splineHandle>0?k.outgoing:Vector3.zero))/splineKnots.Count;}return center;
        }
        private void ApplySplineTranslation(Vector3 delta)
        {
            var s=EditingSpline;var local=SplineMatrix.inverse.MultiplyVector(delta);
            foreach(int i in splineKnots){var k=splineBeforeDrag.knots[i];if(splineHandle==0)s.knots[i].position=k.position+local;else s.SetHandle(i,splineHandle<0,(splineHandle<0?k.incoming:k.outgoing)+local);}
            s.RefreshAutomaticTangents();
        }
        private void DrawSplineGizmo(SceneView view)
        {
            if(splineKnots.Count==0||tool==Tool.None)return;
            var center=SplineSelectionCenter();EditorGUI.BeginChangeCheck();
            var frame=splineBeforeDrag!=null?splineFrame:local&&!CenterMode?(Transforms[Active].rotation*Document.nodes[Active].pivotRotation):Quaternion.identity;
            var moved=center;var rotation=splineBeforeDrag!=null?splineRotation:frame;var scale=splineScale;
            if(tool==Tool.Rotate)rotation=Handles.RotationHandle(rotation,splineBeforeDrag!=null?splineDragStart:center);
            else if(tool==Tool.Scale)scale=Handles.ScaleHandle(splineScale,splineBeforeDrag!=null?splineDragStart:center,frame,HandleUtility.GetHandleSize(center));
            else moved=Handles.PositionHandle(center,frame);
            if(!EditorGUI.EndChangeCheck())return;
            BeginSplineGesture(center);var matrix=SplineMatrix;var inverse=matrix.inverse;var s=EditingSpline;
            if(tool==Tool.Move){if(pivotSnapHeld||edgeSnapHeld||gridSnapHeld){if(!TrySplinePoint(view,Event.current.mousePosition,out var snap))return;moved=snap;}ApplySplineTranslation(moved-splineDragStart);}
            else foreach(int i in splineKnots){var k=splineBeforeDrag.knots[i];Vector3 Map(Vector3 p){var world=matrix.MultiplyPoint3x4(p)-splineDragStart;return inverse.MultiplyPoint3x4(splineDragStart+(tool==Tool.Rotate?rotation*Quaternion.Inverse(splineFrame)*world:splineFrame*Vector3.Scale(Quaternion.Inverse(splineFrame)*world,scale)));}
                if(splineHandle!=0)s.SetHandle(i,splineHandle<0,Map(k.position+(splineHandle<0?k.incoming:k.outgoing))-s.knots[i].position);
                else{var pos=Map(k.position);s.knots[i].automatic=false;s.knots[i].position=pos;s.knots[i].incoming=Map(k.position+k.incoming)-pos;s.knots[i].outgoing=Map(k.position+k.outgoing)-pos;}}
            s.RefreshAutomaticTangents();splineRotation=rotation;splineScale=scale;Changed(false);
        }
        private Bounds SplineBounds(int index)
        {
            var s=Document.nodes[index].spline;var matrix=Transforms[index].localToWorldMatrix;var bounds=new Bounds(matrix.MultiplyPoint3x4(s.knots[0].position),Vector3.zero);
            foreach(var k in s.knots){bounds.Encapsulate(matrix.MultiplyPoint3x4(k.position));if(s.kind==StudioSplineKind.Bezier){bounds.Encapsulate(matrix.MultiplyPoint3x4(k.position+k.incoming));bounds.Encapsulate(matrix.MultiplyPoint3x4(k.position+k.outgoing));}}return bounds;
        }
    }
}
