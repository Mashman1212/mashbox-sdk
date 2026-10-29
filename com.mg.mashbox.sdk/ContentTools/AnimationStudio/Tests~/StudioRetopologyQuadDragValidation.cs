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
                Check(doc.parts[0].uv.Distinct().Count()==4,"A free dragged quad retains its four-corner UV layout");
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
                StudioRetopologyDocument.Part BaseQuad()
                {
                    var p=new StudioRetopologyDocument.Part{name="Welded drag",reference=reference};
                    p.vertices.AddRange(new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up});
                    p.uv.AddRange(Enumerable.Repeat(Vector2.zero,4));p.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));return p;
                }
                Vector2 Screen(Vector3 p)=>new Vector2(p.x,p.y)*100;
                var basePart=BaseQuad();
                var attached=StudioRetopologyEditor.DragQuadCorners(Vector3.zero,new Vector3(1,-1,0),Vector3.forward,Vector3.right,Vector3.up);
                attached[1]+=new Vector3(.08f,-.06f,0);
                var snaps=StudioRetopologyEditor.SnapDraggedQuad(attached,basePart.vertices,new[]{0,1,2,3},Screen,24,id=>true);
                Check(snaps[0]==0&&snaps[1]==1&&attached[1]==basePart.vertices[1],"The generated side corner snaps to the neighboring quad, not just the cursor corner");
                Check(StudioRetopologyEditor.DraggedQuadAttachmentWarning(basePart,attached,Vector3.forward,.00001f)==null,"A single open edge is a valid weld target");
                Call("ClearPatchCorners",true,true);doc.parts[0]=basePart;
                Call("AddConstructionPoint",attached[0],Vector3.forward);group=Undo.GetCurrentGroup();Call("CreateDraggedQuad",attached,Vector3.forward,group);
                Check(basePart.faces.Count==2&&basePart.vertices.Count==6&&StudioRetopologyOperations.Edges(basePart)[StudioRetopologyOperations.Edge(0,1)].Count==2,"Releasing reuses both border vertex IDs and seals the shared edge");
                Check(basePart.faces.All(f=>f.vertices.Length==4)&&StudioRetopologyLoops.Boundaries(basePart).Count==1,"Attached pullout stays one quad with only one outer boundary");
                UndoOne();Check(doc.parts[0].faces.Count==1&&doc.parts[0].vertices.Count==4&&Points().Count==0,"One Undo removes a welded drag without changing the original quad");
                Undo.PerformRedo();Check(doc.parts[0].faces.Count==2&&doc.parts[0].vertices.Count==6,"Redo restores the same sealed edge");
                var bridge=BaseQuad();bridge.vertices.AddRange(new[]{new Vector3(0,-2,0),new Vector3(1,-2,0),new Vector3(1,-1,0),new Vector3(0,-1,0)});
                bridge.uv.AddRange(Enumerable.Repeat(Vector2.zero,4));bridge.faces.Add(new StudioRetopologyDocument.Face(new[]{4,5,6,7}));
                var bridgePoints=new[]{Vector3.zero,new Vector3(1.07f,-.04f,0),new Vector3(1.03f,-.97f,0),new Vector3(-.06f,-1.05f,0)};
                snaps=StudioRetopologyEditor.SnapDraggedQuad(bridgePoints,bridge.vertices,Enumerable.Range(0,8),Screen,24,id=>true);
                Check(snaps.SequenceEqual(new[]{0,1,6,7}),"Preview identifies all four potential corner welds across two quads");
                Call("ClearPatchCorners",true,true);doc.parts[0]=bridge;
                Call("AddConstructionPoint",bridgePoints[0],Vector3.forward);group=Undo.GetCurrentGroup();Call("CreateDraggedQuad",bridgePoints,Vector3.forward,group);
                var edges=StudioRetopologyOperations.Edges(bridge);
                Check(bridge.faces.Count==3&&bridge.vertices.Count==8&&edges[StudioRetopologyOperations.Edge(0,1)].Count==2&&edges[StudioRetopologyOperations.Edge(6,7)].Count==2,"A pulled quad closes the gap between two existing squares without duplicate seam vertices");
                Check(StudioRetopologyLoops.Boundaries(bridge).Count==1,"The two-sided weld leaves no internal holes");
                var closePoints=new[]{new Vector3(-3,-3,0),new Vector3(1.05f,0,0),new Vector3(1.01f,0,0),new Vector3(-3,3,0)};
                snaps=StudioRetopologyEditor.SnapDraggedQuad(closePoints,basePart.vertices,new[]{1},Screen,24,id=>true);
                Check(snaps[2]==1&&snaps[1]<0,"The cursor corner has priority and two corners cannot snap to the same vertex");
                snaps=StudioRetopologyEditor.SnapDraggedQuad(closePoints,basePart.vertices,new[]{1},Screen,24,id=>false);
                Check(snaps.All(id=>id<0),"Hidden border candidates do not produce weld hints");
                var aligned=BaseQuad();var rotation=Quaternion.Euler(0,0,22);aligned.vertices=aligned.vertices.Select(p=>rotation*p).ToList();doc.parts[0]=aligned;
                Set("quadDragStart",aligned.vertices[0]);Set("quadDragNormal",Vector3.forward);Set("quadDragRight",Vector3.right);Set("quadDragUp",Vector3.up);Call("PrepareQuadDragWelding");
                Check(Vector3.Angle((Vector3)Get("quadDragRight"),aligned.vertices[1]-aligned.vertices[0])<.01f,"Starting on a border aligns the pullout to the existing grid");
                doc.project=true;doc.offset=.01f;Set("quadDragSnaps",new[]{0,1,-1,-1});
                var held=new[]{Vector3.zero,new Vector3(1,0,.2f),new Vector3(1,-1,0),new Vector3(0,-1,.2f)};Call("ProjectQuadDragCorners",held,Vector3.forward);
                Check(Mathf.Abs(held[1].z-.2f)<.000001f&&Mathf.Abs(held[3].z-.01f)<.000001f,"Surface projection preserves a welded derived corner while projecting the free corner");
                Call("ReleaseQuadDrag");doc.project=false;
                Check(Get("quadDragSnaps")==null&&Get("quadDragBorderVertices")==null&&Get("quadDragWarning")==null,"Ending the gesture clears weld-preview state");
                var multi=new StudioRetopologyDocument.Part{name="Multi-edge border",reference=reference};
                multi.vertices.AddRange(new[]{Vector3.zero,Vector3.right,Vector3.right*2,Vector3.up,new Vector3(1,1,0),new Vector3(2,1,0)});
                multi.uv.AddRange(Enumerable.Repeat(Vector2.zero,6));multi.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,4,3}));multi.faces.Add(new StudioRetopologyDocument.Face(new[]{1,2,5,4}));
                var tooWide=StudioRetopologyEditor.DragQuadCorners(Vector3.zero,new Vector3(2,-1,0),Vector3.forward,Vector3.right,Vector3.up);
                Check(StudioRetopologyEditor.DraggedQuadAttachmentWarning(multi,tooWide,Vector3.forward,.00001f)!=null,"A single quad warns before spanning a subdivided border and making a T-junction");
                Call("ClearPatchCorners",true,true);doc.parts[0]=multi;Call("AddConstructionPoint",tooWide[0],Vector3.forward);group=Undo.GetCurrentGroup();
                bool refused=false;try{Call("CreateDraggedQuad",tooWide,Vector3.forward,group);}catch(TargetInvocationException e){refused=e.GetBaseException() is InvalidOperationException;}
                Check(refused&&multi.faces.Count==2&&multi.vertices.Count==6&&Points().Count==1,"Invalid release preserves the existing mesh and first construction point");
                var occupied=BaseQuad();occupied.vertices.AddRange(new[]{new Vector3(1,-1,0),new Vector3(0,-1,0)});occupied.uv.AddRange(new[]{Vector2.zero,Vector2.zero});occupied.faces.Add(new StudioRetopologyDocument.Face(new[]{1,0,5,4}));
                Check(StudioRetopologyEditor.DraggedQuadAttachmentWarning(occupied,attached,Vector3.forward,.00001f)!=null,"Occupied edges cannot be welded to a third face");
                Check(StudioRetopologyEditor.DraggedQuadAttachmentWarning(BaseQuad(),new[]{Vector3.zero,Vector3.right,new Vector3(1,.8f,0),new Vector3(0,.8f,0)},Vector3.forward,.00001f)!=null,"Overlap with the source square is rejected before creating geometry");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally{editor?.Dispose();Undo.ClearUndo(doc);Object.DestroyImmediate(doc);if(reference)Object.DestroyImmediate(reference);File.WriteAllLines(Report,log);}
        }
    }
}
