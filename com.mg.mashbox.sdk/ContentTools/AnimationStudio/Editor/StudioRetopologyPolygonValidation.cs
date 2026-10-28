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
        [InitializeOnLoadMethod] static void Schedule() { if(Application.dataPath.EndsWith("MashBoxContentCreation - U6/Assets")&&!File.Exists(Report)) EditorApplication.delayCall+=Run; }
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
                Check(coarse.faces.Count>4&&coarse.faces.All(f=>f.vertices.Length==4),"Concave outline fills with a subdivided all-quad patch");
                Check(Mathf.Abs(QuadArea(coarse)-3)<.0001f,"Concave quad patch stays inside its outline without gaps or overlaps");
                Check(dense.faces.Count>coarse.faces.Count&&Mathf.Abs(QuadArea(dense)-3)<.0001f,"Higher divisions produce a denser quad grid with the same outline");
                Check(StudioRetopologyLoops.Boundaries(dense).Count==1&&StudioRetopologyOperations.Edges(dense).All(e=>e.Value.Count<=2),"Dense quad patch is connected and has no cracks or nonmanifold edges");
                var rectangle=new[]{Vector3.zero,new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(0,1,0)};
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
                Call("CreatePolygon");
                Check(document.parts[0].faces.Count>4&&document.parts[0].faces.All(f=>f.vertices.Length==4)&&corners.Count==0&&document.polygonPoints.Count==0,"Enter fills with quads and completes the draft");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(document.parts[0].faces.Count==0&&document.parts[0].vertices.Count==0&&corners.SequenceEqual(polygon),"Undo fill restores the complete editable outline");
                Undo.PerformUndo();Check(corners.Count==5,"Undo after reopening removes the most recent point");
                Undo.PerformUndo();Check(corners.Count==4,"A second Undo removes the preceding point");
                Undo.PerformRedo();Undo.PerformRedo();Undo.PerformRedo();
                Check(corners.Count==0&&document.parts[0].faces.Count>4,"Redo restores both points and the completed quad patch");
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
                Check(seam.faces.Count==5&&seam.vertices.Count==11&&seam.faces[0].vertices.Length==5,"Grid welds existing corners and conforms its subdivided shared border");
                Check(StudioRetopologyLoops.Boundaries(seam).Count==1&&StudioRetopologyOperations.Edges(seam).All(e=>e.Value.Count<=2),"Attached polygon grid has no T-junctions or occupied edges");
                foreach(var point in new[]{Vector3.zero,new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(0,2,0)})Call("AddPolygonPoint",point,Vector3.forward);
                bool overlap=false;try{Call("CreatePolygon");}catch(TargetInvocationException e){overlap=e.GetBaseException() is InvalidOperationException;}
                Check(overlap&&seam.faces.Count==5&&corners.Count==4,"Overlapping fill rolls back geometry and preserves the editable outline");
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

