using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioModelEditor
    {
        internal enum MarkingLevel { Components, Transform, Pivot, Selection, UV, UVSets, AxisSpace }
        internal enum MarkingAction
        {
            None, Object, Vertex, Edge, Face, TransformMenu, PivotMenu, SelectionMenu, UVMenu, UVSetsMenu,
            Move, Rotate, Scale, Axes, EditPivot, CenterPivot, Freeze, SelectAll, Clear, Frame, Through,
            OpenUV, UV0, UV1, UV2, UV3, UV4, UV5, UV6, UV7, ObjectAxes, WorldAxes, ComponentAxes
        }
        private bool markingOpen, suppressModelContext;
        private SceneView markingView;
        private int markingControl, markingButton, markingHover=-1;
        private KeyCode markingKey;
        private double radialHoverSince;
        private const double RadialSubmenuDelay = .25;
        private Vector2 markingPointer, markingOrigin, markingAnchor, radialLastPointer;
        private bool radialHasPointer, radialBackArmed;
        private StudioCustomToolbarDefinition radialPreset;
        private List<StudioCustomToolbarDefinition> radialDefinitions;
        private List<ToolbarCommand> radialCommands;
        private readonly List<string> radialParents=new List<string>();
        private Rect[] radialRects=Array.Empty<Rect>();
        private string[] radialVisible=Array.Empty<string>();
        private GUIStyle markingLabel;
        private Rect radialBack;
        // Retained as a topology-free compatibility helper; live picking uses Hit only.
        internal static int MarkingSector(Vector2 delta) => -1;
        private bool MarkingActionEnabled(MarkingAction action)
        {
            if (action == MarkingAction.None) return false;
            if (action == MarkingAction.ObjectAxes || action == MarkingAction.WorldAxes || action == MarkingAction.ComponentAxes) return true;
            if (action == MarkingAction.Object || action == MarkingAction.TransformMenu || action == MarkingAction.SelectionMenu ||
                action == MarkingAction.Move || action == MarkingAction.Rotate || action == MarkingAction.Scale || action == MarkingAction.Axes || action == MarkingAction.Through) return true;
            if (action == MarkingAction.Vertex || action == MarkingAction.Edge || action == MarkingAction.Face)
                return true;
            if (action == MarkingAction.UVMenu || action == MarkingAction.UVSetsMenu || action == MarkingAction.OpenUV || (action >= MarkingAction.UV0 && action <= MarkingAction.UV7))
                return true;
            if (action == MarkingAction.EditPivot && CenterMode) return true;
            if (action == MarkingAction.PivotMenu || action == MarkingAction.EditPivot || action == MarkingAction.CenterPivot)
                return CanAdjustPivot;
            if (action == MarkingAction.Freeze)
                return Document && Active >= 0 && mode == ComponentMode.Object && !Document.IsBone(Active);
            return Document && (mode == ComponentMode.Object || ActiveMesh);
        }
        private string MarkingText(MarkingAction action)
        {
            switch (action)
            {
                case MarkingAction.Object: return "Object Mode";
                case MarkingAction.ObjectAxes: return "Object";
                case MarkingAction.ComponentAxes: return "Component";
                case MarkingAction.WorldAxes: return "World";
                case MarkingAction.TransformMenu: return "Transform ›";
                case MarkingAction.PivotMenu: return "Pivot ›";
                case MarkingAction.SelectionMenu: return "Selection ›";
                case MarkingAction.UVMenu: return "UV ›";
                case MarkingAction.UVSetsMenu: return "UV Sets ›";
                case MarkingAction.EditPivot: return pivot ? "Finish Pivot Edit" : "Edit Pivot";
                case MarkingAction.CenterPivot: return "Center Pivot";
                case MarkingAction.Freeze: return "Freeze Transforms";
                case MarkingAction.SelectAll: return "Select All";
                case MarkingAction.Clear: return "Deselect";
                case MarkingAction.Axes: return local ? "World Axes" : "Object Axes";
                case MarkingAction.Through: return xray ? "Visible Only" : "Through Selection";
                case MarkingAction.OpenUV: return "UV Editor";
                default: return action >= MarkingAction.UV0 && action <= MarkingAction.UV7 ? "UV " + (action - MarkingAction.UV0) : action.ToString();
            }
        }
        private bool MarkingIsCurrent(MarkingAction action) =>
            (action == MarkingAction.Object && mode == ComponentMode.Object) ||
            (action == MarkingAction.Vertex && mode == ComponentMode.Vertex) ||
            (action == MarkingAction.Edge && mode == ComponentMode.Edge) ||
            (action == MarkingAction.Face && mode == ComponentMode.Face) ||
            (action == MarkingAction.Move && tool == Tool.Move) ||
            (action == MarkingAction.Rotate && tool == Tool.Rotate) ||
            (action == MarkingAction.Scale && tool == Tool.Scale) ||
            (action == MarkingAction.ObjectAxes && local && !componentAxes) ||
            (action == MarkingAction.ComponentAxes && ToolbarComponentAxes) || (action == MarkingAction.WorldAxes && !local);

        // Called before SceneView gets RMB, so its fly camera and native context menu
        // cannot claim the gesture. Alt navigation and existing handle drags pass through.
        internal List<ToolbarCommand> RadialCommands(SceneView view)
        {
            var result=ToolbarCommands(view as MashBoxStudioViewport);
            result.Add(new ToolbarCommand { Key = StudioRadialMenus.MaterialAssignBranch, Path = "Material/Assign Selection", Label = "Assign Material ›", Enabled = CanAssignMaterialSlot });
            result.Add(new ToolbarCommand{Key=StudioRadialMenus.SpacerKey,Path="Layout/Empty Spacer",Label="Empty Spacer",Enabled=false});
            result.Add(new ToolbarCommand{Key="UV/Select UVs",Path="UV/Select UVs",Label="Select UVs",Enabled=false});
            result.Add(new ToolbarCommand{Key="UV/Select UV Shells",Path="UV/Select UV Shells",Label="Select UV Shells",Enabled=false});
            foreach(MarkingAction action in Enum.GetValues(typeof(MarkingAction)))
            {
                if(action!=MarkingAction.Axes&&action!=MarkingAction.Clear&&action!=MarkingAction.Through&&!(action>=MarkingAction.UV0&&action<=MarkingAction.ComponentAxes))continue;
                var captured=action;
                result.Add(new ToolbarCommand{Key="Legacy/"+action,Path="Radial Actions/"+MarkingText(action),Label=MarkingText(action),Enabled=MarkingActionEnabled(action),Checked=MarkingIsCurrent(action),Execute=()=>Run(()=>ExecuteMarkingAction(captured,view))});
            }
            foreach(var preset in StudioRadialMenus.Read())
                result.Add(new ToolbarCommand{Key=StudioRadialMenus.Branch(preset.id),Path="Radial Menus/"+preset.name,Label=preset.name+" ›",Enabled=true});
            return result;
        }
        private ToolbarCommand RadialCommand(string key)=>radialCommands?.Find(x=>x.Key==key);
        private string RadialLabel(string key)=>RadialCommand(key)?.Label??key.Substring(key.LastIndexOf('/')+1);
        internal void BeforeModelSceneGUI(SceneView view)
        {
            var evt=Event.current;
            view.wantsMouseEnterLeaveWindow=true;
            if(evt.isMouse){radialLastPointer=evt.mousePosition;radialHasPointer=true;}
            int id=GUIUtility.GetControlID("MashBoxStudioMarkingMenu".GetHashCode(),FocusType.Passive);
            if(markingOpen){HandleModelMarkingEvent(view,evt,id);return;}
            if(HandleLoopCountWheel(view,evt)||BeforeSelectionPaintInput(view)||CancelMarqueeOutside(view))return;
            if(evt.rawType==EventType.MouseUp&&evt.button==0&&gestureUndo>=0){Undo.CollapseUndoOperations(gestureUndo);gestureUndo=-1;}
            // Preserve RMB Apply Cut; other assigned presets remain available.
            if(cutTool!=CutTool.None&&evt.type==EventType.MouseDown&&StudioShortcuts.Matches(StudioShortcut.MarkingMenu,evt))
            {CommitPendingCutGesture();suppressModelContext=true;evt.Use();repaint();return;}
            HandleModelMarkingEvent(view,evt,id);
            if(evt.type!=EventType.Used)HandleModelKeyboard(view);
        }
        private bool TryOpenRadial(SceneView view,Event evt,int id)
        {
            if((evt.type!=EventType.MouseDown&&evt.type!=EventType.KeyDown)||GUIUtility.hotControl!=0||EditorGUIUtility.editingTextField)return false;
            var definitions=StudioRadialMenus.Read();
            var preset=definitions.Find(x=>StudioShortcuts.Matches(StudioRadialMenus.Shortcut(x.id),evt));
            if(preset==null)return false;
            var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(Vector2.zero,view.position.size);
            var point=evt.isMouse?evt.mousePosition:radialHasPointer?radialLastPointer:canvas.center;
            if(!canvas.Contains(point)||(view is MashBoxStudioViewport overlay&&overlay.IsOverViewportControls(point)))return false;
            radialDefinitions=definitions.ToList();radialCommands=RadialCommands(view);
            AddMaterialSlotRadialContext(radialDefinitions,radialCommands);
            radialParents.Clear();radialPreset=preset;radialBackArmed=false;
            markingView=view;markingControl=id;markingButton=evt.isMouse?evt.button:-1;markingKey=evt.isKey?evt.keyCode:KeyCode.None;
            markingOpen=true;suppressModelContext=true;GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
            markingPointer=GUIUtility.GUIToScreenPoint(point);markingOrigin=markingPointer;markingAnchor=markingPointer;
            LayoutRadial();evt.Use();repaint();return true;
        }
        internal void HandleModelMarkingEvent(SceneView view,Event evt,int id)
        {
            if(evt.type==EventType.ContextClick&&(markingOpen||suppressModelContext)){suppressModelContext=false;evt.Use();return;}
            if(!markingOpen){TryOpenRadial(view,evt,id);return;}
            if(view!=markingView)return;
            if(StudioShortcuts.Pressed(StudioShortcut.Cancel,evt)){CancelMarkingMenu();evt.Use();return;}
            if(evt.type==EventType.MouseLeaveWindow||GUIUtility.hotControl!=markingControl){CancelMarkingMenu();return;}
            if(evt.isMouse)markingPointer=GUIUtility.GUIToScreenPoint(evt.mousePosition);
            bool release=evt.type==EventType.MouseUp&&(evt.button==markingButton||evt.button==0)||evt.type==EventType.KeyUp&&evt.keyCode==markingKey;
            if(evt.isMouse||release)
            {
                bool overBack=radialBack.Contains(markingPointer-markingOrigin);
                // A new context opens underneath the pointer. Back must be left
                // and deliberately re-entered; tiny movement inside it cannot arm it.
                if((evt.type==EventType.MouseMove||evt.type==EventType.MouseDrag)&&!overBack)radialBackArmed=true;
                int hover=radialParents.Count>0&&radialBackArmed&&overBack?-2:StudioRadialMenus.Hit(radialRects,markingPointer-markingOrigin);
                if(hover>=0&&radialVisible[hover]==StudioRadialMenus.SpacerKey)hover=-1;
                if(hover!=markingHover){markingHover=hover;radialHoverSince=EditorApplication.timeSinceStartup;}
                if(release)
                {
                    try
                    {
                    var point=markingPointer-markingOrigin;
                    if(radialParents.Count>0&&radialBackArmed&&radialBack.Contains(point))
                    {string parent=radialParents[radialParents.Count-1];radialParents.RemoveAt(radialParents.Count-1);SetRadialPreset(parent);}
                    else if(markingHover>=0)
                    {
                        string key=radialVisible[markingHover];
                        if(key.StartsWith("RadialPreset/",StringComparison.Ordinal))
                        {
                            string target=key.Substring("RadialPreset/".Length);
                            if(radialDefinitions.Exists(x=>x.id==target)&&!radialParents.Contains(target)&&radialPreset.id!=target)
                            {radialParents.Add(radialPreset.id);SetRadialPreset(target);}
                        }
                        else
                        {
                            CancelMarkingMenu();
                            // Resolve again at release, never execute a stale context closure.
                            var command=ResolveMaterialSlotRadialCommand(key)??RadialCommands(view).Find(x=>x.Key==key);
                            if(command!=null&&command.Enabled&&key.StartsWith("Control/",StringComparison.Ordinal))StudioRadialControlWindow.Open(key,view as MashBoxStudioViewport);
                            else if(command!=null&&command.Enabled)command.Execute?.Invoke();
                        }
                    }
                    else CancelMarkingMenu();
                    }
                    finally
                    {
                        // Navigation may change the active preset on this event,
                        // but releasing the initiating gesture always relinquishes
                        // capture and its tether, including a simultaneous Back hover.
                        CancelMarkingMenu();
                    }
                }
                if(evt.isMouse||release)evt.Use();repaint();return;
            }
            if(evt.isKey)evt.Use();
        }
        private void SetRadialPreset(string id)
        {
            var next=radialDefinitions.Find(x=>x.id==id);if(next==null)return;
            markingOrigin=markingPointer;markingAnchor=markingPointer;
            radialPreset=next;radialBackArmed=false;LayoutRadial();repaint();
        }
        private void LayoutRadial()
        {
            if(markingLabel==null)markingLabel=new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter,fontSize=11,clipping=TextClipping.Clip};
            float width=80;
            foreach(string key in radialPreset.commands)
                if(key!=StudioRadialMenus.SpacerKey)width=Mathf.Max(width,Mathf.Min(128,markingLabel.CalcSize(new GUIContent(RadialLabel(key))).x+30));
            int count=radialPreset.commands.Count;
            var sizes=new Vector2[count];
            for(int i=0;i<count;i++)sizes[i]=new Vector2(width,24);
            radialRects=StudioRadialMenus.Layout(count,sizes);
            radialVisible=radialPreset.commands.ToArray();
            // Every command belongs to this single pie. Never page, cap, hide or
            // viewport-fit the list; large lists grow the radius as needed.
            radialBack=new Rect(-24,-10,48,20);
            if(count==1)radialBack.y=radialRects[0].yMax+8;
            // The center navigation controls must have the same gaps as commands.
            markingHover=-1;
        }
        private void TickMarkingMenu()
        {
            if(!markingOpen||EditorApplication.timeSinceStartup-radialHoverSince<RadialSubmenuDelay)return;
            if(markingHover==-2)
            {
                if(radialParents.Count==0||!radialBackArmed||!radialBack.Contains(markingPointer-markingOrigin))return;
                string parent=radialParents[radialParents.Count-1];radialParents.RemoveAt(radialParents.Count-1);SetRadialPreset(parent);
                return;
            }
            if(markingHover<0||markingHover>=radialVisible.Length)return;
            string key=radialVisible[markingHover];
            if(!key.StartsWith("RadialPreset/",StringComparison.Ordinal)||RadialCommand(key)?.Enabled!=true)return;
            // Only the same visible button held for the full delay can enter.
            if(StudioRadialMenus.Hit(radialRects,markingPointer-markingOrigin)!=markingHover)return;
            string target=key.Substring("RadialPreset/".Length);
            if(target==radialPreset.id||radialParents.Contains(target)||!radialDefinitions.Exists(x=>x.id==target))return;
            radialParents.Add(radialPreset.id);SetRadialPreset(target);
            // Layout clears hover: a child requires fresh pointer input and its
            // own delay, rather than cascading under a stationary pointer.
        }
        internal void CancelMarkingMenu()
        {
            if(!markingOpen)return;
            if(GUIUtility.hotControl==markingControl)GUIUtility.hotControl=0;
            markingOpen=false;markingView=null;markingHover=-1;radialBackArmed=false;radialParents.Clear();radialCommands=null;repaint();
        }
        internal void DrawMarkingMenu()
        {
            if(!markingOpen||Event.current.type!=EventType.Repaint)return;
            var origin=GUIUtility.ScreenToGUIPoint(markingOrigin);var palette=StudioTheme.Current;
            DrawMarkingDragLine();
            void Button(Rect relative,string text,bool enabled,bool hovered,string tooltip=null,string iconKey=null,bool isChecked=false)
            {
                var rect=new Rect(origin+relative.position,relative.size);
                StudioTheme.DrawRadialButtonBackground(rect,hovered&&enabled);
                markingLabel.normal.textColor=enabled?new Color(.9f,.91f,.93f):new Color(.48f,.5f,.53f);
                // 24px row: 16px icon with uniform 4px insets and a 4px text gap.
                if(iconKey!=null)
                {
                    var icon=StudioCommandIcons.Get(iconKey);
                    var previous=GUI.color;
                    if(!enabled)GUI.color=new Color(previous.r,previous.g,previous.b,previous.a*.45f);
                    if(icon)GUI.DrawTexture(new Rect(rect.x+4,rect.center.y-8,16,16),icon,ScaleMode.ScaleToFit,true);
                    GUI.color=previous;
                    if(isChecked)GUI.Label(new Rect(rect.x+12,rect.center.y,10,10),"✓",markingLabel);
                    rect=new Rect(rect.x+24,rect.y,Mathf.Max(0,rect.width-28),rect.height);
                }
                markingLabel.alignment=iconKey!=null?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter;
                GUI.Label(rect,new GUIContent(text,tooltip??text),markingLabel);
            }
            for(int i=0;i<radialVisible.Length;i++)
            {
                string key=radialVisible[i];if(key==StudioRadialMenus.SpacerKey)continue;var command=RadialCommand(key);
                bool enabled=command!=null&&command.Enabled;
                Button(radialRects[i],RadialLabel(key),enabled,markingHover==i,command?.Path??key,key,command?.Checked==true);
            }
            var pointer=markingPointer-markingOrigin;
            if(radialParents.Count>0)Button(radialBack,"‹ Back",true,radialBackArmed&&radialBack.Contains(pointer));
        }
        private void DrawMarkingDragLine()
        {
            var start=GUIUtility.ScreenToGUIPoint(markingAnchor);
            var end=GUIUtility.ScreenToGUIPoint(markingPointer);
            var delta=end-start;if(delta.sqrMagnitude<1)return;
            var previous=GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
                EditorGUI.DrawRect(new Rect(start.x,start.y-1,delta.magnitude,2),new Color(.08f,.08f,.08f,.8f));
                EditorGUI.DrawRect(new Rect(start.x,start.y-.5f,delta.magnitude,1),StudioTheme.Current.Text);
            }
            finally { GUI.matrix=previous; }
        }
        internal void ExecuteMarkingAction(MarkingAction action, SceneView view)
        {
            if (!MarkingActionEnabled(action)) return;
            var before = action == MarkingAction.Clear || action == MarkingAction.SelectAll ? CaptureSelection() : null;
            switch (action)
            {
                case MarkingAction.Object: SetComponentMode(ComponentMode.Object,view); break;
                case MarkingAction.Vertex: SetComponentMode(ComponentMode.Vertex,view); break;
                case MarkingAction.Edge: SetComponentMode(ComponentMode.Edge,view); break;
                case MarkingAction.Face: SetComponentMode(ComponentMode.Face,view); break;
                case MarkingAction.Move: ActivateTool(Tool.Move); break;
                case MarkingAction.Rotate: ActivateTool(Tool.Rotate); break;
                case MarkingAction.Scale: ActivateTool(Tool.Scale); break;
                case MarkingAction.Axes: SetToolbarLocalAxes(!local); break;
                case MarkingAction.ObjectAxes: SetToolbarLocalAxes(true); break;
                case MarkingAction.ComponentAxes: SetToolbarComponentAxes(); break;
                case MarkingAction.WorldAxes: SetToolbarLocalAxes(false); break;
                case MarkingAction.EditPivot: TogglePivotEdit(view); break;
                case MarkingAction.CenterPivot: CenterEditingPivot(); break;
                case MarkingAction.Freeze: FreezeEditingObject(); RememberAction(RepeatKind.Freeze); break;
                case MarkingAction.OpenUV: StudioUVEditor.Open(this); break;
                case MarkingAction.Frame: Frame(view); break;
                case MarkingAction.Through: xray = !xray; SaveDisplayConfiguration(); break;
                case MarkingAction.Clear: ClearAllComponentSelections(); if (mode == ComponentMode.Object) { objects.Clear(); Active = -1; } break;
                case MarkingAction.SelectAll:
                    if (mode == ComponentMode.Object) { for (int i = 0; i < Transforms.Length; i++) objects.Add(i); if (Active < 0 && Transforms.Length > 0) Active = 0; }
                    else { foreach(int node in ComponentTargetNodes()) using(new ComponentTargetScope(this,node)) for (int i = 0; i < ComponentCount(); i++) if(IsolationAllowsComponent(i)) Components.Add(i); }
                    break;
                default: if (action >= MarkingAction.UV0 && action <= MarkingAction.UV7) StudioUVEditor.Open(this, action - MarkingAction.UV0); break;
            }
            RecordSelectionChange(before);
            repaint(); toolOptions?.Repaint();
        }
        private void SetComponentMode(ComponentMode next, SceneView view = null) => ChangeComponentMode(next,view);
    }
}
