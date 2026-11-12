using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField, Tooltip("Select explicitly baked HLOD meshes. No LOD generation occurs during rendering or gameplay.")]
        bool m_SurfaceLodEnabled = true;
        [SerializeField, Range(.25f, 8f), Tooltip("Maximum projected base-mesh error in pixels. Shader displacement remains active on retained vertices.")]
        float m_SurfaceLodPixelError = 1.5f;
        [SerializeField, Min(0f), Tooltip("Minimum distance to a tile's nearest bound before selecting a baked HLOD. Near tessellation always retains the original geometry.")]
        float m_SurfaceLodMinDistance = 256f;
        [SerializeField] MGTerrainSurfaceLodAsset m_BakedSurfaceLod;
        public MGTerrainSurfaceLodAsset BakedSurfaceLod => m_BakedSurfaceLod;
        sealed class SurfaceLod
        {
            internal Mesh mesh;
            internal float error;
            internal int triangles;
        }
        readonly List<SurfaceLod> m_SurfaceLods = new List<SurfaceLod>();
        int m_ActiveSurfaceLod = -1;
        MGTerrainSurfaceLodAsset m_LoadedSurfaceLod;
        Mesh m_LoadedSurfaceSource;
        bool m_SurfaceLodChecked, m_RuntimeSurfaceLodStale;
        string m_SurfaceLodStatus = "HLOD not baked";
#if UNITY_EDITOR
        int m_SurfaceLodSourceDirtyCount;
#endif
        public int CachedSurfaceLodCount => m_SurfaceLods.Count;
        public int ActiveSurfaceLod => m_ActiveSurfaceLod + 1;
        public int ActiveSurfaceLodTriangleCount => m_ActiveSurfaceLod >= 0 ? m_SurfaceLods[m_ActiveSurfaceLod].triangles : 0;
        bool SurfaceLodEnabled => m_World != null ? m_World.SurfaceLodEnabled : m_SurfaceLodEnabled;
        float SurfaceLodPixelError => m_World != null ? m_World.SurfaceLodPixelError : Mathf.Clamp(m_SurfaceLodPixelError, .25f, 8f);
        float SurfaceLodMinDistance => m_World != null ? m_World.SurfaceLodMinDistance : Mathf.Max(0f, m_SurfaceLodMinDistance);

        // Bind serialized mesh references only. No tasks, mesh creation, asset writes,
        // automatic baking, or forced SceneView repaints exist in this rendering path.
        void EnsureBakedSurfaceLods()
        {
            var source = MeshFilter != null ? MeshFilter.sharedMesh : null;
#if UNITY_EDITOR
            int dirty = source != null ? UnityEditor.EditorUtility.GetDirtyCount(source) : 0;
            bool changed = dirty != m_SurfaceLodSourceDirtyCount;
#else
            bool changed = false;
#endif
            if (m_SurfaceLodChecked && !changed && m_LoadedSurfaceLod == m_BakedSurfaceLod && m_LoadedSurfaceSource == source) return;
            m_SurfaceLods.Clear(); m_ActiveSurfaceLod = -1;
            m_SurfaceLodChecked = true; m_LoadedSurfaceLod = m_BakedSurfaceLod; m_LoadedSurfaceSource = source;
#if UNITY_EDITOR
            m_SurfaceLodSourceDirtyCount = dirty;
#endif
            m_SurfaceLodStatus = "HLOD not baked";
            if (m_BakedSurfaceLod == null || source == null) return;
            m_SurfaceLodStatus = "Source changed: rebake HLOD";
            if (m_RuntimeSurfaceLodStale || m_BakedSurfaceLod.Source != source || m_BakedSurfaceLod.SourceVertexCount != source.vertexCount) return;
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path) && (UnityEditor.EditorUtility.IsDirty(source)
                || UnityEditor.AssetDatabase.GetAssetDependencyHash(path).ToString() != m_BakedSurfaceLod.SourceDependencyHash)) return;
