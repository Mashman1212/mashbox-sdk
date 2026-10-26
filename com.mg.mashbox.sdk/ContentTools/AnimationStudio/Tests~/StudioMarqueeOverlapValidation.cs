using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public static class StudioMarqueeOverlapValidation
{
    static int checks;
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static void Run() { EditorApplication.delayCall += Validate; }
    static void Validate()
    {
        var go = new GameObject("Marquee camera"); var camera = go.AddComponent<Camera>();
        int exit = 0;
        try
        {
            camera.transform.position = Vector3.zero; camera.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.fieldOfView = 60; camera.aspect = 1; camera.orthographicSize = 2;
            foreach (bool ortho in new[] { false, true })
            foreach (bool originAtEye in new[] { false, true })
            {
                camera.orthographic = ortho;
                Ray RayAt(float x, float y)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(x, y, 0));
                    return originAtEye && !ortho ? new Ray(camera.transform.position, ray.direction) : ray;
                }
                var volume = new StudioModelEditor.MarqueeVolume(camera, RayAt(.4f,.6f), RayAt(.6f,.6f), RayAt(.6f,.4f), RayAt(.4f,.4f), RayAt(.5f,.5f));
                Check(volume.Segment(new Vector3(-3,0,3), new Vector3(.2f,0,3), out var edge), "Partial edge overlap missed");
                Check(Mathf.Abs(edge.x) < 1, "Edge visibility witness outside clipped portion");
                Check(volume.Segment(new Vector3(-3,0,3), new Vector3(3,0,3), out _), "Crossing edge with endpoints outside missed");
                Check(!volume.Segment(new Vector3(3,3,3), new Vector3(4,4,3), out _), "Disjoint edge selected");
                Check(volume.Triangle(new Vector3(-4,-3,3), new Vector3(4,-3,3), new Vector3(0,4,3), out var face), "Rectangle inside large face missed");
                Check(Mathf.Abs(face.x) < 1 && Mathf.Abs(face.y) < 1, "Face witness outside overlap");
                Check(!volume.Triangle(new Vector3(3,3,3), new Vector3(4,3,3), new Vector3(3,4,3), out _), "Disjoint face selected");
                Check(!volume.Triangle(new Vector3(-1,-1,-3), new Vector3(1,-1,-3), new Vector3(0,1,-3), out _), "Behind-camera face selected");
                Check(volume.Segment(new Vector3(0,0,-1), new Vector3(0,0,3), out var clipped) && clipped.z >= .1f, "Near-plane crossing lost");
            }
            StartNative(); return;
        }
        catch (Exception error) { exit = 1; File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../marquee-overlap-result.txt")), error.ToString()); }
        finally { Object.DestroyImmediate(go); }
        EditorApplication.delayCall += () => EditorApplication.Exit(exit);
    }
    static SceneView nativeView;
    static StudioModelEditor nativeEditor;
    static StudioModelDocument nativeDoc;
    static Mesh nativeMesh;
    static int nativeStage;
    static void StartNative()
    {
        nativeDoc = ScriptableObject.CreateInstance<StudioModelDocument>();
        nativeMesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Quad, out var polygons);
        nativeDoc.nodes.Add(new StudioModelDocument.Node { name = "Overlap", mesh = nativeMesh, cutPolygons = polygons, cutTriangles = nativeMesh.triangles });
        nativeEditor = new StudioModelEditor(() => { }); nativeEditor.Load(nativeDoc); nativeEditor.SelectObject(0); nativeEditor.ActivateTool(Tool.None);
        nativeView = ScriptableObject.CreateInstance<SceneView>(); nativeView.position = new Rect(50,50,700,600); nativeView.Show();
        nativeView.LookAtDirect(Vector3.zero, Quaternion.identity, 1); nativeView.orthographic = false;
        SceneView.duringSceneGui += NativeGUI; nativeView.Repaint();
    }
    static void NativeGUI(SceneView view)
    {
        if (view != nativeView || Event.current.type != EventType.Repaint || nativeStage >= 2) return;
        try
        {
            var editor = nativeEditor; var mesh = nativeMesh;
            void Pick(Rect rect) => typeof(StudioModelEditor).GetMethod("Pick", F).Invoke(editor, new object[] { view, rect, false });
            typeof(StudioModelEditor).GetField("xray", F).SetValue(editor, true);
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Face);
            var center = HandleUtility.WorldToGUIPoint(new Vector3(-.3f,.25f,0));
            var rect = new Rect(center.x-3,center.y-3,6,6);
            Check(!rect.Contains(HandleUtility.WorldToGUIPoint(Vector3.zero)), "Test must exclude face center");
            Pick(rect); Check(editor.Components.SetEquals(new[] { 0 }), "Native face overlap missed");
            typeof(StudioModelEditor).GetField("xray", F).SetValue(editor, false);
            editor.Components.Clear(); Pick(rect); Check(editor.Components.SetEquals(new[] { 0 }), "Visible partial face rejected");
            typeof(StudioModelEditor).GetField("subtract", F).SetValue(editor, true);
            Pick(rect); Check(editor.Components.Count == 0, "Subtract overlap failed");
            typeof(StudioModelEditor).GetField("subtract", F).SetValue(editor, false);
            typeof(StudioModelEditor).GetField("xray", F).SetValue(editor, true);
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);
            var edges = (System.Collections.Generic.List<(int a,int b)>)typeof(StudioModelEditor).GetMethod("Edges",F).Invoke(editor,null);
            var positions = mesh.vertices;
            var sample = Vector3.Lerp(positions[edges[0].a], positions[edges[0].b], .15f);
            center = HandleUtility.WorldToGUIPoint(sample); rect = new Rect(center.x-3,center.y-3,6,6);
            Pick(rect); Check(editor.Components.Contains(0), "Partial edge without midpoint missed");
            Pick(new Rect(center.x, center.y-3, 0, 6)); Check(editor.Components.Contains(0) && editor.Components.Count < edges.Count, "Thin marquee became an unrestricted volume");
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Vertex);
            center = HandleUtility.WorldToGUIPoint(positions[0]); rect = new Rect(center.x+1,center.y-1,2,2);
            Pick(rect); Check(editor.Components.Contains(0), "Partial vertex marker overlap missed");
            editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Object);
            center = HandleUtility.WorldToGUIPoint(new Vector3(-.3f,.25f,0)); rect = new Rect(center.x-3,center.y-3,6,6);
            Pick(rect); Check(editor.Active == 0, "Object face overlap without enclosed vertices missed");
            nativeStage++;
            if (nativeStage == 1) EditorApplication.delayCall += () => { nativeView.orthographic = true; nativeView.Repaint(); };
            else EditorApplication.delayCall += () => FinishNative(null);
        }
        catch (Exception error) { nativeStage = 2; EditorApplication.delayCall += () => FinishNative(error.ToString()); }
    }
    static void FinishNative(string failure)
    {
        SceneView.duringSceneGui -= NativeGUI;
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../marquee-overlap-result.txt")), failure ?? ("PASS " + checks + " marquee overlap assertions"));
        if (nativeView) nativeView.Close(); nativeEditor?.Dispose();
        if (nativeDoc) Object.DestroyImmediate(nativeDoc); if (nativeMesh) Object.DestroyImmediate(nativeMesh);
        EditorApplication.delayCall += () => EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
