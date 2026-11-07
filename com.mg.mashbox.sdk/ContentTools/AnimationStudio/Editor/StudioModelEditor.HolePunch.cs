using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        private ToolChannelPane holePunchPane;
        private StudioMeshToolPreview holePunchPreview;
        private int holePunchNode=-1,holePunchFace=-1;
        private Vector3 holePunchCenter,holePunchNormal;
        private Matrix4x4 holePunchWorld;
        private float holePunchRadius=.1f,holePunchDepth=.1f,holePunchRotation;
        private int holePunchSides=16;
        private bool holePunchLimitDepth=true;
        private bool holePunchThrough,holePunchClean=true,holePunchPlaced,holePunchValid;
        private int[] holePunchAffected;
        private void OpenHolePunchTool()
        {
            if(holePunchPane){holePunchPane.Focus();return;}
            if(symmetryEnabled)throw new InvalidOperationException("Turn Symmetry off before using Hole Puncher."); FinishToolsForSwitch();StopCutTool();StopSplineEditing();
            holePunchPane=ToolChannelPane.Open("Hole Puncher",DrawHolePunchOptions,()=>{holePunchPane=null;CancelHolePunch();if(cutTool==CutTool.HolePunch)cutTool=CutTool.None;repaint();},
                execute:ApplyHolePunch,canExecute:()=>holePunchPlaced&&CanCutMesh,failed:ex=>{error=ex.Message;repaint();},owner:ToolChannelPaneOwner,
                preview:()=>{if(holePunchPlaced)Run(EvaluateHolePunch,false);},cancelPreview:()=>CancelHolePunch(),history:PerformWorkspaceHistory,repeat:PerformRepeatLastAction);
            if(repeatingAction?.kind==RepeatKind.HolePunch){var p=repeatingAction.parameters;holePunchRotation=repeatingAction.flow.x;holePunchRadius=p.x;holePunchSides=Mathf.RoundToInt(p.y);holePunchDepth=p.z;holePunchThrough=(((int)p.w)&1)!=0;holePunchClean=(((int)p.w)&2)!=0;holePunchLimitDepth=(((int)p.w)&4)!=0;} cutTool=CutTool.HolePunch;tool=Tool.None;pivot=false;error="Hover the surface and click to place the circle.";FocusToolOptions?.Invoke();repaint();
        }
        private void DrawHolePunchOptions()
        {
            EditorGUI.BeginChangeCheck();
            holePunchRadius=StudioSceneUnits.Field(new GUIContent("Radius"),holePunchRadius);
            holePunchSides=Mathf.Clamp(EditorGUILayout.IntField("Radial Face Count",holePunchSides),3,128);
            holePunchRotation=EditorGUILayout.FloatField(new GUIContent("Rotation","Rotate the radial edges around the cut axis, in degrees."),holePunchRotation);
            holePunchThrough=EditorGUILayout.Toggle("Through",holePunchThrough);
            if(holePunchThrough)holePunchLimitDepth=EditorGUILayout.Toggle(new GUIContent("Limit Depth","Confine the cut to the yellow volume, leaving farther mesh pieces untouched."),holePunchLimitDepth);
            using(new EditorGUI.DisabledScope(holePunchThrough&&!holePunchLimitDepth))holePunchDepth=StudioSceneUnits.Field(new GUIContent("Depth"),holePunchDepth);
            holePunchClean=EditorGUILayout.Toggle("Clean Patch Topology",holePunchClean);
            if(EditorGUI.EndChangeCheck())repaint();
            GUILayout.Label("Click the surface to place the circle across adjoining faces. Depth cuts to a plane behind the placement point; Through with Limit Depth stops at the yellow end cap. Drag its arrow or edit Depth to reach the intended exit; ending inside a solid leaves a floor. Turn Limit Depth off to cut every intersecting layer. Requires a closed surface. Clean Patch rebuilds a wider surface area with a quad ring, a transition into the mesh and a gridded floor. It can slightly reshape the local surface.",StudioTheme.Styles.wordWrappedMiniLabel);
            if(holePunchPlaced&&GUILayout.Button("Reposition")){CancelHolePunch();repaint();}
        }
        private void RefreshHolePunch(){var old=livePreview;livePreview=holePunchPreview;try{RefreshLiveMeshes(false);}finally{livePreview=old;}}
        private void CancelHolePunch(bool afterUndo=false)
        {
            if(holePunchPreview!=null){holePunchPreview.Restore(afterUndo);if(Document)RefreshHolePunch();holePunchPreview.Dispose();holePunchPreview=null;}
            holePunchPlaced=holePunchValid=false;holePunchFace=-1;holePunchNode=-1;holePunchAffected=null;
        }
        private void CloseHolePunchTool(){var pane=holePunchPane;holePunchPane=null;CancelHolePunch();if(pane)pane.Close();}
        private void EvaluateHolePunch()
        {
            holePunchValid=false;
            if(!holePunchPlaced||!CanCutMesh)throw new InvalidOperationException("Place a circle on an editable static mesh.");
            if(holePunchPreview!=null&&(Active!=holePunchNode||ActiveMesh!=holePunchPreview.Targets[0].Live)){CancelHolePunch();throw new InvalidOperationException("The target changed. Place the circle again.");}
            if(holePunchPreview==null){FlushModifierEdits();PrepareIsolationTopologyEdit();holePunchNode=Active;holePunchWorld=Transforms[Active].localToWorldMatrix;holePunchPreview=new StudioMeshToolPreview(Document,new[]{Active});}
            var target=holePunchPreview.Targets[0];target.Reset();
            try{holePunchAffected=target.Topology.PunchHole(target.Work,holePunchFace,holePunchCenter,holePunchWorld,holePunchRadius,holePunchSides,holePunchDepth,holePunchThrough,holePunchClean,holePunchRotation,holePunchNormal,holePunchLimitDepth);holePunchPreview.Publish(true);RefreshHolePunch();holePunchValid=true;error=null;}
            catch{holePunchPreview.Restore();RefreshHolePunch();throw;}finally{repaint();}
        }
        private void ApplyHolePunch()
        {
            EvaluateHolePunch();if(!holePunchValid)return;
            writingModifiers=true;try{holePunchPreview.Commit("Hole Puncher",()=>RecordDirectPreviewModifiers(holePunchPreview,"Hole Puncher"));}finally{writingModifiers=false;}holePunchPreview.Dispose();holePunchPreview=null;holePunchPlaced=holePunchValid=false;holePunchFace=-1;holePunchAffected=null;
            ClearAllComponentSelections();mode=ComponentMode.Object;MeshTopologyChanged();Changed();EstablishSelectionBaseline();cutTopology=null; RememberAction(RepeatKind.HolePunch,new Vector4(holePunchRadius,holePunchSides,holePunchDepth,(holePunchThrough?1:0)+(holePunchClean?2:0)+(holePunchLimitDepth?4:0)));if(repeatingAction==null&&lastRepeatAction!=null)lastRepeatAction.flow=new Vector3(holePunchRotation,0,0);repaint();
        }
        private void DrawHolePunchDepthHandle()
        {
            if(float.IsNaN(holePunchDepth)||float.IsInfinity(holePunchDepth))return;
            var oldColor=Handles.color;var oldDepth=Handles.zTest;
            try
            {
                Handles.color=new Color(1,.88f,.2f,.8f);Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
                var end=holePunchCenter-holePunchNormal*holePunchDepth;
                EditorGUI.BeginChangeCheck();
                var moved=Handles.Slider(end,-holePunchNormal,HandleUtility.GetHandleSize(end)*.12f,Handles.ArrowHandleCap,0);
                if(EditorGUI.EndChangeCheck())
                {
                    holePunchDepth=Mathf.Max(.00001f,Vector3.Dot(holePunchCenter-moved,holePunchNormal));
                    Run(EvaluateHolePunch,false);holePunchPane?.Repaint();repaint();
                }
            }
            finally{Handles.color=oldColor;Handles.zTest=oldDepth;}
        }
        private void DrawHolePunchVolume(Vector3[] outline,Matrix4x4 matrix)
        {
            float front=holePunchRadius,back=holePunchDepth;
            if(!holePunchLimitDepth)
            {
                // Unlimited cuts only act on this target; display a prism spanning
                // its captured bounds rather than pretending to draw infinity.
                front=back=Mathf.Max(.00001f,holePunchRadius*.01f);
                var bounds=(holePunchPreview!=null?holePunchPreview.Targets[0].Source:ActiveMesh).bounds;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    float along=Vector3.Dot(matrix.MultiplyPoint3x4(corner)-holePunchCenter,holePunchNormal);
                    front=Mathf.Max(front,along);back=Mathf.Max(back,-along);
                }
            }
            if(!float.IsNaN(front)&&!float.IsInfinity(front)&&!float.IsNaN(back)&&!float.IsInfinity(back)&&front>0&&back>0)
            {
                var start=outline.Select(p=>p+holePunchNormal*front).ToArray();
                var end=outline.Select(p=>p-holePunchNormal*back).ToArray();
                // A faint through-surface fill keeps depth legible inside the mesh.
                // The ordinary placement ring retains its normal depth test.
                Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
                Handles.color=new Color(1,.9f,.15f,.035f);
                for(int i=0;i<holePunchSides;i++)Handles.DrawAAConvexPolygon(start[i],start[i+1],end[i+1],end[i]);
                Handles.DrawAAConvexPolygon(end.Take(holePunchSides).ToArray());
                Handles.color=new Color(1,.88f,.18f,.35f);
                Handles.DrawAAPolyLine(1.5f,start);Handles.DrawAAPolyLine(2,end);
                for(int i=0;i<holePunchSides;i+=Mathf.Max(1,holePunchSides/8))Handles.DrawAAPolyLine(1,start[i],end[i]);
            }
        }
        private bool HolePunchSceneGUI(SceneView view)
        {
            var evt=Event.current;if(HandleModelKeyboard(view))return true;
            if(!CanCutMesh){error="Select an editable static mesh for Hole Puncher.";return true;}
            if(holePunchPreview!=null&&Active!=holePunchNode){CancelHolePunch();return true;}
            var topology=holePunchPreview?.Targets[0].Topology??cutTopology;
            if(topology==null||cutMesh!=ActiveMesh){topology=new StudioMeshCuts(Document.nodes[Active]);cutTopology=topology;cutMesh=ActiveMesh;}
            var canvas=view is MashBoxStudioViewport host?host.ModelCanvasRect:new Rect(0,0,view.position.width,view.position.height);
            bool inside=canvas.Contains(evt.mousePosition)&&(!(view is MashBoxStudioViewport overlay)||!overlay.IsOverViewportControls(evt.mousePosition));
            if(evt.type==EventType.Layout&&!evt.alt&&inside)HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if(!holePunchPlaced&&!evt.alt&&inside&&(evt.type==EventType.MouseMove||evt.type==EventType.MouseDown))
            {
                var ray=HandleUtility.GUIPointToWorldRay(evt.mousePosition);var matrix=Transforms[Active].localToWorldMatrix;float nearest=float.PositiveInfinity;holePunchFace=-1;
                var surface=topology.BevelSourceTriangles(holePunchPreview!=null?holePunchPreview.Targets[0].Source:ActiveMesh);for(int f=0;f<surface.Length;f++){var r=surface[f];for(int t=0;t<r.Count;t+=3){var a=matrix.MultiplyPoint3x4(topology.Positions[r[t]]);var b=matrix.MultiplyPoint3x4(topology.Positions[r[t+1]]);var c=matrix.MultiplyPoint3x4(topology.Positions[r[t+2]]);if(RayTriangle(ray,a,b,c,out float distance)&&distance<nearest){nearest=distance;holePunchFace=f;holePunchCenter=ray.GetPoint(distance);holePunchNormal=Vector3.Cross(b-a,c-a).normalized;}}}
                repaint();
                if(evt.type==EventType.MouseDown&&evt.button==0&&holePunchFace>=0&&GUIUtility.hotControl==0){holePunchPlaced=true;Run(EvaluateHolePunch,false);evt.Use();}
            }
            if(holePunchPlaced&&holePunchThrough&&holePunchLimitDepth&&!evt.alt)DrawHolePunchDepthHandle();
            if(evt.type==EventType.Repaint&&holePunchFace>=0)
            {
                var saved=Handles.color;var depth=Handles.zTest;Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;
                try{
                    var matrix=holePunchPreview!=null?holePunchWorld:Transforms[Active].localToWorldMatrix;
                    foreach(int f in holePunchAffected??new[]{holePunchFace})if(f>=0&&f<topology.Faces.Count){var ring=topology.Faces[f].Vertices.Select(v=>matrix.MultiplyPoint3x4(topology.Positions[v])).ToArray();Handles.color=new Color(1,.65f,.1f,.15f);Handles.DrawAAConvexPolygon(ring);Handles.color=new Color(1,.7f,.15f,.9f);foreach(var p in ring)Handles.DotHandleCap(0,p,Quaternion.identity,HandleUtility.GetHandleSize(p)*.035f,EventType.Repaint);}
                    var x=Vector3.Cross(Mathf.Abs(holePunchNormal.y)<.9f?Vector3.up:Vector3.right,holePunchNormal).normalized;var y=Vector3.Cross(holePunchNormal,x);
                    var outline=Enumerable.Range(0,holePunchSides+1).Select(i=>holePunchCenter+holePunchRadius*(x*Mathf.Cos(i*Mathf.PI*2/holePunchSides+(holePunchRotation%360)*Mathf.Deg2Rad)+y*Mathf.Sin(i*Mathf.PI*2/holePunchSides+(holePunchRotation%360)*Mathf.Deg2Rad))).ToArray();Handles.color=holePunchPlaced&&!holePunchValid?Color.red:new Color(1,.7f,.1f);Handles.DrawAAPolyLine(3,outline);
                    if(holePunchThrough)DrawHolePunchVolume(outline,matrix);
                }finally{Handles.color=saved;Handles.zTest=depth;}
            }
            return true;
        }
    }
}




