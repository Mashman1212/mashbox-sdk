using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class StudioUVToolkitValidation
{
    private static readonly List<string> Results = new List<string>();
    private static readonly List<Object> Cleanup = new List<Object>();
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static StudioModelEditor editor;
    private static StudioUVEditor window;
    private static double next;
    private static int step;
    private static string errors = "";
    private static bool preview;
    private const string AssetPath = "Assets/UVToolkitValidation.asset";
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Results.Add("PASS: " + message); }
    private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-8f;
    public static void Preview() { preview = true; Run(); }
    public static void Run()
    {
        try
        {
            TestRemap(); TestLayout(); TestRelax(); TestProjections();
            AssetDatabase.DeleteAsset(AssetPath);
            var document = StudioModelDocument.CreateEmpty(AssetPath); document.AddPrimitive(PrimitiveType.Cube);
            editor = new StudioModelEditor(() => { }); editor.Load(document); editor.SelectObject(0);
            StudioUVEditor.Open(editor, 0); window = Resources.FindObjectsOfTypeAll<StudioUVEditor>().Single();
            window.position = new Rect(100, 100, 1050, 800);
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
            next = EditorApplication.timeSinceStartup + 1;
        }
        catch (Exception e) { Finish(1, e.ToString()); }
    }
    private static void TestRemap()
    {
        var mesh = new Mesh { name = "Attribute preservation" }; Cleanup.Add(mesh);
        var positions = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up };
        mesh.vertices = positions; mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
        mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
        mesh.colors = new[] { Color.red, Color.green, Color.blue, Color.white };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        var three = Enumerable.Range(0, 4).Select(i => new Vector3(i, i + 1, i + 2)).ToList(); mesh.SetUVs(2, three);
        var four = Enumerable.Range(0, 4).Select(i => new Vector4(i, i + 1, i + 2, i + 3)).ToList(); mesh.SetUVs(7, four);
        mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) };
        var weights = Enumerable.Range(0, 4).SelectMany(i => new[] { new BoneWeight1 { boneIndex = 0, weight = .75f }, new BoneWeight1 { boneIndex = 1, weight = .25f } }).ToArray();
        using (var counts = new NativeArray<byte>(new byte[] { 2, 2, 2, 2 }, Allocator.Temp))
        using (var bones = new NativeArray<BoneWeight1>(weights, Allocator.Temp)) mesh.SetBoneWeights(counts, bones);
        mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 0, 2, 3 }, 1);
        var delta = positions.Select(v => v + Vector3.forward).ToArray(); mesh.AddBlendShapeFrame("Smile", 100, delta, new Vector3[4], new Vector3[4]);
        var corners = new[] { Vector2.zero, Vector2.right, Vector2.one, new Vector2(2, 0), new Vector2(3, 1), new Vector2(2, 1) };
        Undo.IncrementCurrentGroup();
        StudioUVTools.ApplyCornerUVs(mesh, 1, corners); Undo.FlushUndoRecordObjects();
        Check(mesh.vertexCount == 6 && mesh.subMeshCount == 2, "UV seam split retains both material submeshes");
        int[] map = { 0, 1, 2, 0, 2, 3 };
        var got3 = new List<Vector3>(); var got4 = new List<Vector4>(); mesh.GetUVs(2, got3); mesh.GetUVs(7, got4);
        Check(Enumerable.Range(0, 6).All(i => mesh.vertices[i] == positions[map[i]] && got3[i] == three[map[i]] && got4[i] == four[map[i]]), "Seam split preserves geometry and 3D/4D UV channels");
        Check(mesh.uv2.SequenceEqual(corners) && mesh.uv[3] == Vector2.zero, "Only the target UV set is replaced");
        using (var counts = mesh.GetBonesPerVertex()) using (var bones = mesh.GetAllBoneWeights())
            Check(counts.Length == 6 && counts.All(c => c == 2) && bones.Length == 12 && bones[7].boneIndex == 1 && Mathf.Approximately(bones[7].weight, .25f), "Variable skin weights survive remapping");
        var gotDelta = new Vector3[6]; mesh.GetBlendShapeFrameVertices(0, 0, gotDelta, null, null);
        Check(mesh.blendShapeCount == 1 && mesh.GetBlendShapeName(0) == "Smile" && Enumerable.Range(0, 6).All(i => gotDelta[i] == delta[map[i]]), "Blend-shape frames survive UV seams");
        Check(mesh.bindposes.Length == 2 && mesh.colors[3] == Color.red && mesh.normals.Length == 6 && mesh.tangents.Length == 6, "Bind poses and render attributes survive remapping");
        Undo.PerformUndo(); Check(mesh.vertexCount == 4 && mesh.uv2.Length == 0 && mesh.subMeshCount == 2, "Undo restores topology and the prior UV channel");
        Undo.PerformRedo(); Check(mesh.vertexCount == 6 && mesh.uv2.SequenceEqual(corners), "Redo restores UV seams and data");
        bool rejected = false; try { StudioUVTools.ApplyCornerUVs(mesh, 1, new[] { new Vector2(float.NaN, 0) }); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && mesh.vertexCount == 6, "Invalid unwrap results leave the mesh unchanged");
        // Sew one shared corner across a seam, leaving noncoincident vertices untouched.
        var uv = mesh.uv2.ToList(); var untouched = uv[1];
        StudioUVTools.Sew(mesh, uv, new[] { 0, 3, 1 }, .0001f);
        Check(Near(uv[0], uv[3]) && Near(uv[0], new Vector2(1, 0)) && uv[1] == untouched, "Sew joins selected coincident vertices without moving unrelated UVs");
    }
    private static void TestLayout()
    {
        var uv = new List<Vector2> { new Vector2(-3, -2), new Vector2(-1, -2), new Vector2(-1, -1), new Vector2(8, 9), new Vector2(9, 9), new Vector2(9, 10), new Vector2(20, 20) };
        var shells = new List<List<int>> { new List<int> { 0, 1, 2 }, new List<int> { 3, 4, 5 } };
        StudioUVTools.Pack(uv, shells, .04f);
        var a = StudioUVTools.Bounds(uv, shells[0]); var b = StudioUVTools.Bounds(uv, shells[1]);
        Check(!a.Overlaps(b) && a.xMin >= 0 && b.xMax <= 1 && a.yMin >= 0 && b.yMax <= 1, "Packing places shell bounds within the tile without overlap");
        Check(Mathf.Abs(a.width / a.height - 2) < .0001f && Mathf.Abs(a.width / b.width - 2) < .0001f && uv[6] == new Vector2(20, 20), "Packing preserves aspect, relative scale, and unselected UVs");
    }
    private static void TestRelax()
    {
        var mesh = new Mesh(); Cleanup.Add(mesh);
        mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up, new Vector3(.5f, .5f) };
        mesh.triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 };
        var uv = new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, new Vector2(.8f, .7f) };
        var selected = new HashSet<int>(Enumerable.Range(0, 5));
        var border = StudioUVTools.Border(mesh, uv); Check(border.SetEquals(new[] { 0, 1, 2, 3 }), "UV border detection excludes the interior vertex");
        StudioUVTools.Relax(mesh, uv, selected, new HashSet<int>(), 20, true);
        Check((uv[4] - Vector2.one * .5f).magnitude < .001f && uv[0] == Vector2.zero && uv[2] == Vector2.one, "Relax smooths the interior and preserves boundaries");
        uv[4] = new Vector2(.8f, .7f); StudioUVTools.Relax(mesh, uv, selected, new HashSet<int> { 4 }, 20, true);
        Check(uv[4] == new Vector2(.8f, .7f), "Pinned UVs stay fixed during relaxation");
        Check(StudioUVTools.Shells(mesh, uv).Count == 1, "Connected triangulated surface forms one shell");
    }
    private static void TestProjections()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(go); Cleanup.Add(mesh);
        var uv = Enumerable.Repeat(Vector2.zero, mesh.vertexCount).ToList(); var indices = Enumerable.Range(0, mesh.vertexCount).ToArray();
        for (int kind = 0; kind < 3; kind++) for (int axis = 0; axis < 3; axis++)
        { StudioUVTools.Project(mesh, uv, indices, kind, axis); Check(uv.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.y) && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1), "Projection " + kind + " axis " + axis + " produces finite normalized UVs"); }
        var old = mesh.vertices; var corners = Unwrapping.GeneratePerTriangleUV(mesh); StudioUVTools.ApplyCornerUVs(mesh, 0, corners);
        Check(mesh.uv.Length == mesh.vertexCount && mesh.uv.All(v => !float.IsNaN(v.x)) && mesh.triangles.Length == 36 && old.All(v => mesh.vertices.Contains(v)), "Unity unwrap produces a valid cube layout and preserves geometry");
    }
    private static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n" + stack; }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .6;
        try
        {
            switch (step++)
            {
                case 0:
                    var selection = (HashSet<int>)typeof(StudioUVEditor).GetField("selection", Flags).GetValue(window); selection.UnionWith(new[] { 0, 1 });
                    typeof(StudioUVEditor).GetMethod("Align", Flags).Invoke(window, new object[] { editor.ActiveMesh.uv.ToList(), 0, 0f }); window.Repaint(); break;
                case 1:
                    Check(editor.ActiveMesh.uv[0].x == editor.ActiveMesh.uv[1].x, "Sidebar alignment action edits the selected mesh");
                    typeof(StudioModelEditor).GetMethod("Edges", Flags).Invoke(editor, null);
                    typeof(StudioUVEditor).GetMethod("AutoUnwrap", Flags).Invoke(window, null); window.Repaint(); break;
                case 2:
                    Check(editor.ActiveMesh.uv.Length == editor.ActiveMesh.vertexCount, "Sidebar automatic unwrap updates the active model");
                    Check(typeof(StudioModelEditor).GetField("cachedEdges", Flags).GetValue(editor) == null, "UV topology changes invalidate the modeling edge cache");
                    editor.Save(); AssetDatabase.SaveAssets();
                    Check(string.IsNullOrEmpty(errors), "UV toolkit renders and edits without GUI exceptions: " + errors);
                    if (preview) { ((HashSet<int>)typeof(StudioUVEditor).GetField("selection", Flags).GetValue(window)).UnionWith(Enumerable.Range(0, editor.ActiveMesh.vertexCount)); var main = EditorGUIUtility.GetMainWindowPosition(); window.position = new Rect(main.x + 100, main.y + 100, 1050, 800); window.Show(); window.Focus(); window.Repaint(); break; }
                    Finish(0, null); break;
                default:
                    if (preview && File.Exists("uv-preview-finish.txt")) Finish(0, null);
                    break;
            }
        }
        catch (Exception e) { Finish(1, e.ToString()); }
    }
    private static void Finish(int code, string failure)
    {
        if (failure != null) Results.Add("FAIL: " + failure);
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        if (window) window.Close(); editor?.Dispose(); AssetDatabase.DeleteAsset(AssetPath);
        foreach (var obj in Cleanup) if (obj) Object.DestroyImmediate(obj);
        File.WriteAllLines("uv-toolkit-result.txt", Results); EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}

