using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;

public static class StudioBikeCurveValidation
{
    [Serializable] class Curve { public string name; public Vector3[] points; }
    [Serializable] class Curves { public Curve[] curves; }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/BmxAnimatorValidation/Assets")) throw new InvalidOperationException("Use isolated fixture.");
        try
        {
            var points = JsonUtility.FromJson<Curves>(File.ReadAllText("Assets/BikeControlCurves.json")).curves.Single(c => c.name == "Bars_Curve").points;
            var method = typeof(AnimationStudioWindow).GetMethod("CenterBarsCurve", BindingFlags.NonPublic | BindingFlags.Static);
            var centered = (Vector3[])method.Invoke(null, new object[] { points });
            var bounds = new Bounds(centered[0], Vector3.zero);
            foreach (var point in centered) bounds.Encapsulate(point);
            if (bounds.center.magnitude > .001f || centered.Any(p => Mathf.Abs(p.y) > .001f) || bounds.size.x < .2f || bounds.size.z < .2f)
                throw new Exception("Bars ring must retain its size, center on zero and lie in the local yaw plane.");
            var position = new Vector3(1, 2, 3); var rotation = Quaternion.Euler(23, 48, 17);
            foreach (var point in centered)
                if (Mathf.Abs(Vector3.Dot(rotation * point + position - position, rotation * Vector3.up)) > .001f)
                    throw new Exception("Ring must follow joint rotation.");
            var centerCurve = typeof(AnimationStudioWindow).GetMethod("CenterBikeCurve", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var curve in JsonUtility.FromJson<Curves>(File.ReadAllText("Assets/BikeControlCurves.json")).curves.Where(c =>
                c.name == "DriveTrain_Curve" || c.name == "LeftPedal_Curve" || c.name == "RightPedal_Curve"))
            {
                var ring = (Vector3[])centerCurve.Invoke(null, new object[] { curve.points, Vector3.right });
                if (ring.Any(p => Mathf.Abs(p.x) > .001f)) throw new Exception(curve.name + " must lie in local pitch plane.");
                foreach (float pitch in new[] { 0f, 45f, 90f, 180f, 270f })
                {
                    var turn = rotation * Quaternion.Euler(pitch, 0, 0);
                    var worldBounds = new Bounds(position + turn * ring[0], Vector3.zero);
                    foreach (var point in ring) worldBounds.Encapsulate(position + turn * point);
                    if (Vector3.Distance(worldBounds.center, position) > .001f)
                        throw new Exception(curve.name + " drifts away from its bone at " + pitch);
                }
            }
            File.WriteAllText("bars-curve-result.txt", "PASS: bars centered and yaw-aligned; drivetrain and both pedal curves centered and pitch-aligned without positional drift through rotation.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("bars-curve-result.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
