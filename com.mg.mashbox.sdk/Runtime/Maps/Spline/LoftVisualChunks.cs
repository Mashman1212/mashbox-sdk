using System;
using System.Collections.Generic;
using UnityEngine;

namespace MashBoxSDK.Maps.Spline
{
    /// <summary>Owns generated loft renderers. Physics remains on the authored collider objects.</summary>
    [DisallowMultipleComponent]
    public sealed class LoftVisualChunks : MonoBehaviour
    {
        [Serializable]
        public sealed class Source
        {
            public MeshRenderer renderer;
            public bool wasForcedOff;
            public bool wasEnabled;
            public MeshRenderer[] chunks;
        }

        [SerializeField] Source[] m_Sources = Array.Empty<Source>();
        [SerializeField] float m_CullDistance = 150f;
        [SerializeField] bool m_Baked;
        CullingGroup m_Group;
        Camera m_Camera;
        BoundingSphere[] m_Spheres;
        MeshRenderer[] m_Renderers;
        Mesh[] m_OwnedMeshes = Array.Empty<Mesh>();
        Matrix4x4 m_LastMatrix;
        static Camera[] s_Cameras = new Camera[4];
        static Camera s_FallbackCamera;
        static int s_CameraFrame = -1;

        /// <summary>Optional gameplay camera override for projects with multiple camera rigs.</summary>
        public static Camera CameraOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCameraCache()
        {
            CameraOverride = null;
            s_FallbackCamera = null;
            s_CameraFrame = -1;
        }

        static Camera ResolveCamera()
        {
            if (CameraOverride != null && CameraOverride.isActiveAndEnabled) return CameraOverride;
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            if (s_CameraFrame == Time.frameCount && s_FallbackCamera != null && s_FallbackCamera.isActiveAndEnabled)
                return s_FallbackCamera;
            s_CameraFrame = Time.frameCount;
            s_FallbackCamera = null;
            int count = Camera.allCamerasCount;
            if (s_Cameras.Length < count) s_Cameras = new Camera[count];
            count = Camera.GetAllCameras(s_Cameras);
            for (int i = 0; i < count; i++)
            {
                Camera candidate = s_Cameras[i];
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.cameraType != CameraType.Game) continue;
                if (s_FallbackCamera == null ||
                    (s_FallbackCamera.targetTexture != null && candidate.targetTexture == null) ||
                    ((s_FallbackCamera.targetTexture == null) == (candidate.targetTexture == null) && candidate.depth > s_FallbackCamera.depth))
                    s_FallbackCamera = candidate;
            }
            return s_FallbackCamera;
        }

        public int ChunkCount => m_Renderers != null ? m_Renderers.Length : GetComponentsInChildren<MeshRenderer>(true).Length;

        // Configure while inactive so no partially constructed output can render.
        public void Configure(Source[] sources, float distance, bool baked)
        {
            m_Sources = sources;
            m_CullDistance = Mathf.Max(1f, distance);
            m_Baked = baked;
            var meshes = new List<Mesh>();
            foreach (Source source in sources)
                foreach (MeshRenderer chunk in source.chunks)
                    meshes.Add(chunk.GetComponent<MeshFilter>().sharedMesh);
            m_OwnedMeshes = meshes.ToArray();
        }

        void OnEnable()
        {
            m_Group?.Dispose();
            m_Group = null;
            m_Camera = null;
            var renderers = new List<MeshRenderer>();
            foreach (Source source in m_Sources)
            {
                if (source.renderer != null)
                {
                    source.renderer.forceRenderingOff = true;
                    if (m_Baked) source.renderer.enabled = false;
                }
                foreach (MeshRenderer chunk in source.chunks)
                    if (chunk != null) chunk.enabled = source.wasEnabled && !source.wasForcedOff;
                renderers.AddRange(source.chunks);
            }
            m_Renderers = renderers.ToArray();
            if (!m_Baked && m_OwnedMeshes.Length == 0)
            {
                m_OwnedMeshes = new Mesh[m_Renderers.Length];
                for (int i = 0; i < m_Renderers.Length; i++)
                    if (m_Renderers[i] != null) m_OwnedMeshes[i] = m_Renderers[i].GetComponent<MeshFilter>().sharedMesh;
            }
            m_Spheres = new BoundingSphere[m_Renderers.Length];
            RefreshBounds();
            RefreshCamera();
        }

