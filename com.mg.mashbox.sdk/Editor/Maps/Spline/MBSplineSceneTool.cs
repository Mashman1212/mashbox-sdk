using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace MashBoxSDK.Maps.Spline
{
    [InitializeOnLoad]
    public static class MBSplineSceneTool
    {
        static bool drawHeld = false;
        static int controlID;

        static MBSplineSceneTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static void OnSceneGUI(SceneView view)
        {
            Event e = Event.current;

            // ✅ HOLD D (not toggle)
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
                drawHeld = true;

            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space)
                drawHeld = false;

            var go = Selection.activeGameObject;
            if (go == null) return;

            var mbSpline = go.GetComponent<MBSplineComponent>();
            if (mbSpline == null || mbSpline.container == null) return;

            var container = mbSpline.container;
            var spline = container.Spline;

            controlID = GUIUtility.GetControlID(FocusType.Passive);

            // ✅ Take control ONLY while holding D
            if (drawHeld && e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlID);
            }

            // Not drawing → just UI
            if (!drawHeld)
            {
                DrawUI(false);
                return;
            }

            // 🔥 Preview (always works, even first point)
            DrawPreview(container, spline);

            // 🔥 Click to place
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 1)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                Vector3 worldPos;

                // ✅ BEST: Unity editor snapping (colliders, etc)
                var hit = HandleUtility.RaySnap(ray);

                if (hit is RaycastHit rayHit)
                {
                    worldPos = rayHit.point;
                }
                else
                {
                    // fallback plane (never fails)
                    Plane plane;

                    if (spline.Count > 0)
                    {
                        Vector3 last = container.transform.TransformPoint(
                            spline[spline.Count - 1].Position
                        );

                        plane = new Plane(Vector3.up, last);
                    }
                    else
                    {
                        plane = new Plane(Vector3.up, Vector3.zero);
                    }

                    if (!plane.Raycast(ray, out float enter))
                        return;

                    worldPos = ray.GetPoint(enter);
                }

                Undo.RecordObject(container, "Add Spline Point");

                //// Optional 90° snap
                //if (spline.Count > 0)
                //{
                //    Vector3 last = container.transform.TransformPoint(
                //        spline[spline.Count - 1].Position
                //    );
                //
                //    //worldPos = Snap90(last, worldPos);
                //}

                Vector3 localPos = container.transform.InverseTransformPoint(worldPos);

                spline.Add(new BezierKnot((float3)localPos));

                // Only fix the new knot (not whole spline)
                int i = spline.Count - 1;
                var knot = spline[i];
                knot.TangentIn = float3.zero;
                knot.TangentOut = float3.zero;
                spline[i] = knot;

                EditorUtility.SetDirty(container);

                e.Use();
            }

            DrawUI(true);
        }

        static void DrawPreview(SplineContainer container, UnityEngine.Splines.Spline spline)
        {
            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            Vector3 previewPos;

            var hit = HandleUtility.RaySnap(ray);

            if (hit is RaycastHit rayHit)
            {
                previewPos = rayHit.point;
            }
            else
            {
                Plane plane = new Plane(Vector3.up, Vector3.zero);
                if (!plane.Raycast(ray, out float enter)) return;

                previewPos = ray.GetPoint(enter);
            }

            Handles.color = Color.yellow;

            if (spline.Count > 0)
            {
                Vector3 last = container.transform.TransformPoint(
                    spline[spline.Count - 1].Position
                );

                Handles.DrawLine(last, previewPos);
            }
            else
            {
                // first point preview
                Handles.DrawWireDisc(previewPos, Vector3.up, 0.2f);
            }
        }

        static void DrawUI(bool active)
        {
            Handles.BeginGUI();

            GUILayout.BeginArea(new Rect(10, 10, 200, 40));

            GUI.color = active ? Color.green : Color.white;
            GUILayout.Label(active ? "DRAW (Hold Space)" : "Hold Space to Draw");

            GUILayout.EndArea();

            Handles.EndGUI();
        }

        static Vector3 Snap90(Vector3 prev, Vector3 current)
        {
            Vector3 delta = current - prev;

            float dist = delta.magnitude;
            if (dist == 0f) return prev;

            Vector3 dir = delta.normalized;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
                dir = new Vector3(Mathf.Sign(dir.x), 0, 0);
            else
                dir = new Vector3(0, 0, Mathf.Sign(dir.z));

            return prev + dir * dist;
        }
    }
}