using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace MG.GodSurface.Editor
{
    public static class MGSurfaceVertexAOValidation
    {
        public static string Render(MGSurfaceLibrary source)
        {
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            GameObject root = null; Mesh mesh = null; Material shared = null, preview = null; MGSurfaceLibrary library = null; RenderTexture target = null;
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var bank = source.banks[0];
                shared = new Material(bank.sharedMaterial); preview = new Material(bank.previewMaterials[0]);
                library = ScriptableObject.CreateInstance<MGSurfaceLibrary>();
                library.banks = new[] { new MGSurfaceLibrary.Bank { sharedMaterial = shared, parameters = bank.parameters, previewMaterials = new[] { preview }, surfaceNames = bank.surfaceNames, arrays = bank.arrays } };
                root = new GameObject("AO rendering validation"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var world = root.AddComponent<MGSurfaceWorld>(); world.roots = new[] { root.transform }; world.previewInEditor = true; world.surfaceLibrary = library;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cube, scene); cube.transform.SetParent(root.transform, false); cube.layer = 30;
                mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh); mesh.colors = Enumerable.Repeat(new Color(.2f, 1, 1, 1), mesh.vertexCount).ToArray();
                cube.GetComponent<MeshFilter>().sharedMesh = mesh; cube.GetComponent<MeshRenderer>().sharedMaterial = preview;
                var cameraObject = new GameObject("AO test camera"); cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
                camera.transform.position = new Vector3(0, 0, -3); camera.nearClipPlane = .1f; camera.farClipPlane = 10; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                target = new RenderTexture(128,128,24,RenderTextureFormat.ARGBHalf); target.Create();
                double Difference(Color[] a, Color[] b) => a.Select((c,i) => Math.Abs(c.r-b[i].r)+Math.Abs(c.g-b[i].g)+Math.Abs(c.b-b[i].b)).Average()/3;
                Color[] Capture() { MGSurfaceValidation.Capture(camera,target); return MGSurfaceValidation.Capture(camera,target); }
                world.Release(); preview.SetFloat(MGSurfaceVertexAO.Strength, 0); var off = Capture();
                preview.SetFloat(MGSurfaceVertexAO.Strength, 1); shared.SetFloat(MGSurfaceVertexAO.Strength, 1); var on = Capture();
                double effect = Difference(off,on);
                if (effect < .000001) throw new Exception("AO strength produced no measurable rendered change: " + effect);
                world.Rebuild(); var instanced = Capture(); double error = Difference(on,instanced);
                if (world.LastError != null || world.InstanceCount != 1 || error > .001) throw new Exception("Vertex AO instancing mismatch: " + error + " " + world.LastError);
                if (ShaderUtil.ShaderHasError(shared.shader)) throw new Exception("God Surface shader compilation failed");
                return $"PASS Vertex AO Strength changes rendered shading (mean RGB difference {effect:F6})\nPASS Instanced vertex AO matches normal rendering (error {error:F6})\n";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (root) Object.DestroyImmediate(root); if (mesh) Object.DestroyImmediate(mesh);
                if (shared) Object.DestroyImmediate(shared); if (preview) Object.DestroyImmediate(preview); if (library) Object.DestroyImmediate(library);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (previousScene.IsValid() && previousScene != scene) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
        public static string Run()
        {
            var log = new StringBuilder();
            void Check(bool pass, string message) { if (!pass) throw new Exception(message); log.AppendLine("PASS " + message); }
            Mesh source = new Mesh { name = "AO corner test" }, baked = null, unreadable = null, readback = null;
            try
            {
                // Floor with a vertex inside the corner plus a vertical wall: cavity vs exposed edge.
                source.vertices = new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(.1f,0,.5f), new Vector3(0,1,0), new Vector3(0,1,1) };
                source.triangles = new[] { 0,2,4, 2,3,4, 3,1,4, 1,0,4, 0,5,6, 0,6,2 };
                source.RecalculateNormals(); source.RecalculateBounds();
                source.uv = source.vertices.Select(v => new Vector2(v.x,v.z)).ToArray();
                source.colors = Enumerable.Repeat(new Color(.7f,.3f,.6f,.8f), source.vertexCount).ToArray();
                var original = source.colors; var geometry = source.vertices; var triangles = source.triangles; var uv = source.uv;
                var settings = new MGSurfaceVertexAO.Settings { samples = 128, relativeDistance = 1, bias = .001f };
                baked = MGSurfaceVertexAO.BakeMesh(source, settings);
                Check(source.colors.SequenceEqual(original), "Source colours remain unchanged");
                Check(baked.vertices.SequenceEqual(geometry) && baked.triangles.SequenceEqual(triangles) && baked.uv.SequenceEqual(uv), "Geometry, topology and UVs preserved");
                Check(baked.bounds == source.bounds && baked.subMeshCount == source.subMeshCount, "Bounds and submeshes preserved");
                Check(baked.colors.Select((c,i) => c.g == original[i].g && c.b == original[i].b && c.a == original[i].a).All(v => v), "Green, blue and alpha preserved");
                Check(baked.colors.All(c => c.r >= 0 && c.r <= 1) && baked.colors.Min(c => c.r) < .95f && baked.colors.Max(c => c.r) - baked.colors.Min(c => c.r) > .05f, "Cavity receives nonuniform AO: " + baked.colors.Min(c => c.r) + " to " + baked.colors.Max(c => c.r));
                bool cancelled = false;
                try { var unexpected = MGSurfaceVertexAO.BakeMesh(source, settings, p => true); Object.DestroyImmediate(unexpected); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && source.colors.SequenceEqual(original), "Cancelled bake preserves source");
                unreadable = Object.Instantiate(source); unreadable.UploadMeshData(true);
                readback = MGSurfaceVertexAO.ReadableCopy(unreadable);
                Check(!unreadable.isReadable && readback.isReadable && readback.vertices.SequenceEqual(geometry) && readback.uv.SequenceEqual(uv), "Unreadable imported-style mesh copied without changing its Read/Write setting");
                var shader = Shader.Find(MGSurfaceLibrary.ShaderName);
                Check(shader && !ShaderUtil.ShaderHasError(shader), "God Surface shader imports without errors");
                var material = new Material(shader);
                try { Check(material.HasProperty(MGSurfaceVertexAO.Strength) && material.GetFloat(MGSurfaceVertexAO.Strength) == 0, "AO strength exposed with safe zero default"); }
                finally { Object.DestroyImmediate(material); }
                return log.AppendLine("SUCCESS").ToString();
            }
            finally { Object.DestroyImmediate(source); if (baked) Object.DestroyImmediate(baked); if (unreadable) Object.DestroyImmediate(unreadable); if (readback) Object.DestroyImmediate(readback); }
        }
    }
}