        void LateUpdate()
        {
            // One group per visual source; no Update components on individual chunks.
            RefreshCamera();
            foreach (Source source in m_Sources)
            {
                bool draw = (m_Baked ? source.wasEnabled : source.renderer != null && source.renderer.enabled) && !source.wasForcedOff;
                if (source.chunks.Length > 0 && source.chunks[0] != null && source.chunks[0].enabled != draw)
                    foreach (MeshRenderer chunk in source.chunks)
                        if (chunk != null) chunk.enabled = draw;
            }
            if (m_LastMatrix != transform.localToWorldMatrix)
                RefreshBounds();
        }

        void RefreshBounds()
        {
            m_LastMatrix = transform.localToWorldMatrix;
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] == null) continue;
                Bounds bounds = m_Renderers[i].bounds;
                m_Spheres[i] = new BoundingSphere(bounds.center, bounds.extents.magnitude);
            }
            ApplyInitialDistances();
        }

        void RefreshCamera()
        {
            Camera camera = ResolveCamera();
            if (camera == m_Camera && (camera == null || m_Group != null)) return;
            m_Group?.Dispose();
            m_Group = null;
            m_Camera = camera;
            if (camera != null && m_Renderers.Length > 0)
            {
                m_Group = new CullingGroup { targetCamera = camera };
                m_Group.SetBoundingSpheres(m_Spheres);
                m_Group.SetBoundingSphereCount(m_Spheres.Length);
                // Infinity keeps distance results available outside the cutoff/frustum.
                m_Group.SetBoundingDistances(new[] { m_CullDistance, float.PositiveInfinity });
                m_Group.SetDistanceReferencePoint(camera.transform);
                m_Group.onStateChanged = OnCullingChanged;
            }
            // Fail open without a gameplay camera, including replacement during scene loads.
            ApplyInitialDistances();
        }

        void ApplyInitialDistances()
        {
            if (m_Renderers == null) return;
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                BoundingSphere sphere = m_Spheres[i];
                float limit = m_CullDistance + sphere.radius;
                bool far = m_Camera != null &&
                    (m_Camera.transform.position - sphere.position).sqrMagnitude > limit * limit;
                if (m_Renderers[i] != null) m_Renderers[i].forceRenderingOff = far;
            }
        }

        void OnCullingChanged(CullingGroupEvent change)
        {
            if (m_Renderers[change.index] != null)
                // Only distance controls this flag. Unity still handles per-camera frustum
                // culling, and offscreen chunks can continue to cast nearby shadows.
                m_Renderers[change.index].forceRenderingOff = change.currentDistance > 0;
        }

        void OnDisable()
        {
            m_Group?.Dispose();
            m_Group = null;
            m_Camera = null;
            foreach (Source source in m_Sources)
            {
                if (source.renderer != null)
                {
                    source.renderer.forceRenderingOff = source.wasForcedOff;
                    if (m_Baked) source.renderer.enabled = source.wasEnabled;
                }
                foreach (MeshRenderer chunk in source.chunks)
                    if (chunk != null) chunk.forceRenderingOff = true;
            }
        }

        void OnDestroy()
        {
            m_Group?.Dispose();
            m_Group = null;
            if (m_Baked) return; // Serialized build meshes are owned by the scene/bundle.
            foreach (Mesh mesh in m_OwnedMeshes)
                if (mesh != null)
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
        }
    }
}
