using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MashBoxSDK.Culling;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MashBoxSDK.AnimationStudio
{
    public static class StudioClothClippingValidation
    {
        public static void Run()
        {
            var log = new List<string>();
            AnimationStudioWindow window = null; GameObject body = null, garment = null;
            Mesh mesh = null; ClothingCullingSurface surface = null; ClothingCullingProfile profile = null; Material material = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object value) => typeof(AnimationStudioWindow).GetField(name, flags).SetValue(window, value);
            void Call(string name, params object[] args) => typeof(AnimationStudioWindow).GetMethod(name, flags).Invoke(window, args);
            void Check(bool value, string name) { if (!value) throw new Exception(name); log.Add("PASS " + name); }
            try
            {
                mesh = new Mesh { vertices = new[]{Vector3.zero, Vector3.right, Vector3.up,Vector3.one}, triangles = new[]{0,1,2,1,3,2} };
                surface = ScriptableObject.CreateInstance<ClothingCullingSurface>(); surface.vertices = mesh.vertices; surface.triangles = mesh.triangles;
                profile = ScriptableObject.CreateInstance<ClothingCullingProfile>(); profile.masks = new[]{new CullingMask{surface=surface,hidden=new byte[]{1,0}}};
                body = new GameObject("Clipping test body") { hideFlags = HideFlags.HideAndDontSave }; body.AddComponent<MeshFilter>().sharedMesh=mesh; var renderer=body.AddComponent<MeshRenderer>();
                garment = new GameObject("Clipping test garment") { hideFlags = HideFlags.HideAndDontSave }; garment.AddComponent<MeshFilter>().sharedMesh=mesh; var clothing=garment.AddComponent<MeshRenderer>();
                var settings=garment.AddComponent<ClothingCullingSettings>();settings.profile=profile;
                Check(AnimationStudioWindow.DetectClothClipping(clothing,null)==profile,"Detects referenced custom clipping data");
                settings.profile=null;Check(!AnimationStudioWindow.DetectClothClipping(clothing,null),"Explicit empty culling settings remain empty");settings.profile=profile;
                window=ScriptableObject.CreateInstance<AnimationStudioWindow>();
                var rows=new[]{new StudioSkinningDocument.Part{sourceMesh=mesh,mesh=mesh,referenceOnly=true,visible=true,role=StudioSkinningDocument.MeshRole.Body},new StudioSkinningDocument.Part{sourceMesh=mesh,mesh=mesh,visible=true,role=StudioSkinningDocument.MeshRole.Clothing}};
                Set("clothRenderers",new Renderer[]{renderer,clothing});Set("clothMeshRows",rows);Set("clothMeshSources",new[]{body,garment});
                Set("clothClippingPreview",0);Call("ApplyClothClipping");
                Check(body.GetComponent<MeshFilter>().sharedMesh!=mesh && body.GetComponent<MeshFilter>().sharedMesh.triangles.Length==3,"Assigned clipping hides only covered body triangles");
                Check(mesh.triangles.Length==6 && garment.GetComponent<MeshFilter>().sharedMesh==mesh,"Preview leaves source mesh and garment untouched");
                rows[1].visible=false;Call("ApplyClothClipping");Check(body.GetComponent<MeshFilter>().sharedMesh==mesh,"Hiding garment restores its body coverage");
                Set("clothClippingPreview",2);Set("clothPreviewClipping",profile);Call("ApplyClothClipping");Check(body.GetComponent<MeshFilter>().sharedMesh.triangles.Length==3,"Explicit preset previews independently of garment visibility");
                Set("clothClippingPreview",1);Call("ApplyClothClipping");Check(body.GetComponent<MeshFilter>().sharedMesh==mesh,"None restores complete body mesh");
                Call("InitializeClothDisplay");material=new Material(Shader.Find("MashBox/Studio/Preview"));Call("AssignClothMaterial",0,0,material);
                Check(renderer.sharedMaterial==material,"Body references accept preview material assignment");
                Call("ReleaseClothClipping");Check(body.GetComponent<MeshFilter>().sharedMesh==mesh,"Cleanup restores original references");
                log.Add("SUCCESS");
            }
            catch(Exception e){log.Add("FAIL "+e);}
            finally
            {
                if(window)Object.DestroyImmediate(window);
                if(body)Object.DestroyImmediate(body);if(garment)Object.DestroyImmediate(garment);
                if(mesh)Object.DestroyImmediate(mesh);if(surface)Object.DestroyImmediate(surface);if(profile)Object.DestroyImmediate(profile);if(material)Object.DestroyImmediate(material);
                File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/cloth-clipping-validation.txt")),log);
            }
        }
    }
}
