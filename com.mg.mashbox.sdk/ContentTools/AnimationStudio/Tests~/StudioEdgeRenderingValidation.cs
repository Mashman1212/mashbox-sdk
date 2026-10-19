using System;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Geometry/offset regression checks. Use StudioHdrpOutlineValidation for actual
// SceneView rendering; the former Camera.Render test did not cover the HDRP path.
public static class StudioEdgeRenderingValidation
{
    static int checks;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks++;Debug.Log("PASS "+message);}
    public static void Run()
    {
        var doc=ScriptableObject.CreateInstance<StudioModelDocument>();
        var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube);
        doc.nodes.Add(new StudioModelDocument.Node{name="Cube",mesh=mesh});
        var editor=new StudioModelEditor(()=>{});var go=new GameObject("Outline validation camera");var camera=go.AddComponent<Camera>();
        try
        {
            editor.Load(doc);editor.SelectObject(0);
            var lines=editor.SelectionWireframeLines(0);
            Check(lines.Length==24,"Regular outline retains twelve cube edges without diagonals");
            Check(ReferenceEquals(lines,editor.SelectionWireframeLines(0)),"Regular outline reuses cached geometry");
            typeof(StudioModelEditor).GetField("outlineCamera",Flags).SetValue(editor,camera);
            var offset=typeof(StudioModelEditor).GetMethod("OffsetOutlineDepth",Flags);
            foreach(bool orthographic in new[]{false,true})
            foreach(var angle in new[]{new Vector3(3,1,-5),new Vector3(5,.2f,-.1f),new Vector3(.1f,4,-5),new Vector3(-5,2,-1)})
            {
                camera.orthographic=orthographic;camera.transform.position=angle;camera.transform.LookAt(Vector3.zero);
                var point=new Vector3(.3f,-.2f,-.5f);var result=(Vector3)offset.Invoke(editor,new object[]{point});
                var before=camera.WorldToViewportPoint(point);var after=camera.WorldToViewportPoint(result);
                Check(Vector2.Distance(new Vector2(before.x,before.y),new Vector2(after.x,after.y))<.00001f && after.z<before.z,
                    "Regular outline offset preserves screen position, ortho="+orthographic+", angle="+angle);
            }
            editor.SetXRayMode(true);
            var original=new Vector3(.3f,-.2f,-.5f);
            Check((Vector3)offset.Invoke(editor,new object[]{original})==original,"X-ray bypasses outline depth offset");
            editor.Changed();
            Check(!ReferenceEquals(lines,editor.SelectionWireframeLines(0)),"Geometry edits invalidate cached outlines");
            Debug.Log("STUDIO_EDGE_RENDERING_VALIDATION_SUCCESS "+checks+" checks");
        }
        finally {editor.Dispose();Object.DestroyImmediate(go);Object.DestroyImmediate(doc);Object.DestroyImmediate(mesh);}
    }
}
