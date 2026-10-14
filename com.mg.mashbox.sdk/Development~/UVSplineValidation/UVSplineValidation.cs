using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

public static class UVSplineValidation
{
    const string ScenePath = "Assets/UvSplineRoundTrip.unity";
    const string MeshPath = "Assets/UvSplineSource.asset";
    static readonly List<string> Results = new List<string>();
    static int failures;
    static readonly Vector3 Edit = new Vector3(0.064132f, 0.003998f, -0.043701f);

    [Serializable]
    public sealed class Snapshot
    {
        public Vector3[] positions;
        public Vector3[] references;
    }

    static void Invoke(UVSpline spline, string name, params object[] args)
    {
        typeof(UVSpline).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(spline, args);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Near(Vector3 actual, Vector3 expected, string message)
    {
        Check((actual - expected).sqrMagnitude < 0.00000004f,
            message + ": expected " + expected.ToString("F7") + ", got " + actual.ToString("F7"));
    }

    static UVSpline Fixture()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var target = new GameObject("Source", typeof(MeshFilter));
        // Exercise conversions with a translated, rotated, scaled hierarchy.
        target.transform.position = new Vector3(193.19513f, 70.62974f, 995.0916f);
        target.transform.rotation = Quaternion.Euler(7, 25, -4);
        target.transform.localScale = new Vector3(2, 0.75f, 1.25f);
        var mesh = new Mesh { name = "UV spline regression strip" };
        var vertices = new Vector3[40];
        var uvs = new Vector2[40];
        var triangles = new int[19 * 6];
        for (int i = 0; i < 20; i++)
        {
            vertices[2 * i] = new Vector3(i * 5, -i, -2);
            vertices[2 * i + 1] = new Vector3(i * 5, -i, 2);
            uvs[2 * i] = new Vector2(0, i / 19f);
            uvs[2 * i + 1] = new Vector2(1, i / 19f);
            if (i == 19) continue;
            int j = 6 * i, v = 2 * i;
            triangles[j] = v; triangles[j + 1] = v + 2; triangles[j + 2] = v + 1;
            triangles[j + 3] = v + 1; triangles[j + 4] = v + 2; triangles[j + 5] = v + 3;
        }
        mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
        mesh.RecalculateNormals();
        target.GetComponent<MeshFilter>().sharedMesh = mesh;
        var child = new GameObject("UV Spline", typeof(SplineContainer));
        child.transform.SetParent(target.transform, false);
        var spline = child.AddComponent<UVSpline>();
        spline.Target = target.GetComponent<MeshFilter>();
        spline.GeneratedPointCount = 20;
        Check(spline.GenerateFromTarget(out string error), error);
        for (int i = 0; i < 20; i++)
            Near(spline.Container.Spline[i].Position, spline.ControlPoints[i].generatedLocalPosition,
                "First generation added an offset at knot " + i);
        var knot = spline.Container.Spline[3];
        knot.Position += (Unity.Mathematics.float3)Edit;
        spline.Container.Spline[3] = knot;
        spline.ControlPoints[3].widthScale = 1.75f;
        spline.ControlPoints[3].mirrorSplitToNext = true;
        spline.ControlPoints[3].mirrorLeftOffset = 0.27f;
        return spline;
    }

    static Snapshot Capture(UVSpline spline)
    {
        var result = new Snapshot { positions = new Vector3[20], references = new Vector3[20] };
        for (int i = 0; i < 20; i++)
        {
            result.positions[i] = spline.Container.Spline[i].Position;
            result.references[i] = spline.ControlPoints[i].generatedLocalPosition;
        }
        return result;
    }

    static void Matches(UVSpline spline, Snapshot expected)
    {
        Check(spline.ControlPoints.Count == 20, "Authored control-point count changed");
        Check(spline.Container.Spline.Count == 20, "Authored knot count changed");
        for (int i = 0; i < 20; i++)
        {
            Near(spline.Container.Spline[i].Position, expected.positions[i], "Knot " + i);
            Near(spline.ControlPoints[i].generatedLocalPosition, expected.references[i], "Reference " + i);
        }
        Check(Mathf.Approximately(spline.ControlPoints[3].widthScale, 1.75f), "UV scale edit lost");
        Check(spline.ControlPoints[3].mirrorSplitToNext, "Mirror split edit lost");
        Check(Mathf.Approximately(spline.ControlPoints[3].mirrorLeftOffset, 0.27f), "Mirror offset edit lost");
    }

