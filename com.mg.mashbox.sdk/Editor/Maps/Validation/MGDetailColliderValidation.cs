using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace MashBoxSDK.MapTools
{
    public static class MGDetailColliderValidation
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static object Field(object item, string name) => item.GetType().GetField(name,Flags).GetValue(item);
        static void Set(object item, string name, object value) => item.GetType().GetField(name,Flags).SetValue(item,value);
        public static string Run()
        {
            var log = new StringBuilder();
            void Check(bool pass,string message) { if (!pass) throw new Exception(message); log.AppendLine("PASS " + message); }
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null; Mesh surface = null, prototypeMesh = null; Material material = null; Texture2D density = null, sizes = null, ids = null;
            MGTerrain terrain = null;
            try
            {
                root = new GameObject("Detail collision validation"); SceneManager.MoveGameObjectToScene(root,scene);
                surface = new Mesh(); surface.vertices = new[] { Vector3.zero,new Vector3(16,0,0),new Vector3(0,2,16),new Vector3(16,2,16) }; surface.triangles = new[] {0,2,1,1,2,3};surface.RecalculateBounds();surface.RecalculateNormals();
                var filter = root.AddComponent<MeshFilter>();filter.sharedMesh=surface;
                var renderer = root.AddComponent<MeshRenderer>();
                var ground = root.AddComponent<MeshCollider>();ground.sharedMesh=surface;
                terrain=root.AddComponent<MGTerrain>();terrain.Configure(filter,renderer,ground);terrain.ConfigureSurfaceGrid(2,2);
                root.transform.SetPositionAndRotation(new Vector3(50,10,30),Quaternion.Euler(0,35,0));root.transform.localScale=new Vector3(1.3f,1.1f,.8f);
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);prototypeMesh=Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(cube);
                material=new Material(Shader.Find("HDRP/Lit"));
                int prototype=terrain.FindOrAddPrototype(prototypeMesh,material,MGTerrain.InstanceKind.Detail);
                density=new Texture2D(4,4,TextureFormat.R16,false,true);var data=density.GetPixelData<ushort>(0);for(int i=0;i<data.Length;i++)data[i]=0;data[5]=2;data[15]=1;density.Apply(false,false);
                sizes=new Texture2D(4,4,TextureFormat.RFloat,false,true);sizes.SetPixels(Enumerable.Repeat(new Color(1.5f,0,0,0),16).ToArray());sizes.Apply(false,false);
                ids=new Texture2D(4,4,TextureFormat.R8,false,true);var idData=ids.GetPixelData<byte>(0);for(int i=0;i<idData.Length;i++)idData[i]=0;idData[15]=3;ids.Apply(false,false);
                terrain.AddDensityDetailLayer(prototype,density,.7f,1.2f,1.1f,1.6f,481,3,-.25f);
                var layer=terrain.DensityDetailLayers[0];Set(layer,"m_SizeMap",sizes);Set(layer,"m_GrassIdMap",ids);Set(layer,"m_UseGrassArray",true);
                Set(terrain,"m_UseBatchRendererGroup",false);Set(terrain,"m_CombineDenseDetailMeshes",false);Set(terrain,"m_DetailChunkCells",4);Set(terrain,"m_OverallDetailDensity",.1f);
                Vector3 focus=root.transform.TransformPoint(new Vector3(8,0,8));
                terrain.UpdateDetailColliders(focus,true);Check(terrain.ActiveDetailColliderCount==0,"Existing detail definitions default to collision off");
                layer.CapsuleCollision.enabled=true;layer.CapsuleCollision.capsule.center=new Vector3(.1f,1.5f,.2f);layer.CapsuleCollision.capsule.height=3;
                layer.CapsuleCollision.subIdOverrides=new[] { new MGTerrain.DetailCapsuleOverride { subId=3, capsule=new MGTerrain.DetailCapsuleShape { center=new Vector3(0,2,0),radius=.4f,height=4 } } };
                terrain.InvalidateDetailColliders();terrain.UpdateDetailColliders(focus,true);
                Check(terrain.ActiveDetailColliderCount==3,"Trees marked Detail create one capsule per generated painted instance");
                CapsuleCollider[] Capsules() => Resources.FindObjectsOfTypeAll<CapsuleCollider>().Where(c=>c.gameObject.scene==scene && c.gameObject.activeInHierarchy).ToArray();
                var capsules=Capsules();Check(capsules.Length==3 && capsules.All(c=>!c.enabled),"Preview capsules never affect editor physics");
                var chunk=typeof(MGTerrain).GetMethod("BuildDensityDetailChunk",Flags).Invoke(terrain,new object[]{0,layer,terrain.Prototypes[prototype],surface.bounds,4,0,0,0,.1f,false});
                var batches=(IList)Field(chunk,"batches");Check(batches.Count>0,"CPU renderer comparison generated batches");
                var matrices=((IEnumerable<Matrix4x4[]>)Field(batches[0],"matrixChunks")).SelectMany(m=>m).ToArray();
                var slices=((IEnumerable<byte[]>)Field(batches[0],"grassSliceChunks")).SelectMany(m=>m).ToArray();
                Check(matrices.Length==3,"Collision-enabled detail population is not density-thinned");
                for(int i=0;i<matrices.Length;i++)
                {
                    var shape=layer.CapsuleCollision.Shape(slices[i]);var matrix=matrices[i];Vector3 center=matrix.MultiplyPoint3x4(shape.center);
                    var capsule=capsules.Single(c=>Vector3.Distance(c.transform.position,center)<.0001f);
                    float radius=shape.radius*Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude,matrix.MultiplyVector(Vector3.forward).magnitude);
                    float expectedHeight=Mathf.Max(2*radius,shape.height*matrix.MultiplyVector(Vector3.up).magnitude);
                    Check(Mathf.Abs(capsule.height-expectedHeight)<.0001f && Vector3.Dot(capsule.transform.up,matrix.MultiplyVector(Vector3.up).normalized)>.9999f,"Capsule height and axis match painted transform");
                    Check(Mathf.Abs(capsule.radius-radius)<.0001f,"Capsule matches renderer position, rotation/scale and painted sub-ID " + slices[i]);
                }
                foreach(var capsule in capsules)
                {
                    capsule.enabled=true;
                    Physics.SyncTransforms();
                    Vector3 side=capsule.transform.right;
                    Check(capsule.Raycast(new Ray(capsule.transform.position + side*(capsule.radius+2),-side),out _,4),"Capsule participates in physics queries when enabled");
                    capsule.enabled=false;
                }
                var beforeHeight=capsules.Select(c=>c.transform.position.y).OrderBy(y=>y).ToArray();
                var vertices=surface.vertices;for(int i=0;i<vertices.Length;i++)vertices[i].y+=1;surface.vertices=vertices;surface.RecalculateBounds();
                terrain.NotifySurfaceMeshChanged(false,true);terrain.UpdateDetailColliders(focus,true);
                var afterHeight=Capsules().Select(c=>c.transform.position.y).OrderBy(y=>y).ToArray();
                Check(afterHeight.Select((y,i)=>Mathf.Abs(y-beforeHeight[i]-root.transform.lossyScale.y)<.0001f).All(v=>v),"Terrain sculpt height updates capsule positions");
                capsules=Capsules();
                float heightSum=capsules.Sum(c=>c.height);
                sizes.SetPixels(Enumerable.Repeat(new Color(2,0,0,0),16).ToArray());sizes.Apply(false,false);
                terrain.RefreshDetailPaintRegion(0,new Rect(0,0,1,1));terrain.UpdateDetailColliders(focus,true);
                Check(Mathf.Abs(Capsules().Sum(c=>c.height)/heightSum-4f/3)<.0001f,"Size painting updates capsule dimensions");
                capsules=Capsules();
                var retained=capsules.Select(c=>c.GetInstanceID()).OrderBy(i=>i).ToArray();terrain.UpdateDetailColliders(focus,true);
                Check(Capsules().Select(c=>c.GetInstanceID()).OrderBy(i=>i).SequenceEqual(retained),"Unchanged cells reuse existing colliders");
                terrain.UpdateDetailColliders(focus+Vector3.right*1000,true);Check(terrain.ActiveDetailColliderCount==0,"Distant capsules are disabled and pooled");
                terrain.UpdateDetailColliders(focus,true);Check(Capsules().Select(c=>c.GetInstanceID()).OrderBy(i=>i).SequenceEqual(retained),"Returning reuses pooled capsule objects");
                layer.CapsuleCollision.subIdOverrides[0].enabled=false;terrain.InvalidateDetailColliders();terrain.UpdateDetailColliders(focus,true);Check(terrain.ActiveDetailColliderCount==2,"Individual painted sub-IDs can exclude collision");
                data=density.GetPixelData<ushort>(0);for(int i=0;i<data.Length;i++)data[i]=0;density.Apply(false,false);terrain.RefreshDetailPaintRegion(0,new Rect(0,0,1,1));terrain.UpdateDetailColliders(focus,true);
                Check(terrain.ActiveDetailColliderCount==0,"Erasing density removes stale capsules");
                terrain.enabled=false;Check(!Resources.FindObjectsOfTypeAll<CapsuleCollider>().Any(c=>c.gameObject.scene==scene),"Disabling terrain releases active and pooled capsules");
                Check(ground.sharedMesh==surface,"Terrain collider and source mesh preserved");
                return log.AppendLine("SUCCESS").ToString();
            }
            finally
            {
                if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
                foreach(var item in new Object[]{surface,prototypeMesh,material,density,sizes,ids})if(item)Object.DestroyImmediate(item);
            }
        }
    }
}
