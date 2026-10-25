using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioSkinningEditor
    {
        private bool sculptMode, brushMenuOpen, adjustingBrush, suppressBrushContext;
        private int interactionControl, brushHover = -1;
        private Vector2 menuOrigin, adjustmentAnchor, adjustmentPointer;
        private enum BrushAdjustmentAxis { Pending, Radius, Strength }
        private BrushAdjustmentAxis adjustmentAxis;
        private Vector2 menuDragAnchor, menuDragPointer;
        private float initialRadius, initialStrength;
        private Brush lastSkinBrush = Brush.Paint, lastSculptBrush = Brush.Inflate;
        private static readonly Brush[] SkinBrushes = { Brush.Paint, Brush.SmoothWeights, Brush.Select, Brush.Pose };
        private static readonly Brush[] SculptBrushes = { Brush.Inflate, Brush.Bulge, Brush.Grab, Brush.SmoothShape, Brush.Select, Brush.Pose };
        private Brush[] ModeBrushes => sculptMode ? SculptBrushes : SkinBrushes;
        private static string BrushLabel(Brush brush) => brush == Brush.SmoothWeights ? "Smooth weights" : brush == Brush.SmoothShape ? "Smooth shape" : brush.ToString();
        private static Rect ModeRect(SceneView view)
        {
            var canvas = view is MashBoxStudioViewport studio ? studio.ModelCanvasRect : new Rect(0,32,view.position.width,view.position.height-32);
            float left = view is MashBoxStudioViewport skinView ? skinView.SkinToolbarLeft : canvas.x;
            return new Rect(left + 44, canvas.y, 108, 32);
        }
        private void ChooseBrush(Brush brush)
        {
            FinishPoseGesture();
            EndStroke(); Tool = brush;
            if (brush == Brush.Pose)
            {
                GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false;
                PausePreviewAnimation();
                if (previewGraph.IsValid()) previewGraph.Destroy();
                if (previewAnimator) previewAnimator.enabled = false;
            }
            if (brush == Brush.Paint || brush == Brush.SmoothWeights) lastSkinBrush = brush;
            else if (brush != Brush.Select && brush != Brush.Pose) lastSculptBrush = brush;
            repaint();
        }
        private void SetSculptMode(bool sculpt)
        {
            if (sculptMode == sculpt && Tool != Brush.Pose) return;
            CancelBrushInteraction(); sculptMode = sculpt;
            ChooseBrush(sculpt ? lastSculptBrush : lastSkinBrush);
            heatmap = !sculpt; UpdateDisplay();
        }
        private void CancelBrushInteraction()
        {
            if (GUIUtility.hotControl == interactionControl) GUIUtility.hotControl = 0;
            brushMenuOpen = adjustingBrush = false; brushHover = -1;
            adjustmentAxis = BrushAdjustmentAxis.Pending;
            repaint();
        }
        private Rect BrushItemRect(int i)
        {
            float angle = i * Mathf.PI * 2 / ModeBrushes.Length - Mathf.PI / 2;
            return new Rect(menuOrigin + new Vector2(Mathf.Cos(angle)*112, Mathf.Sin(angle)*62) - new Vector2(53,13), new Vector2(106,26));
        }
        internal void BeforeSkinSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (Tool == Brush.Pose && e.type == EventType.MouseDown && e.button == 0 && !e.alt &&
                view is MashBoxStudioViewport poseView && poseView.ModelCanvasRect.Contains(e.mousePosition) &&
                !poseView.IsOverSkinningPanels(e.mousePosition) && !ModeRect(view).Contains(e.mousePosition) && !PoseToolbarRect(view).Contains(e.mousePosition))
            { GUIUtility.keyboardControl = 0; EditorGUIUtility.editingTextField = false; }
            // Buttons can retain keyboard focus too; only an actual text edit or
            // active mouse gesture should prevent the viewport tool shortcuts.
            if (Tool == Brush.Pose && e.type == EventType.KeyDown && !e.alt && !e.control && !e.command && !e.shift && !EditorGUIUtility.editingTextField && GUIUtility.hotControl == 0)
            {
                if (e.keyCode == KeyCode.W || e.keyCode == KeyCode.E)
                { FinishPoseGesture(); poseTranslate = e.keyCode == KeyCode.W; e.Use(); repaint(); return; }
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
                    for (int i=0;i<ModeBrushes.Length;i++) if (BrushItemRect(i).Contains(e.mousePosition)) brushHover=i;
                    if (e.type == EventType.MouseUp && e.button == 1)
                    {
                        bool symmetry = sculptMode && SymmetryMenuRect.Contains(e.mousePosition);
                        var symmetryRect = SymmetryMenuRect;
                        int selected = brushHover; CancelBrushInteraction();
                        if (symmetry) ShowSymmetryMenu(symmetryRect);
                        else if (selected >= 0) ChooseBrush(ModeBrushes[selected]);
                    }
                    e.Use(); repaint(); return;
                }
                if (adjustingBrush && e.rawType == EventType.MouseUp && e.button == 2)
                { CancelBrushInteraction(); e.Use(); return; }
                if (e.isMouse || e.isKey || e.type == EventType.ScrollWheel) e.Use();
                return;
            }
            if (e.type != EventType.MouseDown || e.alt || GUIUtility.hotControl != 0 || stroke) return;
            if (Tool == Brush.Pose) return;
            if (ModeRect(view).Contains(e.mousePosition)) return;
            if (OverSymmetry(view,e.mousePosition)) return;
            if (view is MashBoxStudioViewport viewport && (!viewport.ModelCanvasRect.Contains(e.mousePosition) || viewport.IsOverSkinningPanels(e.mousePosition))) return;
            if (e.button != 1 && !(e.button == 2 && e.control)) return;
            interactionControl = id; GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            if (e.button == 1)
            {
                brushMenuOpen = suppressBrushContext = true; brushHover = -1;
                menuDragAnchor = menuDragPointer = e.mousePosition;
                menuOrigin = new Vector2(Mathf.Clamp(e.mousePosition.x,170,view.position.width-170), Mathf.Clamp(e.mousePosition.y,110,view.position.height-90));
            }
            else
            {
                adjustingBrush = true; adjustmentAnchor = adjustmentPointer = e.mousePosition;
                adjustmentAxis = BrushAdjustmentAxis.Pending;
                initialRadius = radius; initialStrength = strength;
            }
            e.Use(); repaint();
        }
        private void DrawBrushOverlay(SceneView view)
        {
            Handles.BeginGUI();
            using (StudioTheme.Scope())
            {
                var rect = ModeRect(view);
                DrawModeIcon(new Rect(rect.x, rect.y, 32, 32), false);
                DrawModeIcon(new Rect(rect.x + 38, rect.y, 32, 32), true);
                DrawPoseModeIcon(new Rect(rect.x + 76, rect.y, 32, 32));
                DrawSymmetryToolbar(view);
                if (Tool == Brush.Pose)
                {
                    DrawPoseToolbar(view);
                }
                if (brushMenuOpen)
                {
                    if(sculptMode)
                    {
                        var item=SymmetryMenuRect;
                        EditorGUI.DrawRect(item,item.Contains(Event.current.mousePosition) ? new Color(.35f,.65f,.6f) : new Color(.08f,.09f,.09f,.98f));
                        GUI.Label(item,"Symmetry…",new GUIStyle(StudioTheme.Styles.label){alignment=TextAnchor.MiddleCenter});
                    }
                    DrawBrushDragLine(menuDragAnchor, menuDragPointer);
                    for (int i=0;i<ModeBrushes.Length;i++)
                    {
                        var item = BrushItemRect(i);
                        EditorGUI.DrawRect(item, i == brushHover ? new Color(.35f,.65f,.6f) : new Color(.08f,.09f,.09f,.98f));
                        GUI.Label(item, BrushLabel(ModeBrushes[i]), new GUIStyle(StudioTheme.Styles.label) { alignment=TextAnchor.MiddleCenter });
                    }
                    GUI.Label(new Rect(menuOrigin.x-50,menuOrigin.y-12,100,24), sculptMode ? "Sculpt" : "Skin weights", new GUIStyle(StudioTheme.Styles.miniLabel) { alignment=TextAnchor.MiddleCenter });
                }
                if (adjustingBrush)
                {
                    DrawBrushDragLine(adjustmentAnchor, adjustmentPointer);
                    string adjustmentText = adjustmentAxis == BrushAdjustmentAxis.Radius ? $"Radius {radius:F3}" :
                        adjustmentAxis == BrushAdjustmentAxis.Strength ? $"Strength {strength:P0}" : "Drag sideways: radius · up/down: strength";
                    GUI.Box(new Rect(Mathf.Clamp(adjustmentAnchor.x+18,0,view.position.width-270),Mathf.Clamp(adjustmentAnchor.y+18,40,view.position.height-36),260,28), adjustmentText);
                }
            }
            Handles.EndGUI();
        }
        private static void DrawBrushDragLine(Vector2 anchor, Vector2 pointer)
        {
            if (Event.current.type != EventType.Repaint) return;
            var delta = pointer - anchor;
            if (delta.sqrMagnitude < 1) return;
            var previous = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, anchor);
                EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 2, delta.magnitude, 4), new Color(.08f,.08f,.08f,.9f));
                EditorGUI.DrawRect(new Rect(anchor.x, anchor.y - 1, delta.magnitude, 2), StudioTheme.Current.Text);
            }
            finally { GUI.matrix = previous; }
            EditorGUI.DrawRect(new Rect(anchor.x - 2, anchor.y - 2, 4, 4), StudioTheme.Current.Text);
        }
        private static bool DrawSkinModeButton(Rect rect, bool active, string tooltip)
        {
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
            bool active = Tool != Brush.Pose && sculptMode == sculpt;
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
