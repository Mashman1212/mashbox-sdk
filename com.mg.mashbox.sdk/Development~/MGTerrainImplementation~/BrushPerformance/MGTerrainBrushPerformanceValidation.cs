#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps.Sculpting;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainBrushPerformanceValidation
    {
        internal static void Measure(string name, Action step)
        {
            for (int i = 0; i < 3; i++) step();
            var timer = new System.Diagnostics.Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            timer.Start();
            for (int i = 0; i < 20; i++) step();
            timer.Stop();
            Debug.Log($"BRUSH PERFORMANCE {name}: {timer.Elapsed.TotalMilliseconds / 20:F3} ms, {(GC.GetAllocatedBytesForCurrentThread()-before)/20} bytes/sample");
        }
        static Mesh Grid(int width, float height)
        {
            var vertices = new Vector3[width*width]; var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            for (int z=0; z<width; z++) for(int x=0; x<width; x++)
            { int i=z*width+x; vertices[i]=new Vector3(x,height,z); uv[i]=new Vector2(x,z)/width; colors[i]=Color.white; }
            var indices=new int[(width-1)*(width-1)*6];
            for(int z=0,t=0;z<width-1;z++) for(int x=0;x<width-1;x++)
            { int i=z*width+x; indices[t++]=i; indices[t++]=i+width; indices[t++]=i+1; indices[t++]=i+1; indices[t++]=i+width; indices[t++]=i+width+1; }
            var mesh=new Mesh { indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=vertices,uv=uv,uv2=uv,uv3=uv,uv4=uv,colors=colors,triangles=indices };
            mesh.RecalculateBounds();mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Brush Performance")]
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this benchmark in an isolated batch project.");
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path)) EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/MergeValidationBootstrap.unity");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene,"Assets/BrushPerformance.unity");
            var go=new GameObject("Brush performance"); SceneManager.MoveGameObjectToScene(go,scene);
            var terrain=go.AddComponent<MGTerrain>(); var mesh=Grid(257,0); var source=Grid(2,30);
            // Two-triangle immutable reference covering the entire destination.
            var sv=source.vertices;for(int i=0;i<sv.Length;i++){sv[i].x*=256;sv[i].z*=256;}source.vertices=sv;source.RecalculateBounds();
            terrain.MeshFilter.sharedMesh=mesh; terrain.ConfigureSurfaceGrid(257,257);terrain.HeightOnlySculpt=true;
            using(var data=new SerializedObject(terrain)){data.FindProperty("m_SceneSculptCopy").boolValue=true;data.ApplyModifiedPropertiesWithoutUndo();}
            var modifier=go.AddComponent<MeshSculptModifier>();modifier.SetTarget(terrain.MeshFilter);
            var stroke=modifier.CreateStroke(MeshSculptModifier.SculptMode.Displace,MeshSculptModifier.StrokeSpace.World,new Vector3(128,0,128),Vector3.up,20,.05f,1);
            typeof(MeshSculptModifier).GetField("m_TerrainStroke",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(modifier,stroke);
            var ray=new Ray(new Vector3(128,100,128),Vector3.down);
            var method=typeof(MGTerrain).GetMethod("RaycastSculptSurface");
            var args=new object[]{ray,default(RaycastHit),200f};
            void Pick(){if(method!=null)method.Invoke(terrain,args);else terrain.RaycastEditingSurface(ray,out _,200);}
            try
            {
                terrain.RefreshSurfaceTiles();
                Measure("regular 257x257 + render chunks + pick",()=>{modifier.ApplyLatestStrokePreview();terrain.RefreshSurfaceTiles();Pick();});
                var notify = typeof(MGTerrain).GetMethod("NotifySurfaceMeshChanged",new[]{typeof(bool),typeof(bool)})
                    ?? typeof(MGTerrain).GetMethod("NotifySurfaceMeshChanged",new[]{typeof(bool)});
                var notifyArgs = notify.GetParameters().Length == 2 ? new object[]{false,true} : new object[]{false};
                Measure("hole picking after geometry edit",()=>{modifier.ApplyLatestStrokePreview();notify.Invoke(terrain,notifyArgs);terrain.RaycastSurfaceIncludingHoles(ray,out _,out _);});
                using(var session=new MGTerrainMergeSession(new List<MGTerrain>{terrain},new[]{new MGTerrainMergeSurface(source,Matrix4x4.identity)}))
                {
                    session.BeginStroke();
                    Measure("merge 257x257 + render chunks + pick",()=>{session.Paint(new Vector3(128,0,128),20,.5f,.03f);terrain.RefreshSurfaceTiles();Pick();});
                    var line=typeof(MGTerrainMergeSession).GetMethod("PaintLine",BindingFlags.Instance|BindingFlags.NonPublic);
                    Measure("merge eight-dab drag",()=>{
                        var from=new Vector3(96,0,128);var to=new Vector3(128,0,128);
                        if(line!=null)line.Invoke(session,new object[]{from,to,8,20f,.5f,.03f});
                        else for(int i=1;i<=8;i++)session.Paint(Vector3.Lerp(from,to,i/8f),20,.5f,.03f);
                        terrain.RefreshSurfaceTiles();Pick();
                    });
                    session.EndStroke();
                }
                MeshSculptWindow.BenchmarkWorldStroke(terrain);
                MeshSculptWindow.ValidateBrushVertexBuffers();
                MeshSculptWindow.ValidateBufferedSeamJoining();
                Debug.Log("BRUSH PERFORMANCE VALIDATION PASSED");
            }
            finally {EditorSceneManager.CloseScene(scene,true);Object.DestroyImmediate(mesh);Object.DestroyImmediate(source);AssetDatabase.DeleteAsset("Assets/BrushPerformance.unity");}
        }
    }
    public sealed partial class MeshSculptWindow
    {
        internal static void BenchmarkWorldStroke(MGTerrain tile)
        {
            var root = new GameObject("Benchmark world"); SceneManager.MoveGameObjectToScene(root,tile.gameObject.scene);
            var world = root.AddComponent<MGTerrainWorld>(); tile.transform.SetParent(root.transform);
            var meshes = new List<Mesh>();
            for(int i=1;i<4;i++)
            {
                var child = new GameObject("Neighbour "+i);child.transform.SetParent(root.transform);
                child.transform.localPosition=new Vector3(i%2*256,0,i/2*256);
                var next=child.AddComponent<MGTerrain>(); var copy=Object.Instantiate(tile.MeshFilter.sharedMesh); meshes.Add(copy);
                next.MeshFilter.sharedMesh=copy; next.ConfigureSurfaceGrid(257,257);next.HeightOnlySculpt=true;
                using(var data=new SerializedObject(next)){data.FindProperty("m_SceneSculptCopy").boolValue=true;data.ApplyModifiedPropertiesWithoutUndo();}
            }
            world.RefreshChunks();
            var window=CreateInstance<MeshSculptWindow>();
            window.m_Modifier=MGTerrainTileAuthoring.Modifier(tile);window.m_Mode=MeshSculptModifier.SculptMode.Displace;
            window.m_Radius=20;window.m_Strength=.01f;window.m_IsSculpting=true;
            try
            {
                Undo.IncrementCurrentGroup();
                MGTerrainBrushPerformanceValidation.Measure("regular world interior dab",()=>window.RecordStroke(new RaycastHit{point=new Vector3(128,0,128),normal=Vector3.up},false,false));
                MGTerrainBrushPerformanceValidation.Measure("regular world border dab",()=>window.RecordStroke(new RaycastHit{point=new Vector3(256,0,128),normal=Vector3.up},false,false));
                window.StopStroke();
            }
            finally
            {
                DestroyImmediate(window); tile.transform.SetParent(null);DestroyImmediate(root);
                foreach(var mesh in meshes)DestroyImmediate(mesh);
            }
        }
    }
}
#endif
