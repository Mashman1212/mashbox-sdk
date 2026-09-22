using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    internal static class MGStampPreviewValidation
    {
        [MenuItem("Tools/MashBox/MG Terrain/Validate Stamp Preview")]
        internal static void Run()
        {
            var log = new StringBuilder();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Stamp preview validation") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var mesh = new Mesh {
                vertices = new[] {new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(0,1,1), new Vector3(-1,2,-1), new Vector3(1,2,-1), new Vector3(0,3,1)},
                triangles = new[] {0,2,1,3,5,4}, uv = new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.zero,Vector2.right,Vector2.up}
            };
            var material = new Material(Shader.Find("HDRP/Lit"));
            PrefabStampSource source = null;
            Mesh preview = null;
            try
            {
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", Color.red);
                renderer.SetPropertyBlock(block);
                source = new PrefabStampSource(root);
                Check(source.PropertyBlocks[0].GetColor("_BaseColor") == Color.red, "Renderer colour override preserved");
                int samples = 0;
                Func<Vector3,float?> surface = point => { samples++; return point.x * .1f; };
                preview = source.PreparePreview(Vector3.zero, 10, 4, 0, 1, false, surface);
                int initialSamples = samples;
                Check(initialSamples == 3, "Shared XZ positions sampled only once");
                for (int i = 0; i < 100; i++)
                    Check(source.PreparePreview(Vector3.zero,10,4,0,1,false,surface) == preview, "Idle preview mesh retained");
                Check(samples == initialSamples, "Idle preview does not resample terrain");
                var center = new Vector3(2,3,4);
                Check(source.PreparePreview(center,7,5,35,2,true,surface) == preview, "Moved preview mesh retained");
                var expected = source.Shape(center,7,5,35,2,true,surface);
                try
                {
                    var a=preview.vertices; var b=expected.vertices;
                    for(int i=0;i<a.Length;i++) Check((a[i]-b[i]).sqrMagnitude < 1e-10f,"Preview agrees with stamp shape");
                }
                finally { Object.DestroyImmediate(expected); }
                int previousSamples=samples;
                source.InvalidatePreview();
                Check(source.PreparePreview(center,7,5,35,2,true,surface)==preview && samples>previousSamples,
                    "Sculpt/Undo invalidation resamples without cloning");
                for (int step = 0; step < 12; step++)
                {
                    var c = new Vector3(step * .73f, -step, step * -.37f);
                    float falloff = step < 6 ? 2f : .5f;
                    Func<Vector3,float?> checkSurface = step < 6 ? (point => 0f) : surface;
                    preview = source.PreparePreview(c, 7, 5, 35, falloff, true, checkSurface);
                    var reference = source.Shape(c, 7, 5, 35, falloff, true, checkSurface);
                    try {
                        var actual = preview.vertices; var correct = reference.vertices;
                        for(int i=0;i<actual.Length;i++) Check((actual[i]-correct[i]).sqrMagnitude < 1e-10f, "Cached moving shape parity");
                        var normals=preview.normals;var expectedNormals=reference.normals;
                        var tangents=preview.tangents;var expectedTangents=reference.tangents;
                        for(int i=0;i<actual.Length;i++) {
                            Check((normals[i]-expectedNormals[i]).sqrMagnitude<1e-6f,"Translation/deformation normal parity");
                            Check((tangents[i]-expectedTangents[i]).sqrMagnitude<1e-6f,"Translation/deformation tangent parity");
                        }
                    } finally { Object.DestroyImmediate(reference); }
                }
                source.Dispose(); source=null;
                Check(preview == null,"Disposed preview mesh released");
                log.AppendLine("PASS: idle 100 updates reuse mesh with no surface resampling; moving mesh reused; shape, normals and tangents parity; unique footprint sampling; collider height parity; Undo invalidation; material override; cleanup.");
                Benchmark(log);
                UnityEngine.Debug.Log(log.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mg-stamp-preview-result.txt"),log.ToString());
            }
            catch(Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mg-stamp-preview-result.txt"),ex.ToString());
                throw;
            }
            finally
            {
                source?.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static void Benchmark(StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maps/Cotswold Ridge/[01] MODELS/SJ_bg_hills/SJ_bg_hills_rockyHills/7_B_asset/SJ_BG_hills_7_B_PR.prefab");
            if(prefab == null) return;
            using var source = new PrefabStampSource(prefab);
            source.PreparePreview(Vector3.zero,128,30,0,1,false,null);
            var timer = new Stopwatch();
            var diagnostic=source.PreparePreview(Vector3.zero,128,30,0,1,false,null);
            timer.Restart();for(int i=0;i<20;i++)diagnostic.RecalculateNormals();timer.Stop();
            log.AppendLine("Normal recalculation: "+timer.Elapsed.TotalMilliseconds/20+" ms.");
            timer.Restart();for(int i=0;i<20;i++)diagnostic.RecalculateTangents();timer.Stop();
            log.AppendLine("Tangent recalculation: "+timer.Elapsed.TotalMilliseconds/20+" ms.");
            timer.Reset();

            timer.Start();
            for(int i=0;i<20;i++) source.PreparePreview(new Vector3(i+1,0,0),128,30,0,1,false,null);
            timer.Stop();
            log.AppendLine("Project prefab " + source.Mesh.vertexCount + " vertices: moving preview " + timer.Elapsed.TotalMilliseconds/20 + " ms/update, " + "geometry only.");
            MashBoxSDK.Maps.TerrainSystem.MGTerrain terrain=null;
            foreach(var candidate in Object.FindObjectsByType<MashBoxSDK.Maps.TerrainSystem.MGTerrain>(FindObjectsSortMode.None))
                if(candidate.MeshFilter!=null && candidate.MeshFilter.sharedMesh!=null &&
                    (terrain==null || candidate.MeshFilter.sharedMesh.vertexCount > terrain.MeshFilter.sharedMesh.vertexCount)) terrain=candidate;
            if(terrain!=null && terrain.MeshFilter!=null && terrain.MeshFilter.sharedMesh!=null)
            {
                var bounds=MGTerrainTileAuthoring.BoundsOf(terrain);
                var center=bounds.center;
                float? CachedSurface(Vector3 point)
                {
                    if(point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) return null;
                    return terrain.RaycastSurface(new Ray(new Vector3(point.x,bounds.max.y+1,point.z),Vector3.down),out var hit,bounds.size.y+2) ? hit.point.y+.02f : (float?)null;
                }
                var fastSurface = new PrefabStampSurfaceSampler();
                fastSurface.Prepare(new[] { terrain });
                for (int i=0;i<100;i++)
                {
                    var point = new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,i/99f),0,Mathf.Lerp(bounds.min.z,bounds.max.z,(i*37%100)/99f));
                    var reference = CachedSurface(point); var fast = fastSurface.Sample(point);
                    Check(reference.HasValue == fast.HasValue && (!reference.HasValue || Mathf.Abs(reference.Value-fast.Value)<.001f), "Cached collider height parity");
                }
                source.InvalidatePreview();
                timer.Restart();
                for(int i=0;i<20;i++) source.PreparePreview(center+Vector3.right*i*.1f,128,30,0,1,false,fastSurface.Sample);
                timer.Stop();
                log.AppendLine("Optimized exact material preview: " + timer.Elapsed.TotalMilliseconds/20 + " ms/moving update.");
                float? OldSurface(Vector3 point)
                {
                    var b=MGTerrainTileAuthoring.BoundsOf(terrain);
                    if(point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) return null;
                    return terrain.RaycastSurface(new Ray(new Vector3(point.x,b.max.y+1,point.z),Vector3.down),out var hit,b.size.y+2) ? hit.point.y+.02f : (float?)null;
                }
                timer.Restart();
                source.PreparePreview(center,128,30,0,1,false,CachedSurface);
                timer.Stop();
                log.AppendLine("Material preview with terrain sampling: " + timer.Elapsed.TotalMilliseconds + " ms.");
                timer.Restart();
                Object.DestroyImmediate(source.Shape(center,128,30,0,1,false,OldSurface));
                timer.Stop();
                log.AppendLine("Previous material preview with per-vertex bounds lookup: " + timer.Elapsed.TotalMilliseconds + " ms.");
                var brush=new MeshStampBrush(source.Mesh);
                timer.Restart();
                var lines=brush.BuildPreviewLines(terrain.MeshFilter,terrain.MeshFilter.transform.TransformPoint(terrain.MeshFilter.sharedMesh.bounds.center),128,30,0,1,1);
                timer.Stop();
                log.AppendLine("Optional terrain wireframe: " + timer.Elapsed.TotalMilliseconds + " ms/build, " + lines.Length + " line vertices on " + terrain.MeshFilter.sharedMesh.vertexCount + " terrain vertices. Skipped with Materials on and Terrain Wireframe off.");
            }
            timer.Restart();
            for(int i=0;i<20;i++) Object.DestroyImmediate(source.Shape(new Vector3(i+1,0,0),128,30,0,1,false));
            timer.Stop();
            log.AppendLine("Clone/shape/destroy comparison: " + timer.Elapsed.TotalMilliseconds/20 + " ms/update, " + "geometry only.");
        }
        static void Check(bool condition,string label) { if(!condition) throw new InvalidOperationException(label); }
    }
}
