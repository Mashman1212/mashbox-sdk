using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.AnimationStudio;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class StudioSilhouetteValidation
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static AnimationStudioWindow controller;static MashBoxStudioViewport view;static StudioModelEditor editor;static StudioModelDocument doc;static double next;
    public static void Run(){EditorApplication.delayCall+=Begin;}
    static void Begin()
    {
        try
        {
            StudioSettings.Current.viewportVSync=false;StudioSettings.Current.disableViewportImageCache=true;StudioSettings.Current.showWorkspaceFps=false;StudioSettings.Current.hideObjectWireframe=false;
            doc=ScriptableObject.CreateInstance<StudioModelDocument>();doc.nodes.Add(new StudioModelDocument.Node{name="Silhouette cube",mesh=StudioModelDocument.BuildPrimitive(PrimitiveType.Cube)});
            controller=ScriptableObject.CreateInstance<AnimationStudioWindow>();typeof(AnimationStudioWindow).GetField("modelingMode",Flags).SetValue(controller,true);typeof(AnimationStudioWindow).GetField("modelDocument",Flags).SetValue(controller,doc);typeof(AnimationStudioWindow).GetMethod("OpenViewport",Flags).Invoke(controller,null);
            view=Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>().Single(v=>v.Studio==controller);view.position=new Rect(0,0,1400,950);
            editor=(StudioModelEditor)typeof(AnimationStudioWindow).GetField("modelEditor",Flags).GetValue(controller);editor.SelectObject(0);
            next=EditorApplication.timeSinceStartup+4;EditorApplication.update+=Tick;
        }catch(Exception e){Finish(e);}
    }
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);Debug.Log("SILHOUETTE_CHECK "+text);}
    static void Paint(){view.MarkDirty(MashBoxStudioViewport.DirtyState.All);view.SendEvent(new Event{type=EventType.Layout});view.SendEvent(new Event{type=EventType.Repaint});}
    static Color[] Pixels(string name)
    {
        var parent=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(view);var rt=new RenderTexture(700,700,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var image=new Texture2D(700,700,TextureFormat.RGBA32,false,true);var active=RenderTexture.active;
        try{rt.Create();parent.GetType().GetMethod("GrabPixels",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(parent,new object[]{rt,new Rect(350,110,700,700)});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,700,700),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../selection-"+name+".png")),image.EncodeToPNG());return image.GetPixels();}
        finally{RenderTexture.active=active;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    }
    static int Amber(Color[] pixels)=>pixels.Count(p=>p.r>.85f&&p.g>.4f&&p.g<p.r*.8f&&p.b<.3f);
    static double Cyan(Color[] pixels)=>pixels.Where(p=>p.g>p.b*1.05f&&p.g>p.r*1.5f&&p.b>p.r*1.4f).Sum(p=>(double)(p.g-p.r));
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;EditorApplication.update-=Tick;
        try
        {
            view.LookAtDirect(Vector3.zero,Quaternion.Euler(20,135,0),1.8f);Paint();
            Check(Amber(Pixels("cube-wire"))>100,"Selected cube has highlighted polygon edges");
            StudioSettings.Current.hideObjectWireframe=true;Paint();var only=Pixels("cube-outline-only");
            Check(Amber(only)>100,"Selected edges remain visible with wireframe disabled");
            editor.SelectObject(0,false,true);Paint();Check(Amber(Pixels("cube-unselected"))<50,"Clearing selection removes edge highlights");
            editor.SelectObject(0);StudioSettings.Current.hideObjectWireframe=false;editor.SelectionOutlineEnabled=false;
            var oldMesh=doc.nodes[0].mesh;var plane=new Mesh();plane.vertices=new[]{new Vector3(-2,-2,0),new Vector3(0,-2,0),new Vector3(2,-2,0),new Vector3(-2,2,0),new Vector3(0,2,0),new Vector3(2,2,0)};
            plane.SetIndices(new[]{0,3,4,1,1,4,5,2},MeshTopology.Quads,0);plane.RecalculateNormals();plane.RecalculateBounds();doc.nodes[0].mesh=plane;typeof(StudioModelEditor).GetMethod("Rebuild",Flags).Invoke(editor,null);Object.DestroyImmediate(oldMesh);editor.SelectObject(0);

            {
                foreach(float size in new[]{1.2f,.6f})
                foreach(float angle in new[]{70f,80f,85f})
                {
                    view.LookAtDirect(Vector3.zero,Quaternion.Euler(0,angle,0),size);

                    Paint();double fixedValue=Cyan(Pixels("fixed-"+angle));
                    typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,true);Paint();double clear=Cyan(Pixels("reference-"+angle));typeof(StudioModelEditor).GetField("displayXray",Flags).SetValue(editor,false);
                    Debug.Log($"GRAZING_COVERAGE angle={angle} zoom={size} fixed={fixedValue:F1} unobstructed={clear:F1} ratio={fixedValue/clear:F3}");
                    Check(fixedValue>clear*.85,"Grazing view retains edge coverage at "+angle+" degrees, zoom "+size);
                }
            }
            Debug.Log("STUDIO_SILHOUETTE_SUCCESS");Finish(null);
        }catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;if(view)view.Close();if(controller)Object.DestroyImmediate(controller);if(doc){foreach(var n in doc.nodes)if(n.mesh)Object.DestroyImmediate(n.mesh);Object.DestroyImmediate(doc);}
        if(error!=null)Debug.LogException(error);EditorApplication.Exit(error==null?0:1);
    }
}


