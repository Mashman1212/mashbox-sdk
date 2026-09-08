using System;
using System.Reflection;
using System.Runtime.InteropServices;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.MapTools
{
    public static class MGTerrainOcclusionValidation
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        [StructLayout(LayoutKind.Sequential)]
        struct Draw { public Vector3 center; public uint offset; public Vector3 extents; public float pad; }

        [MenuItem("MashBox/Validation/Validate Terrain GPU Occlusion")]
        public static void Run()
        {
            var go = new GameObject("Occlusion test"); go.SetActive(false);
            Mesh mesh = new Mesh();
            GraphicsBuffer instances = null, ranges = null, draws = null, sourceArgs = null, args = null, output = null;
            MGTerrain terrain = null;
            try
            {
                terrain = go.AddComponent<MGTerrain>();
                var vertices = new Vector3[81];
                for (int z = 0; z < 9; z++) for (int x = 0; x < 9; x++)
                    vertices[z * 9 + x] = new Vector3(x, z >= 3 && z <= 5 ? 10f : 0f, z);
                mesh.vertices = vertices;
                int[] triangles = new int[8 * 8 * 6]; int ti = 0;
                for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
                { int a = z * 9 + x; triangles[ti++] = a; triangles[ti++] = a + 9; triangles[ti++] = a + 1;
                    triangles[ti++] = a + 1; triangles[ti++] = a + 9; triangles[ti++] = a + 10; }
                mesh.triangles = triangles; mesh.RecalculateBounds(); go.GetComponent<MeshFilter>().sharedMesh = mesh;
                Set(terrain, "m_CachedDetailSurfaceVertices", vertices);
                Set(terrain, "m_DetailGpuSurfaceWidth", 9); Set(terrain, "m_DetailGpuSurfaceHeight", 9);
                Check((bool)typeof(MGTerrain).GetMethod("BuildOcclusionHeightMin", Flags).Invoke(terrain, null), "Height hierarchy not built.");
                var texture = (Texture2D)typeof(MGTerrain).GetField("m_OcclusionHeightMin", Flags).GetValue(terrain);
                var shader = Resources.Load<ComputeShader>("MGTerrainDetailInstances");
                Check(shader != null, "Compute shader missing.");
                int reset = shader.FindKernel("ResetCulledArgs"), cull = shader.FindKernel("CullTerrainDetails");
                float[] matrices = new float[60];
                for (int i = 0; i < 3; i++)
                {
                    int a = 24 + i * 12;
                    matrices[a] = 1; matrices[a + 4] = i == 1 ? 30 : 1; matrices[a + 8] = 1;
                    matrices[a + 9] = 4; matrices[a + 11] = i == 2 ? 1 : 7;
                }
                instances = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 60, 4); instances.SetData(matrices);
                ranges = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16); ranges.SetData(new[] { new Vector4Int(0, 3, 0, 0) });
                draws = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 32);
                draws.SetData(new[] { new Draw { center = new Vector3(0, 0.5f, 0), extents = new Vector3(0.1f, 0.5f, 0.1f) } });
                sourceArgs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, 4); sourceArgs.SetData(new uint[] { 3, 3, 0, 0, 0 });
                args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 5, 4);
                output = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 3, 4);
                shader.SetInt("_OcclusionDrawCount", 1); shader.SetInt("_ObjectToWorldAddress", 96); shader.SetInt("_VisibilityRangeOffset", 0);
                shader.SetBuffer(reset, "_SourceArgs", sourceArgs); shader.SetBuffer(reset, "_CulledArgs", args);
                shader.SetBuffer(cull, "_VisibilityRanges", ranges); shader.SetBuffer(cull, "_OcclusionDraws", draws);
                shader.SetBuffer(cull, "_InstanceData", instances); shader.SetBuffer(cull, "_CulledArgs", args); shader.SetBuffer(cull, "_CulledIndices", output);
                shader.SetTexture(cull, "_TerrainHeightMin", texture); shader.SetMatrix("_OcclusionWorldToLocal", Matrix4x4.identity);
                shader.SetInt("_CullRenderedDepth", 0); shader.SetTexture(cull, "_RenderedDepthPyramid", Texture2D.whiteTexture);
                shader.SetVector("_OcclusionCameraLocal", new Vector3(4, 2, 0)); shader.SetVector("_OcclusionPadding", Vector4.zero);
                shader.SetVector("_OcclusionSurface", new Vector4(0, 0, 1, 1)); shader.SetVector("_OcclusionGrid", new Vector4(8, 8, 3, 0));
                var planes = new Vector4[6]; for (int i = 0; i < 6; i++) planes[i] = new Vector4(0, 0, 0, 1);
                planes[0] = new Vector4(0, 0, -1, 2); shader.SetVectorArray("_OcclusionPlanes", planes);
                Action<int, int, uint> run = (frustum, occlusion, expected) =>
                {
                    shader.SetInt("_CullDetailFrustum", frustum); shader.SetInt("_CullTerrainOcclusion", occlusion);
                    shader.Dispatch(reset, 1, 1, 1); shader.Dispatch(cull, 1, 1, 1);
                    uint[] result = new uint[5]; args.GetData(result);
                    Check(result[1] == expected, $"Expected {expected} survivors for {frustum}/{occlusion}, got {result[1]}.");
                    uint[] ids = new uint[3]; output.GetData(ids);
                    Debug.Log($"GPU culling {frustum}/{occlusion}: count={result[1]}, ids={string.Join(",", ids)}");
                    for (int i = 0; i < expected; i++)
                    { Check(ids[i] < 3, "Invalid instance index."); for (int j = 0; j < i; j++) Check(ids[i] != ids[j], "Duplicate instance."); }
                    if (occlusion == 1 && frustum == 0 && expected == 2)
                        Check((ids[0] == 1 && ids[1] == 2) || (ids[0] == 2 && ids[1] == 1), "Occlusion hid the tall mesh or foreground mesh.");
                };
                run(0, 0, 3); run(0, 1, 2); run(1, 0, 1); run(1, 1, 1); run(0, 0, 3);
                shader.SetVector("_OcclusionPadding", new Vector3(0, 30, 0)); run(0, 1, 3);
                shader.SetVector("_OcclusionPadding", Vector4.zero);
                Action<int[]> rebuild = visibleTriangles =>
                {
                    typeof(MGTerrain).GetMethod("ReleaseTerrainOcclusion", Flags).Invoke(terrain, null);
                    mesh.triangles = visibleTriangles;
                    Check((bool)typeof(MGTerrain).GetMethod("BuildOcclusionHeightMin", Flags).Invoke(terrain, null), "Hole-aware hierarchy was bypassed.");
                    shader.SetTexture(cull, "_TerrainHeightMin", (Texture2D)typeof(MGTerrain).GetField("m_OcclusionHeightMin", Flags).GetValue(terrain));
                };
                // A distant partial hole must not disable occlusion across the terrain.
                var distantHole = new int[triangles.Length - 3]; Array.Copy(triangles, 3, distantHole, 0, distantHole.Length);
                rebuild(distantHole); run(0, 1, 2);
                // A corridor opening through the bank must not act like solid ground.
                var corridor = new System.Collections.Generic.List<int>();
                for (int q = 0; q < 64; q++) if (q % 8 != 3 && q % 8 != 4)
                    for (int t = 0; t < 6; t++) corridor.Add(triangles[q * 6 + t]);
                rebuild(corridor.ToArray()); run(0, 1, 3);
                rebuild(triangles); run(0, 1, 2);
                // Scene-depth test uses the same pyramid builder as the HDRP adapter.
                using (var depthPyramid = new TerrainRenderedDepthPyramid())
                using (var depthCommands = new UnityEngine.Rendering.CommandBuffer())
                {
                    var depthTexture = new Texture2D(64, 64, TextureFormat.RFloat, false, true);
                    try
                    {
                        float[] depthValues = new float[64 * 64];
                        Matrix4x4 clip = Matrix4x4.identity;
                        clip.m00 = 0.1f; clip.m03 = -0.4f; clip.m11 = 0.05f; clip.m13 = -0.5f; clip.m22 = 0.1f;
                        Action<float, bool> fillDepth = (value, reversed) =>
                        {
                            for (int i = 0; i < depthValues.Length; i++) depthValues[i] = value;
                            depthTexture.SetPixelData(depthValues, 0); depthTexture.Apply(false, false);
                            depthCommands.Clear(); depthPyramid.Record(depthCommands, depthTexture, 64, 64, reversed);
                            Graphics.ExecuteCommandBuffer(depthCommands);
                            shader.SetTexture(cull, "_RenderedDepthPyramid", depthPyramid.Texture);
                            shader.SetVector("_RenderedDepthSize", new Vector4(64, 64, depthPyramid.Texture.mipmapCount - 1, reversed ? 1 : 0));
                            shader.SetVector("_RenderedDepthConvention", new Vector4(0.5f, 0.5f, 1, 0));
                            shader.SetMatrix("_RenderedDepthLocalToClip", clip);
                            shader.SetInt("_CullRenderedDepth", 1);
                        };
                        fillDepth(0.3f, false); run(0, 0, 2);
                        // Empty current-frame depth must immediately reveal everything.
                        fillDepth(1f, false); run(0, 0, 3);
                        clip.m22 = -0.1f; clip.m23 = 1f;
                        fillDepth(0.7f, true); run(0, 0, 2);
                        fillDepth(0f, true); run(0, 0, 3);
                        shader.SetInt("_CullRenderedDepth", 0);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(depthTexture); }
                }
                uint[] original = new uint[5]; sourceArgs.GetData(original); Check(original[1] == 3, "Shadow/source arguments were modified.");
                Debug.Log("TERRAIN_OCCLUSION_PASS: GPU readback verified independent toggles, hidden short mesh, visible tall/foreground meshes, displacement padding, restored baseline and preserved shadow arguments.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
            finally
            {
                instances?.Dispose(); ranges?.Dispose(); draws?.Dispose(); sourceArgs?.Dispose(); args?.Dispose(); output?.Dispose();
                if (terrain != null) typeof(MGTerrain).GetMethod("ReleaseTerrainOcclusion", Flags).Invoke(terrain, null);
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Vector4Int
        { public int x, y, z, w; public Vector4Int(int a, int b, int c, int d) { x = a; y = b; z = c; w = d; } }
    }
}
