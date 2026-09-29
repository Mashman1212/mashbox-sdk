#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MashBoxSDK.Maps.TerrainSystem;
namespace MashBoxSDK.MapTools
{
    [InitializeOnLoad]
    internal static class MGTerrainSurfaceLodProbe
    {
        static double next;
        static MGTerrainSurfaceLodProbe() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            next=EditorApplication.timeSinceStartup+.5;
            string root=Path.Combine(Path.GetTempPath(),"mg-surface-lod"), path=Path.Combine(root,"request.txt");
            if(Application.dataPath.Replace('\\','/')!="D:/MappyX/Assets" || !File.Exists(path)) return;
            string action=File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                if(action=="validate") { MGTerrainSurfaceLodValidation.Run(); MGTerrainStreamingCacheValidation.Run(); MGTerrainTileGeometryValidation.Run(); }
                var text=new StringBuilder();
                foreach(var tile in UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                {
                    var mesh=tile.MeshFilter.sharedMesh;
                    text.AppendLine(tile.name+" source="+mesh.vertexCount+" tris="+mesh.triangles.Length/3+" cached="+tile.CachedSurfaceLodCount+" active="+tile.ActiveSurfaceLod+" lodTris="+tile.ActiveSurfaceLodTriangleCount);
                    if(SceneView.lastActiveSceneView!=null)
                    {
                        string reason=tile.GetSurfaceLodPreview(SceneView.lastActiveSceneView.camera,out int chosen,out long original,out long selected);
                        text.AppendLine(" Scene camera: "+reason+" LOD="+chosen+" triangles="+selected+"/"+original);
                    }
                    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    foreach(var lod in (System.Collections.IEnumerable)typeof(MGTerrain).GetField("m_SurfaceLods",flags).GetValue(tile)) text.AppendLine("  level tris="+lod.GetType().GetField("triangles",flags).GetValue(lod)+" error="+lod.GetType().GetField("error",flags).GetValue(lod));
                    foreach(var mat in tile.MeshRenderer.sharedMaterials) if(mat!=null) text.AppendLine(" material="+mat.name+" morph="+(mat.HasProperty("_DistantSurfaceStrength")?mat.GetFloat("_DistantSurfaceStrength"):0));
                }
                File.WriteAllText(Path.Combine(root,"snapshot.txt"),text.ToString());
            } catch(Exception e) { File.WriteAllText(Path.Combine(root,"probe-error.txt"),e.ToString()); Debug.LogException(e); }
        }
    }
}
#endif
