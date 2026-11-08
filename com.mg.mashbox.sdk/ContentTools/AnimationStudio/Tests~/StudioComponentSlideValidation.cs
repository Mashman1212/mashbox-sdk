using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public static class StudioComponentSlideValidation
    {
        static readonly List<string> report=new List<string>();
        static void Check(bool pass,string label){if(!pass)throw new Exception(label);report.Add("PASS "+label);}
        static bool Near(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-9f;
        public static void Run()
        {
            if(!Application.dataPath.Replace('\\','/').EndsWith("/Splines/SplineProject/Assets"))throw new Exception("Disposable SplineProject only");
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ComponentSlide/result.txt"));
            StudioModelEditor editor=null;StudioModelDocument document=null;Mesh mesh=null;
            try
            {
                var rails=new[]{Vector3.right*2,Vector3.left,Vector3.up};
                Check(Near(StudioModelEditor.SlideDisplacement(rails,Matrix4x4.identity,new Vector3(.6f,.2f,1)),Vector3.right*.6f),"Follows incident edge, removes off-edge displacement");
                Check(Near(StudioModelEditor.SlideDisplacement(rails,Matrix4x4.identity,Vector3.right*4),Vector3.right*2),"Clamps to forward neighbor");
                Check(Near(StudioModelEditor.SlideDisplacement(rails,Matrix4x4.identity,Vector3.left*4),Vector3.left),"Reverse movement and opposite clamp");
                Check(Near(StudioModelEditor.SlideDisplacement(rails,Matrix4x4.Scale(new Vector3(3,2,1)),Vector3.right*3),Vector3.right),"World distance respects nonuniform scale");
                Check(Near(StudioModelEditor.SlideDisplacement(Array.Empty<Vector3>(),Matrix4x4.identity,Vector3.one),Vector3.zero),"No eligible rails leaves vertex fixed");
                mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.one,Vector3.up,Vector3.zero,Vector3.left,Vector3.left+Vector3.up},triangles=new[]{0,1,2,0,2,3,4,5,6}};
                mesh.RecalculateNormals();document=ScriptableObject.CreateInstance<StudioModelDocument>();
                document.nodes.Add(new StudioModelDocument.Node{name="Slide",mesh=mesh,cutPolygons=new[]{0,4,0,1,2,3,0,3,4,5,6},cutTriangles=mesh.triangles,geometryVertices=new[]{0,1,2,3,4,5,6}});
                editor=new StudioModelEditor(()=>{});editor.Load(document);editor.SelectObject(0);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
                typeof(StudioModelEditor).GetField("weld",flags).SetValue(editor,true);
                var capture=typeof(StudioModelEditor).GetMethod("CaptureSlideRails",flags);
                var captured=(Dictionary<int,Vector3[]>)capture.Invoke(editor,new object[]{mesh.vertices,new[]{0}});
                Check(captured[0].Length==2&&captured[0].All(v=>v!=Vector3.one&&v!=Vector3.left),"Authored adjacency excludes render diagonal and coincident detached island");
                captured=(Dictionary<int,Vector3[]>)capture.Invoke(editor,new object[]{mesh.vertices,new[]{0,1}});
                Check(captured[0].Length==1&&captured[0][0]==Vector3.up,"Selected edge endpoints slide toward unselected neighbors");
                var ids=document.nodes[0].geometryVertices;ids[4]=0;
                captured=(Dictionary<int,Vector3[]>)capture.Invoke(editor,new object[]{mesh.vertices,new[]{0,4}});
                Check(captured[0].Any(v=>v==Vector3.left)&&captured[0].SequenceEqual(captured[4]),"Weld aliases share rails across authored seams");
                Check(mesh.triangles.SequenceEqual(new[]{0,1,2,0,2,3,4,5,6}),"Capture never changes tessellation");
                report.Add("PASS "+report.Count+" checks");File.WriteAllLines(output,report);
            }
            catch(Exception ex){File.WriteAllText(output,string.Join("\n",report)+"\nFAIL "+ex);Debug.LogException(ex);}
            finally{editor?.Dispose();if(document)UnityEngine.Object.DestroyImmediate(document);if(mesh)UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
