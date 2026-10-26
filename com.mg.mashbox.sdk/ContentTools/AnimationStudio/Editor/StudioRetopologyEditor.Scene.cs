using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioRetopologyEditor
    {
        private enum RetopoTool { Select, Quad, Patch, Tube, Resolution, RelaxBrush, Strip, TargetWeld }
        private RetopoTool activeTool;
        private Tool transformTool=Tool.Move;
        private readonly List<Vector3> corners=new List<Vector3>();
        private Vector3 cornerNormal;
        private bool continuousPatch=EditorPrefs.GetBool("MashBox.Retopology.ContinuousPatch",false);
        private int[] patchContinuation,mirroredPatchContinuation;
        private StudioRetopologyDocument.Part patchOwner;
        private void ClearPatchCorners()
        {weldSource=-1;if(!stripStroke)ResetStrip();corners.Clear();patchContinuation=null;mirroredPatchContinuation=null;patchOwner=null;}
        private void ToggleContinuousPatch()
        {continuousPatch=!continuousPatch;EditorPrefs.SetBool("MashBox.Retopology.ContinuousPatch",continuousPatch);ClearPatchCorners();repaint();}
        private int dragUndo=-1;
        private Quaternion rotation=Quaternion.identity;
        private Vector3 scale=Vector3.one;
        private bool selectingVertices, selectionAdd, selectionSubtract;
        private Vector2 selectionStart, selectionEnd;
        private int selectionControl, selectionClicks;
        private void CancelVertexSelection()
        {
            if(selectingVertices&&GUIUtility.hotControl==selectionControl)GUIUtility.hotControl=0;
            selectingVertices=false;
        }
        private bool HandleVertexSelection(Event evt)
        {
            if(!selectingVertices)return false;
            if(GUIUtility.hotControl!=selectionControl||evt.type==EventType.Ignore||evt.type==EventType.MouseLeaveWindow||Active==null||!Active.showOutput)
            {CancelVertexSelection();repaint();return true;}
            if(evt.type==EventType.KeyDown&&evt.keyCode==KeyCode.Escape)
            {CancelVertexSelection();evt.Use();repaint();return true;}
            if(evt.type==EventType.MouseDrag&&evt.button==0)
            {selectionEnd=evt.mousePosition;evt.Use();repaint();}
            else if(evt.type==EventType.MouseUp&&evt.button==0)
            {
                selectionEnd=evt.mousePosition;
                if(selectionMode!=SelectionMode.Vertices)FinishComponentSelection();
                else
                {
                var hits=new HashSet<int>();
                if((selectionEnd-selectionStart).sqrMagnitude<16)
                {
                    int vertex=PickVertex(selectionEnd);
                    if(vertex>=0)
                    {
                        var loop=selectionClicks==2?StudioRetopologyLoops.Boundaries(Active).FirstOrDefault(r=>r.Contains(vertex)):null;
                        if(loop!=null)hits.UnionWith(loop);else hits.Add(vertex);
                    }
                }
                else
                {
                    var rect=VertexSelectionRect(selectionStart,selectionEnd);
                    for(int i=0;i<Active.vertices.Count;i++)
                        if(view.camera.WorldToViewportPoint(Active.vertices[i]).z>0&&rect.Contains(HandleUtility.WorldToGUIPoint(Active.vertices[i]))&&IsSelectionPointVisible(Active.vertices[i]))hits.Add(i);
                }
                if(!selectionAdd&&!selectionSubtract)selected.Clear();
                if(selectionSubtract)selected.ExceptWith(hits);else selected.UnionWith(hits);
                }
                rotation=Quaternion.identity;scale=Vector3.one;
                CancelVertexSelection();evt.Use();repaint();
            }
            else if(evt.type==EventType.Repaint)
            {
                if((selectionEnd-selectionStart).sqrMagnitude>=16)
                {
                    Handles.BeginGUI();
                    var rect=VertexSelectionRect(selectionStart,selectionEnd);var color=StudioTheme.Current.Accent;
                    EditorGUI.DrawRect(rect,new Color(color.r,color.g,color.b,.12f));
                    EditorGUI.DrawRect(new Rect(rect.x,rect.y,rect.width,1),color);
                    EditorGUI.DrawRect(new Rect(rect.x,rect.yMax-1,rect.width,1),color);
                    EditorGUI.DrawRect(new Rect(rect.x,rect.y,1,rect.height),color);
                    EditorGUI.DrawRect(new Rect(rect.xMax-1,rect.y,1,rect.height),color);
                    Handles.EndGUI();
                }
            }
            return true;
        }
        internal static Rect VertexSelectionRect(Vector2 a,Vector2 b)=>Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        private bool contextHeld,contextSelection;
        private Vector2 contextOrigin, contextPointer;
        private int contextControl, contextPage, contextHover=-1;
        private bool suppressContext;
        private static readonly Vector2[] WheelOffsets={new Vector2(0,-94),new Vector2(132,-64),new Vector2(154,0),new Vector2(132,64),new Vector2(0,94),new Vector2(-132,64),new Vector2(-154,0),new Vector2(-132,-64)};
        private static readonly string[][] WheelLabels={
            new[]{"Draw Quad","Quad Patch","Topology ›","Display ›","Symmetry X","Surface Snap","Select >","Tube"},
            new[]{"Project","Relax","Bridge Loops","Delete Selected","‹ Back","Pants / Crotch","Target Weld",""},
            new[]{"Reference Wire","Working Wire","Reference","Working Mesh","‹ Back","X-ray Display","X-ray Selection",""},
            new[]{"Draw Quad","Quad Patch","Tube","Resolution","< Back","Continuous Patch","Relax Brush","Quad Strip"},
            new[]{"Vertices","Edges","Faces","","< Back","","",""}};
        private static readonly string[] SelectionWheelLabels={"Vertices","Edges","Faces","Topology >","Display >","Draw >","Delete Selected","Frame Mesh"};
        private string[] CurrentWheelLabels=>contextPage==0&&contextSelection?SelectionWheelLabels:WheelLabels[contextPage];
        private int WheelBranch(int slot)
        {
            if(contextPage!=0)return -1;
            if(contextSelection)return slot==3?1:slot==4?2:slot==5?3:-1;
            return slot==2?1:slot==3?2:slot==6?4:-1;
        }
        private static Rect WheelRect(Vector2 center,int slot)=>new Rect(center+WheelOffsets[slot]-new Vector2(56,15),new Vector2(112,30));
        private bool WheelEnabled(int slot)=>!string.IsNullOrEmpty(CurrentWheelLabels[slot]) &&
            (contextPage!=1 || slot==4 || slot==6 || (Active.faces.Count>0 && (slot<2 || selected.Count>0)));
        private void PositionWheel(SceneView canvas,Vector2 mouse)
        {
            var point=new Vector2(Mathf.Clamp(mouse.x,Mathf.Min(218,canvas.position.width/2),Mathf.Max(canvas.position.width-218,canvas.position.width/2)),
                Mathf.Clamp(mouse.y,Mathf.Min(148,canvas.position.height/2),Mathf.Max(canvas.position.height-120,canvas.position.height/2)));
            contextOrigin=GUIUtility.GUIToScreenPoint(point);contextHover=-1;
        }
        internal void BeforeRetopologySceneGUI(SceneView canvas)
        {
            if(HandleBrushAdjustment(canvas))return;
            var e=Event.current;int id=GUIUtility.GetControlID("Retopology wheel".GetHashCode(),FocusType.Passive);
            if(e.type==EventType.ContextClick&&suppressContext){suppressContext=false;e.Use();return;}
            if(!contextHeld)
            {
                if(Active==null||e.type!=EventType.MouseDown||e.button!=1||e.alt||GUIUtility.hotControl!=0)return;
                if(canvas is MashBoxStudioViewport viewport && (!viewport.ModelCanvasRect.Contains(e.mousePosition)||viewport.IsOverViewportControls(e.mousePosition)||viewport.IsOverSkinningPanels(e.mousePosition)))return;
                contextHeld=suppressContext=true;contextControl=id;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                EditorGUIUtility.editingTextField=false;contextPage=0;contextSelection=activeTool==RetopoTool.Select;contextPointer=GUIUtility.GUIToScreenPoint(e.mousePosition);
                PositionWheel(canvas,e.mousePosition);e.Use();repaint();return;
            }
            if(GUIUtility.hotControl!=contextControl||e.type==EventType.MouseLeaveWindow||e.type==EventType.Ignore)
            {CancelContextWheel();return;}
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){CancelContextWheel();e.Use();return;}
            if(e.type==EventType.MouseDrag||e.type==EventType.MouseMove||(e.type==EventType.MouseUp&&e.button==1))
            {
                contextPointer=GUIUtility.GUIToScreenPoint(e.mousePosition);contextHover=-1;
                for(int i=0;i<8;i++)if(WheelEnabled(i)&&WheelRect(contextOrigin,i).Contains(contextPointer))contextHover=i;
                int branchPage=WheelBranch(contextHover);bool branch=branchPage>=0;
                bool back=contextPage!=0&&contextHover==4;
                if((branch||back)&&e.type!=EventType.MouseUp)
                {contextPage=back?0:branchPage;PositionWheel(canvas,e.mousePosition);}
                else if(e.type==EventType.MouseUp)
                {int page=contextPage,item=contextHover;CancelContextWheel();if(item>=0&&!branch&&!back)Run(()=>ApplyWheel(page,item));}
                e.Use();repaint();return;
            }
            if(e.isMouse||e.isKey)e.Use();
        }
        private void CancelContextWheel()
        {
            if(contextHeld&&GUIUtility.hotControl==contextControl)GUIUtility.hotControl=0;
            contextHeld=false;contextHover=-1;repaint();
        }
        private void ApplyWheel(int page,int item)
        {
            if(page==0&&contextSelection)
            {
                if(item>=0&&item<=2)SetSelectionMode((SelectionMode)item);
                else if(item==6)DeleteComponents();else if(item==7)Frame();
            }
            else if(page==4){if(item>=0&&item<=2)SetSelectionMode((SelectionMode)item);}
            else if(page==3)
            {
                if(item==7)StartStrip();else if(item==2)StartTube();else if(item==3)StartResolution();else if(item==6)StartRelaxBrush();
                else if(item==5){ToggleContinuousPatch();activeTool=RetopoTool.Patch;}
                else if(item==0||item==1){activeTool=item==0?RetopoTool.Quad:RetopoTool.Patch;ClearPatchCorners();}
            }
            else if(page==0)
            {
                if(item==6){selectionMode=SelectionMode.Vertices;ClearComponents();}
                if(item==0||item==1||item==6){activeTool=item==0?RetopoTool.Quad:item==1?RetopoTool.Patch:RetopoTool.Select;ClearPatchCorners();}
                else if(item==4)Document.symmetry=!Document.symmetry;
                else if(item==5)Document.project=!Document.project;
                else if(item==7)StartTube();
            }
            else if(page==1)
            {if(item==0)Project();else if(item==1)Relax();else if(item==2)BridgeLoops();else if(item==3)DeleteComponents();else if(item==5)BridgePants();else if(item==6)StartTargetWeld();}
            else
            {
                if(item==0){referenceWire=!referenceWire;EditorPrefs.SetBool("MashBox.Retopology.ReferenceWire",referenceWire);}
                else if(item==1)wire=!wire;
                else if(item==2){Active.showReference=!Active.showReference;Rebuild();}
                else if(item==3){Active.showOutput=!Active.showOutput;Rebuild();}
                else if(item==5)ToggleWorkingXray();
                else if(item==6)ToggleXraySelection();
            }
            repaint();
        }
        internal void DrawContextWheel()
        {
            DrawBrushAdjustment();
            if(!contextHeld||Event.current.type!=EventType.Repaint)return;
            var center=GUIUtility.ScreenToGUIPoint(contextOrigin);var pointer=GUIUtility.ScreenToGUIPoint(contextPointer);
            var delta=pointer-center;var matrix=GUI.matrix;
            try{GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,center);
                EditorGUI.DrawRect(new Rect(center.x,center.y-1,delta.magnitude,2),StudioTheme.Current.Accent);}
            finally{GUI.matrix=matrix;}
            var label=new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter,fontSize=11};
            for(int i=0;i<8;i++)
            {
                if(string.IsNullOrEmpty(CurrentWheelLabels[i]))continue;
                var rect=WheelRect(center,i);bool enabled=WheelEnabled(i);
                EditorGUI.DrawRect(rect,i==contextHover?StudioTheme.Current.Hover:new Color(.035f,.04f,.04f,.97f));
                bool active=contextPage==0&&contextSelection?(i<3&&(int)selectionMode==i):contextPage==4?(i<3&&(int)selectionMode==i):contextPage==0?((i==0&&activeTool==RetopoTool.Quad)||(i==1&&activeTool==RetopoTool.Patch)||(i==6&&activeTool==RetopoTool.Select)||(i==4&&Document.symmetry)||(i==5&&Document.project)||(i==7&&activeTool==RetopoTool.Tube)):
                    contextPage==3?(i==5&&continuousPatch):contextPage==2&&((i==0&&referenceWire)||(i==1&&wire)||(i==2&&Active.showReference)||(i==3&&Active.showOutput)||(i==5&&workingXray)||(i==6&&xraySelection));
                if(active)EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-2,rect.width-10,2),StudioTheme.Current.Accent);
                label.normal.textColor=enabled?StudioTheme.Current.Text:StudioTheme.Current.Muted;
                GUI.Label(rect,CurrentWheelLabels[i],label);
            }
            GUI.Label(new Rect(center.x-56,center.y-12,112,24),contextPage==0?(contextSelection?"Selection":"Retopology"):contextPage==1?"Topology":contextPage==2?"Display":contextPage==3?"Draw":"Selection",new GUIStyle(StudioTheme.Styles.miniLabel){alignment=TextAnchor.MiddleCenter});
        }

        internal void DrawToolbar()
        {
            using(new GUILayout.HorizontalScope())
            {
                var spaceRect=GUILayoutUtility.GetRect(82,24,GUILayout.Width(82),GUILayout.Height(24));
                if(EditorGUI.DropdownButton(spaceRect,new GUIContent(local?"Local":"Global","Local uses the selected surface orientation"),FocusType.Passive,StudioTheme.Styles.menuDropdown))
                {var menu=new StudioMenu();menu.AddItem(new GUIContent("Local"),local,()=>{local=true;rotation=Quaternion.identity;repaint();});menu.AddItem(new GUIContent("Global"),!local,()=>{local=false;rotation=Quaternion.identity;repaint();});menu.DropDown(spaceRect);}
                GUILayout.Space(6);
                DrawSelectionMode();
                if(IconButton(12,xraySelection,"X-ray Selection: select through the mesh. Off selects visible components only; independent of X-ray display."))ToggleXraySelection();
                GUILayout.Space(6);
                if(IconButton(15,activeTool==RetopoTool.TargetWeld,"Target Weld: click the source vertex, then its destination. Escape cancels."))StartTargetWeld();
                ToolButton(RetopoTool.Quad,"Draw quad: click four corners. For a triangle, place three points then click the first point or press Enter. Snapped vertices are welded.");
                ToolButton(RetopoTool.Patch,"Draw patch: four corners generate a projected quad grid.");
                ToolButton(RetopoTool.Strip,"Quad Strip: drag connected quads on the reference. Resume near the yellow end; Escape starts a new strip. [ / ] adjusts width.");
                ToolButton(RetopoTool.Tube,"Tube: click start and end along an arm or leg, then Enter to create quad rings. Sides and length divisions are in Topology Tools.");
                ToolButton(RetopoTool.Resolution,"Resolution brush: paint to increase; Ctrl-paint to reduce clean quad blocks. [ / ] changes radius.");
                ToolButton(RetopoTool.RelaxBrush,"Relax brush: drag to smooth edge spacing on the reference. [ / ] changes radius; settings control strength and open-border protection.");
                GUILayout.Space(6);
                if(IconButton(3,wire,"Toggle working mesh polygon edges")){wire=!wire;repaint();}
                if(IconButton(5,referenceWire,"Toggle reference quad wireframe. Imported triangles are paired for display where possible; the reference mesh is unchanged."))
                {referenceWire=!referenceWire;EditorPrefs.SetBool("MashBox.Retopology.ReferenceWire",referenceWire);repaint();}
                if(IconButton(6,workingXray,"Shallow X-ray: reveal nearby patches through the reference. Adjust X-ray depth in Topology Tools to hide distant faces."))ToggleWorkingXray();
                if(IconButton(4,Document.symmetry,"Mirror drawing about local X=0"))Document.symmetry=!Document.symmetry;
                GUILayout.Space(6);GUILayout.Label(activeTool==RetopoTool.Select?"":activeTool==RetopoTool.TargetWeld?(weldSource<0?"Weld: source":"Weld: target"):activeTool==RetopoTool.Quad?"Draw Quad":activeTool==RetopoTool.Strip?"Quad Strip":activeTool==RetopoTool.Tube?"Tube":activeTool==RetopoTool.Resolution?"Resolution":activeTool==RetopoTool.RelaxBrush?"Relax":continuousPatch?"Patch +":"Quad Patch",StudioTheme.Styles.miniLabel,GUILayout.Height(24));
            }
        }
        private void ToolButton(RetopoTool value,string tooltip)
        {
            if(IconButton(value==RetopoTool.Strip?14:value==RetopoTool.RelaxBrush?13:value==RetopoTool.Resolution?11:value==RetopoTool.Tube?7:(int)value,activeTool==value,tooltip)){activeTool=value;ClearPatchCorners();repaint();}
        }
        private bool IconButton(int icon,bool active,string tooltip)
        {
            var rect=GUILayoutUtility.GetRect(24,24,GUILayout.Width(24),GUILayout.Height(24));
            bool clicked=GUI.Button(rect,new GUIContent("",tooltip),GUIStyle.none);
            if(Event.current.type==EventType.Repaint)
            {
                EditorGUI.DrawRect(rect,new Color(.035f,.04f,.04f));
                if(active)EditorGUI.DrawRect(new Rect(rect.x+4,rect.yMax-2,16,2),new Color(.5f,1,.87f));
                var previous=Handles.color;var matrix=Handles.matrix;Handles.matrix=Matrix4x4.identity;
                Handles.color=active?new Color(.5f,1,.87f):new Color(.75f,.81f,.81f);
                Vector3 P(float x,float y)=>new Vector3(rect.center.x+x,rect.center.y+y,0);
                if(icon==0)
                {Handles.DrawAAPolyLine(1.6f,P(-7,-8),P(-7,7),P(-3,3),P(1,9),P(4,7),P(0,1),P(6,1),P(-7,-8));}
                else if(icon==15)
                {Handles.DrawSolidDisc(P(-7,0),Vector3.forward,2);Handles.DrawWireDisc(P(7,0),Vector3.forward,3);Handles.DrawAAPolyLine(1.5f,P(-3,0),P(3,0),P(0,-3));Handles.DrawAAPolyLine(1.5f,P(3,0),P(0,3));}
                else if(icon==14)
                {
                    Handles.DrawAAPolyLine(1.5f,P(-9,6),P(-3,3),P(1,-3),P(6,-8));
                    Handles.DrawAAPolyLine(1.5f,P(-6,10),P(2,7),P(6,1),P(10,-4));
                    Handles.DrawAAPolyLine(1.5f,P(-9,6),P(-6,10));Handles.DrawAAPolyLine(1.5f,P(-3,3),P(2,7));
                    Handles.DrawAAPolyLine(1.5f,P(1,-3),P(6,1));Handles.DrawAAPolyLine(1.5f,P(6,-8),P(10,-4));
                }
                else if(icon==13)
                {
                    Handles.DrawAAPolyLine(1.5f,P(-8,4),P(-4,-3),P(0,0),P(4,3),P(8,-4));
                    foreach(var p in new[]{P(-8,4),P(0,0),P(8,-4)})Handles.DrawSolidDisc(p,Vector3.forward,1.7f);
                    Handles.DrawAAPolyLine(1f,P(-7,8),P(0,5),P(7,8));
                }
                else if(icon==12)
                {
                    Handles.DrawAAPolyLine(1.2f,P(-8,-8),P(3,-8),P(3,3),P(-8,3),P(-8,-8));
                    Handles.DrawDottedLine(P(-3,-3),P(8,-3),2);Handles.DrawDottedLine(P(8,-3),P(8,8),2);
                    Handles.DrawDottedLine(P(8,8),P(-3,8),2);Handles.DrawDottedLine(P(-3,8),P(-3,-3),2);
                }
                else if(icon==11)
                {
                    Handles.DrawAAPolyLine(1.3f,P(-8,-7),P(8,-7),P(8,7),P(-8,7),P(-8,-7));
                    Handles.DrawAAPolyLine(1.3f,P(0,-7),P(0,7));Handles.DrawAAPolyLine(1.3f,P(-8,0),P(8,0));
                    Handles.DrawAAPolyLine(1.3f,P(-4,-7),P(-4,0));Handles.DrawAAPolyLine(1.3f,P(-8,-3.5f),P(0,-3.5f));
                }
                else if(icon>=8&&icon<=10)
                {
                    if(icon==10){var fill=Handles.color;Handles.color=new Color(fill.r,fill.g,fill.b,.35f);Handles.DrawAAConvexPolygon(P(-6,-6),P(6,-6),P(6,6),P(-6,6));Handles.color=fill;}
                    Handles.DrawAAPolyLine(icon==9?2.4f:1f,P(-7,-7),P(7,-7),P(7,7),P(-7,7),P(-7,-7));
                    if(icon==8)foreach(var point in new[]{P(-7,-7),P(7,-7),P(7,7),P(-7,7)})Handles.DrawSolidDisc(point,Vector3.forward,2.2f);
                }
                else if(icon==7)
                {
                    var top=new Vector3[25];var bottom=new Vector3[25];
                    for(int i=0;i<=24;i++){float a=i*Mathf.PI/12;top[i]=P(Mathf.Cos(a)*7,-6+Mathf.Sin(a)*3);bottom[i]=P(Mathf.Cos(a)*7,6+Mathf.Sin(a)*3);}
                    Handles.DrawAAPolyLine(1.3f,top);Handles.DrawAAPolyLine(1.3f,bottom);
                    Handles.DrawAAPolyLine(1.3f,P(-7,-6),P(-7,6));Handles.DrawAAPolyLine(1.3f,P(7,-6),P(7,6));
                }
                else if(icon==4)
                {Handles.DrawAAPolyLine(1.3f,P(0,-9),P(0,9));Handles.DrawAAPolyLine(1.3f,P(-3,-6),P(-9,0),P(-3,6),P(-3,-6));Handles.DrawAAPolyLine(1.3f,P(3,-6),P(9,0),P(3,6),P(3,-6));}
                else if(icon==6)
                {
                    Handles.DrawAAPolyLine(1.3f,P(-9,0),P(-5,-5),P(0,-7),P(5,-5),P(9,0),P(5,5),P(0,7),P(-5,5),P(-9,0));
                    Handles.DrawWireDisc(P(0,0),Vector3.forward,3);
                }
                else if(icon==5)
                {
                    // An offset backing sheet distinguishes the reference from working topology.
                    Handles.DrawAAPolyLine(1.2f,P(-9,5),P(-9,-9),P(5,-9));
                    Handles.DrawAAPolyLine(1.4f,P(-5,-5),P(9,-5),P(9,9),P(-5,9),P(-5,-5));
                    Handles.DrawAAPolyLine(1.2f,P(2,-5),P(2,9));Handles.DrawAAPolyLine(1.2f,P(-5,2),P(9,2));
                }
                else
                {
                    Handles.DrawAAPolyLine(1.4f,P(-8,-7),P(8,-7),P(8,7),P(-8,7),P(-8,-7));
                    if(icon>=2){Handles.DrawAAPolyLine(1.2f,P(0,-7),P(0,7));Handles.DrawAAPolyLine(1.2f,P(-8,0),P(8,0));}
                    if(icon==1)foreach(var p in new[]{P(-8,-7),P(8,-7),P(8,7),P(-8,7)})Handles.DrawSolidDisc(p,Vector3.forward,1.6f);
                    if(icon==3)Handles.DrawAAPolyLine(1.2f,P(-8,-7),P(8,7));
                }
                Handles.color=previous;Handles.matrix=matrix;
            }
            GUILayout.Space(4);return clicked;
        }
        private void ToggleWorkingXray()
        {
            workingXray=!workingXray;EditorPrefs.SetBool("MashBox.Retopology.WorkingXray",workingXray);repaint();
        }
        private void DrawWorkingXray()
        {
            if(!workingXray||!Active.showOutput||Document.selected<0||Document.selected>=display.Count)return;
            var mesh=display[Document.selected];
            if(!mesh||mesh.vertexCount==0)return;
            if(!workingXrayMaterial)
            {
                var shader=Shader.Find("Hidden/MashBox/RetopologyXray");
                if(!shader)return;
                workingXrayMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};

            }
            var bounds=Active.reference?Active.reference.bounds:mesh.bounds;
            float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
            workingXrayMaterial.SetFloat("_Depth",size*xrayDepthPercent*.01f);
            workingXrayMaterial.SetFloat("_Near",view.camera.nearClipPlane);
            workingXrayMaterial.SetColor("_Color",new Color(.35f,1f,.8f,.14f));
            // Reuse the preview mesh; never offset or modify the authored geometry.
            for(int sub=0;sub<mesh.subMeshCount;sub++)
                if(mesh.GetIndexCount(sub)>0&&workingXrayMaterial.SetPass(0))Graphics.DrawMeshNow(mesh,Matrix4x4.identity,sub);
            if(wire&&Document.selected<xrayLines.Count)
            {
                var lines=xrayLines[Document.selected];
                workingXrayMaterial.SetColor("_Color",new Color(.35f,1f,.8f,.45f));
                if(lines&&lines.GetIndexCount(0)>0&&workingXrayMaterial.SetPass(0))Graphics.DrawMeshNow(lines,Matrix4x4.identity);
            }
        }
        internal void SceneGUI(MashBoxStudioViewport canvas)
        {
            view=canvas;var evt=Event.current;
            lighting.ConfigureCamera(canvas);StudioPreviewLighting.ConfigureStudioBackground(canvas);
            if(canvas.modelGridVisible)canvas.DrawGroundGrid();
            bool overUI=canvas.IsOverSkinningPanels(evt.mousePosition);
            if(!overUI)HandleDrop(evt);
            canvas.wantsMouseMove=true;
            if(evt.type==EventType.MouseMove)canvas.Repaint();
            if(Active==null)
            {
                if(!overUI&&!contextHeld&&!adjustingRetopoBrush&&StudioBrushCursor.InViewport(canvas,evt.mousePosition)&&evt.type==EventType.Repaint)
                {
                    if(activeTool==RetopoTool.Strip)
                        StudioBrushCursor.Draw(canvas,evt.mousePosition,stripWidth*.5f,false,default,default,Color.white,ref stripCursorDepth);
                    else if(activeTool==RetopoTool.RelaxBrush||activeTool==RetopoTool.Resolution)
                    {
                        var old=Handles.color;Handles.BeginGUI();
                        try{Handles.color=StudioBrushCursor.Inactive;Handles.DrawWireDisc(evt.mousePosition,Vector3.forward,activeTool==RetopoTool.RelaxBrush?relaxRadius:resolutionRadius);}
                        finally{Handles.color=old;Handles.EndGUI();}
                    }
                }
                return;
            }
            if(referenceWire&&Active.showReference&&Active.reference&&evt.type==EventType.Repaint)
            {
                if(!referenceWireLines.TryGetValue(Active.reference,out var lines))
                {lines=StudioQuadWire.Lines(Active.reference);referenceWireLines.Add(Active.reference,lines);}
                var oldDepth=Handles.zTest;
                try
                {
                    Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;
                    using(new Handles.DrawingScope(new Color(.68f,.79f,.86f,.85f),references[Document.selected].transform.localToWorldMatrix))
                        Handles.DrawLines(lines);
                }
                finally {Handles.zTest=oldDepth;}
            }
            if(evt.type==EventType.Repaint)DrawWorkingXray();
            if((wire||(activeTool==RetopoTool.Select&&selectionMode==SelectionMode.Edges))&&Active.showOutput&&evt.type==EventType.Repaint)
            {
                var old=Handles.color;var oldDepth=Handles.zTest;
                try
                {
                    foreach(var f in Active.faces)
                    {
                        var points=new Vector3[f.vertices.Length+1];for(int i=0;i<f.vertices.Length;i++)points[i]=Active.vertices[f.vertices[i]];points[points.Length-1]=points[0];
                        Handles.zTest=UnityEngine.Rendering.CompareFunction.LessEqual;
                        Handles.color=new Color(.35f,1,.8f,.85f);
                        Handles.DrawAAPolyLine(1.4f,points);
                    }
                }
                finally {Handles.color=old;Handles.zTest=oldDepth;}
            }
            if(HandleTargetWeld(evt,canvas,overUI||canvas.IsOverViewportControls(evt.mousePosition)))return;
            if(HandleStrip(evt,canvas,overUI||canvas.IsOverViewportControls(evt.mousePosition)))return;
            if(HandleRelaxBrush(evt,canvas,overUI||canvas.IsOverViewportControls(evt.mousePosition)))return;
            if(HandleResolution(evt,canvas,overUI||canvas.IsOverViewportControls(evt.mousePosition)))return;
            if(activeTool==RetopoTool.Select&&evt.type==EventType.MouseMove)canvas.Repaint();
            if(evt.type==EventType.Repaint)DrawComponentSelection(!overUI&&!canvas.IsOverViewportControls(evt.mousePosition)&&canvas.ModelCanvasRect.Contains(evt.mousePosition)&&!contextHeld&&!evt.alt&&GUIUtility.hotControl==0&&!selectingVertices);
            // Finish captured drags even when the pointer crosses a side panel.
            if(HandleVertexSelection(evt))return;
            if(overUI||canvas.IsOverViewportControls(evt.mousePosition))return;
            int id=GUIUtility.GetControlID("Retopology edit".GetHashCode(),FocusType.Passive);
            if(evt.type==EventType.Layout&&!evt.alt)HandleUtility.AddDefaultControl(id);
            if(evt.type==EventType.KeyDown&&!EditorGUIUtility.editingTextField&&!evt.alt)
            {
                if(evt.keyCode==KeyCode.Escape){ClearPatchCorners();ClearComponents();evt.Use();}
                else if(activeTool==RetopoTool.Quad&&corners.Count==3&&(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter)){Run(CreateDrawTriangle);evt.Use();}
                else if(activeTool==RetopoTool.Tube&&(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter)){Run(CreateTube);evt.Use();}
                else if(evt.keyCode==KeyCode.F){Frame();evt.Use();}
                else if((evt.control||evt.command)&&evt.keyCode==KeyCode.S){Run(Document.SaveWorkspace);evt.Use();}
                else if((evt.control||evt.command)&&evt.keyCode==KeyCode.A){SelectAllComponents();evt.Use();}
                else if(evt.keyCode==KeyCode.W||evt.keyCode==KeyCode.E||evt.keyCode==KeyCode.R||evt.keyCode==KeyCode.Q)
                {activeTool=RetopoTool.Select;transformTool=evt.keyCode==KeyCode.E?Tool.Rotate:evt.keyCode==KeyCode.R?Tool.Scale:evt.keyCode==KeyCode.Q?Tool.None:Tool.Move;rotation=Quaternion.identity;scale=Vector3.one;evt.Use();}
                else if(evt.keyCode==KeyCode.Delete||evt.keyCode==KeyCode.Backspace){Run(DeleteComponents);evt.Use();}
            }
            if(contextHeld)return;
            // Resolve a double-click on geometry before Unity's transform handles
            // consume it. A normal press/drag still belongs to the gizmo.
            if(activeTool==RetopoTool.Select && Active.showOutput &&
                evt.type==EventType.MouseDown && evt.button==0 && evt.clickCount>=2 &&
                !evt.alt && GUIUtility.hotControl==0 && canvas.ModelCanvasRect.Contains(evt.mousePosition))
            {
                bool hit=selectionMode==SelectionMode.Faces ? PickFace(evt.mousePosition)>=0 :
                    selectionMode==SelectionMode.Edges ? PickEdge(evt.mousePosition,StudioRetopologyOperations.Edges(Active).Keys).Item1>=0 :
                    PickVertex(evt.mousePosition)>=0;
                if(hit)
                {
                    selectingVertices=true;selectionControl=id;GUIUtility.hotControl=id;
                    GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;
                    selectionStart=selectionEnd=evt.mousePosition;selectionClicks=evt.clickCount;
                    selectionAdd=evt.shift;selectionSubtract=evt.control||evt.command;
                    evt.Use();canvas.Repaint();return;
                }
            }
            if(activeTool==RetopoTool.Select)DrawTransform();
            if(evt.type==EventType.MouseDown&&evt.button==0&&!evt.alt&&GUIUtility.hotControl==0)
            {
                if(activeTool==RetopoTool.Select)
                {
                    if(!Active.showOutput||!canvas.ModelCanvasRect.Contains(evt.mousePosition)||HandleUtility.nearestControl!=id)return;
                    selectingVertices=true;selectionControl=id;GUIUtility.hotControl=id;
                    GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;
                    selectionStart=selectionEnd=evt.mousePosition;selectionClicks=evt.clickCount;
                    selectionAdd=evt.shift;selectionSubtract=evt.control||evt.command;
                    evt.Use();canvas.Repaint();
                }
                else if(activeTool==RetopoTool.Tube)
                {
                    if(ReferenceHit(evt.mousePosition,out var tubeHit))
                    {
                        if(corners.Count>=2){ClearPatchCorners();tubePreview=null;}
                        corners.Add(tubeHit.point);if(corners.Count==2)Run(UpdateTubePreview);
                        evt.Use();canvas.Repaint();
                    }
                }
                else if(activeTool!=RetopoTool.Resolution&&activeTool!=RetopoTool.RelaxBrush&&ReferenceHit(evt.mousePosition,out var hit))
                {
                    int snap=PickVertex(evt.mousePosition,24f);
                    var point=snap>=0?Active.vertices[snap]:hit.point+hit.normal*Document.offset;
                    if(activeTool==RetopoTool.Quad&&corners.Count==3&&Vector2.Distance(HandleUtility.WorldToGUIPoint(corners[0]),evt.mousePosition)<=24f)
                    {Run(CreateDrawTriangle);evt.Use();canvas.Repaint();return;}
                    if(!corners.Contains(point))corners.Add(point);
                    cornerNormal=hit.normal;
                    if(corners.Count==4)Run(CreatePatch);
                    evt.Use();canvas.Repaint();
                }
            }
            if(activeTool!=RetopoTool.Select&&activeTool!=RetopoTool.Resolution&&activeTool!=RetopoTool.RelaxBrush&&!evt.alt)
            {
                if(evt.type==EventType.MouseMove)canvas.Repaint();
                if(evt.type==EventType.Repaint){if(activeTool==RetopoTool.Tube)DrawTubePreview(evt.mousePosition);else DrawQuadSnapPreview(evt.mousePosition);}
            }
            if(dragUndo>=0 && (evt.rawType==EventType.MouseUp||GUIUtility.hotControl==0))
            {Undo.CollapseUndoOperations(dragUndo);dragUndo=-1;Rebuild();}
        }
        private void DrawQuadSnapPreview(Vector2 mouse)
        {
            bool hitSurface=ReferenceHit(mouse,out var hit);
            int snap=hitSurface?PickVertex(mouse,24f):-1;
            Vector3 point=snap>=0?Active.vertices[snap]:hit.point+hit.normal*Document.offset;
            var previous=Handles.color;var depth=Handles.zTest;
            try
            {
                Handles.zTest=UnityEngine.Rendering.CompareFunction.Always;
                Handles.color=new Color(.5f,1f,.87f,.9f);
                if(corners.Count>1)Handles.DrawAAPolyLine(2,corners.ToArray());
                if(activeTool==RetopoTool.Quad&&corners.Count==3)
                {
                    bool close=Vector2.Distance(HandleUtility.WorldToGUIPoint(corners[0]),mouse)<=24f;
                    Handles.color=close?Color.yellow:new Color(.5f,1f,.87f,.5f);
                    Handles.DrawDottedLine(corners[2],corners[0],3);
                    if(close){Handles.color=new Color(1,.8f,.15f,.2f);Handles.DrawAAConvexPolygon(corners.ToArray());}
                    Handles.Label(corners[0],"Triangle: click first point or Enter");
                }
                if(activeTool==RetopoTool.Patch&&patchContinuation!=null&&patchOwner==Active)
                {
                    Handles.color=new Color(1,.8f,.15f);
                    Handles.DrawAAPolyLine(4,patchContinuation.Select(i=>Active.vertices[i]).ToArray());
                }
                if(corners.Count>0&&hitSurface)Handles.DrawDottedLine(corners[corners.Count-1],point,3);
                Handles.BeginGUI();
                try
                {
                    void Dot(Vector2 center,float radius,Color color)
                    {
                        Handles.color=new Color(.025f,.04f,.04f,.95f);
                        Handles.DrawSolidDisc(center,Vector3.forward,radius+1.5f);
                        Handles.color=color;Handles.DrawSolidDisc(center,Vector3.forward,radius);
                    }
                    // Snap dots use the same shallow depth checks as picking; never reveal far-side targets.
                    if(Active.showOutput)
                        for(int i=0;i<Active.vertices.Count;i++)
                        {
                            var vertex=Active.vertices[i];
                            if(view.camera.WorldToViewportPoint(vertex).z<=0)continue;
                            var screen=HandleUtility.WorldToGUIPoint(vertex);
                            if(i!=snap&&(screen-mouse).sqrMagnitude<80*80&&IsSelectionPointVisible(vertex,false))Dot(screen,2.5f,new Color(.48f,.67f,.82f,.8f));
                        }
                    foreach(var corner in corners)Dot(HandleUtility.WorldToGUIPoint(corner),4f,new Color(.4f,1f,.8f));
                    if(snap>=0)
                    {
                        var screen=HandleUtility.WorldToGUIPoint(point);
                        bool used=corners.Contains(point);
                        var color=used?new Color(1f,.35f,.25f):new Color(1f,.82f,.12f);
                        Dot(screen,6f,color);
                        Handles.color=new Color(.025f,.04f,.04f,.95f);Handles.DrawWireDisc(screen,Vector3.forward,11);
                        Handles.color=color;Handles.DrawWireDisc(screen,Vector3.forward,10);
                        var labelRect=new Rect(screen.x+15,screen.y-24,104,19);
                        EditorGUI.DrawRect(labelRect,new Color(.025f,.035f,.04f,.94f));
                        var labelStyle=new GUIStyle(EditorStyles.miniBoldLabel){alignment=TextAnchor.MiddleCenter};
                        labelStyle.normal.textColor=color;
                        GUI.Label(labelRect,used?"Already placed":"Snap to vertex",labelStyle);
                    }
                }
                finally {Handles.EndGUI();}
            }
            finally {Handles.color=previous;Handles.zTest=depth;}
        }
        private int PickVertex(Vector2 mouse,float radius=12f)
        {
            if(!Active.showOutput)return -1;
            int best=-1;float distance=radius*radius;
            for(int i=0;i<Active.vertices.Count;i++)
            {
                if(view.camera.WorldToViewportPoint(Active.vertices[i]).z<=0)continue;
                float d=(HandleUtility.WorldToGUIPoint(Active.vertices[i])-mouse).sqrMagnitude;
                if(d<distance&&IsSelectionPointVisible(Active.vertices[i],activeTool==RetopoTool.Select)){best=i;distance=d;}
            }
            return best;
        }
        private bool ReferenceHit(Vector2 mouse,out RaycastHit hit)
        {
            hit=default;if(Active==null||!Active.reference||!Active.showReference)return false;
            return references[Document.selected].GetComponent<MeshCollider>().Raycast(HandleUtility.GUIPointToWorldRay(mouse),out hit,float.MaxValue);
        }
        private void DrawTransform()
        {
            selected.RemoveWhere(i=>i<0||i>=Active.vertices.Count);
            if(selected.Count==0||transformTool==Tool.None||!Active.showOutput)return;
            Vector3 center=Vector3.zero;foreach(int i in selected)center+=Active.vertices[i];center/=selected.Count;
            var orientation=Quaternion.identity;
            if(local)
            {
                Vector3 normal=Vector3.zero;var normals=display[Document.selected].normals;
                foreach(int i in selected)if(i<normals.Length)normal+=normals[i];
                if(normal.sqrMagnitude>1e-8f)orientation=Quaternion.FromToRotation(Vector3.forward,normal.normalized);
            }
            EditorGUI.BeginChangeCheck();Vector3 moved=center;Quaternion turn=rotation;Vector3 resized=scale;
            if(transformTool==Tool.Rotate)turn=Handles.RotationHandle(rotation*orientation,center)*Quaternion.Inverse(orientation);
            else if(transformTool==Tool.Scale)
            {
                resized=Handles.ScaleHandle(scale,center,orientation,HandleUtility.GetHandleSize(center));
                // Keep every axis positive, including uniform scale, to prevent
                // winding inversions and zero-scale collapse while dragging.
                resized=new Vector3(Mathf.Max(.001f,resized.x),Mathf.Max(.001f,resized.y),Mathf.Max(.001f,resized.z));
            }
            else moved=Handles.PositionHandle(center,orientation);
            if(!EditorGUI.EndChangeCheck())return;
            if(dragUndo<0){Undo.IncrementCurrentGroup();dragUndo=Undo.GetCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Edit retopology vertices");}
            foreach(int i in selected)
            {
                var p=Active.vertices[i];
                if(transformTool==Tool.Rotate)p=center+(turn*Quaternion.Inverse(rotation))*(p-center);
                else if(transformTool==Tool.Scale)p=center+orientation*Vector3.Scale(Quaternion.Inverse(orientation)*(p-center),new Vector3(SafeRatio(resized.x,scale.x),SafeRatio(resized.y,scale.y),SafeRatio(resized.z,scale.z)));
                else p+=moved-center;
                Active.vertices[i]=Document.project?Surface.Project(p,Document.offset):p;
            }
            rotation=turn;scale=resized;Document.dirty=true;EditorUtility.SetDirty(Document);
            var mesh=display[Document.selected];xrayLines[Document.selected].SetVertices(Active.vertices);mesh.SetVertices(Active.vertices);mesh.RecalculateNormals();mesh.RecalculateBounds();repaint();
        }
        private static float SafeRatio(float a,float b)=>Mathf.Abs(b)<1e-5f?1:a/b;
        private void CreatePatch()
        {
            var points=corners.ToArray();int divisions=activeTool==RetopoTool.Quad?1:patchSegments;
            bool chain=continuousPatch&&activeTool==RetopoTool.Patch;
            var seam=chain&&patchOwner==Active?patchContinuation:null;
            int columns=seam!=null?seam.Length-1:divisions,rows=divisions;
            if(activeTool==RetopoTool.Patch&&patchAutoDensity)
            {
                var grid=PatchGridSize(points,PatchQuadSize,seam);
                columns=grid.x;rows=grid.y;
            }
            var mirrorSeam=seam!=null?mirroredPatchContinuation:null;
            bool reverse=Vector3.Dot(Vector3.Cross(points[1]-points[0],points[3]-points[0]),cornerNormal)<0;
            int[] end=null,mirrorEnd=null;
            Change("Draw projected quad patch",()=>
            {
                end=AddPatch(points,columns,rows,reverse,seam);
                if(Document.symmetry)mirrorEnd=AddPatch(points.Select(p=>new Vector3(-p.x,p.y,p.z)).ToArray(),columns,rows,!reverse,mirrorSeam);
                Active.needsUV=true;
            });
            status="Patch created. Existing borders reuse their full vertex chains and matching division counts.";
            if(chain)
            {
                patchContinuation=end;mirroredPatchContinuation=mirrorEnd;patchOwner=Active;
                corners.Add(Active.vertices[end[0]]);corners.Add(Active.vertices[end[end.Length-1]]);
                status="Continue patch: place the next corner beyond the highlighted edge's end, then the far corner. Escape starts a fresh patch.";
            }
        }
        internal static Vector2Int PatchGridSize(Vector3[] points,float target,int[] seam)
        {
            float width=(Vector3.Distance(points[0],points[1])+Vector3.Distance(points[3],points[2]))*.5f;
            float length=(Vector3.Distance(points[0],points[3])+Vector3.Distance(points[1],points[2]))*.5f;
            target=Mathf.Max(.0001f,target);
            return new Vector2Int(seam!=null?seam.Length-1:Mathf.Clamp(Mathf.CeilToInt(width/target),1,96),Mathf.Clamp(Mathf.CeilToInt(length/target),1,96));
        }
        private int[] AddPatch(Vector3[] points,int columns,int rows,bool reverse,int[] seam=null)
        {
            float tolerance=Mathf.Max(1e-6f,Active.reference.bounds.size.magnitude*1e-5f);
            var sides=activeTool==RetopoTool.Patch?StudioRetopologyPatchBorders.Find(Active,points,tolerance):new int[4][];
            if(seam!=null)sides[0]=seam;
            StudioRetopologyPatchBorders.Match(Active,sides,ref columns,ref rows,ref reverse);
            var grid=new int[columns+1,rows+1];
            var projectionAxis=Vector3.Cross(points[1]-points[0],points[3]-points[0])+Vector3.Cross(points[2]-points[1],points[0]-points[1]);
            if(projectionAxis.sqrMagnitude<1e-12f)projectionAxis=cornerNormal;
            float cellWidth=(Vector3.Distance(points[0],points[1])+Vector3.Distance(points[3],points[2]))/(2*columns);
            float cellHeight=(Vector3.Distance(points[0],points[3])+Vector3.Distance(points[1],points[2]))/(2*rows);
            float projectionSpan=Mathf.Min(cellWidth,cellHeight);
            Vector3 Boundary(int side,int index,int count)
            {
                if(sides[side]!=null)return Active.vertices[sides[side][index]];
                return Vector3.Lerp(points[side],points[(side+1)%4],(float)index/count);
            }
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            {
                // Reuse the complete projected boundary, not just its two endpoints.
                if(y==0&&sides[0]!=null){grid[x,y]=sides[0][x];continue;}
                if(x==columns&&sides[1]!=null){grid[x,y]=sides[1][y];continue;}
                if(y==rows&&sides[2]!=null){grid[x,y]=sides[2][columns-x];continue;}
                if(x==0&&sides[3]!=null){grid[x,y]=sides[3][rows-y];continue;}
                float u=(float)x/columns,v=(float)y/rows;
                var bilinear=Vector3.Lerp(Vector3.Lerp(points[0],points[1],u),Vector3.Lerp(points[3],points[2],u),v);
                // Coons interpolation carries curved/nonuniform shared borders into
                // the interior instead of abruptly meeting a straight uniform grid.
                var p=Vector3.Lerp(Boundary(0,x,columns),Boundary(2,columns-x,columns),v)
                    +Vector3.Lerp(Boundary(3,rows-y,rows),Boundary(1,y,rows),u)-bilinear;
                int existing=Active.vertices.FindIndex(a=>(a-p).sqrMagnitude<tolerance*tolerance);
                if(existing<0)
                {
                    if(Document.project&&!((x==0||x==columns)&&(y==0||y==rows)))p=Surface.ProjectAlong(p,projectionAxis,Document.offset,projectionSpan);
                    existing=Active.vertices.FindIndex(a=>(a-p).sqrMagnitude<tolerance*tolerance);
                }
                if(existing<0){existing=Active.vertices.Count;Active.vertices.Add(p);Active.uv.Add(new Vector2(u,v));}
                grid[x,y]=existing;
            }
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
            {
                int[] ids={grid[x,y],grid[x+1,y],grid[x+1,y+1],grid[x,y+1]};if(reverse)System.Array.Reverse(ids);
                if(ids.Distinct().Count()<4)continue;
                if(Active.faces.Any(f=>f.vertices.Length==4&&f.vertices.OrderBy(i=>i).SequenceEqual(ids.OrderBy(i=>i))))continue;
                Active.faces.Add(new Face(ids));
            }
            return Enumerable.Range(0,columns+1).Select(x=>grid[x,rows]).ToArray();
        }
        private void DeleteSelection()
        {
            if(selected.Count==0)return;
            Change("Delete topology faces",()=>{Active.faces.RemoveAll(f=>f.vertices.Any(selected.Contains));StudioRetopologyOperations.Compact(Active);});
        }
    }
}
