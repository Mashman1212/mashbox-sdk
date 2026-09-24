using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnitySpline = UnityEngine.Splines.Spline;

namespace MashBoxSDK.Maps.Roads
{
    public static class MGRoadKnotShape
    {
        const string ScaleKey = "MashBox.Road.KnotScale";
        public static Vector3 GetScale(UnitySpline spline, int knot)
        {
            if (spline.TryGetFloat4Data(ScaleKey, out var data))
                foreach (var point in data)
                    if (Mathf.Abs(point.Index - knot) < .0001f) return new Vector3(point.Value.x, point.Value.y, point.Value.z);
            return Vector3.one;
        }
        public static void SetScale(UnitySpline spline, int knot, Vector3 scale)
        {
            var data = spline.GetOrCreateFloat4Data(ScaleKey);
            data.PathIndexUnit = PathIndexUnit.Knot;
            if (data.Count == 0) for (int i = 0; i < spline.Count; i++) data.Add(i, new float4(1));
            var value = new float4(Mathf.Max(.05f, scale.x), Mathf.Max(.05f, scale.y), Mathf.Max(.05f, scale.z), 0);
            for (int i = 0; i < data.Count; i++)
                if (Mathf.Abs(data[i].Index - knot) < .0001f) { data.SetDataPoint(i, new DataPoint<float4>(knot, value)); return; }
            data.Add(knot, value);
        }
        public static Vector3 ScaleAt(UnitySpline spline, int curve, float t) => Vector3.Lerp(GetScale(spline, curve), GetScale(spline, (curve + 1) % spline.Count), t);
        public static Quaternion Frame(UnitySpline spline, int curveIndex, float t, Transform transform)
        {
            var curve = spline.GetCurve(curveIndex);
            Vector3 direction = transform.TransformVector((Vector3)CurveUtility.EvaluateTangent(curve, t));
            if (direction.sqrMagnitude < 1e-8f) direction = transform.TransformVector((Vector3)(curve.P3 - curve.P0));
            if (direction.sqrMagnitude < 1e-8f) direction = transform.forward;
            Quaternion rotation = Quaternion.Slerp((Quaternion)spline[curveIndex].Rotation, (Quaternion)spline[(curveIndex + 1) % spline.Count].Rotation, t);
            Vector3 up = transform.TransformVector(rotation * Vector3.up);
            if (Vector3.Cross(direction, up).sqrMagnitude < 1e-8f) up = transform.TransformVector(rotation * Vector3.right);
            return Quaternion.LookRotation(direction.normalized, up.normalized);
        }
        public static void Rotate(UnitySpline spline, int index, Quaternion worldDelta, Transform transform)
        {
            var knot = spline[index];
            spline.SetTangentMode(index, TangentMode.Broken);
            knot.Rotation = (quaternion)(Quaternion.Inverse(transform.rotation) * worldDelta * transform.rotation * (Quaternion)knot.Rotation);
            spline[index] = knot;
        }
        public static void Scale(UnitySpline spline, int index, Vector3 value)
        {
            value = Vector3.Max(Vector3.one * .05f, value);
            var old = GetScale(spline, index);
            if (Mathf.Abs(value.z - old.z) > .00001f)
            {
                var knot = spline[index];
                spline.SetTangentMode(index, TangentMode.Broken);
                float ratio = value.z / Mathf.Max(.05f, old.z);
                knot.TangentIn *= ratio; knot.TangentOut *= ratio; spline[index] = knot;
            }
            SetScale(spline, index, value);
        }
        public static void Ring(MGRoad road, UnitySpline spline, int curve, float t, System.Func<Vector3, Vector3> project, Vector3[] vertices)
        {
            Vector3 centre = road.transform.TransformPoint((Vector3)CurveUtility.EvaluatePosition(spline.GetCurve(curve), t));
            Quaternion frame = Frame(spline, curve, t, road.transform) * Quaternion.AngleAxis(road.bankAngle, Vector3.forward);
            Vector3 right = frame * Vector3.right, up = frame * Vector3.up;
            Vector3 scale = ScaleAt(spline, curve, t);
            float half = Mathf.Max(.1f, road.width) * .5f * scale.x;
            float edge = half + Mathf.Max(0, road.shoulderWidth) * scale.x;
            for (int col = 0; col < 5; col++)
            {
                float offset = col == 0 ? -edge : col == 1 ? -half : col == 2 ? 0 : col == 3 ? half : edge;
                Vector3 v = centre + right * offset;
                // Preserve banking above the terrain-projected cross section.
                if (project != null) { float bankHeight = right.y * offset; v = project(v); v.y += bankHeight; }
                v += up * (col == 2 ? road.crown * scale.y : (col == 0 || col == 4 ? -road.shoulderDrop * scale.y : 0));
                vertices[col] = v;
            }
        }
    }
}
