#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainSculptPreviewValidation
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Mesh Grid()
        {
            const int w = 17;
            var v = new Vector3[w * w]; var uv = new Vector2[v.Length]; var c = new Color[v.Length];
            for (int z = 0; z < w; z++) for (int x = 0; x < w; x++)
            { int i = z * w + x; v[i] = new Vector3(x, 0, z); uv[i] = new Vector2(x, z) / 16; c[i] = Color.red; }
            var t = new int[16 * 16 * 6];
            for (int z = 0, n = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            { int i = z * w + x; t[n++] = i; t[n++] = i+w; t[n++] = i+1; t[n++] = i+1; t[n++] = i+w; t[n++] = i+w+1; }
            var mesh = new Mesh { vertices = v, uv = uv, uv4 = uv, colors = c, triangles = t };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); return mesh;
        }
        static MGTerrain Tile(Scene scene, string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
            var tile = go.AddComponent<MGTerrain>(); tile.MeshFilter.sharedMesh = Grid(); tile.ConfigureSurfaceGrid(17,17);
            using var data = new SerializedObject(tile); data.FindProperty("m_SceneSculptCopy").boolValue = true; data.ApplyModifiedPropertiesWithoutUndo();
            return tile;
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Sculpt Preview Performance Safety")]
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this validation in an isolated batch project.");
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path)) EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/MergeValidationBootstrap.unity");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene,"Assets/SculptPreviewValidation.unity");
            var tile = Tile(scene,"Query/render validation"); var mesh = tile.MeshFilter.sharedMesh;
            var other = Tile(scene,"Sequential merge"); var batch = Tile(scene,"Batched merge");
            var originals = new[]{mesh,other.MeshFilter.sharedMesh,batch.MeshFilter.sharedMesh};
            var source = Grid(); var v = source.vertices; for(int i=0;i<v.Length;i++)v[i].y=8;source.vertices=v;
            try
            {
                var ray = new Ray(new Vector3(8,100,8),Vector3.down);
                Check(tile.RaycastSculptSurface(ray,out var hit,200) && Mathf.Abs(hit.point.y)<.001f,"Initial brush query failed.");
                tile.RefreshSurfaceTiles();
                var normals = mesh.normals; normals[0] = new Vector3(1,1,0).normalized; mesh.normals = normals;
                var before = mesh.vertices; v = mesh.vertices; v[8*17+8].y = 3; mesh.vertices = v; mesh.RecalculateBounds();
                Check(tile.SculptBordersUnchanged(before,v),"Interior edit classified as a boundary edit.");
                tile.RecalculateSculptNormals(true);
                Check(mesh.normals[0] == normals[0],"Interior dab erased welded border shading.");
                tile.NotifySurfaceMeshChanged(topologyChanged: false, geometryOnly:true); EditorUtility.SetDirty(mesh);
                Check(tile.RaycastSculptSurface(ray,out hit,200) && Mathf.Abs(hit.point.y-3)<.001f,"Brush query used stale height.");
                Check(!tile.RaycastSculptSurface(ray,out _,50),"Brush query ignored maximum distance.");
                tile.RefreshSurfaceTiles();
                foreach(var filter in tile.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter==tile.MeshFilter)continue;
                    Check(filter.sharedMesh.colors.Length==filter.sharedMesh.vertexCount && filter.sharedMesh.colors[0]==Color.red,"Geometry update lost render colours.");
                    Check(filter.sharedMesh.uv4.Length==filter.sharedMesh.vertexCount,"Geometry update lost UV channels.");
                }
                tile.transform.localScale=new Vector3(2,3,4);
                Check(tile.RaycastSculptSurface(new Ray(new Vector3(16,100,32),Vector3.down),out hit,200) && Mathf.Abs(hit.point.y-9)<.001f,"Brush query ignored nonuniform scale.");
                Check(tile.RaycastSurfaceIncludingHoles(new Ray(new Vector3(16,100,32),Vector3.down),out var holePoint,out _) && Mathf.Abs(holePoint.y-9)<.001f,"Retained-face query ignored nonuniform scale.");
                mesh.triangles=Array.Empty<int>(); tile.NotifySurfaceMeshChanged(true);
                Check(!tile.RaycastSculptSurface(ray,out _,200),"Query retained removed faces after a topology change.");
                var snapshot = new MGTerrainMergeSurface(source,Matrix4x4.identity);
                using(var sequential = new MGTerrainMergeSession(new List<MGTerrain>{other},new[]{snapshot}))
                using(var batched = new MGTerrainMergeSession(new List<MGTerrain>{batch},new[]{snapshot}))
                {
                    sequential.BeginStroke();batched.BeginStroke();
                    var a = new Vector3(6,0,8); var b = new Vector3(10,0,8);
                    for(int i=1;i<=8;i++) sequential.Paint(Vector3.Lerp(a,b,i/8f),2,.4f,.2f);
                    batched.PaintLine(a,b,8,2,.4f,.2f);
                    var expected=other.MeshFilter.sharedMesh.vertices;var actual=batch.MeshFilter.sharedMesh.vertices;
                    for(int i=0;i<actual.Length;i++)Check((actual[i]-expected[i]).sqrMagnitude<1e-10f,"Batched merge changed brush strength/falloff.");
                    sequential.EndStroke();batched.EndStroke();
                    var finished = batch.MeshFilter.sharedMesh;
                    var expectedMesh = Object.Instantiate(finished);
                    try
                    {
                        expectedMesh.RecalculateTangents(); var expectedTangents = expectedMesh.tangents; var actualTangents = finished.tangents;
                        for (int i=0;i<actualTangents.Length;i++) Check((expectedTangents[i]-actualTangents[i]).sqrMagnitude<1e-10f,"Mouse-up did not finalize deferred tangents.");
                    }
                    finally { Object.DestroyImmediate(expectedMesh); }
                }
                Debug.Log("SCULPT PREVIEW VALIDATION PASSED: deformed picking, max distance, nonuniform scale, topology invalidation, render colours/UVs, welded normals and batched/sequential equivalence.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true); foreach(var original in originals)Object.DestroyImmediate(original); Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset("Assets/SculptPreviewValidation.unity");
            }
            MGTerrainMergeValidation.Run();
            MGTerrainSeamValidation.Run();
            MeshSculptWindow.ValidateSeamStrokePerformance();
        }
    }
}
#endif
