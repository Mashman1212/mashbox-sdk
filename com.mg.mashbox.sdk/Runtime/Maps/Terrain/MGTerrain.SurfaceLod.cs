using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField, Tooltip("Use cached, edge-preserving render LODs for distant terrain. Editing, grass sampling and collision always use the original mesh.")]
        bool m_SurfaceLodEnabled = true;
        [SerializeField, Range(.25f, 8f), Tooltip("Maximum projected source-mesh geometry error in pixels. Smaller values preserve more distant detail. Shader-deformed distant surfaces keep their original geometry.")]
        float m_SurfaceLodPixelError = 1.5f;
        [SerializeField, Min(0f), Tooltip("Keep the original surface within this distance of the tile bounds, in metres.")]
        float m_SurfaceLodMinDistance = 256f;
        sealed class SurfaceLod
        {
            internal Mesh mesh;
            internal float error;
            internal int triangles;
        }
        readonly List<SurfaceLod> m_SurfaceLods = new List<SurfaceLod>();
        SurfaceTile m_SurfaceLodProxy;
        int m_ActiveSurfaceLod = -1;
        bool m_SurfaceLodRequested;
        double m_SurfaceLodNotBefore;
        Task<MGTerrainSurfaceLodBuilder.Level[]> m_SurfaceLodTask;
        CancellationTokenSource m_SurfaceLodCancellation;
        MGTerrainSurfaceLodBuilder.Input m_SurfaceLodInput;
        MGTerrainSurfaceLodBuilder.Level[] m_PendingSurfaceLods;
        int m_NextSurfaceLodUpload;
        static Task s_SurfaceLodWorker;
        static double s_NextSurfaceLodWork;
        static readonly Unity.Profiling.ProfilerMarker s_SurfaceLodUploadMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.SurfaceLodUpload");
#if UNITY_EDITOR
        static readonly HashSet<MGTerrain> s_PendingEditorSurfaceLods = new HashSet<MGTerrain>();
        static readonly List<MGTerrain> s_EditorSurfaceLodWork = new List<MGTerrain>();
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterSurfaceLodUpdates()
        {
            UnityEditor.EditorApplication.update -= UpdateEditorSurfaceLods;
            UnityEditor.EditorApplication.update += UpdateEditorSurfaceLods;
        }
        static void UpdateEditorSurfaceLods()
        {
            if (Application.isPlaying || UnityEditor.EditorApplication.isCompiling || s_PendingEditorSurfaceLods.Count == 0) return;
            s_EditorSurfaceLodWork.Clear(); s_EditorSurfaceLodWork.AddRange(s_PendingEditorSurfaceLods);
            foreach (var terrain in s_EditorSurfaceLodWork)
            {
                if (terrain == null || !terrain.isActiveAndEnabled) { s_PendingEditorSurfaceLods.Remove(terrain); continue; }
                terrain.PumpSurfaceLods();
                if (terrain.m_SurfaceLodRequested && terrain.m_SurfaceLodTask == null && terrain.m_PendingSurfaceLods == null)
                    s_PendingEditorSurfaceLods.Remove(terrain);
            }
            s_EditorSurfaceLodWork.Clear();
        }
