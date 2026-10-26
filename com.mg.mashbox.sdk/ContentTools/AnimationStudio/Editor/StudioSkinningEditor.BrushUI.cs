using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool sculptMode, brushMenuOpen, adjustingBrush, suppressBrushContext;
        private int interactionControl, brushHover = -1;
        private bool symmetryWheel;
        private Vector2 parentWheelOrigin;
        private Vector2 menuOrigin, adjustmentAnchor, adjustmentPointer;
        private enum BrushAdjustmentAxis { Pending, Radius, Strength }
        private BrushAdjustmentAxis adjustmentAxis;
        private Vector2 menuDragAnchor, menuDragPointer;
        private float initialRadius, initialStrength;
        private Brush lastSkinBrush = Brush.Paint, lastSculptBrush = Brush.Grab;
        private static readonly Brush[] SkinBrushes = { Brush.Paint, Brush.SmoothWeights, Brush.Select, Brush.Pose };
        private static readonly Brush[] SculptBrushes = { Brush.Inflate, Brush.Bulge, Brush.Grab, Brush.SmoothShape, Brush.FitBody, Brush.Select, Brush.Pose };
        private Brush[] ModeBrushes => sculptMode ? SculptBrushes : SkinBrushes;
        private static string BrushLabel(Brush brush) => brush == Brush.FitBody ? "Fit to Body" : brush == Brush.SmoothWeights ? "Smooth weights" : brush == Brush.SmoothShape ? "Smooth shape" : brush.ToString();
        private static Rect ModeRect(SceneView view)
        {
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(0,32,view.position.width,view.position.height-32);
            float left = view is MashBoxStudioViewport skinView ? skinView.SkinToolbarLeft : canvas.x;
            return new Rect(left + 44, canvas.y, 108, 32);
        }
        private static Rect DisplayToolbarRect(SceneView view)
        {
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(0,32,view.position.width,view.position.height-88);
            // The canvas extends behind the posing bar. Anchor above the bar itself
            // so all four 32px buttons remain visible and their hit areas match.
            float bottom = view is MashBoxStudioViewport skinView ? skinView.SkinPreviewBarTop : canvas.yMax;
            return new Rect(canvas.x + 8, Mathf.Max(canvas.y + 40, bottom - 8 - 32), 146, 32);
        }
        private bool RequirePaintableSkin()
        {
            var part = Part;
            if (part != null && part.mesh && part.bound && CanPaintPart(part)) return true;
            string message = part == null ? "Select a skinned mesh to paint weights."
                : part.referenceOnly ? "Default reference meshes are locked. Select your own skinned mesh."
                : "Skin this mesh first: Edit Skins > Skin. Then you can paint weights.";
            if (lastView) lastView.ShowNotification(new GUIContent(message));
            repaint();
            return false;
        }
        private void ChooseBrush(Brush brush)
        {
            if ((brush == Brush.Paint || brush == Brush.SmoothWeights) && !RequirePaintableSkin()) return;
            FinishObjectGesture();
            FinishPoseGesture();
            EndStroke(); Tool = brush;
            if (brush == Brush.Pose || brush == Brush.Object)
            {
                GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false;
                PausePreviewAnimation();
                if (previewGraph.IsValid()) previewGraph.Destroy();
                if (previewAnimator) previewAnimator.enabled = false;
            }
            if (brush == Brush.Paint || brush == Brush.SmoothWeights) lastSkinBrush = brush;
            else if (brush != Brush.Select && brush != Brush.Pose && brush != Brush.Object) lastSculptBrush = brush;
            repaint();
        }
        private void SetSculptMode(bool sculpt)
        {
            if (!sculpt && !RequirePaintableSkin()) return;
            if (sculptMode == sculpt && Tool != Brush.Pose && Tool != Brush.Object) return;
            CancelBrushInteraction(); sculptMode = sculpt;
            ChooseBrush(sculpt ? lastSculptBrush : lastSkinBrush);
            heatmap = !sculpt; UpdateDisplay();
        }
        private void CancelBrushInteraction()
        {
            if (GUIUtility.hotControl == interactionControl) GUIUtility.hotControl = 0;
            brushMenuOpen = adjustingBrush = symmetryWheel = false; brushHover = -1;
            adjustmentAxis = BrushAdjustmentAxis.Pending;
            repaint();
        }
        private static Rect WheelItemRect(Vector2 origin, int slot)
        {
            // Eight deliberately spaced compass positions rather than a crowded ellipse.
            var offsets = new[] { new Vector2(0,-94), new Vector2(132,-64), new Vector2(154,0),
                new Vector2(132,64), new Vector2(0,94), new Vector2(-132,64), new Vector2(-154,0), new Vector2(-132,-64) };
            return new Rect(origin + offsets[slot] - new Vector2(56,15), new Vector2(112,30));
        }
        private Rect BrushItemRect(int i) => WheelItemRect(menuOrigin, sculptMode ? (i < 4 ? i : i+1) : i*2);
        private static Vector2 ClampWheel(Vector2 point, SceneView view) => new Vector2(
            Mathf.Clamp(point.x, Mathf.Min(218,view.position.width*.5f), Mathf.Max(view.position.width-218,view.position.width*.5f)),
            Mathf.Clamp(point.y, Mathf.Min(148,view.position.height*.5f), Mathf.Max(view.position.height-120,view.position.height*.5f)));
        private static readonly string[] SymmetryWheelLabels = { "Off", "World X", "World Y", "World Z", "Local X", "Local Y", "Local Z", "‹ Brushes" };
        private bool SymmetryWheelSelected(int i) => i == 0 ? Document.sculptSymmetryAxis == 0 :
            i < 7 && Document.sculptSymmetryAxis == (i-1)%3+1 && Document.sculptSymmetryLocal == (i>=4);
        internal void BeforeSkinSceneGUI(SceneView view)
        {
            var e = Event.current;
            if ((Tool == Brush.Pose || Tool == Brush.Object) && e.type == EventType.MouseDown && e.button == 0 && !e.alt &&
                view is MashBoxStudioViewport poseView && poseView.ModelCanvasRect.Contains(e.mousePosition) &&
                !poseView.IsOverSkinningPanels(e.mousePosition) && !ModeRect(view).Contains(e.mousePosition) && !DisplayToolbarRect(view).Contains(e.mousePosition) && !PoseToolbarRect(view).Contains(e.mousePosition))
            { GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false; }
            // Buttons can retain keyboard focus too; only an actual text edit or
            // active mouse gesture should prevent the viewport tool shortcuts.
            if ((Tool == Brush.Pose || Tool == Brush.Object) && e.type == EventType.KeyDown && !e.alt && !e.control && !e.command && !e.shift && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                if (e.keyCode == KeyCode.W || e.keyCode == KeyCode.E || (Tool == Brush.Object && e.keyCode == KeyCode.R))
                { FinishObjectGesture(); FinishPoseGesture(); objectScale = Tool == Brush.Object && e.keyCode == KeyCode.R; poseTranslate = e.keyCode == KeyCode.W; e.Use(); repaint(); return; }
            }
            int id = GUIUtility.GetControlID("MashBoxSkinBrushMenu".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.ContextClick && suppressBrushContext) { suppressBrushContext = false; e.Use(); return; }
            if (brushMenuOpen || adjustingBrush)
            {
                if (GUIUtility.hotControl != interactionControl || e.type == EventType.MouseLeaveWindow || e.type == EventType.Ignore)
                { CancelBrushInteraction(); return; }
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    if (adjustingBrush) { radius = initialRadius; strength = initialStrength; }
                    CancelBrushInteraction(); e.Use(); return;
                }
                if (adjustingBrush && e.type == EventType.MouseDrag && e.button == 2)
                {
                    var drag = e.mousePosition - adjustmentAnchor;
                    // Ignore initial hand jitter, then hold one axis until release.
                    if (adjustmentAxis == BrushAdjustmentAxis.Pending && Mathf.Max(Mathf.Abs(drag.x), Mathf.Abs(drag.y)) >= 4)
                        adjustmentAxis = Mathf.Abs(drag.x) >= Mathf.Abs(drag.y) ? BrushAdjustmentAxis.Radius : BrushAdjustmentAxis.Strength;
                    if (adjustmentAxis == BrushAdjustmentAxis.Radius)
                    {
                        radius = Mathf.Clamp(initialRadius * Mathf.Exp(drag.x*.01f), .005f, .75f);
                        adjustmentPointer = adjustmentAnchor + new Vector2(drag.x, 0);
                    }
                    else if (adjustmentAxis == BrushAdjustmentAxis.Strength)
                    {
                        strength = Mathf.Clamp(initialStrength - drag.y*.005f, .001f, 1);
                        adjustmentPointer = adjustmentAnchor + new Vector2(0, drag.y);
                    }
                    e.Use(); repaint(); return;
                }
                if (brushMenuOpen && (e.type == EventType.MouseDrag || e.type == EventType.MouseMove || e.type == EventType.MouseUp))
                {
                    menuDragPointer = e.mousePosition;
                    brushHover = -1;
                    if (!symmetryWheel && sculptMode && WheelItemRect(menuOrigin,4).Contains(e.mousePosition) && e.type != EventType.MouseUp)
                    {
                        parentWheelOrigin = menuOrigin;
                        symmetryWheel = true;
                        menuOrigin = ClampWheel(e.mousePosition,view);
                        menuDragAnchor = e.mousePosition;
                    }
                    else if (symmetryWheel)
                    {
                        for (int i=0;i<8;i++) if (WheelItemRect(menuOrigin,i).Contains(e.mousePosition)) brushHover=i;
                        if (brushHover == 7 && e.type != EventType.MouseUp)
                        { symmetryWheel=false; menuOrigin=parentWheelOrigin; menuDragAnchor=menuOrigin; brushHover=-1; }
                    }
                    else
                        for (int i=0;i<ModeBrushes.Length;i++) if (BrushItemRect(i).Contains(e.mousePosition)) brushHover=i;
                    if (e.type == EventType.MouseUp && e.button == 1)
                    {
                        int selected = brushHover; bool symmetry = symmetryWheel;
                        CancelBrushInteraction();
                        if (symmetry && selected >= 0 && selected < 7)
                            SetSymmetry(selected == 0 ? 0 : (selected-1)%3+1, selected == 0 ? Document.sculptSymmetryLocal : selected>=4);
                        else if (!symmetry && selected >= 0) ChooseBrush(ModeBrushes[selected]);
                    }
                    e.Use(); repaint(); return;
                }
                if (adjustingBrush && e.rawType == EventType.MouseUp && e.button == 2)
                { CancelBrushInteraction(); e.Use(); return; }
                if (e.isMouse || e.isKey || e.type == EventType.ScrollWheel) e.Use();
                return;
            }
            if (e.type != EventType.MouseDown || e.alt || GUIUtility.hotControl != 0 || stroke) return;
            if ((Tool == Brush.Pose || Tool == Brush.Object)) return;
            if (ModeRect(view).Contains(e.mousePosition) || DisplayToolbarRect(view).Contains(e.mousePosition)) return;
            if (OverSymmetry(view,e.mousePosition)) return;
            if (view is MashBoxStudioViewport viewport && (!viewport.ModelCanvasRect.Contains(e.mousePosition) || viewport.IsOverSkinningPanels(e.mousePosition))) return;
            if (e.button != 1 && !(e.button == 2 && e.control)) return;
            interactionControl = id; GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            if (e.button == 1)
            {
                brushMenuOpen = suppressBrushContext = true; brushHover = -1;
                menuDragAnchor = menuDragPointer = e.mousePosition;
                symmetryWheel = false;
                menuOrigin = ClampWheel(e.mousePosition,view);
            }
            else
            {
                adjustingBrush = true; adjustmentAnchor = adjustmentPointer = e.mousePosition;
                adjustmentAxis = BrushAdjustmentAxis.Pending;
                initialRadius = radius; initialStrength = strength;
            }
            e.Use(); repaint();
        }
        private void DrawSkinMeshStatistics(Rect toolbar)
        {
            if (Part == null || !Part.mesh) return;
            var mesh = Part.mesh;
            long faces = 0, triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                long indices = mesh.GetIndexCount(i);
                switch (mesh.GetTopology(i))
                {
                    case MeshTopology.Triangles: faces += indices / 3; triangles += indices / 3; break;
                    case MeshTopology.Quads: faces += indices / 4; triangles += indices / 2; break;
                }
            }
            if (lastView && toolbar.y + 100 > lastView.position.height) return;
            var style = StudioTheme.Styles.miniLabel;
            float y = toolbar.yMax + 38;
            GUI.Label(new Rect(toolbar.x, y, 80, 18), "Vertices", style);
            GUI.Label(new Rect(toolbar.x + 84, y, 80, 18), "Faces", style);
            GUI.Label(new Rect(toolbar.x + 168, y, 80, 18), "Triangles", style);
            GUI.Label(new Rect(toolbar.x, y + 18, 80, 18), mesh.vertexCount.ToString("N0"), style);
            GUI.Label(new Rect(toolbar.x + 84, y + 18, 80, 18), faces.ToString("N0"), style);
            GUI.Label(new Rect(toolbar.x + 168, y + 18, 80, 18), triangles.ToString("N0"), style);
        }
        private void DrawBrushOverlay(SceneView view)
        {
            skinToolbarTooltip=null;
            if(Event.current.type==EventType.MouseLeaveWindow)repaint();
            Handles.BeginGUI();
            using (StudioTheme.Scope())
            {
                var rect = ModeRect(view);
                DrawModeIcon(new Rect(rect.x, rect.y, 32, 32), false);
                DrawModeIcon(new Rect(rect.x + 38, rect.y, 32, 32), true);
                DrawObjectModeIcon(new Rect(rect.x + 76, rect.y, 32, 32));
                var display = DisplayToolbarRect(view);
                DrawUVCheckerButton(new Rect(display.x, display.y, 32, 32));
                DrawTexturedButton(new Rect(display.x + 38, display.y, 32, 32));
                DrawWireframeButton(new Rect(display.x + 76, display.y, 32, 32));
                DrawSameShadingButton(new Rect(display.x + 114, display.y, 32, 32));
                if (sculptMode && Tool != Brush.Pose && Tool != Brush.Object)
                {
                    var labelRect = new Rect(rect.xMax + 12, rect.y + 6, 160, 20);
                    GUI.Label(labelRect, new GUIContent("Sculpt · " + BrushLabel(Tool), "Active sculpt brush — hold right-click to change"),
                        new GUIStyle(StudioTheme.Styles.miniLabel) { alignment = TextAnchor.MiddleLeft });
                }
                DrawSkinMeshStatistics(rect);
                DrawSymmetryToolbar(view);
                if ((Tool == Brush.Pose || Tool == Brush.Object))
                {
                    DrawPoseToolbar(view);
                }
                if (brushMenuOpen)
                {
                    DrawBrushDragLine(menuDragAnchor, menuDragPointer);
                    int count = symmetryWheel ? 8 : ModeBrushes.Length;
                    for (int i=0;i<count;i++)
                    {
                        var item = symmetryWheel ? WheelItemRect(menuOrigin,i) : BrushItemRect(i);
                        EditorGUI.DrawRect(item, i == brushHover ? new Color(.23f,.40f,.36f,.98f) : new Color(.045f,.055f,.055f,.98f));
                        bool active = symmetryWheel ? SymmetryWheelSelected(i) : ModeBrushes[i] == Tool;
                        if (active) EditorGUI.DrawRect(new Rect(item.x+8,item.yMax-2,item.width-16,2),new Color(.5f,1,.87f));
                        GUI.Label(item, symmetryWheel ? SymmetryWheelLabels[i] : BrushLabel(ModeBrushes[i]), new GUIStyle(StudioTheme.Styles.label) { alignment=TextAnchor.MiddleCenter });
                    }
                    if (sculptMode && !symmetryWheel)
                    {
                        var item=WheelItemRect(menuOrigin,4);
                        EditorGUI.DrawRect(item,new Color(.045f,.055f,.055f,.98f));
                        GUI.Label(item,"Symmetry ›",new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter});
                    }
                    GUI.Label(new Rect(menuOrigin.x-60,menuOrigin.y-12,120,24), symmetryWheel ? "Symmetry" : sculptMode ? "Sculpt" : "Skin weights", new GUIStyle(StudioTheme.Styles.miniLabel) { alignment=TextAnchor.MiddleCenter });
                }
                if (adjustingBrush)
                {
                    DrawBrushDragLine(adjustmentAnchor, adjustmentPointer);
                    string adjustmentText = adjustmentAxis == BrushAdjustmentAxis.Radius ? $"Radius {radius:F3}" :
                        adjustmentAxis == BrushAdjustmentAxis.Strength ? $"Strength {strength:P0}" : "Drag sideways: radius · up/down: strength";
                    GUI.Box(new Rect(Mathf.Clamp(adjustmentAnchor.x+18,0,view.position.width-270),Mathf.Clamp(adjustmentAnchor.y+18,40,view.position.height-36),260,28), adjustmentText);
                }
                if(!brushMenuOpen && !adjustingBrush)DrawSkinToolbarTooltip(view);
            }
            Handles.EndGUI();
        }
        private static void DrawBrushDragLine(Vector2 anchor, Vector2 pointer)
        {
            if (Event.current.type != EventType.Repaint) return;
            var delta = pointer - anchor;
            if (delta.sqrMagnitude < 1) return;
            // Adjustment feedback is screen-space UI, independent of the mirrored
            // brush and scene transforms. Draw signed endpoints directly.
            if (Mathf.Abs(delta.y) < .01f)
            {
                float x = Mathf.Min(anchor.x, pointer.x), width = Mathf.Abs(delta.x);
                EditorGUI.DrawRect(new Rect(x, anchor.y-2, width, 4), new Color(.08f,.08f,.08f,.9f));
                EditorGUI.DrawRect(new Rect(x, anchor.y-1, width, 2), StudioTheme.Current.Text);
            }
            else if (Mathf.Abs(delta.x) < .01f)
            {
                float y = Mathf.Min(anchor.y, pointer.y), height = Mathf.Abs(delta.y);
                EditorGUI.DrawRect(new Rect(anchor.x-2, y, 4, height), new Color(.08f,.08f,.08f,.9f));
                EditorGUI.DrawRect(new Rect(anchor.x-1, y, 2, height), StudioTheme.Current.Text);
            }
            else
            {
                var previousMatrix = Handles.matrix; var previousColor = Handles.color;
                try
                {
                    Handles.matrix = Matrix4x4.identity;
                    Handles.color = new Color(.08f,.08f,.08f,.9f);
                    Handles.DrawAAPolyLine(4, (Vector3)anchor, (Vector3)pointer);
                    Handles.color = StudioTheme.Current.Text;
                    Handles.DrawAAPolyLine(2, (Vector3)anchor, (Vector3)pointer);
                }
                finally { Handles.matrix = previousMatrix; Handles.color = previousColor; }
            }
            EditorGUI.DrawRect(new Rect(anchor.x - 2, anchor.y - 2, 4, 4), StudioTheme.Current.Text);
            EditorGUI.DrawRect(new Rect(pointer.x - 2, pointer.y - 2, 4, 4), StudioTheme.Current.Text);
        }
        private string skinToolbarTooltip;
        private Rect skinToolbarTooltipAnchor;
        private GUIStyle skinToolbarTooltipStyle;
        private void DrawSkinToolbarTooltip(SceneView view)
        {
            if(Event.current.type!=EventType.Repaint || string.IsNullOrEmpty(skinToolbarTooltip) ||
                EditorWindow.mouseOverWindow!=view || GUIUtility.hotControl!=0)return;
            var canvas=view is MashBoxStudioViewport studio?studio.ModelCanvasRect:new Rect(0,32,view.position.width,view.position.height-32);
            if(skinToolbarTooltipStyle==null)
                skinToolbarTooltipStyle=new GUIStyle(EditorStyles.label){wordWrap=true,padding=new RectOffset(8,8,6,6)};
            skinToolbarTooltipStyle.normal.textColor=new Color(.92f,.94f,.94f);
            float width=Mathf.Min(340,Mathf.Max(40,canvas.width-8));
            var content=new GUIContent(skinToolbarTooltip);
            float height=skinToolbarTooltipStyle.CalcHeight(content,width);
            float x=Mathf.Clamp(skinToolbarTooltipAnchor.x,canvas.x+4,Mathf.Max(canvas.x+4,canvas.xMax-width-4));
            float y=skinToolbarTooltipAnchor.y-height-6;
            if(y<canvas.y+4)y=skinToolbarTooltipAnchor.yMax+6;
            var box=new Rect(x,Mathf.Clamp(y,canvas.y+4,Mathf.Max(canvas.y+4,canvas.yMax-height-4)),width,height);
            EditorGUI.DrawRect(box,new Color(.035f,.045f,.045f,.98f));
            GUI.Label(box,content,skinToolbarTooltipStyle);
        }
        private bool DrawSkinModeButton(Rect rect, bool active, string tooltip)
        {
            if(rect.Contains(Event.current.mousePosition))
            { skinToolbarTooltip=tooltip;skinToolbarTooltipAnchor=rect; }
            bool clicked = GUI.Button(rect, new GUIContent("",tooltip), GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, rect.Contains(Event.current.mousePosition) ? new Color(.10f,.12f,.12f) : new Color(.035f,.04f,.04f));
                if (active) EditorGUI.DrawRect(new Rect(rect.x+5,rect.yMax-3,rect.width-10,2),new Color(.5f,1,.87f));
            }
            return clicked && !active;
        }
        private void DrawModeIcon(Rect rect, bool sculpt)
        {
            bool active = Tool != Brush.Pose && Tool != Brush.Object && sculptMode == sculpt;
            if (DrawSkinModeButton(rect, active, sculpt ? "Sculpt — shape the mesh" : "Skin Weight Paint — edit bone influences"))
                SetSculptMode(sculpt);
            if (Event.current.type != EventType.Repaint) return;
            var previous = Handles.color;
            var matrix = Handles.matrix;
            Handles.matrix = Matrix4x4.identity;
            Vector3 P(float x, float y) => new Vector3(rect.center.x + x, rect.center.y + y, 0);
            try
            {
                Handles.color = active ? new Color(.5f,1,.87f) : new Color(.75f,.81f,.81f);
                if (sculpt)
                {
                    // Raised mesh patch: distinguish geometry editing from paint.
                    Handles.DrawAAPolyLine(1.6f, P(-11,6), P(-5,1), P(0,-8), P(5,1), P(11,6), P(0,11), P(-11,6));
                    Handles.DrawAAPolyLine(1.3f, P(-11,6), P(0,3), P(11,6));
                    Handles.DrawAAPolyLine(1.3f, P(0,-8), P(0,3), P(0,11));
                }
                else
                {
                    // Paint brush over a colored influence swatch.
                    Handles.DrawAAPolyLine(2.3f, P(-1,1), P(8,-8), P(10,-6), P(1,3));
                    Handles.DrawAAPolyLine(1.6f, P(-1,1), P(-5,2), P(-7,7), P(-2,6), P(1,3));
                    for (int i=0;i<3;i++)
                    {
                        Handles.color = Color.Lerp(new Color(.25f,.55f,1), new Color(1,.5f,.2f), i*.5f);
                        Handles.DrawSolidDisc(P(2+i*4,9), Vector3.forward, 1.6f);
                    }
                }
            }
            finally { Handles.color = previous; Handles.matrix = matrix; }
        }
    }
}
