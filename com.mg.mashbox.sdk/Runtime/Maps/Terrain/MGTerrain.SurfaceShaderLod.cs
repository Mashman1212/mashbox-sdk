using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField, Min(0f), Tooltip("Keep MG Trail tessellation within this distance of each render chunk. Beyond it use the matching non-tessellated shader. Zero forces the non-tessellated shader. Geometry and collision LOD are independent.")]
        float m_SurfaceShaderDistance = 96f;
        float SurfaceShaderDistance => m_World != null ? m_World.SurfaceShaderDistance : Mathf.Max(0f, m_SurfaceShaderDistance);
        static Shader s_SurfaceFarShader;
        public static Shader SurfaceFarShader => s_SurfaceFarShader != null ? s_SurfaceFarShader : (s_SurfaceFarShader = Resources.Load<Shader>("MG_Lit_Trail_Terrain"));
        public int NearSurfaceShaderChunks => CountSurfaceShaderChunks(false);
        public int FarSurfaceShaderChunks => CountSurfaceShaderChunks(true);
        int CountSurfaceShaderChunks(bool far)
        {
            int count = 0;
            foreach (var tile in m_SurfaceTiles)
                if (tile.renderer != null && tile.renderer.enabled && !tile.renderer.forceRenderingOff
                    && tile.shaderMaterials == (far ? m_SurfaceFarMaterials : m_SurfaceNearMaterials) && m_HasSurfaceShaderLod) count++;
            return count;
        }
        sealed class SurfaceShaderPair
        {
            internal Material source, near, far;
            internal int crc;
            internal float distance = -1f;
            internal bool eligible;
        }
        readonly List<SurfaceShaderPair> m_SurfaceShaderPairs = new List<SurfaceShaderPair>();
        readonly List<Material> m_SurfaceNearMaterials = new List<Material>();
        readonly List<Material> m_SurfaceFarMaterials = new List<Material>();
        SurfaceTile m_SurfaceCombinedProxy;
        bool m_HasSurfaceShaderLod;
        bool m_SurfaceShaderSelectionDirty = true, m_LastSurfaceShaderAllowed;
        Vector3 m_LastSurfaceShaderCamera;
        float m_LastSurfaceShaderDistance = -1f;

        // The saved master material is never replaced or edited. One transient pair per
        // material slot is shared by all this terrain's chunks, not cloned per chunk.
        void PrepareSurfaceShaderLod()
        {
            MeshRenderer.GetSharedMaterials(m_SurfaceTileMaterials);
            bool rebuild = m_SurfaceShaderPairs.Count != m_SurfaceTileMaterials.Count;
            for (int i = 0; !rebuild && i < m_SurfaceShaderPairs.Count; i++)
                rebuild = m_SurfaceShaderPairs[i].source != m_SurfaceTileMaterials[i];
            if (rebuild)
            {
                ReleaseSurfaceShaderMaterials();
                m_SurfaceShaderSelectionDirty = true;
                foreach (var material in m_SurfaceTileMaterials) m_SurfaceShaderPairs.Add(new SurfaceShaderPair { source = material });
                foreach (var tile in m_SurfaceTiles) tile.shaderMaterials = null;
            }
            if (s_SurfaceFarShader == null) s_SurfaceFarShader = Resources.Load<Shader>("MG_Lit_Trail_Terrain");
            m_SurfaceNearMaterials.Clear(); m_SurfaceFarMaterials.Clear(); m_HasSurfaceShaderLod = false;
            float distance = SurfaceShaderDistance;
            foreach (var pair in m_SurfaceShaderPairs)
            {
                var source = pair.source;
                bool eligible = s_SurfaceFarShader != null && source != null && source.shader != null
                    && source.shader.name == "Shader Graphs/MG_Lit_Trail" && source.GetFloat("_TessellationMode") == 0f;
                if (pair.eligible != eligible) { m_SurfaceShaderSelectionDirty = true; foreach (var tile in m_SurfaceTiles) tile.shaderMaterials = null; pair.eligible = eligible; }
                if (!eligible) { m_SurfaceNearMaterials.Add(source); m_SurfaceFarMaterials.Add(source); continue; }
                int crc = source.ComputeCRC();
                if (pair.near == null || pair.far == null || pair.crc != crc || pair.distance != distance)
                {
                    if (pair.near == null) pair.near = new Material(source) { hideFlags = HideFlags.HideAndDontSave, name = source.name + " (Near Surface)" };
                    if (pair.far == null) pair.far = new Material(source) { hideFlags = HideFlags.HideAndDontSave, name = source.name + " (Far Surface)" };
                    pair.near.CopyPropertiesFromMaterial(source);
                    pair.far.CopyPropertiesFromMaterial(source);
                    pair.far.shader = s_SurfaceFarShader;
                    // Reach factor one before any chunk can switch. Original shorter fades
                    // remain intact. Both shaders then displace the same existing vertices.
                    float end = source.GetFloat("_TessellationFactorMaxDistance");
                    end = end > 0f ? Mathf.Min(end, distance * .75f) : distance * .75f;
                    pair.near.SetFloat("_TessellationFactorMaxDistance", end);
                    pair.near.SetFloat("_TessellationFactorMinDistance", Mathf.Min(source.GetFloat("_TessellationFactorMinDistance"), end * .5f));
                    pair.crc = crc; pair.distance = distance;
                }
                m_SurfaceNearMaterials.Add(pair.near); m_SurfaceFarMaterials.Add(pair.far); m_HasSurfaceShaderLod = true;
            }
            // Reuse one renderer for the distant whole-tile path. Its filter selects
            // original or baked geometry; the proxy owns no mesh and no collider.
            if ((m_HasSurfaceShaderLod || SurfaceLodEnabled) && m_SurfaceCombinedProxy == null)
            {
                var go = new GameObject("Surface HLOD") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(m_SurfaceTileRoot.transform, false);
                var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = m_TiledSource;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.forceRenderingOff = true;
                m_SurfaceCombinedProxy = new SurfaceTile { mesh = m_TiledSource, filter = filter, renderer = renderer, isCombinedProxy = true };
                m_SurfaceTiles.Add(m_SurfaceCombinedProxy);
                m_HasSurfaceRendererState = false;
#if UNITY_EDITOR
                UnityEditor.SceneVisibilityManager.instance.DisablePicking(go, true);
#endif
            }
        }

        bool SurfaceTileHidden(SurfaceTile tile) => m_MasterWasForceRenderingOff
            || (tile.isCombinedProxy ? !m_CombinedSurface : m_CombinedSurface);

        void SelectSurfaceShaderLod(Camera camera)
        {
            bool allowed = m_HasSurfaceShaderLod && camera != m_AppearanceCaptureCamera
                && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView);
            float distance = SurfaceShaderDistance;
            Vector3 position = camera.transform.position;
            if (!m_SurfaceShaderSelectionDirty && position == m_LastSurfaceShaderCamera
                && allowed == m_LastSurfaceShaderAllowed && distance == m_LastSurfaceShaderDistance) return;
            m_SurfaceShaderSelectionDirty = false; m_LastSurfaceShaderCamera = position;
            m_LastSurfaceShaderAllowed = allowed; m_LastSurfaceShaderDistance = distance;
            foreach (var tile in m_SurfaceTiles)
            {
                if (SurfaceTileHidden(tile) || tile.renderer == null) continue;
                // Enclosing renderer bounds include displacement and canopy. Never use
                // centre distance: a large chunk can extend right up to the camera.
                float threshold = distance * (tile.farShader ? 1f : 1.1f);
                bool far = allowed && (distance <= 0f || tile.shaderBounds.SqrDistance(position) > threshold * threshold);
                var materials = far ? m_SurfaceFarMaterials : allowed ? m_SurfaceNearMaterials : m_SurfaceTileMaterials;
                if (tile.shaderMaterials != materials)
                {
                    tile.renderer.SetSharedMaterials(materials);
                    tile.shaderMaterials = materials;
                }
                tile.farShader = far;
            }
        }

        void ReleaseSurfaceShaderMaterials()
        {
            foreach (var pair in m_SurfaceShaderPairs)
            {
                if (pair.near != null) { if (Application.isPlaying) Destroy(pair.near); else DestroyImmediate(pair.near); }
                if (pair.far != null) { if (Application.isPlaying) Destroy(pair.far); else DestroyImmediate(pair.far); }
            }
            m_SurfaceShaderPairs.Clear(); m_SurfaceNearMaterials.Clear(); m_SurfaceFarMaterials.Clear();
            m_HasSurfaceShaderLod = false;
        }
    }
}