#endif
        public int CachedSurfaceLodCount => m_SurfaceLods.Count;
        public int ActiveSurfaceLod => m_ActiveSurfaceLod + 1;
        public int ActiveSurfaceLodTriangleCount => m_ActiveSurfaceLod >= 0 ? m_SurfaceLods[m_ActiveSurfaceLod].triangles : 0;
        bool SurfaceLodEnabled => m_World != null ? m_World.SurfaceLodEnabled : m_SurfaceLodEnabled;
        float SurfaceLodPixelError => m_World != null ? m_World.SurfaceLodPixelError : Mathf.Clamp(m_SurfaceLodPixelError, .25f, 8f);
        float SurfaceLodMinDistance => m_World != null ? m_World.SurfaceLodMinDistance : Mathf.Max(0f, m_SurfaceLodMinDistance);

        // Only LateUpdate calls this. Camera movement and camera callbacks never build meshes.
        // One worker across all terrains and one main-thread snapshot/upload per scheduling slot.
        void PumpSurfaceLods()
        {
            if (!SurfaceLodEnabled || !m_MasterRenderingSuppressed || m_TiledSource == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < m_SurfaceLodNotBefore || now < s_NextSurfaceLodWork) return;
            if (m_SurfaceLodTask != null && m_SurfaceLodTask.IsCompleted)
            {
                if (m_SurfaceLodTask.Status == TaskStatus.RanToCompletion) m_PendingSurfaceLods = m_SurfaceLodTask.Result;
                else if (m_SurfaceLodTask.IsFaulted) Debug.LogWarning("MG Terrain surface LOD generation kept the original mesh: " + m_SurfaceLodTask.Exception.GetBaseException().Message, this);
                m_SurfaceLodTask = null;
                m_SurfaceLodCancellation?.Dispose(); m_SurfaceLodCancellation = null;
                if (m_PendingSurfaceLods == null || m_PendingSurfaceLods.Length == 0) { m_SurfaceLodInput = null; m_PendingSurfaceLods = null; }
            }
            if (m_PendingSurfaceLods != null && m_NextSurfaceLodUpload < m_PendingSurfaceLods.Length)
            {
                s_NextSurfaceLodWork = now + .008;
                UploadSurfaceLod(m_PendingSurfaceLods[m_NextSurfaceLodUpload++]);
                if (m_NextSurfaceLodUpload == m_PendingSurfaceLods.Length) { m_PendingSurfaceLods = null; m_SurfaceLodInput = null; }
                return;
            }
            if (m_SurfaceLodRequested || s_SurfaceLodWorker != null && !s_SurfaceLodWorker.IsCompleted) return;
            if (s_SurfaceLodWorker != null && s_SurfaceLodWorker.IsFaulted) _ = s_SurfaceLodWorker.Exception;
            m_SurfaceLodRequested = true;
            if (m_SurfaceGridWidth < 3 || m_SurfaceGridHeight < 3 || !m_TiledSource.isReadable) return;
            var input = new MGTerrainSurfaceLodBuilder.Input
            {
                width = m_SurfaceGridWidth, height = m_SurfaceGridHeight,
                vertices = m_TileSourceVertices.ToArray(), normals = m_TileSourceNormals.ToArray(),
                tangents = m_TileSourceTangents.ToArray(), colors = m_TileSourceColors.ToArray(),
                uv = new Vector4[8][], triangles = new int[m_TiledSource.subMeshCount][]
            };
            for (int i = 0; i < 8; i++) input.uv[i] = m_TileSourceUVs[i]?.ToArray() ?? Array.Empty<Vector4>();
            for (int i = 0; i < input.triangles.Length; i++) input.triangles[i] = m_TiledSource.GetTriangles(i);
            m_SurfaceLodInput = input;
            m_SurfaceLodCancellation = new CancellationTokenSource();
            var token = m_SurfaceLodCancellation.Token;
            m_SurfaceLodTask = Task.Run(() => MGTerrainSurfaceLodBuilder.Build(input, token), token);
            s_SurfaceLodWorker = m_SurfaceLodTask;
            s_NextSurfaceLodWork = now + .008;
        }

        void UploadSurfaceLod(MGTerrainSurfaceLodBuilder.Level level)
        {
            using var profile = s_SurfaceLodUploadMarker.Auto();
            var input = m_SurfaceLodInput;
            var mesh = new Mesh { name = name + " Surface LOD " + (m_SurfaceLods.Count + 1), hideFlags = HideFlags.HideAndDontSave,
                indexFormat = level.sourceIndices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = Gather(input.vertices, level.sourceIndices);
            mesh.normals = Gather(input.normals, level.sourceIndices);
            mesh.tangents = Gather(input.tangents, level.sourceIndices);
            mesh.colors = Gather(input.colors, level.sourceIndices);
            for (int i = 0; i < 8; i++) if (input.uv[i].Length != 0) mesh.SetUVs(i, Gather(input.uv[i], level.sourceIndices));
            mesh.subMeshCount = level.triangles.Length;
            for (int i = 0; i < level.triangles.Length; i++) mesh.SetTriangles(level.triangles[i], i, false);
            // Original bounds also cover excluded holes, stitching and shader displacement.
            mesh.bounds = m_TiledSource.bounds;
            mesh.UploadMeshData(true);
            m_SurfaceLods.Add(new SurfaceLod { mesh = mesh, error = level.error, triangles = level.triangleCount });
            if (m_SurfaceLodProxy == null)
            {
                var child = new GameObject("Surface LOD") { hideFlags = HideFlags.HideAndDontSave };
                child.transform.SetParent(m_SurfaceTileRoot.transform, false);
                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.forceRenderingOff = true;
                m_SurfaceLodProxy = new SurfaceTile { mesh = mesh, renderer = renderer, filter = filter, isLodProxy = true };
                m_SurfaceTiles.Add(m_SurfaceLodProxy);
#if UNITY_EDITOR
                UnityEditor.SceneVisibilityManager.instance.DisablePicking(child, true);
#endif
                // A new renderer must receive all state even when existing chunks are unchanged.
                m_PreviousTileMaterials.Clear();
                m_HasSurfaceRendererState = false;
            }
            SyncSurfaceTileRenderers(MeshRenderer);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
#endif
        }

        int SelectSurfaceLod(Camera camera) => EvaluateSurfaceLod(camera, out _);

        // Shared by rendering and inspector diagnostics; querying does not change render state.
        int EvaluateSurfaceLod(Camera camera, out string reason)
        {
            reason = "LOD disabled";
            if (!SurfaceLodEnabled) return -1;
            reason = "Shader deformation: original protected";
            if (!float.IsNegativeInfinity(m_DistantMorphTop)) return -1;
            reason = "Camera requires original";
            if (camera == null || camera == m_AppearanceCaptureCamera
                || camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return -1;
            reason = "Surface rendering not prepared";
            if (!m_MasterRenderingSuppressed || MeshRenderer == null) return -1;
            float distance = Mathf.Sqrt(MeshRenderer.bounds.SqrDistance(camera.transform.position));
            reason = "Inside LOD start distance";
            if (distance < SurfaceLodMinDistance * (m_ActiveSurfaceLod >= 0 ? 1f : 1.1f)) return -1;
            reason = m_SurfaceLodRequested && m_SurfaceLodTask == null && m_PendingSurfaceLods == null
                ? "No safe reduced mesh" : "Preparing LOD cache";
            if (m_SurfaceLods.Count == 0) return -1;
            // Depth to the nearest enclosing sphere point is conservative for perspective projection.
            var bounds = MeshRenderer.bounds;
            float depth = Vector3.Dot(bounds.center - camera.transform.position, camera.transform.forward) - bounds.extents.magnitude;
            reason = "Bounds too close for projected error";
            if (!camera.orthographic && depth <= .01f) return -1;
            float projection = camera.pixelHeight * Mathf.Abs(camera.nonJitteredProjectionMatrix.m11) * .5f;
            if (!camera.orthographic) projection /= depth;
            float scale = Mathf.Abs(MeshFilter.transform.lossyScale.y);
            int selected = -1;
            for (int i = 0; i < m_SurfaceLods.Count; i++)
            {
                // A lower entry threshold avoids flicker; promotion never exceeds the pixel limit.
                float threshold = SurfaceLodPixelError * (i == m_ActiveSurfaceLod ? 1f : .8f);
                if (m_SurfaceLods[i].error * scale * projection <= threshold) selected = i;
            }
            reason = selected < 0 ? "Pixel error requires original"
                : selected == m_SurfaceLods.Count - 1 ? "Coarsest cached LOD" : "Selected by pixel error";
            return selected;
        }

#if UNITY_EDITOR
        /// <summary>Read-only prediction for an explicitly chosen camera, before visibility culling.</summary>
        public string GetSurfaceLodPreview(Camera camera, out int lod, out long originalTriangles, out long selectedTriangles)
        {
            var mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            originalTriangles = 0;
            if (mesh != null)
                for (int i = 0; i < mesh.subMeshCount; i++) originalTriangles += (long)mesh.GetIndexCount(i) / 3;
            int selected = EvaluateSurfaceLod(camera, out string reason);
            lod = selected + 1;
            selectedTriangles = selected >= 0 ? m_SurfaceLods[selected].triangles : originalTriangles;
            if (selected >= 0 && selected == m_SurfaceLods.Count - 1 && m_SurfaceLods[selected].error <= .00001f)
                reason = "Coarsest LOD: negligible height error";
            return reason;
        }
#endif

        void ReleaseSurfaceLods()
        {
#if UNITY_EDITOR
            s_PendingEditorSurfaceLods.Remove(this);
#endif
            m_SurfaceLodCancellation?.Cancel();
            m_SurfaceLodCancellation?.Dispose(); m_SurfaceLodCancellation = null;
            m_SurfaceLodTask = null; m_SurfaceLodInput = null; m_PendingSurfaceLods = null;
            m_NextSurfaceLodUpload = 0; m_SurfaceLodRequested = false;
            m_SurfaceLodNotBefore = Time.realtimeSinceStartupAsDouble + .25;
            m_ActiveSurfaceLod = -1;
            if (m_SurfaceLodProxy != null)
            {
                m_SurfaceTiles.Remove(m_SurfaceLodProxy);
                if (m_SurfaceLodProxy.renderer != null)
                {
                    m_SurfaceLodProxy.renderer.enabled = false;
                    var go = m_SurfaceLodProxy.renderer.gameObject;
                    if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                }
                m_SurfaceLodProxy = null;
            }
            foreach (var lod in m_SurfaceLods) if (lod.mesh != null)
            { if (Application.isPlaying) Destroy(lod.mesh); else DestroyImmediate(lod.mesh); }
            m_SurfaceLods.Clear();
        }
    }
}
