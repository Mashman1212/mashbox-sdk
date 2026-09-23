#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MashBoxSDK.MapTools
{
    internal static class MGTerrainScaledTileValidation
    {
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Scaled Background Tiles")]
        internal static void Run()
        {
            var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Scaled Test");root.SetActive(true);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var world=root.AddComponent<MGTerrainWorld>();world.TileSize=512;world.TileResolution=17;
            root.transform.position=new Vector3(-115,235,632);
            try
            {
                var a=MGTerrainWorldEditor.GenerateTile(world);
                var b=MGTerrainTileAuthoring.AddTile(a,Vector2Int.right);
                var c=MGTerrainTileAuthoring.AddTile(a,Vector2Int.up);
                var d=MGTerrainTileAuthoring.AddTile(b,Vector2Int.up);
                var central=new[]{a,b,c,d};
                foreach(var tile in central)
                {
                    var mesh=tile.MeshFilter.sharedMesh;var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++) {var p=tile.transform.TransformPoint(vertices[i])-root.transform.position;vertices[i].y=20*Mathf.Sin(p.x*.021f)+13*Mathf.Cos(p.z*.017f);}
                    mesh.vertices=vertices;mesh.RecalculateBounds();
                }
                var original=central.Select(t=>t.MeshFilter.sharedMesh.vertices).ToArray();
                var ring=new List<MGTerrain>();
                bool Overlap(Bounds x,Bounds y)=>Mathf.Min(x.max.x,y.max.x)-Mathf.Max(x.min.x,y.min.x)>.001f && Mathf.Min(x.max.z,y.max.z)-Mathf.Max(x.min.z,y.min.z)>.001f;
                for(int added=0;added<8;added++)
                {
                    var tiles=world.Chunks.ToArray();bool found=false;
                    foreach(var source in tiles)
                    {
                        foreach(var candidate in MGTerrainTileAuthoring.PlacementBounds(source,1024))
                        {
                            var min=candidate.bounds.min-root.transform.position;
                            if(min.x < -1024.01f || min.z < -1024.01f || min.x >1024.01f ||min.z>1024.01f)continue;
                            if(tiles.Any(t=>Overlap(candidate.bounds,MGTerrainTileAuthoring.BoundsOf(t))))continue;
                            using var preview=new MGTerrainPlacementPreview(source,candidate.direction,candidate.bounds,tiles);
                            Check(preview.Hit(new Ray(preview.LabelPosition+Vector3.up*10000,Vector3.down),out var picked),"Preview mesh picking failed");
                            Check(Mathf.Abs(picked.point.y-preview.LabelPosition.y)<.01f,"Picking does not follow preview surface");
                            var tile=MGTerrainTileAuthoring.AddTile(source,candidate.direction,candidate.bounds);
                            Check(preview.Mesh.vertices.SequenceEqual(tile.MeshFilter.sharedMesh.vertices),"Preview differs from created terrain vertices");
                            Check(preview.Mesh.triangles.SequenceEqual(tile.MeshFilter.sharedMesh.triangles),"Preview differs from created terrain stitching");
                            Check(preview.Matrix==tile.MeshFilter.transform.localToWorldMatrix,"Preview placement differs from created terrain");
                            Check(tile.SurfaceGridWidth==17 && tile.SurfaceGridHeight==17,"Core resolution changed while extending ring");
                            Check(tile.MeshFilter.sharedMesh.vertexCount>=289 && tile.MeshFilter.sharedMesh.vertexCount<600,"Stitching exceeded small rim budget");
                            Check(Mathf.Abs(MGTerrainTileAuthoring.BoundsOf(tile).size.x-1024)<.01f,"Wrong large tile footprint");
                            ring.Add(tile);found=true;break;
                        }
                        if(found)break;
                    }
                    Check(found,"Could not place all eight tiles in surrounding ring");
                }
                for(int i=0;i<4;i++)Check(original[i].SequenceEqual(central[i].MeshFilter.sharedMesh.vertices),"Existing fine terrain was changed");
                Check(world.Chunks.Select(MGTerrainAssetStore.Prefix).Distinct().Count()==12,"Mixed-size asset names collided");
                foreach(var tile in ring)
                {
                    var mesh=tile.MeshFilter.sharedMesh;var vertices=mesh.vertices;var tris=mesh.triangles;
                    for(int i=0;i<tris.Length;i+=3)
                    {var n=Vector3.Cross(vertices[tris[i+1]]-vertices[tris[i]],vertices[tris[i+2]]-vertices[tris[i]]);Check(n.y>0,"Degenerate or inverted stitch triangle");}
                    var bounds=MGTerrainTileAuthoring.BoundsOf(tile);
                    var positions=vertices.Select(tile.transform.TransformPoint).ToArray();
                    foreach(var fine in central)foreach(var local in fine.MeshFilter.sharedMesh.vertices)
                    {
                        var p=fine.transform.TransformPoint(local);
                        bool on=(Mathf.Abs(p.x-bounds.min.x)<.001f||Mathf.Abs(p.x-bounds.max.x)<.001f||Mathf.Abs(p.z-bounds.min.z)<.001f||Mathf.Abs(p.z-bounds.max.z)<.001f)
                            &&p.x>=bounds.min.x-.001f&&p.x<=bounds.max.x+.001f&&p.z>=bounds.min.z-.001f&&p.z<=bounds.max.z+.001f;
                        if(on)Check(positions.Any(v=>(v-p).sqrMagnitude<.00001f),"Fine border sample is missing from large tile");
                    }
                }
                var large=MGTerrainTileAuthoring.PlacementBounds(ring[0],2048).FirstOrDefault(p=>!world.Chunks.Any(t=>Overlap(p.bounds,MGTerrainTileAuthoring.BoundsOf(t))));
                Check(large.bounds.size.x==2048,"No 4x expansion available");
                var far=MGTerrainTileAuthoring.AddTile(ring[0],large.direction,large.bounds);
                Check(far.SurfaceGridWidth==17 && far.MeshFilter.sharedMesh.vertexCount<900,"4x core budget changed");
                var gpu=typeof(MGTerrain).GetMethod("EnsureGpuSurfaceHeightBuffer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Check(gpu!=null && (bool)gpu.Invoke(far,null),"GPU surface grid rejected stitched vertices");
                int countBeforeUndo=world.Chunks.Count;
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();world.RefreshChunks();
                Check(world.Chunks.Count==countBeforeUndo-1,"Undo did not remove new background tile");
                Undo.PerformRedo();world.RefreshChunks();
                Check(world.Chunks.Count==countBeforeUndo,"Redo did not restore new background tile");
                File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-scaled-tile-result.txt"),"PASS: 4 central tiles + 8 surrounding 2x tiles + 4x extension; core grid stays 17x17; exact wavy seam samples; positive triangle winding; original terrain unchanged; distinct base-grid asset names; GPU surface buffer accepts stitched mesh; Undo/Redo; previews match created geometry and native surface picking.");
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Path.GetTempPath(),"mg-scaled-tile-result.txt"),e.ToString());throw;}
            finally {UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
#endif
