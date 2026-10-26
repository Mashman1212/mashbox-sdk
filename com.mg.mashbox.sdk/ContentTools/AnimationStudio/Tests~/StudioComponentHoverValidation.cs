using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class StudioComponentHoverValidation
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Call(object editor, string name, params object[] args) => editor.GetType().GetMethod(name, Flags).Invoke(editor, args);
    static object Get(object editor, string name) => editor.GetType().GetField(name, Flags).GetValue(editor);
    static void Set(object editor, string name, object value) => editor.GetType().GetField(name, Flags).SetValue(editor, value);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Debug.Log("PASS " + message);
    }
    public static void Run()
    {
        var doc = ScriptableObject.CreateInstance<StudioModelDocument>();
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
        mesh.triangles = new[] { 0,2,1,0,3,2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        doc.nodes.Add(new StudioModelDocument.Node { name = "Quad", mesh = mesh });
        using (var editor = new StudioModelEditor(() => {}))
        try
        {
            editor.Load(doc); Call(editor, "SelectObject", 0, false, false);
            foreach (string mode in new[] { "Vertex", "Edge", "Face" })
            {
                var field = editor.GetType().GetField("mode", Flags);
                field.SetValue(editor, Enum.Parse(field.FieldType, mode));
                Call(editor, "EnsureComponentDrawing");
                Set(editor, "hoveredComponent", 0); Call(editor, "EnsureComponentHover");
                var hover = (Mesh)Get(editor, "componentHover");
                Check(hover && hover.GetIndexCount(0) > 0, mode + " preview has geometry");
                Check(editor.Components.Count == 0, mode + " hover preserves selection");
                Call(editor, "EnsureComponentHover");
                Check((Mesh)Get(editor, "componentHover") == hover, mode + " stationary hover reuses buffer");
                editor.Components.Add(0); Call(editor, "EnsureComponentDrawing"); Call(editor, "EnsureComponentHover");
                Check(!(Mesh)Get(editor, "componentHover") && (Mesh)Get(editor, "componentHighlight"), mode + " selected color takes priority");
                editor.Components.Clear(); Call(editor, "EnsureComponentDrawing"); Call(editor, "EnsureComponentHover");
                Check((Mesh)Get(editor, "componentHover"), mode + " deselection restores preview");
                Set(editor, "hoveredComponent", -1); Call(editor, "EnsureComponentHover");
                Check(!(Mesh)Get(editor, "componentHover"), mode + " clearing hover releases preview");
            }
            Set(editor, "hoveredComponent", 0); Call(editor, "EnsureComponentHover");
            var previous = (Mesh)Get(editor, "componentHover");
            Call(editor, "Changed", true); Call(editor, "EnsureComponentDrawing"); Call(editor, "EnsureComponentHover");
            Check(!previous && (Mesh)Get(editor, "componentHover"), "Geometry changes replace the hover buffer");
            Call(editor, "ClearComponentDrawing");
            Check(!(Mesh)Get(editor, "componentHover"), "Cleanup releases hover geometry");
        }
        finally { Object.DestroyImmediate(doc); Object.DestroyImmediate(mesh); }
    }
}
