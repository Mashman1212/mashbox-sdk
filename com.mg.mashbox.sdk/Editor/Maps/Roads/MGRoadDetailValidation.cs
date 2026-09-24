using System;
using System.Collections.Generic;
using System.Reflection;
using MashBoxSDK.Maps.TerrainSystem;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace MashBoxSDK.Maps.Roads.Editor
{
    public static class MGRoadDetailValidation
    {
        [MenuItem("MashBox/Validation/Validate Road Detail Clearing")]
        public static void Run()
        {
            int checks = 0;
            void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
            var scene = EditorSceneManager.NewPreviewScene();
            Mesh mesh = null; Texture2D paint = null, second = null;
            try
            {
                var root = new GameObject("Road detail tests"); SceneManager.MoveGameObjectToScene(root, scene);
                var network = root.AddComponent<MGRoadNetwork>();
                var worldGo = new GameObject("World"); worldGo.transform.SetParent(root.transform, false);
                var world = worldGo.AddComponent<MGTerrainWorld>(); network.terrainWorld = world;
                var tileGo = new GameObject("Tile"); tileGo.transform.SetParent(worldGo.transform, false);
                var filter = tileGo.AddComponent<MeshFilter>(); var renderer = tileGo.AddComponent<MeshRenderer>(); var collider = tileGo.AddComponent<MeshCollider>();
                mesh = new Mesh { vertices = new[] { new Vector3(-16,0,-16), new Vector3(-16,0,16), new Vector3(16,0,-16), new Vector3(16,0,16) }, triangles = new[] {0,1,2,2,1,3} }; mesh.RecalculateBounds(); mesh.RecalculateNormals();
                filter.sharedMesh = mesh; collider.sharedMesh = mesh;
                var tile = tileGo.AddComponent<MGTerrain>(); tile.Configure(filter, renderer, collider); world.RefreshChunks();
                var prototype = new GameObject("Detail prototype"); prototype.transform.SetParent(root.transform, false);
                int proto = tile.FindOrAddPrototype(prototype, MGTerrain.InstanceKind.Detail);
                paint = new Texture2D(32,32,TextureFormat.R16,false,true);
                var pixels = paint.GetPixelData<ushort>(0); for(int i=0;i<pixels.Length;i++) pixels[i]=23; paint.Apply(false,false);
                tile.AddDensityDetailLayer(proto, paint,1,1,1,1,17,32*32*23);
                MGRoad Road(string name)
                {
                    var go = new GameObject(name); go.transform.SetParent(root.transform,false);
                    var road = go.AddComponent<MGRoad>(); road.width=2; road.shoulderWidth=0; road.crown=0; road.generateCollider=false;
                    road.Container.Spline.Add(new BezierKnot(new float3(0,2,-10)), TangentMode.Linear);
                    road.Container.Spline.Add(new BezierKnot(new float3(0,2,10)), TangentMode.Linear);
                    return road;
                }
                bool Hidden(int x) => tile.IsRoadDetailCellCleared(32,32,x,16);
                var density = typeof(MGTerrain).GetMethod("ReadDensity", BindingFlags.Instance|BindingFlags.NonPublic);
                int Count(int x) => (int)density.Invoke(tile,new object[]{paint,x,16});
                var a = Road("A"); var b = Road("B");
                Check(!a.DetailSettings.clearDetails, "Opt-in default");
                MGRoadDetailService.Apply(new[]{a});
                Check(a.DetailSettings.clearDetails && a.detailMaskEnabled, "Explicit Apply enables selected road");
                Check(Hidden(16) && !Hidden(24), "Footprint clears while outside remains visible");
                Check(Count(16)==0 && Count(24)==23, "Shared CPU/GPU generation density path respects mask");
                Check(paint.GetPixelData<ushort>(0)[16*32+16]==23, "Original paint preserved");
                Check(!AssetDatabase.Contains(paint), "Live apply creates no texture asset");
                MGRoadDetailService.Apply(new[]{a}); Check(Count(16)==0, "Repeated Apply does not accumulate");
                MGRoadDetailService.Apply(new[]{b}); MGRoadDetailService.Remove(new[]{a});
                Check(Hidden(16), "Removing overlapping road preserves other mask");
                MGRoadDetailService.Remove(new[]{b}); Check(!Hidden(16) && Count(16)==23, "Removing last mask restores paint");
                Undo.PerformUndo(); Check(Hidden(16), "Undo restore recovers clearing");
                Undo.PerformRedo(); Check(!Hidden(16), "Redo restore reveals paint");
                MGRoadDetailService.Apply(new[]{a});
                pixels = paint.GetPixelData<ushort>(0); pixels[16*32+16]=37; paint.Apply(false,false);
                Check(Count(16)==0, "New painting stays hidden beneath active road");
                a.transform.position = Vector3.right*8; MGRoadDetailService.UpdateLive(scene);
                Check(!Hidden(16) && Hidden(24), "Live movement restores old footprint and clears new");
                Check(Count(16)==37, "Independent paint survives road movement");
                a.details.autoUpdate=false; a.transform.position=Vector3.left*8; MGRoadDetailService.UpdateLive(scene);
                Check(Hidden(24) && !Hidden(8), "Manual mode retains mask until Apply");
                MGRoadDetailService.Apply(new[]{a}); Check(!Hidden(24) && Hidden(8), "Manual Apply replaces footprint");
                a.details.clearDetails=false; MGRoadDetailService.UpdateLive(scene); Check(!Hidden(8), "Disabling clear details restores immediately");
                a.details.clearDetails=true; a.details.autoUpdate=true; MGRoadDetailService.UpdateLive(scene); Check(Hidden(8), "Re-enable resumes automatic mask");
                MGRoadDetailService.Remove(new[]{a}); a.transform.position=Vector3.zero; MGRoadDetailService.UpdateLive(scene);
                Check(!Hidden(8) && !Hidden(16), "Restore pauses automatic clearing");
                MGRoadDetailService.Apply(new[]{a});
                second=new Texture2D(64,16,TextureFormat.R16,false,true);
                tile.AddDensityDetailLayer(proto, second,1,1,1,1,18,0); MGRoadDetailService.UpdateLive(scene);
                Check(tile.IsRoadDetailCellCleared(64,16,32,8), "New detail-map resolution gets its own mask");
                Check(tile.DensityDetailLayers[0].DensityMap==paint && tile.DensityDetailLayers[1].DensityMap==second, "Original map references retained");
                var saved=EditorJsonUtility.ToJson(tile); tile.SetRoadDetailMasks(new List<MGTerrain.DetailExclusionMask>());
                Check(!Hidden(16), "Mask clears independently from source paint");
                EditorJsonUtility.FromJsonOverwrite(saved,tile); tile.RefreshRoadDetailMasks(); Check(Hidden(16), "Combined mask survives serialization");
                // Preview-scene references have no saved scene file IDs in this JSON round-trip.
                tile.Configure(filter,renderer,collider);
                tile.DensityDetailLayers[0].AssignPaintMapCopy(paint,MGTerrain.DetailPaintMap.Density);
                tile.DensityDetailLayers[1].AssignPaintMapCopy(second,MGTerrain.DetailPaintMap.Density);
                root.transform.rotation=Quaternion.Euler(0,30,0); root.transform.localScale=Vector3.one*1.5f;
                MGRoadDetailService.UpdateLive(scene);
                Check(Hidden(16) && !Hidden(24), "Rotated/scaled world footprint stays aligned");
                a.enabled=false; MGRoadDetailService.UpdateLive(scene); Check(!Hidden(16), "Disabled road restores details");
                a.enabled=true; MGRoadDetailService.UpdateLive(scene); Check(Hidden(16), "Enabled road restores mask");
                var otherWorld=new GameObject("Other world"); otherWorld.transform.SetParent(root.transform,false);
                network.terrainWorld=otherWorld.AddComponent<MGTerrainWorld>(); MGRoadDetailService.UpdateLive(scene);
                Check(!Hidden(16), "Changing worlds removes old contribution");
                network.terrainWorld=world; MGRoadDetailService.UpdateLive(scene); Check(Hidden(16), "Returning to world restores mask");
                Object.DestroyImmediate(a.gameObject); MGRoadDetailService.UpdateLive(scene); Check(!Hidden(16), "Deleted road restores details");
                MGRoadDetailService.Apply(new[]{b}); new MGRoadDetailBuildProcessor().OnProcessScene(scene,null);
                Check(tile.GetComponent<MGRoadDetailLayers>()==null && Hidden(16), "Build strips authoring history and retains playable mask");
                Check(paint.GetPixelData<ushort>(0)[16*32+16]==37, "All operations preserved independent paint");
                Debug.Log($"ROAD_DETAIL_VALIDATION_PASS: {checks} checks");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if(mesh!=null) Object.DestroyImmediate(mesh);
                if(paint!=null) Object.DestroyImmediate(paint);
                if(second!=null) Object.DestroyImmediate(second);
            }
        }
    }
}