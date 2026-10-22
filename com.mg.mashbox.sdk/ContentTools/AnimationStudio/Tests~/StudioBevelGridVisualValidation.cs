using System;using System.IO;using System.Linq;using System.Reflection;using MashBoxSDK.AnimationStudio;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
public static class StudioBevelGridVisualValidation
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static AnimationStudioWindow owner;static MashBoxStudioViewport view;static StudioModelDocument doc;static ToolChannelPane pane;static double due;static int phase;
 public static void Run(){EditorApplication.delayCall+=Begin;}
 static void Begin(){try{
  doc=ScriptableObject.CreateInstance<StudioModelDocument>();var mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube,out var polygons);var node=new StudioModelDocument.Node{name="Rounded grid junction",mesh=mesh,cutPolygons=polygons,cutTriangles=mesh.triangles};doc.nodes.Add(node);
  var t=new StudioMeshCuts(node);var edges=t.Faces.SelectMany(f=>Enumerable.Range(0,f.Vertices.Count).Select(i=>(f.Vertices[i],f.Vertices[(i+1)%f.Vertices.Count]))).ToArray();
  t.BevelEdges(node,edges,new StudioMeshCuts.BevelOptions{Width=.5f,Subdivisions=3,Tension=1,Junction=StudioMeshCuts.BevelJunction.Uniform});
  owner=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",F).SetValue(owner,true);typeof(AnimationStudioWindow).GetField("modelDocument",F).SetValue(owner,doc);typeof(AnimationStudioWindow).GetMethod("OpenViewport",F).Invoke(owner,null);
  view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==owner);view.position=new Rect(0,0,1200,800);view.pivot=new Vector3(.4f,.4f,.4f);view.rotation=Quaternion.LookRotation(-Vector3.one.normalized);view.size=.48f;view.modelGridVisible=false;
  var editor=owner.ToolbarModelEditor;editor.SelectObject(0);editor.SetToolbarComponentMode(StudioModelEditor.ComponentMode.Edge);editor.SetModelDrawMode(view,DrawCameraMode.TexturedWire);editor.Components.Add(0);
  typeof(StudioModelEditor).GetMethod("OpenBevelTool",F).Invoke(editor,null);pane=Resources.FindObjectsOfTypeAll<ToolChannelPane>().Single(p=>p.titleContent.text=="Bevel");pane.position=new Rect(810,90,340,235);
  due=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Tick;
 }catch(Exception e){Finish(e);}}
 static void Tick(){if(EditorApplication.timeSinceStartup<due)return;try{if(phase++==0){view.orthographic=true;view.LookAtDirect(new Vector3(.38f,.38f,.38f),Quaternion.LookRotation(-Vector3.one.normalized),.4f);view.Repaint();due=EditorApplication.timeSinceStartup+.75;return;}Snapshot();Finish(null);}catch(Exception e){Finish(e);}}
    static void Snapshot()
    {
        var parent = typeof(EditorWindow).GetField("m_Parent", F).GetValue(view);
        var rt = new RenderTexture(1200, 800, 0); var pixels = new Texture2D(1200, 800, TextureFormat.RGBA32, false); var old = RenderTexture.active;
        try
        {
            rt.Create(); parent.GetType().GetMethod("GrabPixels", F).Invoke(parent, new object[] { rt, new Rect(0, 0, 1200, 800) });
            RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
            var colors = pixels.GetPixels(); var flipped = new Color[colors.Length];
            for (int y = 0; y < 800; y++) Array.Copy(colors, y * 1200, flipped, (799-y)*1200, 1200);
            pixels.SetPixels(flipped); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../bevel-grid-corner.png"), pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = old; Object.DestroyImmediate(pixels); Object.DestroyImmediate(rt); }
    }

 static void Finish(Exception e){EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(Application.dataPath,"../bevel-grid-visual-result.txt"),e==null?"PASS grid corner and restored tool pane rendered":e.ToString());if(pane)pane.Close();if(view)view.Close();if(owner)Object.DestroyImmediate(owner);if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}EditorApplication.Exit(0);}
}