#endif
            foreach (var level in m_BakedSurfaceLod.Levels ?? Array.Empty<MGTerrainSurfaceLodAsset.Level>())
                if (level != null && level.Mesh != null && level.Mesh.subMeshCount == source.subMeshCount
                    && level.TriangleCount > 0 && !float.IsNaN(level.Error) && !float.IsInfinity(level.Error) && level.Error >= 0)
                    m_SurfaceLods.Add(new SurfaceLod { mesh = level.Mesh, error = level.Error, triangles = level.TriangleCount });
            m_SurfaceLodStatus = m_SurfaceLods.Count == 0 ? "Bake contains no safe reduced mesh" : "Baked HLOD ready";
        }
        int SelectSurfaceLod(Camera camera)
        {
            EnsureBakedSurfaceLods();
            return EvaluateSurfaceLod(camera, out _);
        }
        int EvaluateSurfaceLod(Camera camera, out string reason)
        {
            reason = "HLOD disabled";
            if (!SurfaceLodEnabled) return -1;
            reason = m_SurfaceLodStatus;
            if (m_SurfaceLods.Count == 0) return -1;
            reason = "Camera requires original";
            if (camera == null || camera == m_AppearanceCaptureCamera
                || camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return -1;
            reason = "Surface rendering not prepared";
            if (!m_MasterRenderingSuppressed || MeshRenderer == null) return -1;
            reason = "Tessellation requires original geometry";
            foreach (var material in m_SurfaceTileMaterials)
                if (material != null && material.HasProperty("_TessellationMode")
                    && (material.shader.name != "Shader Graphs/MG_Lit_Trail" || !m_HasSurfaceShaderLod || material.GetFloat("_TessellationMode") != 0)) return -1;
            var bounds = MeshRenderer.bounds;
            float distance = Mathf.Sqrt(bounds.SqrDistance(camera.transform.position));
            if (m_HasSurfaceShaderLod && SurfaceShaderDistance > 0 && distance <= SurfaceShaderDistance * 1.1f) return -1;
            reason = "Inside HLOD start distance";
            if (distance < SurfaceLodMinDistance * (m_ActiveSurfaceLod >= 0 ? 1f : 1.1f)) return -1;
            // Project the AABB onto the view direction. A bounding sphere needlessly
            // rejects distant broad terrain viewed at an angle.
            Vector3 forward = camera.transform.forward;
            float extent = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z)));
            float depth = Vector3.Dot(bounds.center - camera.transform.position, forward) - extent;
            reason = "Bounds too close for projected error";
            if (!camera.orthographic && depth <= .01f) return -1;
            float projection = camera.pixelHeight * Mathf.Abs(camera.nonJitteredProjectionMatrix.m11) * .5f;
            if (!camera.orthographic) projection /= depth;
            float scale = Mathf.Abs(MeshFilter.transform.lossyScale.y);
            int selected = -1;
            for (int i = 0; i < m_SurfaceLods.Count; i++)
                if (m_SurfaceLods[i].error * scale * projection <= SurfaceLodPixelError * (i == m_ActiveSurfaceLod ? 1f : .8f)) selected = i;
            reason = selected < 0 ? "Pixel error requires original" : $"Baked HLOD {selected + 1}: one combined renderer";
            return selected;
        }
#if UNITY_EDITOR
        public void AssignBakedSurfaceLod(MGTerrainSurfaceLodAsset asset)
        {
            ReleaseSurfaceLods(); m_BakedSurfaceLod = asset; m_RuntimeSurfaceLodStale = false;
            EnsureBakedSurfaceLods();
        }
        public string GetSurfaceLodPreview(Camera camera, out int lod, out long originalTriangles, out long selectedTriangles)
        {
            EnsureBakedSurfaceLods();
            var mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            originalTriangles = 0;
            if (mesh != null) for (int i = 0; i < mesh.subMeshCount; i++) originalTriangles += (long)mesh.GetIndexCount(i) / 3;
            int selected = EvaluateSurfaceLod(camera, out string reason);
            lod = selected + 1; selectedTriangles = selected >= 0 ? m_SurfaceLods[selected].triangles : originalTriangles;
            return reason;
        }
#endif
        void ReleaseSurfaceLods()
        {
            // Baked meshes belong to assets, never to this disposable render cache.
            if (m_SurfaceCombinedProxy != null && m_SurfaceCombinedProxy.filter != null)
                m_SurfaceCombinedProxy.filter.sharedMesh = m_SurfaceCombinedProxy.mesh;
            m_SurfaceLods.Clear(); m_ActiveSurfaceLod = -1; m_SurfaceLodChecked = false;
        }
    }
}
