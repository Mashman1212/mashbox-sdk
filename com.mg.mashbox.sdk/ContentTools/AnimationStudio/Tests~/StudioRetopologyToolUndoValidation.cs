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
    public static class StudioRetopologyToolUndoValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Report=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/retopology-tool-undo-validation.txt"));
        public static void Run()
        {
            var log=new List<string>();var doc=ScriptableObject.CreateInstance<StudioRetopologyDocument>();
            StudioRetopologyEditor editor=null;Mesh reference=null,cylinder=null;
            void Check(bool yes,string message){if(!yes)throw new Exception(message);log.Add("PASS "+message);}
            object Get(string name)=>typeof(StudioRetopologyEditor).GetField(name,Flags).GetValue(editor);
            void Set(string name,object value)=>typeof(StudioRetopologyEditor).GetField(name,Flags).SetValue(editor,value);
            object Call(string name,params object[] args)=>typeof(StudioRetopologyEditor).GetMethod(name,Flags).Invoke(editor,args);
            void Tool(string name){var field=typeof(StudioRetopologyEditor).GetField("activeTool",Flags);field.SetValue(editor,Enum.Parse(field.FieldType,name));}
            List<Vector3> Points()=> (List<Vector3>)Get("corners");
            void UndoOne(){Undo.FlushUndoRecordObjects();Undo.PerformUndo();}
            void RedoOne()=>Undo.PerformRedo();
            void Fresh(string tool)
            {
                Undo.ClearUndo(doc);Call("ClearPatchCorners",true,true);Call("ClearComponents");Tool(tool);doc.project=false;doc.symmetry=false;
                doc.parts.Clear();doc.parts.Add(new StudioRetopologyDocument.Part{name="Undo validation",reference=reference});doc.selected=0;
                Set("patchAutoDensity",false);Set("patchSegments",2);Set("continuousPatch",false);Call("Rebuild");
            }
            var quad=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
            void Point(Vector3 p)=>Call("AddConstructionPoint",p,Vector3.forward);
            try
            {
                reference=new Mesh();reference.vertices=new[]{new Vector3(-5,-5,0),new Vector3(5,-5,0),new Vector3(5,5,0),new Vector3(-5,5,0)};reference.triangles=new[]{0,1,2,0,2,3};reference.RecalculateBounds();
                doc.selected=0;doc.project=false;doc.parts.Add(new StudioRetopologyDocument.Part{name="Undo validation",reference=reference});
                editor=new StudioRetopologyEditor(doc,()=>{});
                foreach(string tool in new[]{"Quad","Patch"})
                {
                    Fresh(tool);
                    Point(quad[0]);Point(quad[1]);Point(quad[2]);
                    UndoOne();Check(Points().Count==2&&doc.parts[0].faces.Count==0,tool+": Undo removes one pending point");
                    RedoOne();Check(Points().Count==3,tool+": Redo restores the pending point");
                    Point(quad[3]);int count=tool=="Quad"?1:4;
                    Check(doc.parts[0].faces.Count==count&&Points().Count==0,tool+": fourth click creates geometry");
                    UndoOne();Check(doc.parts[0].faces.Count==0&&Points().SequenceEqual(quad.Take(3)),tool+": one Undo reverses the entire fourth click and restores three corners");
                    UndoOne();Check(Points().Count==2,tool+": next Undo removes the third click");
                    RedoOne();RedoOne();Check(doc.parts[0].faces.Count==count&&Points().Count==0,tool+": Redo restores point and completed mesh");
                }
                Fresh("Quad");foreach(var p in quad.Take(3))Point(p);Call("CreateDrawTriangle");
                UndoOne();Check(doc.parts[0].faces.Count==0&&Points().Count==3,"Triangle: Undo Enter restores all three corners");
                UndoOne();Check(Points().Count==2,"Triangle: subsequent Undo removes the third corner");

                Fresh("Patch");Set("continuousPatch",true);foreach(var p in quad)Point(p);
                Check(Points().Count==2&&Get("patchContinuation")!=null,"Continuous patch seeds its next shared edge");
                Point(new Vector3(1,2,0));Point(new Vector3(0,2,0));
                Check(doc.parts[0].faces.Count==8,"Continuous patch creates its next quad grid");
                UndoOne();Check(doc.parts[0].faces.Count==4&&Points().Count==3&&Get("patchContinuation")!=null&&ReferenceEquals(Get("patchOwner"),doc.parts[0]),"Continuous patch: Undo restores the previous seam and unfinished third corner");
                UndoOne();Check(Points().Count==2&&doc.parts[0].faces.Count==4,"Continuous patch: Undo removes the first new corner without removing the previous patch");
                RedoOne();RedoOne();Check(doc.parts[0].faces.Count==8&&Points().Count==2,"Continuous patch: Redo restores connected geometry and continuation");
                Call("CancelConstruction");UndoOne();Check(Points().Count==2&&Get("patchContinuation")!=null,"Canceling construction is undoable");

                Fresh("TargetWeld");var part=doc.parts[0];part.vertices.AddRange(quad);part.uv.AddRange(quad.Select(p=>Vector2.zero));part.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                Call("SetWeldSource",0);UndoOne();Check((int)Get("weldSource")==-1&&doc.parts[0].faces.Count==1,"Target Weld: Undo clears source choice without deleting geometry");
                RedoOne();Check((int)Get("weldSource")==0,"Target Weld: Redo restores source choice");
                Call("Change","Weld validation",(Action)(()=>typeof(StudioRetopologyEditor).GetMethod("WeldVertexToTarget",Flags|BindingFlags.Static).Invoke(null,new object[]{doc.parts[0],0,1})));
                UndoOne();Check(doc.parts[0].vertices.Count==4&&(int)Get("weldSource")==0,"Target Weld: Undo restores geometry and selected source");

                Fresh("EdgeBridge");part=doc.parts[0];part.vertices.AddRange(new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up,new Vector3(2,0,0),new Vector3(3,0,0),new Vector3(3,1,0),new Vector3(2,1,0)});part.uv.AddRange(part.vertices.Select(p=>Vector2.zero));part.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));part.faces.Add(new StudioRetopologyDocument.Face(new[]{4,5,6,7}));
                Call("SetBridgeSource",((int,int)?)(1,2));UndoOne();Check(Get("bridgeSource")==null&&doc.parts[0].faces.Count==2,"Edge Bridge: Undo clears first edge choice without deleting geometry");
                RedoOne();Call("Change","Bridge validation",(Action)(()=>typeof(StudioRetopologyEditor).GetMethod("BridgeEdgePair",Flags|BindingFlags.Static).Invoke(null,new object[]{doc.parts[0],(1,2),(4,7)})));
                UndoOne();Check(doc.parts[0].faces.Count==2&&Get("bridgeSource")!=null,"Edge Bridge: Undo restores the source edge and original topology");

                Fresh("Select");part=doc.parts[0];part.vertices.AddRange(quad);part.uv.AddRange(quad.Select(p=>Vector2.zero));part.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                Call("SelectAllComponents");UndoOne();Check(((HashSet<int>)Get("selected")).Count==0&&doc.parts[0].faces.Count==1,"Selection: Undo reverses Select All without removing geometry");
                RedoOne();Check(((HashSet<int>)Get("selected")).Count==4,"Selection: Redo restores selected vertices");

                Fresh("Strip");part=doc.parts[0];part.vertices.AddRange(quad);part.uv.AddRange(quad.Select(p=>Vector2.zero));part.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));Set("stripLeft",2);Set("stripRight",3);Set("stripCenter",new Vector3(.5f,1,0));Set("stripNormal",Vector3.forward);Set("stripSide",Vector3.left);
                int group=(int)Call("RecordToolAction","Strip gesture validation");Set("stripStroke",true);
                Call("Change","Strip row one",(Action)(()=>doc.parts[0].vertices[2]+=Vector3.up));Set("stripCenter",new Vector3(.5f,2,0));Call("StoreToolState");
                Call("Change","Strip row two",(Action)(()=>doc.parts[0].vertices[2]+=Vector3.up));Set("stripCenter",new Vector3(.5f,3,0));Call("StoreToolState");Set("stripStroke",false);Undo.CollapseUndoOperations(group);
                UndoOne();Check(doc.parts[0].vertices[2]==quad[2]&&(int)Get("stripLeft")==2&&(Vector3)Get("stripCenter")==new Vector3(.5f,1,0),"Strip: Undo reverses an entire gesture and restores its continuation");
                RedoOne();Check(doc.parts[0].vertices[2]==quad[2]+Vector3.up*2&&(Vector3)Get("stripCenter")==new Vector3(.5f,3,0),"Strip: Redo restores geometry and the final continuation");

                var tubeSource=new StudioRetopologyDocument.Part();int sides=32;
                for(int row=0;row<2;row++)for(int i=0;i<sides;i++){float a=2*Mathf.PI*i/sides;tubeSource.vertices.Add(new Vector3(Mathf.Cos(a),row*3,Mathf.Sin(a)));tubeSource.uv.Add(Vector2.zero);}
                for(int i=0;i<sides;i++){int next=(i+1)%sides;tubeSource.faces.Add(new StudioRetopologyDocument.Face(new[]{i,sides+i,sides+next,next}));}
                cylinder=StudioRetopologyDocument.BuildMesh(tubeSource);Fresh("Tube");doc.parts[0].reference=cylinder;
                Point(new Vector3(0,.2f,-1));Point(new Vector3(0,2.8f,-1));
                Check(Get("tubePreview")!=null&&Points().Count==2,"Tube: second click builds its preview");
                UndoOne();Check(Points().Count==1&&Get("tubePreview")==null,"Tube: Undo removes its endpoint and preview");
                RedoOne();Check(Points().Count==2&&Get("tubePreview")!=null,"Tube: Redo rebuilds the preview");
                Call("CreateTube");UndoOne();Check(doc.parts[0].faces.Count==0&&Points().Count==2&&Get("tubePreview")!=null,"Tube: Undo creation restores both endpoints and preview");
                UndoOne();Check(Points().Count==1,"Tube: further Undo removes the last endpoint");
                RedoOne();Call("RemoveConstructionPoint");UndoOne();Check(Points().Count==2&&Get("tubePreview")!=null,"Tube: Backspace can be undone");
                // Recreate the editor to exercise the same path as a script reload.
                editor.Dispose();editor=new StudioRetopologyEditor(doc,()=>{});
                Check(Get("activeTool").ToString()=="Tube"&&Points().Count==2&&Get("tubePreview")!=null,"Reload restores the active tool, pending points and preview");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally
            {
                editor?.Dispose();Undo.ClearUndo(doc);Object.DestroyImmediate(doc);if(reference)Object.DestroyImmediate(reference);if(cylinder)Object.DestroyImmediate(cylinder);File.WriteAllLines(Report,log);
            }
        }
    }
}
