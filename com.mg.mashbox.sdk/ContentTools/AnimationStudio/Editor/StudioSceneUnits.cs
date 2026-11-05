using System;
using UnityEditor;
using UnityEngine;
using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
namespace MashBoxSDK.AnimationStudio
{
    public enum StudioLengthUnit { Meters, Millimeters, Centimeters, Inches }
    internal static class StudioSceneUnits
    {
        internal static StudioModelDocument Document => StudioModelCommands.ActiveDocument;
        internal static StudioLengthUnit Current => Document ? Valid(Document.sceneUnits) : StudioLengthUnit.Meters;
        internal static StudioLengthUnit Valid(StudioLengthUnit unit) => Enum.IsDefined(typeof(StudioLengthUnit),unit) ? unit : StudioLengthUnit.Meters;
        internal static double MetersPerUnit(StudioLengthUnit unit) => Valid(unit) switch { StudioLengthUnit.Millimeters => .001, StudioLengthUnit.Centimeters => .01, StudioLengthUnit.Inches => .0254, _ => 1 };
        internal static string Symbol => Current switch { StudioLengthUnit.Millimeters => "mm", StudioLengthUnit.Centimeters => "cm", StudioLengthUnit.Inches => "in", _ => "m" };
        internal static float Display(float meters) => (float)(meters / MetersPerUnit(Current));
        internal static float Internal(float displayed) => (float)(displayed * MetersPerUnit(Current));
        internal static float DefaultGrid(StudioLengthUnit unit) => unit == StudioLengthUnit.Meters ? .25f : (float)MetersPerUnit(unit);
        internal static float GridSpacing => Document ? (Document.gridSpacing > 0 ? Document.gridSpacing : DefaultGrid(Document.sceneUnits)) : DefaultGrid(StudioLengthUnit.Meters);
        internal static void SetGrid(float meters)
        {
            if(float.IsNaN(meters)||float.IsInfinity(meters)||meters<.000001f)return;
            if(Document){if(Document.gridSpacing==meters)return;Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Grid spacing");Document.gridSpacing=meters;EditorUtility.SetDirty(Document);Document.Save();Undo.IncrementCurrentGroup();}
            else return;
            Redraw();
        }
        internal static void ResetGrid() => SetGrid(DefaultGrid(Current));
        internal static void Redraw(){foreach(var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>())view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);}
        internal static void SetUnit(StudioLengthUnit unit)
        {
            if(!Document)return;
            unit=Valid(unit);
            if(Document && Document.sceneUnits!=unit){Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(Document,"Scene units");Document.sceneUnits=unit;Document.gridSpacing=DefaultGrid(unit);EditorUtility.SetDirty(Document);Document.Save();Undo.IncrementCurrentGroup();}
            Redraw();
        }
        internal static void DrawPreference()
        {
            using(new EditorGUI.DisabledScope(!Document))
            {
                var next=(StudioLengthUnit)EditorGUILayout.EnumPopup(new GUIContent("Scene units","Saved only in the current workspace. New workspaces start in meters."),Current);
                if(next!=Current){GUI.FocusControl(null);SetUnit(next);}
            }
        }
        internal static float Field(string label,float meters) => Field(new GUIContent(label),meters);
        internal static float Field(GUIContent label,float meters)
        {
            var content=new GUIContent(label.text+" ("+Symbol+")",label.tooltip);
            EditorGUI.BeginChangeCheck();float edited=EditorGUILayout.FloatField(content,Display(meters));
            return EditorGUI.EndChangeCheck()?Internal(edited):meters;
        }
    }

    // Unity's PositionHandle consumes the main Scene view's incremental snap setting.
    // Studio move handles pass zero snap explicitly and leave EditorSnapSettings untouched.
    internal static class StudioUnsnappedHandles
    {
        internal static Vector3 PositionHandle(Vector3 position, Quaternion rotation)
        {
            var evt = Event.current;
            var modifiers = evt != null ? evt.modifiers : EventModifiers.None;
            if (evt != null) evt.modifiers &= ~(EventModifiers.Control | EventModifiers.Command);
            try
            {
                float size = HandleUtility.GetHandleSize(position);
                Vector3 result = position;
                using (new Handles.DrawingScope())
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int id = GUIUtility.GetControlID(FocusType.Passive);
                        Handles.color = axis == 0 ? Handles.xAxisColor : axis == 1 ? Handles.yAxisColor : Handles.zAxisColor;
                        Vector3 direction = rotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                        Vector3 next = Handles.Slider(id, position, direction, size, Handles.ArrowHandleCap, 0);
                        if (GUIUtility.hotControl == id) result = next;
                    }
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int a = plane == 2 ? 1 : 0, b = plane == 0 ? 1 : 2;
                        Vector3 first = rotation * (a == 0 ? Vector3.right : Vector3.up);
                        Vector3 second = rotation * (b == 1 ? Vector3.up : Vector3.forward);
                        Vector3 offset = (first + second) * size * .23f;
                        int id = GUIUtility.GetControlID(FocusType.Passive);
                        Handles.color = plane == 0 ? Handles.zAxisColor : plane == 1 ? Handles.yAxisColor : Handles.xAxisColor;
                        Vector3 next = Handles.Slider2D(id, position + offset, Vector3.Cross(first, second),
                            first, second, size * .065f, Handles.RectangleHandleCap, Vector2.zero);
                        if (GUIUtility.hotControl == id) result = next - offset;
                    }
                    int centerId = GUIUtility.GetControlID(FocusType.Passive);
                    Handles.color = Color.yellow;
                    Vector3 free = Handles.FreeMoveHandle(centerId, position, size * .075f, Vector3.zero, Handles.RectangleHandleCap);
                    if (GUIUtility.hotControl == centerId) result = free;
                }
                return result;
            }
            finally { if (evt != null) evt.modifiers = modifiers; }
        }
    }
}
