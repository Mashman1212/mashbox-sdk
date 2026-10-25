using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Part=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Part;
using Face=MashBoxSDK.AnimationStudio.StudioRetopologyDocument.Face;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioRetopologyValidation
    {
        [InitializeOnLoadMethod] private static void RequestedValidation()
        {
            const string marker="Library/MashBox/RunRetopologyValidation";
            if(!File.Exists(marker))return;
            // A project-level delayCall can throw before ours is reached. Run on
            // the first idle update instead, and consume the request only then.
            void Tick()
            {
                if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
                EditorApplication.update-=Tick;File.Delete(marker);Run();
            }
            EditorApplication.update+=Tick;
        }
        [MenuItem("MashBox/Retopology/Validate Tools")]
        private static void Run()
        {
            var messages=new List<string>();var owned=new List<UnityEngine.Object>();
            void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);messages.Add("PASS: "+name);}
            try
            {
                var p=Grid(14);int original=p.faces.Count;
                var source=StudioRetopologyDocument.BuildMesh(p);owned.Add(source);p.reference=source;
                var before=source.vertices;var surface=new StudioRetopologySurface(source);
                var projected=surface.Project(new Vector3(.35f,2,.43f),.01f);
                Check(Mathf.Abs(projected.y-.01f)<1e-5f,"Surface projection and offset");
                var boundary=StudioRetopologyOperations.Edges(p).Where(e=>e.Value.Count==1).SelectMany(e=>new[]{p.vertices[e.Key.Item1],p.vertices[e.Key.Item2]}).Distinct().ToArray();
                int triangles=StudioRetopologyReduction.Reduce(p,120,true,60);
                Check(triangles<original,"Reduction lowers the triangle count");
                Check(boundary.All(v=>p.vertices.Contains(v)),"Clothing opening boundaries stay fixed");
                Check(StudioRetopologyOperations.Edges(p).All(e=>e.Value.Count<=2),"Reduction preserves manifold edges");
                Check(p.faces.All(f=>Vector3.Cross(p.vertices[f.vertices[1]]-p.vertices[f.vertices[0]],p.vertices[f.vertices[2]]-p.vertices[f.vertices[0]]).y>0),"Reduction keeps winding and nonzero faces");
                Check(p.uv.Count==p.vertices.Count&&p.faces.All(f=>f.vertices.All(i=>i>=0&&i<p.vertices.Count)),"Reduced UV stream and indices remain valid");
                Check(source.vertices.SequenceEqual(before),"Reference geometry is untouched");
                var cancel=Grid(14);var cancellationBefore=cancel.vertices.ToArray();int faceCount=cancel.faces.Count;bool cancelled=false;
                try{StudioRetopologyReduction.Reduce(cancel,60,true,60,_=>true);}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled&&cancel.vertices.SequenceEqual(cancellationBefore)&&cancel.faces.Count==faceCount,"Cancelled reduction leaves input intact");
                var pair=Grid(2);int pairs=StudioRetopologyOperations.PairQuads(pair);
                Check(pairs==4&&pair.faces.All(f=>f.vertices.Length==4),"Coplanar triangle pairs become quads");
                pair.faces[0].material=1;var mesh=StudioRetopologyDocument.BuildMesh(pair);owned.Add(mesh);
                Check(mesh.subMeshCount==2&&mesh.triangles.Length==24,"Quad rendering preserves materials and triangle counts");
                StudioRetopologyOperations.Subdivide(pair,surface,0);
                Check(pair.faces.Count==16&&pair.faces.All(f=>f.vertices.Length==4),"Subdivision retains quad topology");
                Check(StudioRetopologyOperations.Edges(pair).All(e=>e.Value.Count<=2),"Subdivision shares edge vertices");
                var relax=Grid(6);var edges=StudioRetopologyOperations.Edges(relax);var locked=edges.Where(e=>e.Value.Count==1).SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}).Distinct().ToArray();
                var positions=relax.vertices.ToArray();relax.vertices[24]+=Vector3.up*.2f;
                StudioRetopologyOperations.Relax(relax,surface,true,0);
                Check(locked.All(i=>relax.vertices[i]==positions[i])&&Mathf.Abs(relax.vertices[24].y)<1e-5f,"Relax pins openings and projects to reference");
                var mirror=Grid(2);StudioRetopologyOperations.Mirror(mirror,true);
                Check(mirror.vertices.All(v=>mirror.vertices.Any(w=>(w-new Vector3(-v.x,v.y,v.z)).sqrMagnitude<1e-10f)),"Mirror creates matching opposite-side positions");
                Check(StudioRetopologyOperations.Edges(mirror).All(e=>e.Value.Count<=2),"Mirror avoids duplicate center topology");
                var crossing=Grid(1);crossing.vertices=crossing.vertices.Select(v=>v-Vector3.right*.5f).ToList();
                StudioRetopologyOperations.Mirror(crossing,true);
                Check(StudioRetopologyOperations.Edges(crossing).Where(e=>Mathf.Abs(crossing.vertices[e.Key.Item1].x)<1e-6f&&Mathf.Abs(crossing.vertices[e.Key.Item2].x)<1e-6f).All(e=>e.Value.Count==2),"Mirror clips crossing faces without a center slit");
                var tubes=new Part{name="Sleeve loops"};
                for(int row=0;row<4;row++)for(int i=0;i<8;i++)
                {float angle=i*Mathf.PI/4;tubes.vertices.Add(new Vector3(Mathf.Cos(angle),row,Mathf.Sin(angle)));tubes.uv.Add(new Vector2(i/8f,row/3f));}
                foreach(int row in new[]{0,2})for(int i=0;i<8;i++)
                {int j=(i+1)%8;tubes.faces.Add(new Face(new[]{row*8+i,(row+1)*8+i,(row+1)*8+j,row*8+j}));}
                Check(StudioRetopologyLoops.Boundaries(tubes).Count==4,"Sleeve boundary loops detected");
                StudioRetopologyLoops.Bridge(tubes,new HashSet<int>(Enumerable.Range(8,16)),3,null,0);
                Check(tubes.faces.Count==40&&tubes.faces.All(f=>f.vertices.Length==4),"Bridge generates aligned quad rows");
                Check(StudioRetopologyLoops.Boundaries(tubes).Count==2&&StudioRetopologyOperations.Edges(tubes).All(e=>e.Value.Count<=2),"Bridge closes both selected seams and keeps cuffs open");
                var clean=Grid(2);clean.faces.Add(new Face((int[])clean.faces[0].vertices.Clone()));clean.faces.Add(new Face(new[]{0,0,1}));clean.vertices.Add(Vector3.one*99);clean.uv.Add(Vector2.zero);
                StudioRetopologyOperations.Clean(clean);Check(clean.faces.Count==8&&clean.vertices.Count==9,"Cleanup removes duplicate, degenerate and unused geometry");
                var document=ScriptableObject.CreateInstance<StudioRetopologyDocument>();owned.Add(document);document.parts.Add(new Part{name="Test",reference=source});document.selected=0;
                using(var editor=new StudioRetopologyEditor(document,()=>{}))Check(editor.Scene.IsValid(),"Retopology preview lifecycle creates successfully");
                // Verify on a real clothing mesh too, without mutating or opening the user's workspace.
                string path="Packages/com.mg.mashbox.sdk/Runtime/Characters/Human/Human_Shirt_Default_Hoodie.fbx";var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(model,"SDK hoodie is available for the real-mesh check");
                if(model)
                {
                    var originalMesh=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh).FirstOrDefault(m=>m);
                    if(originalMesh)
                    {
                        var copy=StudioSkinningDocument.CreateEditableCopy(originalMesh);owned.Add(copy);var clothing=new Part{name="Hoodie",reference=copy};StudioRetopologyDocument.CopyReference(clothing);
                        int start=clothing.faces.Sum(f=>f.vertices.Length-2);
                        var clock=System.Diagnostics.Stopwatch.StartNew();int end=StudioRetopologyReduction.Reduce(clothing,Mathf.Max(500,start/2),true,65);
                        Check(end<start,"Real hoodie reduces with protected seams");
                        Check(StudioRetopologyOperations.Edges(clothing).All(e=>e.Value.Count<=2),"Reduced hoodie has no non-manifold edges");
                        messages.Add($"Hoodie: {start} to {end} triangles in {clock.ElapsedMilliseconds} ms");
                        StudioRetopologyOperations.PairQuads(clothing);
                        var exportedMesh=StudioRetopologyDocument.BuildMesh(clothing);owned.Add(exportedMesh);
                        string exportPath="Assets/__RetopologyValidation_"+Guid.NewGuid().ToString("N")+".fbx";
                        var exportScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                        var exportRoot=new GameObject("Retopology check"){hideFlags=HideFlags.HideAndDontSave};
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(exportRoot,exportScene);
                        try
                        {
                            exportRoot.AddComponent<MeshFilter>().sharedMesh=exportedMesh;
                            exportRoot.AddComponent<MeshRenderer>();
                            StudioModelEditor.ExportTo(Path.GetFullPath(exportPath),exportRoot);
                            AssetDatabase.ImportAsset(exportPath,ImportAssetOptions.ForceSynchronousImport);
                            var imported=AssetDatabase.LoadAssetAtPath<GameObject>(exportPath);
                            Check(imported,"Reduced clothing FBX reimports successfully");
                            var result=imported.GetComponentInChildren<MeshFilter>().sharedMesh;
                            Check(result.triangles.Length==exportedMesh.triangles.Length,"FBX round trip preserves triangle count");
                            Check(result.uv.Length==result.vertexCount&&result.normals.Length==result.vertexCount,"FBX contains UVs and normals");
                        }
                        finally
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(exportScene);
                            AssetDatabase.DeleteAsset(exportPath);
                        }
                    }
                }
                messages.Add("SUCCESS");Debug.Log("Retopology validation:\n"+string.Join("\n",messages));
            }
            catch(Exception e){messages.Add("FAIL: "+e);Debug.LogException(e);}
            finally
            {
                foreach(var obj in owned)if(obj)UnityEngine.Object.DestroyImmediate(obj);
                Directory.CreateDirectory("Library/MashBox");File.WriteAllLines("Library/MashBox/RetopologyValidation.txt",messages);
            }
        }
        private static Part Grid(int count)
        {
            var part=new Part { name="Grid" };
            for(int z=0;z<=count;z++)for(int x=0;x<=count;x++){part.vertices.Add(new Vector3((float)x/count,0,(float)z/count));part.uv.Add(new Vector2((float)x/count,(float)z/count));}
            for(int z=0;z<count;z++)for(int x=0;x<count;x++)
            {int a=z*(count+1)+x,b=a+1,c=a+count+1,d=c+1;part.faces.Add(new Face(new[]{a,c,d}));part.faces.Add(new Face(new[]{a,d,b}));}
            return part;
        }
    }
}