    static void ValidationWhileContainerIsIncomplete(int temporaryCount)
    {
        var spline = Fixture();
        var expected = Capture(spline);
        var authored = spline.Container.Spline;
        if (temporaryCount < 0) spline.Container.Splines = Array.Empty<Spline>();
        else
        {
            var temporary = new Spline();
            for (int i = 0; i < temporaryCount; i++) temporary.Add(new BezierKnot(Vector3.zero));
            spline.Container.Spline = temporary;
        }
        Invoke(spline, "OnValidate");
        Check(spline.ControlPoints.Count == 20, "Validation resized controls using a partially loaded container");
        spline.Container.Splines = new[] { authored };
        Matches(spline, expected);
        Check(spline.GenerateFromTarget(out string error), error);
        Matches(spline, expected);
    }

    static void ResampleReferences()
    {
        var spline = Fixture();
        var expected = Capture(spline);
        Invoke(spline, "EnsureControlPointCount", 39);
        for (int i = 0; i < 39; i++)
        {
            float index = i * 0.5f;
            int a = Mathf.Min((int)index, 18);
            Near(spline.ControlPoints[i].generatedLocalPosition,
                Vector3.Lerp(expected.references[a], expected.references[a + 1], index - a),
                "Resampled reference " + i);
        }
        Invoke(spline, "EnsureControlPointCount", 20);
        Matches(spline, expected);
        Check(spline.GenerateFromTarget(out string error), error);
        Matches(spline, expected);
    }

    static void RepeatedGenerationAndCountChange()
    {
        var spline = Fixture();
        var expected = Capture(spline);
        for (int i = 0; i < 3; i++)
        {
            Check(spline.GenerateFromTarget(out string error), error);
            Check(spline.RebuildOutputMesh() != null, "UV output mesh failed");
            Matches(spline, expected);
        }
        spline.GeneratedPointCount = 39;
        Check(spline.GenerateFromTarget(out string resizeError), resizeError);
        Near((Vector3)spline.Container.Spline[6].Position - spline.ControlPoints[6].generatedLocalPosition,
            Edit, "Changing generated point count lost manual knot movement");
        spline.GeneratedPointCount = 20;
        Check(spline.GenerateFromTarget(out resizeError), resizeError);
        Matches(spline, expected);
    }

    static void SynchronizeAfterEnable()
    {
        var spline = Fixture();
        var loft = spline.transform.parent.gameObject.AddComponent<MultiSplineLoft>();
        spline.enabled = false;
        spline.enabled = true;
        Check(loft.pointCount == 20, "OnEnable did not synchronize loft settings");
        int count = loft.synchronizationCount;
        Invoke(spline, "OnValidate");
        Check(loft.synchronizationCount == count, "OnValidate accessed a different component");
    }

    static void SaveRoundTripFixture()
    {
        var spline = Fixture();
        var expected = Capture(spline);
        AssetDatabase.CreateAsset(spline.SourceMesh, MeshPath);
        Check(spline.RebuildOutputMesh() != null, "UV output mesh failed");
        AssetDatabase.CreateAsset(spline.OutputMesh, "Assets/UvSplineOutput.asset");
        File.WriteAllText("expected.json", JsonUtility.ToJson(expected, true));
        Check(EditorSceneManager.SaveScene(spline.gameObject.scene, ScenePath), "Could not save fixture");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        VerifySavedFixture();
    }

    static void VerifySavedFixture()
    {
        var expected = JsonUtility.FromJson<Snapshot>(File.ReadAllText("expected.json"));
        EditorSceneManager.OpenScene(ScenePath);
        var spline = Object.FindFirstObjectByType<UVSpline>();
        Check(spline != null, "Reloaded scene missing UVSpline");
        Matches(spline, expected);
        for (int i = 0; i < 3; i++)
        {
            Check(spline.GenerateFromTarget(out string error), error);
            Check(spline.RebuildOutputMesh() != null, "Reloaded UV output mesh failed");
            Matches(spline, expected);
        }
    }

    static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception e) { failures++; Results.Add("FAIL " + name + ": " + e); }
    }

    static void Finish(string report)
    {
        File.WriteAllLines(report, Results);
        foreach (string line in Results) Debug.Log(line);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    public static void Run()
    {
        Test("Validation with a temporarily empty spline", () => ValidationWhileContainerIsIncomplete(0));
        Test("Validation with a temporarily one-knot spline", () => ValidationWhileContainerIsIncomplete(1));
        Test("Validation before the spline array is available", () => ValidationWhileContainerIsIncomplete(-1));
        Test("Control-point resizing retains reference positions", ResampleReferences);
        Test("Repeated regeneration, UV output, edits, transforms, and point-count changes", RepeatedGenerationAndCountChange);
        Test("Loft settings synchronize after enabling, not during validation", SynchronizeAfterEnable);
        Test("Scene serialization and reopen retain authored knot/UV edits", SaveRoundTripFixture);
        Finish("regression-results.txt");
    }

    public static void VerifyFreshEditor()
    {
        Test("Fresh Unity process reload and repeated regeneration retain all edits", VerifySavedFixture);
        Finish("fresh-editor-results.txt");
    }
}
