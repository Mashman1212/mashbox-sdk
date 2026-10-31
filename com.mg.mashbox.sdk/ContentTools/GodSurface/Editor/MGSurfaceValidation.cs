using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MG.GodSurface.Editor
{
    public static class MGSurfaceValidation
    {
        static readonly string report = Path.Combine(Path.GetTempPath(), "MGSurfaceValidation.txt");
        static void Check(bool pass, string message, StringBuilder log) { if (!pass) throw new Exception(message); log.AppendLine("PASS " + message); }
        [MenuItem("MashBox/Content Tools/Validate God Surface Cells")]
        public static void Run()
        {
            var log = new StringBuilder(); Scene testScene = default; Scene originalScene = SceneManager.GetActiveScene(); Material material = null; GameObject root = null;
            try
            {
                Check(MGSurfaceMath.Cell(new Vector3(-.1f, 0, -64), 64) == new Vector2Int(-1, -1), "Negative grid coordinates use floor", log);
                var matrix = Matrix4x4.TRS(new Vector3(63, 0, 0), Quaternion.Euler(10, 35, 0), new Vector3(-4, 2, 1));
                matrix.m01 += .7f;
                var bounds = MGSurfaceMath.TransformBounds(new Bounds(Vector3.zero, Vector3.one * 2), matrix);
                for (int i = 0; i < 8; i++)
                {
                    var corner = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var expanded = bounds; expanded.Expand(.001f); Check(expanded.Contains(corner), "Transformed bounds contain corner " + i, log);
                }
                Check(MGSurfaceMath.SelectLOD(.6f, new[] {.5f, .1f}) == 0 && MGSurfaceMath.SelectLOD(.2f, new[] {.5f, .1f}) == 1 && MGSurfaceMath.SelectLOD(.05f, new[] {.5f, .1f}) == -1, "LOD selection and terminal cull threshold", log);
                Check(MGSurfaceMath.Inside(new Bounds(Vector3.zero, Vector3.one * 4), new Plane(Vector3.right, -1)), "Intersecting bounds survive a plane even when pivot is outside", log);
                testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                root = new GameObject("God Surface Validation"); SceneManager.MoveGameObjectToScene(root, testScene);
                var world = root.AddComponent<MGSurfaceWorld>(); world.roots = new[] { root.transform }; world.previewInEditor = true;
                var shader = Shader.Find("HDRP/Lit"); Check(shader && !ShaderUtil.ShaderHasError(shader), "HDRP Lit is available", log);
                material = new Material(shader) { enableInstancing = false };
                var one = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(one, testScene); one.transform.SetParent(root.transform, false); one.transform.position = new Vector3(63, 0, 0); one.transform.localScale = new Vector3(8, 1, 1);
                var two = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(two, testScene); two.transform.SetParent(root.transform, false); two.transform.position = new Vector3(128, 0, 0);
                one.GetComponent<MeshRenderer>().sharedMaterial = material; two.GetComponent<MeshRenderer>().sharedMaterial = material;
                var collider = one.GetComponent<BoxCollider>(); var originalMesh = one.GetComponent<MeshFilter>().sharedMesh;
                world.Rebuild();
                Check(world.LastError == null && world.IsRendering, "BRG and raw instance buffer created: " + world.LastError, log);
                Check(world.InstanceCount == 2 && world.PrototypeCount == 1 && world.CellCount == 2 && world.DrawGroupCount == 1, "Two cells share one mesh and one automatic render group", log);
                Check(one.GetComponent<MeshRenderer>().forceRenderingOff && two.GetComponent<MeshRenderer>().forceRenderingOff, "Original renderers suppressed only after successful registration", log);
                Check(collider && collider.enabled && one.activeSelf && one.GetComponent<MeshFilter>().sharedMesh == originalMesh, "Collision, GameObject and mesh references preserved", log);
                Check(!material.enableInstancing, "Source material remains unmodified", log);
                Check(world.CellBounds.Any(b => b.max.x >= 67 && b.min.x <= 59), "Cell bounds include geometry crossing nominal cell edge", log);
                CheckPixels(world, one, two, material, testScene, log);
                var sourceMaterial = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.shader && m.shader.name == "MGShaders/HDRP/Lit/MG_Lit_Basic" && AssetDatabase.Contains(m));
                if (sourceMaterial)
                {
                    var graphMaterial = new Material(sourceMaterial);
                    try
                    {
                        one.GetComponent<MeshRenderer>().sharedMaterial = graphMaterial;
                        two.GetComponent<MeshRenderer>().sharedMaterial = graphMaterial;
                        log.AppendLine("Checking project shader with material: " + sourceMaterial.name);
                        CheckPixels(world, one, two, graphMaterial, testScene, log);
                    }
                    finally
                    {
                        one.GetComponent<MeshRenderer>().sharedMaterial = material;
                        two.GetComponent<MeshRenderer>().sharedMaterial = material;
                        UnityEngine.Object.DestroyImmediate(graphMaterial);
                    }
                }
                world.Release(); Check(!one.GetComponent<MeshRenderer>().forceRenderingOff, "Original rendering restored on release", log);
                var block = new MaterialPropertyBlock(); block.SetFloat("_Test", 1); two.GetComponent<MeshRenderer>().SetPropertyBlock(block);
                world.Rebuild(); Check(world.InstanceCount == 1 && !two.GetComponent<MeshRenderer>().forceRenderingOff && world.FallbackCount == 1, "Unsupported instance remains on its original renderer", log);
                world.Release(); two.GetComponent<MeshRenderer>().SetPropertyBlock(null);
                one.transform.localScale = new Vector3(-1, 2, 1); world.Rebuild(); Check(world.DrawGroupCount == 2, "Mirrored winding gets an independent automatic group", log);
                world.enabled = false; Check(!one.GetComponent<MeshRenderer>().forceRenderingOff && !two.GetComponent<MeshRenderer>().forceRenderingOff, "Disabling world restores all renderer states", log);
                log.AppendLine("SUCCESS");
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); Debug.LogException(ex); }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (material) UnityEngine.Object.DestroyImmediate(material);
                if (originalScene.IsValid()) SceneManager.SetActiveScene(originalScene);
                if (testScene.IsValid()) EditorSceneManager.CloseScene(testScene, true);
                File.WriteAllText(report, log.ToString()); Debug.Log(log.ToString());
            }
        }
        internal static Color[] Capture(Camera camera, RenderTexture target)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            var old = RenderTexture.active; RenderTexture.active = target;
            var read = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try { read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); read.Apply(); return read.GetPixels(); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(read); }
        }
        static void CheckPixels(MGSurfaceWorld world, GameObject one, GameObject two, Material material, Scene scene, StringBuilder log)
        {
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var go = new GameObject("God Surface Render Test Camera"); SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
            camera.transform.position = new Vector3(63, 0, -12); camera.transform.rotation = Quaternion.identity; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGBHalf); target.Create();
            one.layer = two.layer = 30;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.8f, .15f, .05f));
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", new Color(.8f, .15f, .05f));
            try
            {
                world.Release(); Capture(camera, target); var reference = Capture(camera, target);
                world.Rebuild(); Capture(camera, target); var instanced = Capture(camera, target);
                SavePixels(reference, "MGSurface-reference.png"); SavePixels(instanced, "MGSurface-instanced.png");
                log.AppendLine($"Camera counters: instances={world.LastVisibleInstances}, commands={world.LastDrawCommands}, cells={world.LastVisibleCells}");
                double error = 0; float maximum = 0;
                for (int i = 0; i < reference.Length; i++) { error += Mathf.Abs(reference[i].r - instanced[i].r) + Mathf.Abs(reference[i].g - instanced[i].g) + Mathf.Abs(reference[i].b - instanced[i].b); maximum = Mathf.Max(maximum, instanced[i].r); }
                error /= reference.Length * 3;
                Check(maximum > .05f && error < .025, $"HDRP instanced pixels match MeshRenderer (mean RGB error {error:F6})", log);
                Check(world.LastVisibleInstances == 1 && world.LastDrawCommands == 1, "Real HDRP camera culls the offscreen cell and submits one instance", log);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0); Capture(camera, target);
                Check(world.LastVisibleInstances == 0 && world.LastDrawCommands == 0, "Turning the camera away culls every cell", log);
            }
            finally { ShaderUtil.allowAsyncCompilation = asyncCompilation; world.Release(); target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go); }
        }
        static void SavePixels(Color[] pixels, string name)
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            try { texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(Path.Combine(Path.GetTempPath(), name), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
