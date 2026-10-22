using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class StudioPolygonAuthoringValidation
{
    static int checks;
    static string current;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Mesh> meshes = new List<Mesh>();
    static void Check(bool value, string message) { if (!value) throw new Exception(current + ": " + message); checks++; }
    static StudioModelDocument.Node Primitive(PrimitiveType type)
    {
        var mesh = StudioModelDocument.BuildPrimitive(type, out var polygons); meshes.Add(mesh);
        return new StudioModelDocument.Node { name = type.ToString(), mesh = mesh, cutPolygons = polygons, cutTriangles = mesh.triangles };
    }
    static List<(int a, int b)> Edges(StudioModelDocument.Node n)
    {
        var p = n.mesh.vertices; var ids = new Dictionary<Vector3, int>();
        var keys = new HashSet<(int, int)>(); var result = new List<(int, int)>();
        foreach (var f in new StudioMeshCuts(n).Faces) for (int e = 0; e < f.Vertices.Count; e++)
        {
            int a = f.Vertices[e], b = f.Vertices[(e + 1) % f.Vertices.Count];
            if (!ids.ContainsKey(p[a])) ids[p[a]] = ids.Count;
            if (!ids.ContainsKey(p[b])) ids[p[b]] = ids.Count;
            int x = ids[p[a]], y = ids[p[b]];
            if (keys.Add((Math.Min(x, y), Math.Max(x, y)))) result.Add((a, b));
        }
        return result;
    }
    static void EditorFaces(StudioModelDocument.Node n)
    {
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>(); doc.nodes.Add(n);
        var editor = new StudioModelEditor(() => { });
        try
        {
            editor.Load(doc); editor.SelectObject(0);
            var faces = (List<StudioMeshCuts.Face>)editor.GetType().GetMethod("Faces", Hidden).Invoke(editor, null);
            var fill = (List<int>[])editor.GetType().GetField("cachedFaceTriangles", Hidden).GetValue(editor);
            Check(fill.Sum(t => t.Count) == n.mesh.triangles.Length, "Every render triangle belongs to exactly one modeling face");
            for (int f = 0; f < faces.Count; f++) Check(fill[f].Count == (faces[f].Vertices.Count - 2) * 3, "Face picking uses derived render triangles");
            var edges = (List<(int a, int b)>)editor.GetType().GetMethod("Edges", Hidden).Invoke(editor, null);
            var positions = n.mesh.vertices; var geometric = Edges(n);
            bool Same((int a, int b) x, (int a, int b) y) => (positions[x.a] == positions[y.a] && positions[x.b] == positions[y.b]) || (positions[x.a] == positions[y.b] && positions[x.b] == positions[y.a]);
            Check(edges.All(e => geometric.Any(g => Same(e, g))) && geometric.All(g => edges.Any(e => Same(e, g))), "Editor exposes all true polygon edges and no rendering diagonals, including seam copies");
        }
        finally { editor.Dispose(); Object.DestroyImmediate(doc); }
    }
    public static void Run()
    {
        try
        {
            var types = new[] { PrimitiveType.Cube, PrimitiveType.Plane, PrimitiveType.Quad, PrimitiveType.Sphere, PrimitiveType.Cylinder };
            var quads = new[] { 6, 1, 1, 448, 32 }; var triangles = new[] { 0, 0, 0, 64, 64 }; var edgeCounts = new[] { 12, 4, 4, 992, 160 };
            for (int i = 0; i < types.Length; i++)
            {
                current = "primitive " + types[i]; var n = Primitive(types[i]); var t = new StudioMeshCuts(n);
                Check(t.Faces.Count(f => f.Vertices.Count == 4) == quads[i], "Explicit quad face count");
                Check(t.Faces.Count(f => f.Vertices.Count == 3) == triangles[i], "Genuine triangle face count");
                Check(Edges(n).Count == edgeCounts[i], "No quad render diagonals in modeling edges");
                EditorFaces(n);
                if (types[i] == PrimitiveType.Sphere)
                {
                    var f = t.Faces.First(p => p.Vertices.Count == 4);
                    var loop = t.Loop(new StudioMeshCuts.Point(f.Vertices[1], f.Vertices[2], .5f));
                    Check(loop.Count > 1 && loop.All(c => t.Faces[c.Face].Vertices.Count == 4), "Loop traversal follows only real quads");
                }
            }
            current = "mixed native quad and triangle import";
            var source = new Mesh(); meshes.Add(source);
            source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up, Vector3.right * 2, new Vector3(3, 0, 0), new Vector3(2, 1, 0) };
            source.uv = Enumerable.Range(0, 7).Select(i => new Vector2(i / 7f, .5f)).ToArray();
            source.colors = Enumerable.Range(0, 7).Select(i => new Color(i / 7f, .4f, .8f, 1)).ToArray();
            source.subMeshCount = 2; source.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0); source.SetIndices(new[] { 4, 5, 6 }, MeshTopology.Triangles, 1);
            source.RecalculateNormals();
            var root = new GameObject("Mixed surface import");
            root.AddComponent<MeshFilter>().sharedMesh = source; root.AddComponent<MeshRenderer>();
            StudioModelDocument.Node mixed;
            try
            {
                var captured = (List<StudioModelDocument.Node>)typeof(StudioModelDocument).GetMethod("CaptureNodes", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
                mixed = captured[0]; meshes.Add(mixed.mesh);
            }
            finally { Object.DestroyImmediate(root); }
            var imported = new StudioMeshCuts(mixed);
            Check(imported.Faces.Count == 2 && imported.Faces[0].Vertices.Count == 4 && imported.Faces[1].Vertices.Count == 3, "Mixed face types retained on actual capture path");
            Check(source.GetTopology(0) == MeshTopology.Quads && source.GetIndices(0).Length == 4, "Source asset unchanged");
            Check(mixed.mesh.GetTopology(0) == MeshTopology.Triangles && mixed.mesh.GetTriangles(0).Length == 6 && mixed.mesh.GetTriangles(1).Length == 3, "Only owned render mesh triangulated");
            Check(source.vertices.SequenceEqual(mixed.mesh.vertices) && source.uv.SequenceEqual(mixed.mesh.uv) && source.colors.SequenceEqual(mixed.mesh.colors), "Vertex attributes preserved");
            EditorFaces(mixed);

            current = "native quad with 16-bit base vertex";
            var baseMesh = new Mesh(); meshes.Add(baseMesh); var basePoints = new Vector3[65540];
            basePoints[65536] = Vector3.zero; basePoints[65537] = Vector3.right; basePoints[65538] = new Vector3(1, 1, 0); basePoints[65539] = Vector3.up;
            baseMesh.vertices = basePoints; baseMesh.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0, true, 65536);
            var baseNode = new StudioModelDocument.Node { mesh = baseMesh }; StudioModelDocument.CaptureMeshTopology(baseNode);
            Check(baseMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 && baseMesh.triangles.SequenceEqual(new[] { 65536, 65537, 65538, 65536, 65538, 65539 }), "Base-vertex indices survive render conversion");
            Check(new StudioMeshCuts(baseNode).Faces.Single().Vertices.SequenceEqual(new[] { 65536, 65537, 65538, 65539 }), "Base-vertex polygon retained");

            current = "real coplanar triangle pair";
            var pair = new Mesh(); meshes.Add(pair); pair.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up };
            pair.triangles = new[] { 0, 1, 2, 0, 2, 3 }; pair.RecalculateNormals();
            var real = new StudioModelDocument.Node { name = "Real triangles", mesh = pair }; StudioModelDocument.CaptureMeshTopology(real);
            var realTopology = new StudioMeshCuts(real);
            Check(realTopology.Faces.Count == 2 && realTopology.Faces.All(f => f.Vertices.Count == 3), "Coplanar actual triangles are never silently merged");
            Check(Edges(real).Count == 5 && Edges(real).Any(e => (e.a == 0 && e.b == 2) || (e.a == 2 && e.b == 0)), "Actual shared triangle diagonal remains editable");
            EditorFaces(real);
            Check(realTopology.TryCut(new StudioMeshCuts.Point(0, 1, .5f), new StudioMeshCuts.Point(1, 2, .5f), out var cut), "Real triangle accepts component cuts");
            realTopology.Apply(real, new[] { cut });
            Check(new StudioMeshCuts(real).Faces.Count == 3, "Triangle editing splits only the selected triangle");

            current = "warped quad and workspace roundtrip";
            var quad = Primitive(PrimitiveType.Quad); var points = quad.mesh.vertices; points[2].z += .2f; quad.mesh.vertices = points;
            Check(new StudioMeshCuts(quad).Faces.Single().Vertices.Count == 4, "Deforming a quad does not turn it into two modeling triangles");
            EditorFaces(quad);
            var saved = new List<StudioModelDocument.Node> { mixed, real, quad };
            foreach (int subdivisions in new[] { 0, 1, 4 })
            {
                current = "single n-gon corner caps, subdivisions=" + subdivisions;
                var cube = Primitive(PrimitiveType.Cube); var topology = new StudioMeshCuts(cube);
                topology.BevelEdges(cube, Edges(cube), new StudioMeshCuts.BevelOptions { Width = .2f, Subdivisions = subdivisions, Tension = 1, Junction = StudioMeshCuts.BevelJunction.Patch });
                var faces = new StudioMeshCuts(cube).Faces; int steps = subdivisions + 1;
                Check(faces.Count == 6 + 12 * steps + 8, "Each corner is exactly one modeling face");
                Check(faces.Skip(faces.Count - 8).All(f => f.Vertices.Count == 3 * steps), "Corner perimeter follows profile without center fans");
                Check(cube.mesh.vertices.Distinct().Count() == 24 * steps, "No interior corner vertices or radial rings");
                EditorFaces(cube); saved.Add(cube);
            }
            current = "workspace preserves mixed triangles, quads and n-gons";
            var document = ScriptableObject.CreateInstance<StudioModelDocument>(); document.nodes.AddRange(saved);
            try
            {
                Directory.CreateDirectory(StudioModelDocument.WorkspaceFolder);
                var path = Path.Combine(StudioModelDocument.WorkspaceFolder, "polygon-authoring-" + Guid.NewGuid().ToString("N") + ".workspace");
                StudioWorkspaceText.Write(document, path); var restored = StudioWorkspaceText.Read(path);
                try
                {
                    Check(restored.nodes.Count == saved.Count, "All nodes restored");
                    for (int i = 0; i < saved.Count; i++)
                    {
                        Check(restored.nodes[i].cutPolygons.SequenceEqual(saved[i].cutPolygons), "Exact face boundaries persist");
                        Check(restored.nodes[i].mesh.triangles.SequenceEqual(saved[i].mesh.triangles), "Derived render buffer persists");
                        EditorFaces(restored.nodes[i]);
                    }
                }
                finally { foreach (var n in restored.nodes) if (n.mesh) Object.DestroyImmediate(n.mesh); Object.DestroyImmediate(restored); }
            }
            finally { Object.DestroyImmediate(document); }
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../polygon-authoring-result.txt")), "PASS " + checks + " polygon authoring checks");
        }
        catch (Exception e) { File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../polygon-authoring-result.txt")), current + "\n" + e); }
        finally { foreach (var m in meshes) if (m) Object.DestroyImmediate(m); EditorApplication.Exit(0); }
    }
}
