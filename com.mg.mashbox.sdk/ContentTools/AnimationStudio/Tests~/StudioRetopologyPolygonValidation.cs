using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioRetopologyPolygonValidation
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static string Report => Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/retopology-polygon-validation.txt"));
        public static void Run()
        {
            var log=new List<string>();var assets=new List<string>();
            StudioRetopologyDocument document=null;Mesh reference=null;StudioRetopologyEditor editor=null;
            StudioSkinningDocument skinDocument=null;StudioSkinningEditor skinEditor=null;
            void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
            object Call(string name,params object[] args)=>typeof(StudioRetopologyEditor).GetMethod(name,Flags).Invoke(editor,args);
            try
            {
                var polygon=new[]{new Vector3(1,0,0),new Vector3(3,0,0),new Vector3(3,1,0),new Vector3(2,1,0),new Vector3(2,2,0),new Vector3(1,2,0)};
                float Area(Vector3[] p,int[] ids){float value=0;for(int i=0;i<ids.Length;i+=3)value+=Vector3.Cross(p[ids[i+1]]-p[ids[i]],p[ids[i+2]]-p[ids[i]]).magnitude*.5f;return value;}
                foreach(bool reverse in new[]{false,true})
                {
                    var points=(reverse?polygon.Reverse():polygon).Select(v=>Quaternion.Euler(25,40,12)*v+Vector3.one*7).ToArray();
                    var indices=StudioRetopologyEditor.TriangulatePolygon(points);
                    Check(indices.Length==12&&Mathf.Abs(Area(points,indices)-3)<.0001f,"Concave outline area and triangle count, reversed="+reverse);
                }
                bool invalid=false;
                try{StudioRetopologyEditor.TriangulatePolygon(new[]{Vector3.zero,new Vector3(2,2,0),new Vector3(0,2,0),new Vector3(3,0,0)});}catch(InvalidOperationException){invalid=true;}
                Check(invalid,"Crossed outline rejected before geometry changes");
                var coarse=StudioRetopologyEditor.BuildPolygonGrid(polygon,0,2);
                var dense=StudioRetopologyEditor.BuildPolygonGrid(polygon,0,8);
                float QuadArea(StudioRetopologyDocument.Part p)=>p.faces.Sum(f=>Area(p.vertices.ToArray(),new[]{f.vertices[0],f.vertices[1],f.vertices[2],f.vertices[0],f.vertices[2],f.vertices[3]}));
                Check(coarse.faces.Count>=4&&coarse.faces.All(f=>f.vertices.Length==4),"Concave outline fills with an all-quad patch");
                Check(Mathf.Abs(QuadArea(coarse)-3)<.0001f,"Concave quad patch stays inside its outline without gaps or overlaps");
                Check(dense.faces.Count>coarse.faces.Count&&Mathf.Abs(QuadArea(dense)-3)<.0001f,"Higher divisions produce a denser quad grid with the same outline");
                Check(StudioRetopologyLoops.Boundaries(dense).Count==1&&StudioRetopologyOperations.Edges(dense).All(e=>e.Value.Count<=2),"Dense quad patch is connected and has no cracks or nonmanifold edges");
                var rectangle=new[]{Vector3.zero,new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(0,1,0)};
                var basePatch=StudioRetopologyEditor.BuildPolygonGrid(rectangle,0,2);
                var closedDraft=new[]{new Vector3(0,1,0),new Vector3(0,2,0),new Vector3(2,2,0),new Vector3(2,1,0)};
                Check(StudioRetopologyEditor.PolygonClosesOnBorder(basePatch,closedDraft,.00001f),"Returning to an existing border auto-closes across intermediate seam vertices");
                Check(!StudioRetopologyEditor.PolygonClosesOnBorder(basePatch,closedDraft.Take(3).ToArray(),.00001f),"An unfinished outline stays editable");
                Check(!StudioRetopologyEditor.PolygonClosesOnBorder(basePatch,rectangle,.00001f),"Tracing an existing face perimeter does not auto-fill it again");
                Check(!StudioRetopologyEditor.PolygonClosesOnBorder(basePatch,new[]{closedDraft[0],closedDraft[2],closedDraft[1],closedDraft[3]},.00001f),"A crossed outline does not auto-complete");
                var regular=StudioRetopologyEditor.BuildPolygonGrid(rectangle,0,5);
                Check(regular.faces.Count==25&&regular.vertices.Count==36,"Four-corner polygon makes a regular five-by-five quad patch");
                var automatic=StudioRetopologyEditor.BuildPolygonGrid(rectangle,.25f,1);
                Check(automatic.faces.Count==32,"Auto density uses patch-sized rows and columns");
                var tracedRectangle=new[]{new Vector3(0,0,0),new Vector3(.3f,0,0),new Vector3(.7f,0,0),new Vector3(1,0,0),new Vector3(1,.3f,0),new Vector3(1,.7f,0),new Vector3(1,1,0),new Vector3(.7f,1,0),new Vector3(.3f,1,0),new Vector3(0,1,0),new Vector3(0,.7f,0),new Vector3(0,.3f,0)};
                var traced=StudioRetopologyEditor.BuildPolygonGrid(tracedRectangle,.15f,1);
                Check(traced.faces.Count==49&&traced.vertices.Count==64,"Twelve-click outline produces the same seven-by-seven grid density as Draw Patch");
                Check(Enumerable.Range(0,tracedRectangle.Length).All(i=>traced.faces.Any(f=>f.vertices.Contains(i))),"Every clicked boundary point remains part of the connected grid");
                Check(Mathf.Abs(QuadArea(traced)-1)<.00001f&&StudioRetopologyLoops.Boundaries(traced).Count==1,"Traced rectangle covers its outline without gaps or overlap");
                float Aspect(StudioRetopologyDocument.Part p)=>p.faces.Max(f=>
                {
                    var edges=Enumerable.Range(0,4).Select(i=>Vector3.Distance(p.vertices[f.vertices[i]],p.vertices[f.vertices[(i+1)%4]])).ToArray();
                    return edges.Max()/Mathf.Max(.000001f,edges.Min());
                });
                Check(Aspect(traced)<2,"Polygon rows and columns have even quad spacing instead of diagonal fans");
                var turn=Quaternion.Euler(23,42,11);
                foreach(bool reverse in new[]{false,true})
                {
                    var curved=(reverse?tracedRectangle.Reverse():tracedRectangle).Select(p=>turn*new Vector3(p.x,p.y,.3f*p.x*p.x)+new Vector3(4,-2,8)).ToArray();
                    var shell=StudioRetopologyEditor.BuildPolygonGrid(curved,.16f,1);
                    Check(shell.faces.Count<=64&&shell.faces.All(f=>f.vertices.Length==4)&&Aspect(shell)<2.5f,"Curved, rotated multi-click patch keeps coherent quad rows, reverse="+reverse);
                    Check(StudioRetopologyOperations.Edges(shell).All(e=>e.Value.Count<=2)&&StudioRetopologyLoops.Boundaries(shell).Count==1,"Curved grid remains connected and manifold, reverse="+reverse);
                }
                var circle=Enumerable.Range(0,16).Select(i=>new Vector3(Mathf.Cos(i*Mathf.PI/8),Mathf.Sin(i*Mathf.PI/8),0)).ToArray();
                var rounded=StudioRetopologyEditor.BuildPolygonGrid(circle,.22f,1);
                Check(rounded.faces.Count<=81&&rounded.faces.All(f=>f.vertices.Length==4)&&Mathf.Abs(QuadArea(rounded)-QuadArea(StudioRetopologyEditor.BuildPolygonGrid(circle,0,4)))<.001f,"Rounded outline gets bounded density and preserves its area");
                var concaveAuto=StudioRetopologyEditor.BuildPolygonGrid(polygon,.3f,1);
                Check(concaveAuto.faces.Count<=100&&Mathf.Abs(QuadArea(concaveAuto)-3)<.0001f,"Concave fill budgets density by area without diagonal-driven over-subdivision");
                var pants=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/PJX CC/Pants/Meshes/Human_Pants_Punch_WC.fbx");
                if(pants)
                {
                    var mesh=pants.GetComponentsInChildren<SkinnedMeshRenderer>(true).First().sharedMesh;
                    var surface=new StudioRetopologySurface(mesh);var bounds=mesh.bounds;
                    // FBX bind meshes can store their height along local Z;
                    // sample garment axes instead of assuming Unity world Y.
                    var axes=Enumerable.Range(0,3).OrderByDescending(i=>bounds.size[i]).ToArray();
                    int heightAxis=axes[0],widthAxis=axes[1],depthAxis=axes[2];
                    foreach(float side in new[]{.2f,.65f})
                    {
                        float width=bounds.size[widthAxis]*.15f,height=bounds.size[heightAxis]*.15f;
                        float density=Mathf.Max(width,height)/7;
                        var direction=Vector3.zero;direction[depthAxis]=1;
                        var outline=tracedRectangle.Select(p=>
                        {
                            var origin=bounds.center;
                            origin[widthAxis]=bounds.min[widthAxis]+bounds.size[widthAxis]*side+p.x*width;
                            origin[heightAxis]=bounds.min[heightAxis]+bounds.size[heightAxis]*.68f+p.y*height;
                            origin[depthAxis]=bounds.max[depthAxis]+bounds.size[depthAxis];
                            return surface.ProjectAlong(origin,direction,0);
                        }).ToArray();
                        var pocket=StudioRetopologyEditor.BuildPolygonGrid(outline,density,1);
                        var normal=Vector3.zero;
                        for(int i=0;i<outline.Length;i++)normal+=Vector3.Cross(outline[i]-outline[0],outline[(i+1)%outline.Length]-outline[0]);
                        var edges=StudioRetopologyOperations.Edges(pocket).Keys.ToArray();
                        float span=edges.Average(e=>Vector3.Distance(pocket.vertices[e.Item1],pocket.vertices[e.Item2]));
                        for(int i=outline.Length;i<pocket.vertices.Count;i++)pocket.vertices[i]=surface.ProjectAlong(pocket.vertices[i],normal,0,span);
                        Check(pocket.faces.Count<=100&&pocket.faces.All(f=>f.vertices.Length==4),"Actual pants pocket outline keeps Draw Patch-sized density, side="+side);
                        Check(edges.All(e=>Vector3.Distance(pocket.vertices[e.Item1],pocket.vertices[e.Item2])<density*4),"Projection on actual pants avoids long, stretched fan edges, side="+side);
                    }
                }
                reference=new Mesh();reference.vertices=polygon;reference.triangles=StudioRetopologyEditor.TriangulatePolygon(polygon);reference.RecalculateNormals();reference.RecalculateBounds();
                document=ScriptableObject.CreateInstance<StudioRetopologyDocument>();
                document.parts.Add(new StudioRetopologyDocument.Part{name="Polygon validation",reference=reference});document.selected=0;
                editor=new StudioRetopologyEditor(document,()=>{});
                var corners=(List<Vector3>)typeof(StudioRetopologyEditor).GetField("corners",Flags).GetValue(editor);
                typeof(StudioRetopologyEditor).GetField("patchAutoDensity",Flags).SetValue(editor,false);
                typeof(StudioRetopologyEditor).GetField("patchSegments",Flags).SetValue(editor,2);
                typeof(StudioRetopologyEditor).GetField("activeTool",Flags).SetValue(editor,Enum.Parse(typeof(StudioRetopologyEditor).GetField("activeTool",Flags).FieldType,"Polygon"));
                foreach(var point in polygon)Call("AddPolygonPoint",point,Vector3.forward);
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(corners.Count==5&&document.polygonPoints.Count==5&&document.parts[0].faces.Count==0,"Undo removes one clicked outline point without changing geometry");
                Undo.PerformRedo();Check(corners.Count==6&&document.polygonPoints.Count==6,"Redo restores a clicked outline point");
                var escape=new Event {type=EventType.KeyDown,keyCode=KeyCode.Escape};
                Check((bool)Call("HandlePolygonEscape",escape)&&escape.type==EventType.Used&&corners.Count==0&&document.polygonPoints.Count==0,"Escape consumes the key and cancels the polygon outline");
                Check(document.parts[0].faces.Count==0&&typeof(StudioRetopologyEditor).GetField("activeTool",Flags).GetValue(editor).ToString()=="Polygon","Escape preserves geometry and keeps the polygon tool active");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(corners.SequenceEqual(polygon)&&document.polygonPoints.Count==6,"Undo cancellation restores every polygon point");
                Call("CreatePolygon");
                Check(document.parts[0].faces.Count>=4&&document.parts[0].faces.All(f=>f.vertices.Length==4)&&corners.Count==0&&document.polygonPoints.Count==0,"Enter fills with quads and completes the draft");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(document.parts[0].faces.Count==0&&document.parts[0].vertices.Count==0&&corners.SequenceEqual(polygon),"Undo fill restores the complete editable outline");
                Undo.PerformUndo();Check(corners.Count==5,"Undo after reopening removes the most recent point");
                Undo.PerformUndo();Check(corners.Count==4,"A second Undo removes the preceding point");
                Undo.PerformRedo();Undo.PerformRedo();Undo.PerformRedo();
                Check(corners.Count==0&&document.parts[0].faces.Count>=4,"Redo restores both points and the completed quad patch");
                Undo.PerformUndo();
                // Branch the restored construction, as a user editing after Undo would.
                Call("RemovePolygonPoint");Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(corners.Count==6,"Backspace is itself undoable");
                document.symmetry=true;Call("CreatePolygon");
                Check(document.parts[0].faces.Count==coarse.faces.Count*2&&document.parts[0].faces.All(f=>f.vertices.Length==4),"Mirrored polygon fill produces quad grids on both sides");
                var before=document.parts[0].faces.Count;Call("AddPolygonPoint",polygon[0],Vector3.forward);Call("AddPolygonPoint",polygon[1],Vector3.forward);
                try{Call("CreatePolygon");}catch(TargetInvocationException){}
                Check(document.parts[0].faces.Count==before&&corners.Count==2,"Incomplete fill keeps its outline and existing geometry");
                Call("ClearPatchCorners",true,true);document.symmetry=false;document.project=false;
                var seam=new StudioRetopologyDocument.Part{name="Seam validation",reference=reference};
                seam.vertices.AddRange(new[]{Vector3.zero,new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(0,2,0)});
                seam.uv.AddRange(Enumerable.Repeat(Vector2.zero,4));seam.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                document.parts[0]=seam;
                foreach(var point in new[]{new Vector3(1,0,0),new Vector3(3,0,0),new Vector3(3,2,0),new Vector3(1,2,0)})Call("AddPolygonPoint",point,Vector3.forward);
                Call("CreatePolygon");
                Check(seam.faces.Count==3&&seam.vertices.Count==8&&seam.faces[0].vertices.Length==4,"Grid matches the existing border count and preserves the neighboring quad");
                Check(StudioRetopologyLoops.Boundaries(seam).Count==1&&StudioRetopologyOperations.Edges(seam).All(e=>e.Value.Count<=2),"Attached polygon grid has no T-junctions or occupied edges");
                foreach(var point in new[]{Vector3.zero,new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(0,2,0)})Call("AddPolygonPoint",point,Vector3.forward);
                bool overlap=false;try{Call("CreatePolygon");}catch(TargetInvocationException e){overlap=e.GetBaseException() is InvalidOperationException;}
                Check(overlap&&seam.faces.Count==3&&corners.Count==4,"Overlapping fill rolls back geometry and preserves the editable outline");
                Call("ClearPatchCorners",true,true);
                var bent=new StudioRetopologyDocument.Part {reference=reference};
                bent.vertices.AddRange(new[]{new Vector3(-1,0,.5f),Vector3.zero,Vector3.up,new Vector3(-1,1,.5f)});
                bent.uv.AddRange(Enumerable.Repeat(Vector2.zero,4));bent.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                document.parts[0]=bent;
                var bendGrid=StudioRetopologyEditor.BuildPolygonGrid(new[]{Vector3.zero,new Vector3(1,0,.5f),new Vector3(1,1,.5f),Vector3.up},0,1);
                Call("AppendPolygonGrid",bendGrid,Vector3.right,4,false);
                Check(bent.faces.Count==2&&StudioRetopologyOperations.Edges(bent)[StudioRetopologyOperations.Edge(1,2)].Count==2,"Curved seam uses its local face plane when the overall outline normal would report a false overlap");
                var guarded=new StudioRetopologyDocument.Part();
                guarded.vertices.AddRange(new[]{new Vector3(-1,0,0),Vector3.zero,Vector3.up,new Vector3(-1,1,0),new Vector3(-.1f,.5f,0)});
                guarded.faces.Add(new StudioRetopologyDocument.Face(new[]{0,1,2,3}));
                var transition=new StudioRetopologyDocument.Part();
                transition.vertices.AddRange(new[]{Vector3.zero,Vector3.up,new Vector3(.1f,.5f,0)});
                transition.faces.Add(new StudioRetopologyDocument.Face(new[]{0,2,1}));
                var beforeGuard=guarded.vertices.Take(4).ToArray();
                StudioRetopologyEditor.ProtectPolygonSeams(guarded,transition,new[]{1,2,4},4,StudioRetopologyOperations.Edges(guarded));
                Check(guarded.vertices[4].x>0&&guarded.vertices.Take(4).SequenceEqual(beforeGuard),"Projection cannot fold a transition triangle across its seam or move existing vertices");
                var safePosition=guarded.vertices[4];
                StudioRetopologyEditor.ProtectPolygonSeams(guarded,transition,new[]{1,2,4},4,StudioRetopologyOperations.Edges(guarded));
                Check(guarded.vertices[4]==safePosition,"Valid projected seam vertices remain unchanged");
                transition.vertices[2]=new Vector3(-.1f,.5f,0);guarded.vertices[4]=transition.vertices[2];
                StudioRetopologyEditor.ProtectPolygonSeams(guarded,transition,new[]{1,2,4},4,StudioRetopologyOperations.Edges(guarded));
                Check(guarded.vertices[4].x<0,"Invalid original overlap is not repaired or concealed by the projection guard");
                StudioRetopologyDocument.Part Ribbons(int leftRows,int rightRows)
                {
                    var p=new StudioRetopologyDocument.Part{name="Curved joined patches",reference=reference};
                    void Ribbon(float from,float to,int rows)
                    {
                        int start=p.vertices.Count;
                        for(int y=0;y<=rows;y++)
                        {
                            float v=(float)y/rows,z=.2f*Mathf.Sin(v*Mathf.PI);
                            p.vertices.Add(new Vector3(from,v,z));p.vertices.Add(new Vector3(to,v,z));
                            p.uv.Add(Vector2.zero);p.uv.Add(Vector2.zero);
                        }
                        for(int y=0;y<rows;y++)p.faces.Add(new StudioRetopologyDocument.Face(new[]{start+y*2,start+y*2+1,start+y*2+3,start+y*2+2}));
                    }
                    Ribbon(0,1,leftRows);Ribbon(2,3,rightRows);return p;
                }
                foreach(bool extraClick in new[]{false,true})
                {
                    Call("ClearPatchCorners",true,true);document.project=false;
                    var joined=Ribbons(7,7);document.parts[0]=joined;
                    var sourceVertices=joined.vertices.ToArray();var sourceFaces=joined.faces.Select(f=>f.vertices.ToArray()).ToArray();
                    var clicked=new List<Vector3>{joined.vertices[1],joined.vertices[16],joined.vertices[30],joined.vertices[15]};
                    if(extraClick)clicked.Add(joined.vertices[7]);
                    typeof(StudioRetopologyEditor).GetField("patchSegments",Flags).SetValue(editor,extraClick?12:2);
                    foreach(var point in clicked)Call("AddPolygonPoint",point,Vector3.forward);
                    Call("CreatePolygon");
                    int added=extraClick?84:14;
                    Check(joined.faces.Count==14+added&&joined.faces.All(f=>f.vertices.Length==4),"Polygon matches both curved seven-edge borders despite requested divisions, extraClick="+extraClick);
                    var joinedEdges=StudioRetopologyOperations.Edges(joined);
                    Check(Enumerable.Range(0,7).All(i=>joinedEdges[StudioRetopologyOperations.Edge(1+i*2,3+i*2)].Count==2&&joinedEdges[StudioRetopologyOperations.Edge(16+i*2,18+i*2)].Count==2),"Every edge on both curved seams is welded to exactly two faces, extraClick="+extraClick);
                    Check(StudioRetopologyLoops.Boundaries(joined).Count==1,"Joining two patches leaves only the outer boundary and no seam holes, extraClick="+extraClick);
                    Check(Enumerable.Range(0,sourceVertices.Length).All(i=>joined.vertices[i]==sourceVertices[i])&&Enumerable.Range(0,sourceFaces.Length).All(i=>joined.faces[i].vertices.SequenceEqual(sourceFaces[i])),"Joining preserves existing border positions and neighboring quads, extraClick="+extraClick);
                    Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                    Check(document.parts[0].faces.Count==14&&corners.SequenceEqual(clicked),"Undo join restores both original patches and the clicked outline, extraClick="+extraClick);
                    Undo.PerformRedo();Check(document.parts[0].faces.Count==14+added,"Redo restores the sealed join, extraClick="+extraClick);
                }
                Call("ClearPatchCorners",true,true);var mismatch=Ribbons(7,5);document.parts[0]=mismatch;
                var mismatchVertices=mismatch.vertices.ToArray();var mismatchFaces=mismatch.faces.Count;
                foreach(var point in new[]{mismatch.vertices[1],mismatch.vertices[16],mismatch.vertices[26],mismatch.vertices[15]})Call("AddPolygonPoint",point,Vector3.forward);
                Call("CreatePolygon");
                Check(mismatch.faces.Count>mismatchFaces&&mismatch.faces.All(f=>f.vertices.Length==3||f.vertices.Length==4),"Unequal border resolutions fill with triangle and quad transitions");
                var transitionEdges=StudioRetopologyOperations.Edges(mismatch);
                Check(Enumerable.Range(0,7).All(i=>transitionEdges[StudioRetopologyOperations.Edge(1+i*2,3+i*2)].Count==2)&&Enumerable.Range(0,5).All(i=>transitionEdges[StudioRetopologyOperations.Edge(16+i*2,18+i*2)].Count==2),"Every edge of both unequal seams is welded to exactly two faces");
                Check(StudioRetopologyLoops.Boundaries(mismatch).Count==1&&transitionEdges.All(e=>e.Value.Count<=2),"Triangle transition has no seam holes or nonmanifold edges");
                Check(mismatchVertices.Select((p,i)=>p==mismatch.vertices[i]).All(v=>v),"Triangle transition preserves existing vertex positions");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(document.parts[0].faces.Count==mismatchFaces&&corners.Count==4,"Undo triangle transition restores both patches and complete outline");
                Undo.PerformRedo();
                Check(StudioRetopologyLoops.Boundaries(document.parts[0]).Count==1,"Redo restores the sealed triangle transition");
                editor.Dispose();editor=null;

                var skeleton=(GameObject)typeof(AnimationStudioWindow).GetMethod("ResolveSdkSkeleton",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                skinDocument=ScriptableObject.CreateInstance<StudioSkinningDocument>();skinDocument.skeleton=skeleton;
                skinEditor=new StudioSkinningEditor(skinDocument,()=>{});
                var staticPart=new StudioSkinningDocument.Part{name="Polygon validation static",mesh=reference,materials=Array.Empty<Material>()};
                var transfer=skinEditor.CreateClothTransfer(staticPart);assets.Add(AssetDatabase.GetAssetPath(transfer));
                var staticMesh=transfer.GetComponent<MeshFilter>().sharedMesh;assets.Add(AssetDatabase.GetAssetPath(staticMesh));
                Check(staticMesh!=reference&&staticMesh.vertices.SequenceEqual(reference.vertices),"Unskinned transfer persists an independent mesh with unchanged coordinates");
                Check(transfer.GetComponentsInChildren<Renderer>(true).Length==1,"Transfer contains only the selected mesh");
                var defaults=StudioDefaultReferences.LoadSkinning(StudioDefaultReferences.SkinningPath);
                if(!defaults||!defaults.shirt)throw new Exception("Default shirt unavailable for skin transfer test");
                skinEditor.Import(defaults.shirt);
                var part=skinDocument.parts[skinDocument.selected];var original=part.mesh.vertices;
                Check(part.bound,"Imported FBX retains skin weights");
                transfer=skinEditor.CreateClothTransfer(part);assets.Add(AssetDatabase.GetAssetPath(transfer));
                var skin=transfer.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();assets.Add(AssetDatabase.GetAssetPath(skin.sharedMesh));
                var weights=new StudioSkinWeights(skin.sharedMesh);
                Check(weights.HasWeights&&skin.sharedMesh.vertexCount==part.mesh.vertexCount,"Skinned transfer preserves vertex count and skin weights");
                Check(weights.Values.SelectMany(v=>v).All(w=>w.boneIndex<skin.bones.Length&&skin.bones[w.boneIndex]),"Every transferred weight resolves to a skeleton bone");
                Check(skin.sharedMesh.bindposes.Length==skin.bones.Length&&skin.sharedMesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)),"Transfer bind poses and coordinates are valid");
                Check(part.mesh.vertices.SequenceEqual(original),"Transfer leaves working skin geometry unchanged");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally
            {
                editor?.Dispose();skinEditor?.Dispose();
                if(document){Undo.ClearUndo(document);Object.DestroyImmediate(document);}
                if(skinDocument){foreach(var part in skinDocument.parts)if(part.mesh&&!EditorUtility.IsPersistent(part.mesh))Object.DestroyImmediate(part.mesh);Undo.ClearUndo(skinDocument);Object.DestroyImmediate(skinDocument);}
                if(reference)Object.DestroyImmediate(reference);
                foreach(var asset in assets)AssetDatabase.DeleteAsset(asset);
                File.WriteAllLines(Report,log);
            }
        }
    }
}
