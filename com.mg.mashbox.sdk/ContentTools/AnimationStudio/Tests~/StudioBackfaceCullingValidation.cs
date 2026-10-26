using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public static class StudioBackfaceCullingValidation
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static int Pixels(Mesh mesh, Material material, bool back, RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var cameraObject = new GameObject("Culling probe camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, back ? 2 : -2);
        camera.transform.LookAt(Vector3.zero);
        camera.orthographic = true; camera.orthographicSize = .8f;
        camera.nearClipPlane = .01f; camera.farClipPlane = 10; camera.aspect = 1;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; GL.PushMatrix();
        try
        {
            GL.Clear(true, true, Color.magenta);
            GL.LoadProjectionMatrix(camera.projectionMatrix); GL.modelview = camera.worldToCameraMatrix;
            Check(material.SetPass(0), "Preview shader pass unavailable");
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
            int count = 0; foreach (var p in texture.GetPixels32()) if (p.r < 245 || p.g > 8 || p.b < 245) count++;
            return count;
        }
        finally { GL.PopMatrix(); RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(cameraObject); }
    }
    public static void Run() { EditorApplication.delayCall += Validate; }
    static void Validate()
    {
        StudioModelEditor editor = null; StudioModelDocument doc = null; Mesh mesh = null; Material source = null; RenderTexture rt = null;
        string report = Path.GetFullPath(Path.Combine(Application.dataPath, "../../backface-culling-result.txt"));
        int exit = 0;
        try
        {
            doc = ScriptableObject.CreateInstance<StudioModelDocument>();
            mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Quad, out var polygons);
            source = new Material(Shader.Find("MashBox/Studio/Preview"));
            source.SetFloat("_Cull", 0); source.SetFloat("_Unlit", 1); source.SetColor("_Color", Color.white);
            doc.nodes.Add(new StudioModelDocument.Node { name = "Culling test", mesh = mesh, cutPolygons = polygons, cutTriangles = mesh.triangles, materials = new[] { source } });
            editor = new StudioModelEditor(() => { }); editor.Load(doc);
            var renderer = editor.Transforms[0].GetComponent<Renderer>();
            var originalVertices = mesh.vertices; var originalTriangles = mesh.triangles;
            int dirty = EditorUtility.GetDirtyCount(doc);
            rt = new RenderTexture(96, 96, 24); rt.Create();
            foreach (bool lighting in new[] { false, true })
            foreach (bool textures in new[] { false, true })
            foreach (bool xray in new[] { false, true })
            {
                editor.SetDisplayMode(lighting, textures); editor.SetXRayMode(xray);
                foreach (bool enabled in new[] { false, true, false })
                {
                    editor.SetBackfaceCulling(enabled);
                    var material = renderer.sharedMaterial;
                    Check(material != source, "Renderer must use preview material, not alter asset");
                    string cull = material.HasProperty("_Cull") ? "_Cull" : "_CullMode";
                    Check(material.GetFloat(cull) == (enabled ? 2 : 0), "Displayed culling state mismatch");
                    // HDRP neutral lit is checked above; the shared preview shader is
                    // rendered directly, using the material assigned to the renderer.
                    if (material.shader == source.shader)
                    {
                        int front = Pixels(mesh, material, false, rt), back = Pixels(mesh, material, true, rt);
                        Check(front > 100, "Front-facing surface disappeared");
                        Check(enabled ? back == 0 : back > 100, "Back-facing rendered pixels do not follow toggle");
                    }
                    Check(source.GetFloat("_Cull") == 0 && source.GetColor("_Color") == Color.white, "Original material changed");
                }
            }
            editor.SetBackfaceCulling(false); StudioPreferenceStore.Flush();
            Check(StudioPreferenceStore.GetFloat("Model.BackfaceCulling", 1) == 0, "Preference not saved");
            Check(File.ReadAllText(StudioPreferenceStore.SettingsPath).Contains("Model.BackfaceCulling"), "Preference absent from file");
            typeof(StudioModelEditor).GetMethod("LoadDisplayConfiguration", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
            Check(!editor.BackfaceCulling, "Preference reload lost state");
            Check(EditorUtility.GetDirtyCount(doc) == dirty, "Display toggle dirtied document");
            Check(mesh.vertexCount == originalVertices.Length && mesh.triangles.Length == originalTriangles.Length, "Display toggle changed topology");
            var preview = renderer.sharedMaterial;
            editor.SetBackfaceCulling(true);
            Check(!preview, "Old preview material leaked");
            File.WriteAllText(report, "PASS " + checks + " backface culling assertions, including front/back GPU pixels");
        }
        catch (Exception error) { exit = 1; File.WriteAllText(report, error.ToString()); }
        finally
        {
            editor?.Dispose(); if (doc) Object.DestroyImmediate(doc); if (mesh) Object.DestroyImmediate(mesh);
            if (source) Object.DestroyImmediate(source); if (rt) Object.DestroyImmediate(rt);
            EditorApplication.delayCall += () => EditorApplication.Exit(exit);
        }
    }
}
