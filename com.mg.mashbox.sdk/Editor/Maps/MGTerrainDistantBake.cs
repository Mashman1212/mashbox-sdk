#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MashBoxSDK.Maps.TerrainSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MashBoxSDK.MapTools
{
    public sealed partial class MGTerrainEditor
    {
        float m_DistantSpacing = 2f;
        float m_DistantFadeStart = 300f, m_DistantFadeEnd = 400f;
        float m_DistantHeadroom = 150f;
        int m_DistantSmoothing = 1;
        LayerMask m_DistantLayers = ~0;

        sealed class AppearanceHeightPass : CustomPass
        {
            [NonSerialized] internal Camera captureCamera;
            [NonSerialized] internal RenderTexture destination;
            [NonSerialized] internal Material decoder;
            [NonSerialized] internal bool captured;
            protected override void Execute(CustomPassContext ctx)
            {
                if (ctx.hdCamera.camera != captureCamera || destination == null || decoder == null) return;
                CoreUtils.SetRenderTarget(ctx.cmd, destination);
                ctx.cmd.SetViewport(new Rect(0, 0, destination.width, destination.height));
                CoreUtils.DrawFullScreen(ctx.cmd, decoder);
                captured = true;
            }
        }

        void DrawDistantSurfaceBake(MGTerrain terrain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Distant Surface Mesh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bakes a separate canopy/rooftop surface with its own appearance textures. Captures actual upright geometry (detail tilt is disabled). Only the topmost depth-writing surface is represented. Originals keep their current draw distances; match the fade below to those distances. Generated surfaces are excluded from captures.", MessageType.Info);
            m_DistantSpacing = Mathf.Max(.25f, EditorGUILayout.FloatField("Mesh Spacing (Metres)", m_DistantSpacing));
            m_DistantSmoothing = EditorGUILayout.IntSlider("Height Smoothing Passes", m_DistantSmoothing, 0, 4);
            m_DistantHeadroom = Mathf.Max(20f, EditorGUILayout.FloatField(new GUIContent("Capture Headroom (Metres)", "Height above the terrain's highest vertex. Increase for unusually tall trees or buildings."), m_DistantHeadroom));
            // LayerField cannot represent a mask; map the named-layer popup back to actual layer bits.
            var names = new List<string>();
            var bits = new List<int>();
            int selected = 0;
            for (int i = 0; i < 32; i++)
            {
                string layer = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(layer)) continue;
                if ((m_DistantLayers.value & (1 << i)) != 0) selected |= 1 << names.Count;
                names.Add(layer); bits.Add(i);
            }
            int mask = EditorGUILayout.MaskField("Capture Layers", selected, names.ToArray());
            int actual = mask == -1 ? -1 : 0;
            if (mask != -1) for (int i = 0; i < bits.Count; i++) if ((mask & (1 << i)) != 0) actual |= 1 << bits[i];
            m_DistantLayers = actual;
            m_DistantFadeStart = Mathf.Max(0, EditorGUILayout.FloatField("Fade In Start (Metres)", m_DistantFadeStart));
            m_DistantFadeEnd = Mathf.Max(m_DistantFadeStart + 1, EditorGUILayout.FloatField("Fade In End (Metres)", m_DistantFadeEnd));
            DrawDistantMorph(terrain);
            using (new EditorGUI.DisabledScope(Application.isPlaying || !terrain.isActiveAndEnabled))
                if (GUILayout.Button("Bake Distant Mesh"))
                {
                    serializedObject.ApplyModifiedProperties();
                    CaptureTerrainAppearance(terrain, true);
                    GUIUtility.ExitGUI();
                }
        }

        void SaveDistantSurface(MGTerrain terrain, Texture2D heightMap, Texture2D appearance, string colourPath)
        {
            Shader shader = Shader.Find("MashBox/Terrain Distant Surface");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The distant surface shader is missing or unsupported.");
            Transform surface = terrain.MeshFilter.transform;
            Bounds bounds = terrain.MeshFilter.sharedMesh.bounds;
            int nx = Mathf.Max(1, Mathf.CeilToInt(surface.TransformVector(Vector3.right * bounds.size.x).magnitude / m_DistantSpacing));
            int nz = Mathf.Max(1, Mathf.CeilToInt(surface.TransformVector(Vector3.forward * bounds.size.z).magnitude / m_DistantSpacing));
            if ((long)(nx + 1) * (nz + 1) > 1048576)
                throw new InvalidOperationException("Mesh would exceed one million vertices. Increase Mesh Spacing.");
            var raw = heightMap.GetPixelData<float>(0);
            int width = heightMap.width, height = heightMap.height;
            var heights = new float[(nx + 1) * (nz + 1)];
            bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > -1e19f;
            // Bilinear sampling with invalid samples ignored; holes stay holes when all samples are empty.
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float px = Mathf.Clamp(x / (float)nx * width - .5f, 0, width - 1);
                float pz = Mathf.Clamp(z / (float)nz * height - .5f, 0, height - 1);
                int ix = Mathf.FloorToInt(px), iz = Mathf.FloorToInt(pz);
                float total = 0, weight = 0;
                for (int dz = 0; dz <= 1; dz++) for (int dx = 0; dx <= 1; dx++)
                {
                    float v = raw[Mathf.Min(iz + dz, height - 1) * width + Mathf.Min(ix + dx, width - 1)];
                    float w = (dx == 0 ? 1 - (px - ix) : px - ix) * (dz == 0 ? 1 - (pz - iz) : pz - iz);
                    if (Valid(v)) { total += v * w; weight += w; }
                }
                heights[z * (nx + 1) + x] = weight > 0 ? total / weight : -1e20f;
            }
            for (int pass = 0; pass < m_DistantSmoothing; pass++)
            {
                var smoothed = (float[])heights.Clone();
                // Preserve outer edges so neighbouring bakes can meet at their captured boundary.
                for (int z = 1; z < nz; z++) for (int x = 1; x < nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    if (!Valid(heights[i])) continue;
                    float sum = heights[i] * 4, count = 4;
                    for (int neighbour = 0; neighbour < 4; neighbour++)
                    {
                        int j = neighbour == 0 ? i - 1 : neighbour == 1 ? i + 1 : neighbour == 2 ? i - nx - 1 : i + nx + 1;
                        if (Valid(heights[j])) { sum += heights[j]; count++; }
                    }
                    smoothed[i] = sum / count;
                }
                heights = smoothed;
            }
            string stem = Path.ChangeExtension(colourPath, null);
            var createdPaths = new List<string>();
            GameObject root = null;
            bool complete = false;
            try
            {
                var material = new Material(shader) { name = terrain.name + " Distant Surface" };
                material.SetTexture("_BaseMap", appearance);
                material.SetFloat("_FadeStart", m_DistantFadeStart);
                material.SetFloat("_FadeEnd", m_DistantFadeEnd);
                string materialPath = AssetDatabase.GenerateUniqueAssetPath(stem + "_Surface.mat");
                AssetDatabase.CreateAsset(material, materialPath); createdPaths.Add(materialPath);
                var savedHeight = MGTerrainHeightEncoding.EncodeDifference(terrain.MeshFilter.sharedMesh, bounds, heights,
                    nx, nz, width, height, out Vector4 heightDecode);
                savedHeight.name = terrain.name + " Surface Height Difference";
                string heightPath = AssetDatabase.GenerateUniqueAssetPath(stem + "_Height.asset");
                AssetDatabase.CreateAsset(savedHeight, heightPath); createdPaths.Add(heightPath);
                MGTerrainHeightEncoding.SaveMetadata(heightPath, heightDecode);
                root = new GameObject(terrain.name + " Distant Surface");
                root.SetActive(false);
                root.transform.SetParent(surface, false);
                root.AddComponent<MGTerrainDistantSurface>().SetSource(terrain);
                const int chunkCells = 128;
                string meshPath = null;
                int triangles = 0, chunks = 0;
                Vector3 Point(int x, int z)
                {
                    float y = heights[z * (nx + 1) + x];
                    return new Vector3(bounds.min.x + bounds.size.x * x / nx, Valid(y) ? y : bounds.min.y,
                        bounds.min.z + bounds.size.z * z / nz);
                }
                for (int z0 = 0; z0 < nz; z0 += chunkCells) for (int x0 = 0; x0 < nx; x0 += chunkCells)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Distant Surface Mesh", "Building mesh chunks", z0 / (float)nz))
                        throw new OperationCanceledException("Distant mesh bake cancelled. The previous mesh was kept.");
                    int cx = Mathf.Min(chunkCells, nx - x0), cz = Mathf.Min(chunkCells, nz - z0);
                    var vertices = new Vector3[(cx + 1) * (cz + 1)];
                    var normals = new Vector3[vertices.Length];
                    var uv = new Vector2[vertices.Length];
                    var indices = new List<int>(cx * cz * 6);
                    for (int z = 0; z <= cz; z++) for (int x = 0; x <= cx; x++)
                    {
                        int i = z * (cx + 1) + x, gx = x0 + x, gz = z0 + z;
                        vertices[i] = Point(gx, gz);
                        uv[i] = new Vector2(gx / (float)nx, gz / (float)nz);
                        normals[i] = Vector3.Cross(Point(gx, Mathf.Min(nz, gz + 1)) - Point(gx, Mathf.Max(0, gz - 1)),
                            Point(Mathf.Min(nx, gx + 1), gz) - Point(Mathf.Max(0, gx - 1), gz)).normalized;
                        if (x == cx || z == cz) continue;
                        int h = gz * (nx + 1) + gx;
                        if (!Valid(heights[h]) || !Valid(heights[h + 1]) || !Valid(heights[h + nx + 1]) || !Valid(heights[h + nx + 2])) continue;
                        indices.Add(i); indices.Add(i + cx + 1); indices.Add(i + 1);
                        indices.Add(i + 1); indices.Add(i + cx + 1); indices.Add(i + cx + 2);
                    }
                    if (indices.Count == 0) continue;
                    var mesh = new Mesh { name = $"Surface_{x0}_{z0}", vertices = vertices, normals = normals, uv = uv };
                    mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                    if (meshPath == null)
                    {
                        meshPath = AssetDatabase.GenerateUniqueAssetPath(stem + "_Meshes.asset");
                        AssetDatabase.CreateAsset(mesh, meshPath); createdPaths.Add(meshPath);
                    }
                    else AssetDatabase.AddObjectToAsset(mesh, meshPath);
                    var child = new GameObject(mesh.name);
                    child.layer = terrain.gameObject.layer;
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    triangles += indices.Count / 3; chunks++;
                }
                if (chunks == 0) throw new InvalidOperationException("No surface was captured. Check Capture Layers and Capture Headroom.");
                AssetDatabase.SaveAssets();
                // Replace only this terrain's scene proxy, after the new assets are complete. Old assets remain reusable.
                foreach (var old in terrain.GetComponentsInChildren<MGTerrainDistantSurface>(true))
                    if (old.Source == terrain && old.gameObject != root) Undo.DestroyObjectImmediate(old.gameObject);
                root.SetActive(true);
                Undo.RegisterCreatedObjectUndo(root, "Bake Distant Terrain Surface");
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                complete = true;
                m_LastMorphHeight = savedHeight;
                EditorGUIUtility.PingObject(root);
                Debug.Log($"Distant surface baked: {chunks} chunks, {triangles:N0} triangles. Fade {m_DistantFadeStart}–{m_DistantFadeEnd} metres; edit the generated material to adjust. Originals keep their existing draw distances. Assets: {materialPath}", terrain);
            }
            finally
            {
                if (!complete)
                {
                    if (root != null) DestroyImmediate(root);
                    foreach (string asset in createdPaths) AssetDatabase.DeleteAsset(asset);
                }
            }
        }
    }
}
#endif
