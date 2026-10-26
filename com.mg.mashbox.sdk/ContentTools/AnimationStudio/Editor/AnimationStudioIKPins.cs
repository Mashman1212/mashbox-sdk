using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        private void DrawIKPinButtons(Rect area, List<RigSelectionItem> selected)
        {
            DrawIKPinButton(new Rect(area.x + 252, area.y + 2, 26, 22), selected, false);
            DrawIKPinButton(new Rect(area.x + 282, area.y + 2, 26, 22), selected.Where(s => !s.Pole).ToList(), true);
        }

        private void DrawIKPinButton(Rect rect, List<RigSelectionItem> selected, bool rotation)
        {
            int pinned = 0;
            foreach (var item in selected) WithBlendActor(item.Actor, () =>
            {
                var e = pose.effectors[item.Index];
                if (rotation ? e.pinRotation : item.Pole ? e.pinPole : e.pinPosition) pinned++;
            });
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                bool all = selected.Count > 0 && pinned == selected.Count;
                string tooltip = rotation
                    ? "Pin hand/foot rotation in world space. Poles have position only. Auto Key or S records the pin."
                    : "Pin selected targets or poles in world space. Auto Key or S records the pin.";
                bool mixed = pinned > 0 && !all;
                if (GUI.Toggle(rect, all, new GUIContent("", tooltip), StudioTheme.Styles.overlayButton) != all)
                    Guard(() => SetIKPins(selected, rotation, !all));
                Color color = all || mixed ? new Color(1f, .85f, .3f) : StudioTheme.Current.Text;
                if (!GUI.enabled) color.a *= .35f;
                DrawIKPinGlyph(new Rect(rect.center.x - 10, rect.center.y - 10, 20, 20), rotation, color);
                if (mixed && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(rect.xMax - 6, rect.yMax - 5, 3, 3), color);
            }
        }

        private void SetIKPins(List<RigSelectionItem> selected, bool rotation, bool enabled)
        {
            FinishSceneEdit(); FinishActorEdits();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(enabled ? "Pin IK controls" : "Unpin IK controls");
            try
            {
                foreach (var actor in selected.GroupBy(s => s.Actor)) WithBlendActor(actor.Key, () =>
                {
                    foreach (var item in actor)
                    {
                        int limb = item.Index;
                        var visible = DisplayEffector(limb);
                        // Store the solved pose before releasing a pin, avoiding a snap.
                        rig.MatchLimbBlend(pose, limb, pose.effectors[limb].weight, true);
                        var e = pose.effectors[limb];
                        if (rotation)
                        {
                            e.pinRotation = enabled;
                            if (enabled) e.pinnedRotation = rig.Ends[limb].rotation;
                        }
                        else if (item.Pole)
                        {
                            e.pinPole = enabled;
                            if (enabled) e.pinnedPole = rig.Root.transform.TransformPoint(visible.pole);
                        }
                        else
                        {
                            e.pinPosition = enabled;
                            if (enabled) e.pinnedPosition = rig.Ends[limb].position;
                        }
                        pose.effectors[limb] = e;
                    }
                    Commit(enabled ? "Pin IK controls" : "Unpin IK controls");
                });
            }
            finally { Undo.CollapseUndoOperations(group); }
            viewport?.Repaint(); Repaint();
        }

        private void DrawIKPinMarker(Vector3 position, bool pinnedPosition, bool pinnedRotation)
        {
            if ((!pinnedPosition && !pinnedRotation) || Event.current.type != EventType.Repaint) return;
            Vector2 point = HandleUtility.WorldToGUIPoint(position);
            Handles.BeginGUI();
            Color color = new Color(1f, .85f, .3f);
            if (pinnedPosition) DrawIKPinGlyph(new Rect(point.x - 8, point.y - 8, 16, 16), false, color);
            if (pinnedRotation) DrawIKPinGlyph(new Rect(point.x - 11, point.y - 11, 22, 22), true, color);
            Handles.EndGUI();
        }

        private static void DrawIKPinGlyph(Rect rect, bool rotation, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color previous = Handles.color;
            try
            {
                Handles.color = color;
                Vector3 Point(float x, float y) => new Vector3(rect.center.x + x * rect.width / 20f, rect.center.y + y * rect.height / 20f);
                if (!rotation)
                {
                    // Pushpin silhouette: flat head, tapered shoulder and pointed stem.
                    Handles.DrawAAPolyLine(1.6f, Point(-3,-7), Point(3,-7), Point(3,-2),
                        Point(5,1), Point(-5,1), Point(-3,-2), Point(-3,-7));
                    Handles.DrawAAPolyLine(1.6f, Point(0,1), Point(0,7));
                }
                else
                {
                    var arc = new Vector3[19];
                    for (int i = 0; i < arc.Length; i++)
                    {
                        float angle = Mathf.Lerp(-45, 245, i / (float)(arc.Length - 1)) * Mathf.Deg2Rad;
                        arc[i] = Point(Mathf.Cos(angle)*7, Mathf.Sin(angle)*7);
                    }
                    Handles.DrawAAPolyLine(1.6f, arc);
                    Vector3 tip = arc[arc.Length - 1];
                    Vector3 tangent = (tip - arc[arc.Length - 2]).normalized;
                    Vector3 side = new Vector3(-tangent.y, tangent.x);
                    float size = rect.width * .18f;
                    Handles.DrawAAPolyLine(1.6f, tip - tangent*size + side*size*.7f, tip,
                        tip - tangent*size - side*size*.7f);
                }
            }
            finally { Handles.color = previous; }
        }
    }
}
