using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.AnimationStudio;
using Object = UnityEngine.Object;

public static class StudioBlackBackfacesValidation
{
    static int checks;
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Color Background = new Color(.25f, .5f, .75f, 1);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static Color32 Pixel(StudioModelEditor editor, bool back, bool drawNativeBacks, RenderTexture rt)
    {
        var previous = RenderTexture.active; var previousEvent = Event.current;
        var cameraObject = new GameObject("Back face probe"); var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, back ? 2 : -2); camera.transform.LookAt(Vector3.zero);
        camera.orthographic = true; camera.orthographicSize = .8f; camera.aspect = 1;
        camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        var renderer = editor.Transforms[0].GetComponent<Renderer>();
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt; GL.PushMatrix();
        try
        {
            GL.Clear(true, true, Background); GL.LoadProjectionMatrix(camera.projectionMatrix); GL.modelview = camera.worldToCameraMatrix;
            if (renderer.enabled && renderer.gameObject.activeInHierarchy)
            {
                Check(renderer.sharedMaterial.SetPass(0), "Surface shader unavailable");
                Graphics.DrawMeshNow(editor.ActiveMesh, renderer.localToWorldMatrix);
            }
            if (drawNativeBacks)
            {
                Event.current = new Event { type = EventType.Repaint };
                typeof(StudioModelEditor).GetMethod("DrawBlackBackfaces", F).Invoke(editor, null);
            }
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
            return texture.GetPixels32()[(rt.height / 2) * rt.width + rt.width / 2];
        }
        finally { Event.current = previousEvent; GL.PopMatrix(); RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(cameraObject); }
    }
    public static void Run() { EditorApplication.delayCall += Validate; }
    static void Validate()
    {
        int exit = 0;
        StudioModelEditor editor = null; StudioModelDocument doc = null; Mesh mesh = null; Material source = null; RenderTexture rt = null;
        string report = Path.GetFullPath(Path.Combine(Application.dataPath, "../../black-backfaces-result.txt"));
        try
        {
            doc = ScriptableObject.CreateInstance<StudioModelDocument>(); mesh = StudioModelDocument.BuildPrimitive(PrimitiveType.Quad, out var polygons);
            source = new Material(Shader.Find("Hidden/StudioValidationFront")); source.SetColor("_Color", Color.white);
            doc.nodes.Add(new StudioModelDocument.Node { name = "Black backs", mesh = mesh, materials = new[] { source }, cutPolygons = polygons, cutTriangles = mesh.triangles });
            editor = new StudioModelEditor(() => { }); editor.Load(doc); editor.SelectObject(0);
            rt = new RenderTexture(96, 96, 24); rt.Create();
            foreach (bool lighting in new[] { false, true })
            foreach (bool textures in new[] { false, true })
            {
                editor.SetDisplayMode(lighting, textures); editor.SetXRayMode(false); editor.SetBackfaceCulling(false);
                var material = editor.Transforms[0].GetComponent<Renderer>().sharedMaterial;
                bool preview = material.HasProperty("_BlackBackfaces");
                // Compare the actual assigned front material before/after the black-back change.
                if (preview) material.SetFloat("_BlackBackfaces", 0);
                var frontBefore = Pixel(editor, false, false, rt);
                if (preview) material.SetFloat("_BlackBackfaces", 1);
                var frontAfter = Pixel(editor, false, true, rt);
                Check(frontBefore.Equals(frontAfter), "Front shading changed");
                var back = Pixel(editor, true, true, rt);
                Check(back.r < 3 && back.g < 3 && back.b < 3, "Rendered back face is not black: " + back + " lighting=" + lighting + " textures=" + textures);
                editor.SetBackfaceCulling(true);
                var hidden = Pixel(editor, true, true, rt);
                Check(hidden.r > 30 && hidden.g > 90 && hidden.b > 140, "Culled back face not absent");
            }
            editor.SetDisplayMode(true, true); editor.SetBackfaceCulling(false);
            var renderer = editor.Transforms[0].GetComponent<Renderer>();
            renderer.enabled = false;
            var disabled = Pixel(editor, true, true, rt);
            Check(disabled.r > 30 && disabled.g > 90 && disabled.b > 140, "Disabled renderer left black geometry");
            renderer.enabled = true; renderer.gameObject.SetActive(false);
            var inactive = Pixel(editor, true, true, rt);
            Check(inactive.r > 30 && inactive.g > 90 && inactive.b > 140, "Inactive object left black geometry");
            renderer.gameObject.SetActive(true);
            editor.SetXRayMode(true);
            var xray = Pixel(editor, true, true, rt);
            Check(xray.r < disabled.r && xray.g < disabled.g && xray.b < disabled.b, "X-ray back face is not transparent black");
            Check(source.GetColor("_Color") == Color.white && source.GetFloat("_Cull") == 2, "Source material changed");
            File.WriteAllText(report, "PASS " + checks + " rendered black-backface assertions (preview and native-material pass)");
        }
        catch (Exception error) { exit = 1; File.WriteAllText(report, error.ToString()); }
        finally { editor?.Dispose(); if (doc) Object.DestroyImmediate(doc); if (mesh) Object.DestroyImmediate(mesh); if (source) Object.DestroyImmediate(source); if (rt) Object.DestroyImmediate(rt); }
        if (exit == 0) StudioBackfaceCullingValidation.Run(); else EditorApplication.delayCall += () => EditorApplication.Exit(1);
    }
}
