#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    // Explicit, bounded inspection. Never runs on import, reload or editor update.
    public static class MGTerrainBakedHlodValidation
    {
        static double observationEnd, previousUpdate, maximumGap;
        static int observationTicks;
        static void ObserveStability()
        {
            double now=EditorApplication.timeSinceStartup;
            maximumGap=Math.Max(maximumGap,now-previousUpdate); previousUpdate=now; observationTicks++;
            if(now<observationEnd) return;
            EditorApplication.update-=ObserveStability;
            string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalDiagnostics","SurfaceHLOD");
            File.WriteAllText(Path.Combine(folder,"stability.txt"),$"60-second passive observation: {observationTicks} editor updates; largest gap {maximumGap:F3}s. No forced repaints or mesh generation requested.");
        }
        [MenuItem("Tools/MashBox/MG Terrain/Validation/Inspect Baked HLOD in Open Map %#F9")]
        public static void Inspect()
        {
            var tiles = UnityEngine.Object.FindObjectsByType<MGTerrain>(FindObjectsSortMode.None)
                .Where(t=>t.gameObject.scene.IsValid() && t.isActiveAndEnabled).ToArray();
            var view = SceneView.lastActiveSceneView;
            if (view == null || tiles.Length == 0) return;
            var notes = new List<string>();
            var renderers = new List<MeshRenderer>();
            GameObject go = null;
            var prepare = typeof(MGTerrain).GetMethod("PrepareSurfaceForCamera",BindingFlags.Instance|BindingFlags.NonPublic);
            void Sample(Camera camera, string label)
            {
                long original = 0, selected = 0; int active = 0, reduced = 0;
                foreach (var tile in tiles)
                {
                    prepare.Invoke(tile,new object[]{camera});
                    string reason = tile.GetSurfaceLodPreview(camera,out int lod,out long before,out long after);
                    original += before; selected += after;
                    tile.GetSurfacePerformanceRenderers(renderers);
                    var visible = renderers.Where(r=>r!=null && r.enabled && !r.forceRenderingOff).ToArray();
                    active += visible.Length; if (lod > 0) reduced++;
                    if (lod > 0 && (visible.Length != 1 || visible[0].GetComponent<MeshFilter>().sharedMesh != tile.BakedSurfaceLod.Levels[lod-1].Mesh))
                        throw new Exception(tile.name + ": actual renderer does not match baked selection.");
                    notes.Add($"{label}: {tile.name}: LOD {lod}, {before} -> {after} triangles, {visible.Length} renderers, near/far shader {tile.NearSurfaceShaderChunks}/{tile.FarSurfaceShaderChunks}; {reason}");
                }
                notes.Add($"{label} TOTAL: {original} -> {selected} triangles, {active} renderers, {reduced}/{tiles.Length} reduced tiles.");
                if(label=="Far Game" && reduced==0) throw new Exception("No distant tile selected a baked LOD.");
            }
            try
            {
                Sample(view.camera,"Current Scene");
                go = new GameObject("HLOD validation camera") { hideFlags=HideFlags.HideAndDontSave };
                var camera = go.AddComponent<Camera>(); camera.enabled=false;
                camera.CopyFrom(view.camera); camera.cameraType=CameraType.Game;
                var bounds=tiles[0].MeshRenderer.bounds;
                foreach(var tile in tiles.Skip(1)) bounds.Encapsulate(tile.MeshRenderer.bounds);
                camera.transform.position=bounds.center+new Vector3(0,bounds.size.magnitude, -bounds.size.magnitude);
                camera.transform.LookAt(bounds.center);
                Sample(camera,"Far Game");
                camera.cameraType=CameraType.SceneView;
                Sample(camera,"Far Scene");
                var first=tiles[0];
                camera.transform.position=first.MeshRenderer.bounds.center;
                prepare.Invoke(first,new object[]{camera});
                if(first.ActiveSurfaceLod!=0) throw new Exception("Close camera did not restore original geometry.");
                notes.Add($"Close Scene: {first.name}, original geometry, near/far shader {first.NearSurfaceShaderChunks}/{first.FarSurfaceShaderChunks}.");
                camera.cameraType=CameraType.Game;
                prepare.Invoke(first,new object[]{camera});
                if(first.ActiveSurfaceLod!=0) throw new Exception("Close Game camera did not restore original geometry.");
                notes.Add($"Close Game: {first.name}, original geometry, near/far shader {first.NearSurfaceShaderChunks}/{first.FarSurfaceShaderChunks}.");
                notes.Add("PASS: close cameras restore original geometry; far Scene/Game renderers reference baked assets.");
            }
            catch(Exception e) { notes.Add("FAIL: "+e); Debug.LogException(e); }
            finally
            {
                foreach(var tile in tiles) prepare.Invoke(tile,new object[]{view.camera});
                if(go!=null) UnityEngine.Object.DestroyImmediate(go);
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"LocalDiagnostics","SurfaceHLOD");
                Directory.CreateDirectory(folder);
                File.WriteAllLines(Path.Combine(folder,"open-map-validation.txt"),notes);
                Debug.Log("Baked HLOD inspection: "+notes.LastOrDefault());
                previousUpdate=EditorApplication.timeSinceStartup; observationEnd=previousUpdate+60;
                maximumGap=0; observationTicks=0;
                EditorApplication.update-=ObserveStability;
                EditorApplication.update+=ObserveStability;
            }
        }
    }
}
#endif