using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxSDK.Maps.Spline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace MashBoxSDK.MapTools
{
    public static class LoftVisualValidation
    {
        static int s_Checks;
        static void Lifecycle(Component component, string method)
        {
            component.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(component, null);
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            s_Checks++;
        }

        [MenuItem("MashBox/Validation/Validate Loft Visual Chunks")]
        public static void Run()
        {
            try
            {
                s_Checks = 0;
                ValidateMeshData();
                ValidateLargeMesh();
                ValidateLoftAndCulling();
                Debug.Log($"LOFT_VISUAL_VALIDATION_PASS: {s_Checks} checks");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void ValidateMeshData()
        {
            var mesh = new Mesh { name = "Visual chunk test" };
            mesh.vertices = new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(0,0,10),
                new Vector3(0,0,100), new Vector3(1,0,100), new Vector3(0,0,110) };
            mesh.normals = Enumerable.Repeat(Vector3.up, 6).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1,0,0,-1), 6).ToArray();
            mesh.colors = Enumerable.Range(0,6).Select(i => new Color(i / 6f, 0.25f, 0.75f, 1f)).ToArray();
            for (int channel = 0; channel < 8; channel++)
                mesh.SetUVs(channel, Enumerable.Range(0,6).Select(i => new Vector4(i,channel,i+channel,0.5f)).ToArray());
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0,1,2 }, 0);
            mesh.SetTriangles(new[] { 3,4,5 }, 1);
            List<Mesh> chunks = LoftVisualMeshBuilder.Split(mesh, new float[] { 0,0,10,100,100,110 }, 50);
            try
            {
                Check(chunks.Count == 2, "Empty sections must not create renderers.");
                Check(chunks.Sum(c => (int)c.GetIndexCount(0) + (int)c.GetIndexCount(1)) == 6, "Triangles were lost or duplicated.");
                foreach (Mesh chunk in chunks)
                {
                    Check(chunk.subMeshCount == 2, "Material slots changed.");
                    for (int i = 0; i < chunk.vertexCount; i++)
                    {
                        int original = Array.IndexOf(mesh.vertices, chunk.vertices[i]);
                        Check(original >= 0, "Vertex positions changed.");
                        Check(chunk.normals[i] == mesh.normals[original], "Normals changed at a seam.");
                        Check(chunk.tangents[i] == mesh.tangents[original], "Tangents changed.");
                        Check(chunk.colors[i] == mesh.colors[original], "Vertex paint changed.");
                        for (int channel = 0; channel < 8; channel++)
                        {
                            var before = new List<Vector4>(); var after = new List<Vector4>();
                            mesh.GetUVs(channel, before); chunk.GetUVs(channel, after);
                            Check(before[original] == after[i], "UV/lightmap data changed.");
                        }
                    }
                }
                bool rejected = false;
                try { LoftVisualMeshBuilder.Split(mesh, new float[1], 50); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid source metadata was accepted.");
            }
            finally
            {
                foreach (Mesh chunk in chunks) Object.DestroyImmediate(chunk);
                Object.DestroyImmediate(mesh);
            }
        }

        static void ValidateLoftAndCulling()
        {
            var go = new GameObject("Loft visual validation");
            var cameraObject = new GameObject("Loft visual validation camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            var target = new RenderTexture(16, 16, 16);
            camera.targetTexture = target;
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                MultiSplineLoft loft = go.AddComponent<MultiSplineLoft>();
                loft.AutoRegenerate = false;
                loft.TargetSegmentLength = 2;
                loft.ColliderChunkLength = 50;
                loft.VisualCullDistance = 150;
                for (int side = 0; side < 2; side++)
                {
                    var curve = new GameObject("Curve " + side);
                    curve.transform.SetParent(go.transform, false);
                    SplineContainer container = curve.AddComponent<SplineContainer>();
                    container.Spline = new UnityEngine.Splines.Spline(new[] {
                        new BezierKnot(new Unity.Mathematics.float3(side * 4,0,0)),
                        new BezierKnot(new Unity.Mathematics.float3(side * 4,0,200)) });
                    loft.Sources.Add(new MultiSplineLoft.SplineSource { container = container });
                }
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                loft.Regenerate();
                Mesh originalMesh = go.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider[] colliders = go.GetComponentsInChildren<MeshCollider>(true);
                Check(colliders.Length == 4, "Expected four 50 m collider sections.");
                loft.BuildVisualChunks(false);
                LoftVisualChunks owner = go.GetComponentInChildren<LoftVisualChunks>(true);
                Check(owner != null && owner.ChunkCount == 4, "Expected four 50 m visual sections.");
                Check(go.GetComponent<MeshFilter>().sharedMesh == originalMesh, "Authoring mesh was replaced.");
                Check(go.GetComponentsInChildren<MeshCollider>(true).Length == colliders.Length, "Visual splitting changed physics.");
                // Explicit lifecycle calls also let this validation run in Edit Mode.
                Lifecycle(owner, "OnEnable");
                MeshRenderer[] renderers = owner.GetComponentsInChildren<MeshRenderer>();
                camera.transform.position = new Vector3(2, 2, 25);
                camera.Render(); camera.Render();
                Check(!renderers[0].forceRenderingOff, "Nearby chunk was culled.");
                camera.transform.position = new Vector3(2,2,1000);
                camera.Render(); camera.Render();
                Check(renderers.All(r => r.forceRenderingOff), "Far chunks remained visible.");
                Check(colliders.All(c => c.enabled), "Distance culling disabled physics.");
                camera.transform.position = new Vector3(2,2,25);
                camera.transform.rotation = Quaternion.Euler(0,180,0);
                camera.Render(); camera.Render();
                Check(!renderers[0].forceRenderingOff, "Offscreen nearby chunks must still cast shadows.");
                cameraObject.tag = "Untagged";
                Lifecycle(owner, "LateUpdate");
                camera.transform.position = new Vector3(2,2,1000);
                camera.Render(); camera.Render();
                Check(renderers.All(r => r.forceRenderingOff), "Untagged free camera did not cull.");
                camera.enabled = false;
                Lifecycle(owner, "LateUpdate");
                Check(renderers.All(r => !r.forceRenderingOff), "Missing camera must fail open.");
                go.GetComponent<MeshRenderer>().enabled = false;
                Lifecycle(owner, "LateUpdate");
                Check(renderers.All(r => !r.enabled), "Source renderer disable was not respected.");
                go.GetComponent<MeshRenderer>().enabled = true;
                Lifecycle(owner, "LateUpdate");
                Check(renderers.All(r => r.enabled), "Source renderer enable was not respected.");
                Lifecycle(owner, "OnDisable");
                Check(!go.GetComponent<MeshRenderer>().forceRenderingOff, "Original renderer was not restored.");
                Check(renderers.All(r => r.forceRenderingOff), "Disabled controller left duplicate visuals.");
                loft.BuildVisualChunks(false);
                Check(go.GetComponentsInChildren<LoftVisualChunks>(true).Length == 1, "Rebuild accumulated visual roots.");
                loft.ReleaseEditorVisualChunks();
                Check(go.GetComponentsInChildren<LoftVisualChunks>(true).Length == 0, "Editor cleanup retained temporary chunks.");
                loft.enabled = false;
                typeof(MultiSplineLoft).GetMethod("PrepareDisabledLofts", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] {
                        go.scene, UnityEngine.SceneManagement.LoadSceneMode.Additive });
                Check(go.GetComponentsInChildren<LoftVisualChunks>(true).Length == 1,
                    "Disabled authoring component bypassed runtime chunking.");
                loft.ReleaseEditorVisualChunks();
                loft.enabled = true;
                Check(loft.RegenerateUvSpline(out string error), "UV spline generation failed: " + error);
                Mesh[] authoringColliderMeshes = colliders.Select(c => c.sharedMesh).ToArray();
                loft.BuildVisualChunks(true);
                owner = go.GetComponentInChildren<LoftVisualChunks>(true);
                Check(loft.VisualsBaked && owner.ChunkCount == 4, "UV-edited loft did not bake into four sections.");
                Check(!go.GetComponent<MeshRenderer>().enabled, "Baked monolithic renderer must be disabled.");
                Check(colliders.All(c => c.enabled && c.gameObject.activeInHierarchy && c.sharedMesh != null),
                    "Baking removed or disabled collision.");
                Check(colliders.All(c => (c.sharedMesh.hideFlags &
                    (HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild)) == 0),
                    "Baked collision meshes would be omitted from the saved scene/build.");
                Check(authoringColliderMeshes.All(m => (m.hideFlags & (HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild)) == 0),
                    "Baking changed shared authoring collider mesh flags.");
                Physics.SyncTransforms();
                Check(colliders.Any(c => c.Raycast(new Ray(new Vector3(2, 10, 25), Vector3.down), out _, 20)),
                    "Baked loft no longer supports a downward collision ray.");
                foreach (Mesh mesh in authoringColliderMeshes.Except(colliders.Select(c => c.sharedMesh))) Object.DestroyImmediate(mesh);
                Lifecycle(loft, "OnEnable");
                Check(loft.VisualsBaked, "Baked loft entered authoring regeneration.");
                Mesh[] bakedColliderMeshes = colliders.Select(c => c.sharedMesh).ToArray();
                // Simulate a loaded scene: this generation buffer is not serialized.
                typeof(MultiSplineLoft).GetField("m_SurfaceTriangleCount", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).SetValue(loft, 0);
                loft.Regenerate();
                loft.RebuildColliderChunks();
                Check(go.GetComponentsInChildren<MeshCollider>(true).SequenceEqual(colliders),
                    "A rebuild call removed baked collider chunks after loading.");
                Check(colliders.Select(c => c.sharedMesh).SequenceEqual(bakedColliderMeshes),
                    "A rebuild call replaced baked collider meshes after loading.");
                Check(colliders.All(c => c.enabled && c.gameObject.activeInHierarchy),
                    "A rebuild call deactivated baked collider chunks.");
            }
            finally
            {
                foreach (LoftVisualChunks owner in go.GetComponentsInChildren<LoftVisualChunks>(true))
                    Lifecycle(owner, "OnDisable");
                var meshes = new HashSet<Mesh>(go.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh));
                foreach (MeshCollider collider in go.GetComponentsInChildren<MeshCollider>(true)) meshes.Add(collider.sharedMesh);
                Object.DestroyImmediate(go);
                foreach (Mesh mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }

        static void ValidateLargeMesh()
        {
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = Enumerable.Range(0, 66000).Select(i => new Vector3(i, i % 2, 0)).ToArray();
            mesh.triangles = Enumerable.Range(0, 66000).ToArray();
            List<Mesh> chunks = LoftVisualMeshBuilder.Split(mesh, new float[66000], 50);
            try
            {
                Check(chunks.Count == 1 && chunks[0].vertexCount == 66000, "Large mesh vertices were lost.");
                Check(chunks[0].indexFormat == IndexFormat.UInt32 && chunks[0].triangles[65999] == 65999,
                    "Large mesh indices overflowed.");
            }
            finally
            {
                foreach (Mesh chunk in chunks) Object.DestroyImmediate(chunk);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
