using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public static class StudioOutfitValidation
    {
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); Debug.Log("PASS: " + name); }
        public static void Run()
        {
            GameObject hat = null;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Skeleton.fbx");
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/SM_Body_01.fbx");
                var original = body.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                int originalIndices = original.triangles.Length;
                hat = GameObject.CreatePrimitive(PrimitiveType.Cube); hat.name = "TestHat";
                using (var rig = new AuthoringRig(source, body, new[] { body, hat }, new[] {
                    new OutfitOptions { role = OutfitRole.SkinnedClothing, hideBody = true, cutoutDistance = 0.03f },
                    new OutfitOptions { role = OutfitRole.Hat, doubleSided = true, position = new Vector3(0, 0.1f, 0), scale = 0.1f }
                }))
                {
                    Check(rig.HiddenBodyTriangles > 0, "Coverage removes body triangles");
                    Check(original.triangles.Length == originalIndices, "Source body mesh remains unchanged");
                    var clipped = rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => s.sharedMesh.name.Contains("Preview cutout"));
                    Check(clipped.sharedMesh.vertexCount == original.vertexCount && clipped.sharedMesh.bindposes.Length == original.bindposes.Length,
                        "Cutout retains vertices, bind poses and skinning layout");
                    var head = rig.Animator.GetBoneTransform(HumanBodyBones.Head);
                    var attached = head.Find("Hat • TestHat");
                    Check(attached && Vector3.Distance(attached.localPosition, new Vector3(0, 0.1f, 0)) < 0.0001f, "Rigid hat attaches to Head with offset");
                    foreach (bool neutral in new[] { false, true })
                    {
                        rig.SetNeutralShading(neutral);
                        Check(attached.GetComponentInChildren<Renderer>().sharedMaterial.GetFloat("_Cull") == 0, "Hat is double sided in both shading modes");
                        Check(clipped.sharedMaterial.GetFloat("_Cull") == 2, "Other pieces retain backface culling");
                    }
                    var shader = Shader.Find("Hidden/MashBox/AnimationStudioPreview");
                    Check(shader && !ShaderUtil.ShaderHasError(shader), "Preview shader imports without errors");
                    var pose = rig.Capture();
                    int headIndex = Array.IndexOf(rig.Bones, head);
                    pose.bones[headIndex].rotation *= Quaternion.Euler(0, 45, 0);
                    rig.Apply(pose);
                    Check(Quaternion.Angle(attached.rotation, head.rotation) < 0.001f, "Hat follows animated head rotation");
                    Check(!rig.Root.GetComponentsInChildren<Collider>().Any(), "Temporary coverage colliders are cleaned up");
                }
                using (var rig = new AuthoringRig(source, body, new[] { body }, new[] { new OutfitOptions { role = OutfitRole.Body, hideBody = true } }))
                    Check(rig.HiddenBodyTriangles == 0, "Body role does not cut other body pieces");
                using (var rig = new AuthoringRig(source, body))
                    Check(rig.HiddenBodyTriangles == 0 && rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s => s.sharedMesh == original), "Removing coverage restores original body geometry");
                UnityEngine.Object.DestroyImmediate(hat);
                File.WriteAllText("outfit-validation-result.txt", "PASS: body cutout, source preservation, skinning layout, hat parenting and animation, body role, cleanup, removal.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex); if (hat) UnityEngine.Object.DestroyImmediate(hat);
                File.WriteAllText("outfit-validation-result.txt", ex.ToString()); EditorApplication.Exit(1);
            }
        }
    }
}

