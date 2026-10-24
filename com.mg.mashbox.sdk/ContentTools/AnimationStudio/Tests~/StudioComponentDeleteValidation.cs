using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public sealed class StudioDeleteInputHost : EditorWindow
{
    internal StudioModelEditor editor;
    internal bool handled;
    void OnGUI() { if (editor != null) handled = editor.HandleObjectDelete(); }
}
public static class StudioComponentDeleteValidation
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Run() { EditorApplication.delayCall += Validate; }
    static void Validate()
    {
        int exit = 0;
        string report = Path.GetFullPath(Path.Combine(Application.dataPath, "../../component-delete-result.txt"));
        try
        {
            foreach (var mode in new[] { StudioModelEditor.ComponentMode.Vertex, StudioModelEditor.ComponentMode.Edge, StudioModelEditor.ComponentMode.Face })
            foreach (bool weld in new[] { false, true })
            foreach (bool all in new[] { false, true })
                Case(mode, weld, all);
            QuadCorners();
            ImportedFrame();
            File.WriteAllText(report, "PASS " + checks + " component delete assertions");
        }
        catch (Exception error) { exit = 1; File.WriteAllText(report, error.ToString()); }
        EditorApplication.delayCall += () => EditorApplication.Exit(exit);
    }
    static void QuadCorners()
    {
        foreach (bool reverse in new[] { false, true })
        for (int corner = 0; corner < 4; corner++)
        {
            var mesh = new Mesh();
            var positions = new[] { Vector3.zero, new Vector3(3,0,0), new Vector3(2,2,0), new Vector3(0,1,0) };
            mesh.vertices = positions; mesh.subMeshCount = 2;
            mesh.SetTriangles(Array.Empty<int>(), 0);
            mesh.SetTriangles(reverse ? new[] { 3,2,1,3,1,0 } : new[] { 0,1,2,0,2,3 }, 1);
            var boundary = reverse ? new[] { 3,2,1,0 } : new[] { 0,1,2,3 };
            var node = new StudioModelDocument.Node { mesh = mesh, cutPolygons = new[] { 1,4 }.Concat(boundary).ToArray(), cutTriangles = mesh.triangles };
            var cuts = new StudioMeshCuts(node); int writes = 0;
            cuts.DeleteComponents(node, new HashSet<int>(), new HashSet<int> { corner }, Array.Empty<(int,int)>(), false, () => writes++);
            var result = new StudioMeshCuts(node);
            Check(writes == 1 && result.Faces.Count == 1 && result.Faces[0].Vertices.Count == 3, "Quad corner deletion removed face instead of clipping corner");
            Check(mesh.vertices.SequenceEqual(positions.Where((v,i) => i != corner)), "Wrong quad corner retained or moved");
            Check(mesh.subMeshCount == 2 && mesh.GetIndices(0).Length == 0 && mesh.GetIndices(1).Length == 3, "Quad corner deletion changed material slots");
            var v = mesh.vertices; var t = mesh.triangles;
            Check(Vector3.Cross(v[t[1]]-v[t[0]], v[t[2]]-v[t[0]]).z * (reverse ? -1 : 1) > 0, "Reduced triangle winding reversed");
            Object.DestroyImmediate(mesh);
        }
    }
    static void Case(StudioModelEditor.ComponentMode mode, bool weld, bool all)
    {
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        var mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Cube, out var polygons);
        var positions = mesh.vertices;
        mesh.colors = positions.Select((p, i) => new Color(i / 30f, .5f, .9f, 1)).ToArray();
        for (int c = 0; c < 8; c++)
        {
            var values = positions.Select((p, i) => new Vector4(i + .1f, c + .2f, i + .3f, .4f)).ToList();
            if (c % 3 == 0) mesh.SetUVs(c, values.Select(v => new Vector2(v.x, v.y)).ToList());
            else if (c % 3 == 1) mesh.SetUVs(c, values.Select(v => new Vector3(v.x, v.y, v.z)).ToList());
            else mesh.SetUVs(c, values);
        }
        doc.nodes.Add(new StudioModelDocument.Node { name = "Delete test", mesh = mesh, cutPolygons = polygons, cutTriangles = mesh.triangles });
        var host = ScriptableObject.CreateInstance<StudioDeleteInputHost>();
        using (var editor = new StudioModelEditor(() => { }))
        try
        {
            editor.Load(doc); editor.SelectObject(0); editor.SetToolbarComponentMode(mode);
            typeof(StudioModelEditor).GetField("weld", F).SetValue(editor, weld);
            var source = JsonUtility.ToJson(doc.nodes[0]);
            var original = Object.Instantiate(editor.ActiveMesh);
            var topology = new StudioMeshCuts(doc.nodes[0]);
            int count = mode == StudioModelEditor.ComponentMode.Vertex ? mesh.vertexCount
                : mode == StudioModelEditor.ComponentMode.Face ? topology.Faces.Count
                : ((List<(int, int)>)typeof(StudioModelEditor).GetMethod("Edges", F).Invoke(editor, null)).Count;
            editor.Components.Clear(); editor.Components.UnionWith(all ? Enumerable.Range(0, count) : new[] { 0 });
            host.editor = editor; host.Show();
            host.SendEvent(new Event { type = EventType.ValidateCommand, commandName = "Delete" });
            Check(mesh.triangles.SequenceEqual(original.triangles), "ValidateCommand mutated mesh");
            var type = mode == StudioModelEditor.ComponentMode.Face ? EventType.ExecuteCommand : EventType.KeyDown;
            host.SendEvent(new Event { type = type, commandName = "Delete", keyCode = mode == StudioModelEditor.ComponentMode.Edge ? KeyCode.Backspace : KeyCode.Delete });
            Check(host.handled, "Delete input not handled in " + mode);
            string error = (string)typeof(StudioModelEditor).GetProperty("error", F).GetValue(editor);
            Check(string.IsNullOrEmpty(error), "Delete reported: " + error);
            var result = new StudioMeshCuts(doc.nodes[0]);
            int expectedRemoved = mode == StudioModelEditor.ComponentMode.Face || !weld ? 1 : mode == StudioModelEditor.ComponentMode.Edge ? 2 : 3;
            Check(result.Faces.Count == (all ? 0 : mode == StudioModelEditor.ComponentMode.Vertex ? 6 : 6 - expectedRemoved), "Wrong retained-face count in " + mode + " weld=" + weld);
            if (!all && mode == StudioModelEditor.ComponentMode.Vertex)
            {
                int clippedFaces = weld ? 3 : 1;
                Check(result.Faces.Count(f => f.Vertices.Count == 3) == clippedFaces, "Deleted quad corners did not become triangles");
                Check(mesh.triangles.Length == (12 - clippedFaces) * 3, "Corner deletion produced wrong render triangle count");
            }
            Check(editor.Document.nodes.Count == 1 && editor.Active == 0, "Component deletion removed object");
            Check(editor.Components.Count == 0, "Deleted selection not cleared");
            Check(mesh.vertexCount == (all ? 0 : mesh.triangles.Distinct().Count()), "Unreferenced vertices retained");
            Check(editor.Transforms[0].GetComponent<MeshFilter>().sharedMesh == mesh, "Renderer lost edited mesh");
            var ids = mesh.colors.Select(color => (int)Math.Round(color.r * 30)).ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                Check(mesh.vertices[i] == original.vertices[ids[i]], "Retained position changed");
                Check(mesh.normals[i] == original.normals[ids[i]], "Retained normal changed");
            }
            for (int c = 0; c < 8 && mesh.vertexCount > 0; c++)
            {
                var actual = new List<Vector4>(); var before = new List<Vector4>(); mesh.GetUVs(c, actual); original.GetUVs(c, before);
                Check(actual.SequenceEqual(ids.Select(i => before[i])), "UV values changed");
                Check(mesh.GetVertexAttributeDimension((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)) == original.GetVertexAttributeDimension((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)), "UV dimension changed");
            }
            var finalTriangles = mesh.triangles; var finalPositions = mesh.vertices;
            UnityEditor.Undo.FlushUndoRecordObjects();
            editor.PerformWorkspaceHistory(false);
            Check(mesh.vertices.SequenceEqual(original.vertices) && mesh.triangles.SequenceEqual(original.triangles), "Undo failed " + mode);
            Check(doc.nodes[0].cutPolygons.SequenceEqual(polygons), "Undo failed polygon topology");
            Check(editor.Components.Count == (all ? count : 1), "Undo did not restore component selection");
            editor.PerformWorkspaceHistory(true);
            Check(mesh.vertices.SequenceEqual(finalPositions) && mesh.triangles.SequenceEqual(finalTriangles), "Redo failed " + mode);
            Object.DestroyImmediate(original);
        }
        finally { host.editor = null; host.Close(); Object.DestroyImmediate(doc); Object.DestroyImmediate(mesh); }
    }
    static void ImportedFrame()
    {
        var path = "Assets/FrameProbe.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!model) throw new Exception("FrameProbe.fbx required for imported polygon regression");
        var source = model.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).OrderByDescending(m => m.vertexCount).First();
        var mesh = StudioWorkspaceText.ReadableSnapshot(source);
        try
        {
            var node = new StudioModelDocument.Node { name = "Imported", mesh = mesh };
            StudioModelDocument.CaptureMeshTopology(node);
            var topology = new StudioMeshCuts(node);
            Check(topology.Faces.All(f => f.Vertices.All(v => v >= 0 && v < mesh.vertexCount)), "Source face index outside mesh");
            int faces = topology.Faces.Count;
            topology.DeleteComponents(node, new HashSet<int> { 0 }, new HashSet<int>(), Array.Empty<(int, int)>(), true, null);
            for (int at = 0; at < node.cutPolygons.Length;) { int sub = node.cutPolygons[at++], count = node.cutPolygons[at++]; for (int i = 0; i < count; i++) { int vertex = node.cutPolygons[at++]; Check(vertex >= 0 && vertex < mesh.vertexCount, "Output vertex " + vertex + " outside " + mesh.vertexCount + " sub=" + sub); } }
            Check(new StudioMeshCuts(node).Faces.Count == faces - 1, "Imported-frame face deletion failed");
            Check(mesh.vertexCount > 1000 && mesh.triangles.Length > 1000, "Imported frame unexpectedly destroyed");
        }
        finally { Object.DestroyImmediate(mesh); }
    }
}
