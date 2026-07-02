using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using MashBoxSDK.EditorResources;

namespace MashBoxSDK.Maps.Spline
{
    [CustomEditor(typeof(MBSplineComponent))]
    public class MBSplineInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            MashBoxInspectorHeaderUtility.DrawScriptHeader();
            DrawDefaultInspector();

            var mbSpline = (MBSplineComponent)target;

            if (mbSpline.container == null)
            {
                EditorGUILayout.HelpBox("No SplineContainer assigned.", MessageType.Warning);

                if (GUILayout.Button("Create SplineContainer"))
                {
                    Undo.RecordObject(mbSpline.gameObject, "Add SplineContainer");

                    var foundContainer = mbSpline.gameObject.GetComponent<SplineContainer>();

                    if (foundContainer == null)
                        foundContainer = mbSpline.gameObject.AddComponent<SplineContainer>();

                    mbSpline.container = foundContainer;

                    EditorUtility.SetDirty(mbSpline);
                }

                return;
            }
            
            var container = mbSpline.container;
            var spline = container.Spline;

            GUILayout.Space(10);
            GUILayout.Label("Ledge Tool Controls", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Add Point (Forward)"))
            {
                Undo.RecordObject(container, "Add Point");

                Vector3 newWorldPos = GetNextPoint(container, spline);

                Vector3 localPos = container.transform.InverseTransformPoint(newWorldPos);

                spline.Add(new BezierKnot((float3)localPos));

                ForceLinear(spline);

                EditorUtility.SetDirty(container);
            }
            
            if (GUILayout.Button("Delete Last Point"))
            {
                if (spline.Count > 0)
                {
                    Undo.RecordObject(container, "Delete Point");

                    spline.RemoveAt(spline.Count - 1);

                    EditorUtility.SetDirty(container);
                }
            }
            
            if (GUILayout.Button("Clear All Points"))
            {
                Undo.RecordObject(container, "Clear Points");

                spline.Clear();

                EditorUtility.SetDirty(container);
            }
        }

        Vector3 GetNextPoint(SplineContainer container, UnityEngine.Splines.Spline spline)
        {
            if (spline.Count == 0)
                return container.transform.position;

            Vector3 last = container.transform.TransformPoint(spline[spline.Count - 1].Position);

            if (spline.Count == 1)
                return last + container.transform.right; // use local forward direction

            Vector3 prev = container.transform.TransformPoint(spline[spline.Count - 2].Position);
            Vector3 dir = (last - prev).normalized;

            return last + dir;
        }

        void ForceLinear(UnityEngine.Splines.Spline spline)
        {
            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i];

                knot.TangentIn = float3.zero;
                knot.TangentOut = float3.zero;

                spline[i] = knot;
            }
        }
    }
}
