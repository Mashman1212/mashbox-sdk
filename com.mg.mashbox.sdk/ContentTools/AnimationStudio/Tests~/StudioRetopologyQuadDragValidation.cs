using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioRetopologyQuadDragValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/retopology-quad-drag-validation.txt"));
        public static void Run()
        {
            var log=new List<string>();StudioRetopologyEditor editor=null;Mesh reference=null;
            var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();
            void Check(bool yes,string name){if(!yes)throw new Exception(name);log.Add("PASS "+name);}
            object Call(string name,params object[] args)=>typeof(StudioRetopologyEditor).GetMethod(name,Flags).Invoke(editor,args);
            object Get(string name)=>typeof(StudioRetopologyEditor).GetField(name,Flags).GetValue(editor);
            void Set(string name,object value)=>typeof(StudioRetopologyEditor).GetField(name,Flags).SetValue(editor,value);
            List<Vector3> Points()=> (List<Vector3>)Get("corners");
            void UndoOne(){Undo.FlushUndoRecordObjects();Undo.PerformUndo();}
            try
            {
                foreach(float x in new[]{-2f,2f})foreach(float y in new[]{-3f,3f})
                {
                    var start=new Vector3(1,2,0);var end=start+new Vector3(x,y,0);
                    var points=StudioRetopologyEditor.DragQuadCorners(start,end,Vector3.forward,Vector3.right,Vector3.up);
                    Check(points[0]==start&&points[2]==end&&points[1]==start+Vector3.right*x&&points[3]==start+Vector3.up*y&&StudioRetopologyEditor.ValidDraggedQuad(points),"Corner drag supports diagonal quadrant "+x+","+y);
                }
                var turn=Quaternion.Euler(24,37,18);var origin=new Vector3(2,3,4);var opposite=origin+turn*new Vector3(2,3,0);
                var rotated=StudioRetopologyEditor.DragQuadCorners(origin,opposite,turn*Vector3.forward,turn*Vector3.right,turn*Vector3.up);
                Check(StudioRetopologyEditor.ValidDraggedQuad(rotated)&&Vector3.Distance(rotated[1],origin+turn*Vector3.right*2)<.00001f,"Drag rectangle follows the viewed surface orientation");
                bool rejected=false;try{StudioRetopologyEditor.DragQuadCorners(Vector3.zero,Vector3.right,Vector3.forward,Vector3.right,Vector3.up);}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"An axis-only drag cannot create a zero-area quad");
                Check(!StudioRetopologyEditor.ValidDraggedQuad(new[]{Vector3.zero,new Vector3(1,1,0),Vector3.up,Vector3.right}),"Folded drag previews are rejected");
                reference=new Mesh();reference.vertices=new[]{new Vector3(-5,-5,0),new Vector3(5,-5,0),new Vector3(5,5,0),new Vector3(-5,5,0)};reference.triangles=new[]{0,1,2,0,2,3};reference.RecalculateBounds();
                doc.parts.Add(new StudioRetopologyDocument.Part{name="Drag validation",reference=reference});doc.selected=0;doc.project=false;
                editor=new StudioRetopologyEditor(doc,()=>{});
                var tool=typeof(StudioRetopologyEditor).GetField("activeTool",Flags);tool.SetValue(editor,Enum.Parse(tool.FieldType,"Quad"));
                var a=new Vector3(1,0,0);var c=new Vector3(2,1,0);
                Call("AddConstructionPoint",a,Vector3.forward);int group=Undo.GetCurrentGroup();
                Check(Points().Count==1&&doc.parts[0].faces.Count==0,"A short first click remains an ordinary construction point");
                var quad=StudioRetopologyEditor.DragQuadCorners(a,c,Vector3.forward,Vector3.right,Vector3.up);
                Call("CreateDraggedQuad",quad,Vector3.forward,group);
                Check(doc.parts[0].faces.Count==1&&doc.parts[0].faces[0].vertices.Length==4&&doc.parts[0].vertices.Count==4&&Points().Count==0,"Releasing a diagonal drag creates exactly one quad");
                UndoOne();Check(doc.parts[0].faces.Count==0&&doc.parts[0].vertices.Count==0&&Points().Count==0,"One Undo reverses the complete drag including its first point");
                Undo.PerformRedo();Check(doc.parts[0].faces.Count==1&&Points().Count==0,"Redo restores the dragged quad");
                UndoOne();doc.symmetry=true;Call("AddConstructionPoint",a,Vector3.forward);group=Undo.GetCurrentGroup();Call("CreateDraggedQuad",quad,Vector3.forward,group);
                Check(doc.parts[0].faces.Count==2&&doc.parts[0].vertices.Count==8,"Drag quad respects mirror drawing");
                Check(doc.parts[0].faces.All(f=>Vector3.Dot(Vector3.Cross(doc.parts[0].vertices[f.vertices[1]]-doc.parts[0].vertices[f.vertices[0]],doc.parts[0].vertices[f.vertices[3]]-doc.parts[0].vertices[f.vertices[0]]),Vector3.forward)>0),"Mirrored drag quads retain outward winding");
                UndoOne();doc.symmetry=false;doc.project=true;doc.offset=.01f;
                var projected=StudioRetopologyEditor.DragQuadCorners(a+Vector3.forward*.01f,c+Vector3.forward*.01f,Vector3.forward,Vector3.right,Vector3.up);
                Call("ProjectQuadDragCorners",projected,Vector3.forward);
                Check(projected.All(p=>Mathf.Abs(p.z-.01f)<.000001f),"Intermediate drag corners project to the reference with the chosen offset");
                Check(projected[0]==a+Vector3.forward*.01f&&projected[2]==c+Vector3.forward*.01f,"Projection keeps the exact first and opposite corner targets");
                doc.project=false;var flat=StudioRetopologyEditor.DragQuadCorners(a+Vector3.forward,c+Vector3.forward,Vector3.forward,Vector3.right,Vector3.up);Call("ProjectQuadDragCorners",flat,Vector3.forward);
                Check(flat.All(p=>p.z==1),"Surface snapping off leaves the dragged plane unchanged");
                Call("AddConstructionPoint",a,Vector3.forward);Set("quadDragHeld",true);Set("quadDragMoved",true);Set("quadDragPreview",quad);Call("ReleaseQuadDrag");
                Check(Points().Count==1&&!(bool)Get("quadDragHeld")&&Get("quadDragPreview")==null,"Releasing capture does not discard an ordinary clicked point");
                Call("CancelConstruction");UndoOne();Check(Points().Count==1,"Canceling a drag construction remains undoable");
                UndoOne();
                // The existing click workflow uses the same point method after release.
                foreach(var point in new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up})Call("AddConstructionPoint",point,Vector3.forward);
                Check(doc.parts[0].faces.Count==1&&Points().Count==0,"Four ordinary clicks still create a single quad");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally{editor?.Dispose();Undo.ClearUndo(doc);Object.DestroyImmediate(doc);if(reference)Object.DestroyImmediate(reference);File.WriteAllLines(Report,log);}
        }
    }
}
