#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainSurfaceLodValidation
    {
        const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        static readonly Type Builder = typeof(MGTerrain).Assembly.GetType("MashBoxSDK.Maps.TerrainSystem.MGTerrainSurfaceLodBuilder");
        static object Get(object o, string n) => o.GetType().GetField(n, Fields).GetValue(o);
        static void Set(object o, string n, object value) => o.GetType().GetField(n, Fields).SetValue(o, value);
        static object Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, Fields).Invoke(o, args);
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static object Input(int n, bool hills = false)
        {
            var input = Activator.CreateInstance(Builder.GetNestedType("Input", BindingFlags.NonPublic), true);
            var v = new Vector3[n*n]; var uv = new Vector4[n*n];
            for(int z=0;z<n;z++) for(int x=0;x<n;x++) { v[z*n+x] = new Vector3(x,hills ? Mathf.Sin(x*.04f)*Mathf.Cos(z*.03f)*3 : 0,z); uv[z*n+x]=new Vector4((float)x/(n-1),(float)z/(n-1),0,0); }
            var tris = new List<int>();
            for(int z=0;z<n-1;z++) for(int x=0;x<n-1;x++) { int a=z*n+x; tris.AddRange(new[]{a,a+n,a+1,a+1,a+n,a+n+1}); }
            Set(input,"width",n); Set(input,"height",n); Set(input,"vertices",v); Set(input,"normals",Enumerable.Repeat(Vector3.up,n*n).ToArray());
            Set(input,"tangents",Array.Empty<Vector4>()); Set(input,"colors",Enumerable.Repeat(Color.white,n*n).ToArray());
            Set(input,"uv",new[]{uv,Array.Empty<Vector4>(),Array.Empty<Vector4>(),Array.Empty<Vector4>(),Array.Empty<Vector4>(),Array.Empty<Vector4>(),Array.Empty<Vector4>(),Array.Empty<Vector4>()});
            Set(input,"triangles",new[]{tris.ToArray()}); return input;
        }
        static Array Build(object input) => (Array)Builder.GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{input,(object)CancellationToken.None});
        static long Edge(int a,int b) => ((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
        static Dictionary<long,int> Edges(object level)
        {
            var result=new Dictionary<long,int>(); var indices=(int[])Get(level,"sourceIndices");
            foreach(var tris in (int[][])Get(level,"triangles")) for(int i=0;i<tris.Length;i+=3)
                for(int e=0;e<3;e++) { long key=Edge(indices[tris[i+e]],indices[tris[i+(e+1)%3]]); result.TryGetValue(key,out int count); result[key]=count+1; }
            return result;
        }
        static HashSet<long> SourceBoundary(object input)
        {
            var identity=Enumerable.Range(0,((Vector3[])Get(input,"vertices")).Length).ToArray();
            var level=Activator.CreateInstance(Builder.GetNestedType("Level",BindingFlags.NonPublic),true);
            Set(level,"sourceIndices",identity); Set(level,"triangles",Get(input,"triangles"));
            return new HashSet<long>(Edges(level).Where(pair=>pair.Value==1).Select(pair=>pair.Key));
        }
        static void ValidateTopology(object input, Array levels)
        {
            var boundary=SourceBoundary(input); var vertices=(Vector3[])Get(input,"vertices");
            foreach(var level in levels)
            {
                var edges=Edges(level);
                Check(edges.All(pair=>pair.Value==1||pair.Value==2),"Non-manifold LOD edge.");
                Check(boundary.SetEquals(edges.Where(pair=>pair.Value==1).Select(pair=>pair.Key)),"LOD changed a boundary or introduced a crack/T junction.");
                var sources=(int[])Get(level,"sourceIndices");
                foreach(var tris in (int[][])Get(level,"triangles")) for(int i=0;i<tris.Length;i+=3)
                {
                    var a=vertices[sources[tris[i]]]; var b=vertices[sources[tris[i+1]]]; var c=vertices[sources[tris[i+2]]];
                    Check(Vector3.Cross(b-a,c-a).y>0,"LOD contains reversed/degenerate triangles.");
                }
            }
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Surface LOD")]
        public static void Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"mg-surface-lod"); Directory.CreateDirectory(root);
            var notes=new List<string>();
            var scene=EditorSceneManager.NewPreviewScene(); GameObject owner=null; Mesh mesh=null; Material materialA=null, materialB=null;
            try
            {
                var input=Input(65); var original=((Vector3[])Get(input,"vertices")).ToArray(); var levels=Build(input);
                Check(levels.Length==3,"Expected three useful flat-grid LODs."); ValidateTopology(input,levels);
                Check((int)Get(levels.GetValue(2),"triangleCount") < 8192/8,"Flat-grid LOD should substantially reduce triangles.");
                Check(original.SequenceEqual((Vector3[])Get(input,"vertices")),"Source was mutated.");
                notes.Add("PASS: flat-grid reduction, all LOD boundary edges identical, manifold topology, winding, immutable source.");
                var hills=Input(65,true); var hillLevels=Build(hills); ValidateTopology(hills,hillLevels);
                Check(hillLevels.Length>0,"Curved terrain must retain useful LODs.");
                notes.Add("PASS: curved terrain adaptive protection and topology.");
                var hole=Input(65); var indices=((int[][])Get(hole,"triangles"))[0].ToList(); indices.RemoveRange((17*64+17)*6,6); Set(hole,"triangles",new[]{indices.ToArray()}); ValidateTopology(hole,Build(hole));
                notes.Add("PASS: hole perimeter remains exact at every LOD.");
                var split=Input(65); var all=((int[][])Get(split,"triangles"))[0]; Set(split,"triangles",new[]{all.Take(all.Length/2).ToArray(),all.Skip(all.Length/2).ToArray()}); ValidateTopology(split,Build(split));
                notes.Add("PASS: material partition and mesh boundaries remain closed.");
                var moved=Input(65); ((Vector3[])Get(moved,"vertices"))[20].x+=.1f; Check(Build(moved).Length==0,"Non-grid source must retain original geometry.");
                notes.Add("PASS: arbitrary X/Z edits use original surface.");
                var paint=Input(65); ((Color[])Get(paint,"colors"))[20*65+20]=Color.red;
                foreach(var level in Build(paint)) Check(((int[])Get(level,"sourceIndices")).Contains(20*65+20),"Paint feature was removed.");
                notes.Add("PASS: isolated vertex paint feature retained.");
                owner=new GameObject("Surface LOD validation"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner,scene); owner.SetActive(false);
                var filter=owner.AddComponent<MeshFilter>(); owner.AddComponent<MeshRenderer>(); var collider=owner.AddComponent<MeshCollider>();
                mesh=new Mesh{vertices=original,triangles=((int[][])Get(input,"triangles"))[0],normals=(Vector3[])Get(input,"normals")}; mesh.RecalculateBounds(); filter.sharedMesh=mesh; collider.sharedMesh=mesh;
                materialA=new Material(Shader.Find("Hidden/InternalErrorShader")); materialB=new Material(Shader.Find("Hidden/InternalErrorShader")); owner.GetComponent<MeshRenderer>().sharedMaterial=materialA;
                var terrain=owner.AddComponent<MGTerrain>(); terrain.Configure(filter,owner.GetComponent<MeshRenderer>(),collider); terrain.ConfigureSurfaceGrid(65,65);
                Set(terrain,"m_DrawInstances",false); owner.SetActive(true); terrain.RefreshSurfaceTiles();
                Set(terrain,"m_SurfaceLodInput",input); foreach(var level in levels) Call(terrain,"UploadSurfaceLod",level);
                var camera=owner.AddComponent<Camera>(); camera.enabled=false; camera.pixelRect=new Rect(0,0,1920,1080);
                camera.transform.position=new Vector3(32,100,-1500); camera.transform.LookAt(new Vector3(32,0,32));
                // Camera shares the owner only in this synthetic preview; use a separate identity terrain transform.
                var camGo=new GameObject("LOD test camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo,scene); var testCamera=camGo.AddComponent<Camera>(); testCamera.enabled=false; testCamera.CopyFrom(camera); testCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation); owner.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod>0,"Distant camera did not select LOD.");
                Check(filter.sharedMesh==mesh && collider.sharedMesh==mesh,"LOD must never replace source/collision mesh.");
                Set(terrain,"m_SurfaceLodPixelError",.25f); Call(terrain,"SelectSurfaceRendering",testCamera);
                int flatLow = terrain.ActiveSurfaceLod;
                Set(terrain,"m_SurfaceLodPixelError",8f); Call(terrain,"SelectSurfaceRendering",testCamera);
                Check(flatLow == terrain.ActiveSurfaceLod && flatLow == levels.Length, "Zero-error flat terrain should use the coarsest LOD at either pixel-error extreme.");
                Set(terrain,"m_SurfaceLodPixelError",1.5f);
                notes.Add("PASS: flat terrain correctly stays at coarsest LOD across the entire pixel-error slider.");
                var meshes=owner.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
                for(int i=0;i<100;i++) { testCamera.transform.position+=Vector3.right; Call(terrain,"SelectSurfaceRendering",testCamera); }
                Check(meshes.SequenceEqual(owner.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh)),"Camera movement rebuilt cached meshes.");
                owner.GetComponent<MeshRenderer>().sharedMaterial=materialB; Call(terrain,"SelectSurfaceRendering",testCamera);
                testCamera.transform.position=new Vector3(32,5,32); Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod==0,"Near camera must retain original surface.");
                Check(owner.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterial==materialB),"Near chunks must receive material changes made while using a distant proxy.");
                notes.Add("PASS: material changes propagate from distant proxy back to every near chunk.");
                testCamera.transform.position=new Vector3(32,100,-1500); Set(terrain,"m_AppearanceCaptureCamera",testCamera); Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod==0,"Appearance capture must retain original geometry."); Set(terrain,"m_AppearanceCaptureCamera",null);
                foreach(var lod in (IEnumerable)Get(terrain,"m_SurfaceLods")) Set(lod,"error",2f); testCamera.fieldOfView=90; Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod>0,"Wide view should permit bounded LOD."); testCamera.fieldOfView=10; Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod==0,"Zooming must restore detail when projected error grows.");
                notes.Add("PASS: capture bypass and field-of-view changes respect the screen-space error limit.");
                Call(terrain,"ReleaseSurfaceLods");
                Set(terrain,"m_SurfaceLodInput",hills); foreach(var level in hillLevels) Call(terrain,"UploadSurfaceLod",level);
                float maximumError = (float)Get(hillLevels.GetValue(hillLevels.Length-1),"error");
                Check(maximumError > .0001f, "Curved fixture must have nonzero simplification error.");
                testCamera.orthographic = true; testCamera.orthographicSize = testCamera.pixelHeight * maximumError * .5f;
                Set(terrain,"m_SurfaceLodPixelError",.25f); Call(terrain,"SelectSurfaceRendering",testCamera);
                int curvedLow = terrain.ActiveSurfaceLod;
                var cachedMeshes = ((IEnumerable)Get(terrain,"m_SurfaceLods")).Cast<object>().Select(l=>Get(l,"mesh")).ToArray();
                Set(terrain,"m_SurfaceLodPixelError",8f);
                int activeBeforePreview = terrain.ActiveSurfaceLod;
                terrain.GetSurfaceLodPreview(testCamera, out int previewLod, out long sourceTriangles, out long previewTriangles);
                Check(terrain.ActiveSurfaceLod == activeBeforePreview, "Diagnostics changed camera render state.");
                Call(terrain,"SelectSurfaceRendering",testCamera);
                Check(terrain.ActiveSurfaceLod > curvedLow && terrain.ActiveSurfaceLod == hillLevels.Length, "Pixel-error slider must select a coarser cached mesh on curved terrain.");
                Check(previewLod == terrain.ActiveSurfaceLod && previewTriangles == terrain.ActiveSurfaceLodTriangleCount, "Diagnostics and rendering disagree.");
                Check(cachedMeshes.SequenceEqual(((IEnumerable)Get(terrain,"m_SurfaceLods")).Cast<object>().Select(l=>Get(l,"mesh"))), "Pixel-error changes rebuilt cached meshes.");
                Set(terrain,"m_SurfaceLodPixelError",1.5f);
                notes.Add("PASS: actual curved-grid LODs switch with pixel error; diagnostics agree without changing render state.");
                testCamera.transform.position=new Vector3(32,100,-1500); Set(terrain,"m_DistantMorphTop",100f); Call(terrain,"SelectSurfaceRendering",testCamera); Check(terrain.ActiveSurfaceLod==0,"Shader-morphed terrain must retain safe original geometry.");
                Set(terrain,"m_DistantMorphTop",float.NegativeInfinity); terrain.NotifySurfaceMeshChanged(); terrain.RefreshSurfaceTiles(); Check(terrain.CachedSurfaceLodCount==0,"Editing must invalidate all stale LODs.");
                UnityEngine.Object.DestroyImmediate(camGo);
                notes.Add("PASS: distance selection, camera reuse, source/collider identity, close-up fidelity, shader morph guard, edit invalidation.");
                File.WriteAllLines(Path.Combine(root,"validation.txt"),notes); Debug.Log("MG Terrain Surface LOD validation passed.");
            }
            catch(Exception e) { notes.Add("FAIL: "+e); File.WriteAllLines(Path.Combine(root,"validation.txt"),notes); Debug.LogException(e); }
            finally { if(owner!=null) UnityEngine.Object.DestroyImmediate(owner); if(mesh!=null) UnityEngine.Object.DestroyImmediate(mesh); if(materialA!=null) UnityEngine.Object.DestroyImmediate(materialA); if(materialB!=null) UnityEngine.Object.DestroyImmediate(materialB); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif
