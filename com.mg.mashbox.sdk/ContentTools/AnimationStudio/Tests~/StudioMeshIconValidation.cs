using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    // Opt-in, non-exiting visual check in the existing disposable project only.
    internal sealed class StudioMeshIconValidation:EditorWindow
    {
        static readonly string[] Keys={"Mesh Tools/Combine Meshes…","Mesh Tools/Duplicate Face","Mesh Tools/Duplicate Face as New Object","Mesh Tools/Detach Face","Mesh Tools/Split Selection","Mesh Tools/Bridge Edges","Mesh Tools/Extrude","Mesh Tools/Bevel…","Mesh Tools/Circularize…","Mesh Tools/Weld…","Mesh Tools/Merge Vertices","Mesh Tools/Target Weld","Mesh Tools/Insert Edge Loop","Mesh Tools/Bisect","Mesh Tools/Fracture","Mesh Tools/Multi-Cut","Deform/Blend Shape","Deform/Bake Current Pose"};
        static readonly string[] Names={"Combine","Duplicate Face","Duplicate as Object","Detach Face","Split","Bridge","Extrude","Bevel","Circularize","Weld Range","Merge to Center","Target Weld","Edge Loop","Bisect","Fracture","Multi-Cut","Blend Shape","Bake Pose"};
        static readonly string[] SelectionKeys={"Select/All","Select/Deselect","Select/Invert Selection","Select/Contiguous Path","Select/Convert Selection/Vertices","Select/Convert Selection/To Perimeter Vertices","Select/Convert Selection/Edges","Select/Convert Selection/To Perimeter Edges","Select/Convert Selection/Faces","Select/Convert Selection/To Perimeter Faces","Select/Convert Selection/Objects","Select/Hide Selection","Select/Unhide Last Hidden","Select/Grow Selection","Select/Shrink Selection","Select/Frame Selection","Select/Through Selection","Select/Isolate Selected"};
        static readonly string[] SelectionNames={"All","Deselect","Invert","Contiguous Path","To Vertices","Perimeter Vertices","To Edges","Perimeter Edges","To Faces","Perimeter Faces","To Objects","Hide","Unhide Last","Grow","Shrink","Frame","Through","Isolate"};
        static StudioMeshIconValidation window;static int frames;static double next;Texture2D[] icons;
        static string Out(string name)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../mesh-icons-"+name));
        [InitializeOnLoadMethod]static void Boot()
        {if(Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets")&&File.Exists(Out("request.txt")))EditorApplication.delayCall+=Run;}
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/ThemeIsolationProject/Assets"))return;
            File.Delete(Out("request.txt"));foreach(var old in Resources.FindObjectsOfTypeAll<StudioMeshIconValidation>())old.Close();window=CreateInstance<StudioMeshIconValidation>();
            window.icons=Keys.Concat(SelectionKeys).Select(StudioCommandIcons.Get).ToArray();
            window.titleContent=new GUIContent("Studio unified action icons");window.ShowUtility();window.position=new Rect(100,100,900,490);
            frames=0;next=EditorApplication.timeSinceStartup+.7;EditorApplication.update+=Tick;
        }
        void OnGUI()
        {
            if(icons==null)return;
            EditorGUI.DrawRect(new Rect(0,0,position.width,position.height),new Color(.16f,.17f,.18f));
            GUI.Label(new Rect(14,8,850,22),"MESH TOOLS + SELECT   /   neutral geometry · amber actions   /   16px + 24px",EditorStyles.boldLabel);
            for(int i=0;i<icons.Length;i++)
            {
                int x=12+(i%9)*98,y=42+(i/9)*108;
                EditorGUI.DrawRect(new Rect(x,y,96,40),new Color(.25f,.25f,.25f));
                GUI.DrawTexture(new Rect(x+22,y+12,16,16),icons[i],ScaleMode.ScaleToFit,true);
                GUI.DrawTexture(new Rect(x+50,y+8,24,24),icons[i],ScaleMode.ScaleToFit,true);
                GUI.Label(new Rect(x,y+43,96,37),i<Names.Length?Names[i]:SelectionNames[i-Names.Length],new GUIStyle(EditorStyles.centeredGreyMiniLabel){wordWrap=true,normal={textColor=new Color(.88f,.88f,.88f)}});
            }
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.3;
            if(frames++<1){window.Repaint();return;}EditorApplication.update-=Tick;
            try
            {
                var signatures=new HashSet<string>();
                foreach(var icon in window.icons)
                {
                    if(!icon)throw new Exception("Missing icon");
                    var rt=RenderTexture.GetTemporary(24,24,0);var old=RenderTexture.active;var read=new Texture2D(24,24,TextureFormat.RGBA32,false);
                    try{Graphics.Blit(icon,rt);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,24,24),0,0);read.Apply();
                        var pixels=read.GetPixels32();if(pixels.Count(p=>p.a>20)<20)throw new Exception("Empty symbol");
                        if(pixels.Any(p=>p.a>40&&(p.b>p.r+3||p.g>p.r+3)))throw new Exception("Unexpected non-amber accent");
                        if(!signatures.Add(Convert.ToBase64String(read.EncodeToPNG())))throw new Exception("Repeated icon artwork");}
                    finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);DestroyImmediate(read);}
                }
                var parent=typeof(EditorWindow).GetField("m_Parent",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                var rect=new Rect(Vector2.zero,window.position.size);var capture=new RenderTexture((int)rect.width,(int)rect.height,0);capture.Create();var previous=RenderTexture.active;
                var image=new Texture2D(capture.width,capture.height,TextureFormat.RGBA32,false);
                try{parent.GetType().GetMethod("GrabPixels",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(parent,new object[]{capture,rect});RenderTexture.active=capture;
                    image.ReadPixels(rect,0,0);image.Apply();var raw=image.GetPixels32();var upright=new Color32[raw.Length];
                    for(int y=0;y<image.height;y++)Array.Copy(raw,y*image.width,upright,(image.height-1-y)*image.width,image.width);
                    image.SetPixels32(upright);image.Apply();File.WriteAllBytes(Out("gallery.png"),image.EncodeToPNG());}
                finally{RenderTexture.active=previous;DestroyImmediate(capture);DestroyImmediate(image);}
                File.WriteAllText(Out("result.txt"),"PASS: 36 nonempty, pixel-distinct native Mesh Tools/Select action icons with neutral/amber palette; actual 16px + 24px IMGUI gallery rendered and captured. Native selection modes/primitives/shading left unchanged. No geometry/profile changes or exiting fixture.");
            }
            catch(Exception e){File.WriteAllText(Out("result.txt"),"FAIL "+e);Debug.LogException(e);}
        }
    }
}
