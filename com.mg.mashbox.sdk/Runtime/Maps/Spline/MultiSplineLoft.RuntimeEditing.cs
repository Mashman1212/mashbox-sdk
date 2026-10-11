using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MashBoxSDK.Maps.Spline
{
    public sealed partial class MultiSplineLoft
    {
        [NonSerialized] bool m_RuntimeEditing;
        [NonSerialized] Mesh m_RuntimeEditVisualMesh;
        [NonSerialized] MeshRenderer m_RuntimePreviewRenderer;
        [NonSerialized] bool m_RuntimePreviewActive, m_RuntimeSourceForcedOff;
        static readonly Unity.Profiling.ProfilerMarker RuntimePreviewMarker = new Unity.Profiling.ProfilerMarker("MapEditor.Loft.Preview");
        static readonly Unity.Profiling.ProfilerMarker RuntimeFinalizeMarker = new Unity.Profiling.ProfilerMarker("MapEditor.Loft.Finalize");
        readonly HashSet<Mesh> m_RuntimeEditMeshes = new HashSet<Mesh>();
        public bool RuntimeEditing => m_RuntimeEditing;

        // Read-only preflight used BEFORE changing any source knots, including a rail
        // shared by multiple lofts. A rail itself may not have a batched renderer.
        public void ValidateRuntimeEditing()
        {
            if (!Application.isPlaying || m_RuntimeEditing) return;
            foreach (var source in m_Sources)
                if (source == null || !source.IsValid) throw new InvalidOperationException($"Loft '{name}' has missing authoring splines. Re-export it from the source map.");
            if (GetComponent<MeshRenderer>().isPartOfStaticBatch && !m_GeneratedMesh)
                throw new InvalidOperationException($"Loft '{name}' has no local source mesh to replace its static batch. Re-export with Prepare map for runtime editing.");
            var overlay = HeightOverlayModifier;
            if (overlay)
                foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))
                    if (!renderer.GetComponentInParent<LoftVisualChunks>() && renderer.isPartOfStaticBatch)
                        throw new InvalidOperationException($"Loft '{name}' has a statically batched overlay. Restart Play Mode or rebuild the map bundle.");
            if (m_GeneratedMesh != null && !m_GeneratedMesh.isReadable)
                throw new InvalidOperationException($"Loft '{name}' has no readable source mesh. Re-export with Prepare map for runtime editing.");
        }
        // Explicit opt-in. Normal map loading retains its baked geometry and never regenerates.
        public void PrepareRuntimeEditing()
        {
            if (!Application.isPlaying || m_RuntimeEditing) return;
            ValidateRuntimeEditing();
            // Modifier output/source meshes can be shared by bundles and duplicated lofts too.
            var copies = new Dictionary<Mesh, Mesh>();
            Mesh Copy(Mesh mesh)
            {
                if (!mesh) return null;
                if (!copies.TryGetValue(mesh, out var value))
                { value = Instantiate(mesh); copies.Add(mesh, value); m_RuntimeEditMeshes.Add(value); }
                return value;
            }
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
                if (component && component.GetType().Assembly == typeof(MultiSplineLoft).Assembly && !(component is LoftVisualChunks))
                    foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (field.FieldType == typeof(Mesh) && !field.IsInitOnly && (field.IsPublic || field.IsDefined(typeof(SerializeField), true)))
                            field.SetValue(component, Copy((Mesh)field.GetValue(component)));
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                // Unity replaces a batched filter with a combined world-space mesh.
                // The serialized GeneratedMesh remains the original local source.
                if (filter.gameObject != gameObject && !filter.GetComponentInParent<LoftVisualChunks>())
                    filter.sharedMesh = Copy(filter.sharedMesh);
            Mesh copy = m_GeneratedMesh != null ? m_GeneratedMesh : new Mesh();
            copy.name = name + " (runtime edit)";
            m_RuntimeEditMeshes.Add(copy);
            // Disable baked owners first so they restore their source renderer's state.
            foreach (var owner in m_VisualChunks)
                if (owner) { owner.gameObject.SetActive(false); owner.transform.SetParent(null); Destroy(owner.gameObject); }
            m_VisualChunks = Array.Empty<LoftVisualChunks>();
            m_VisualsBaked = false;
            Transform chunks = FindColliderChunksRoot();
            if (chunks)
                foreach (var collider in chunks.GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh)
                    {
                        var collisionCopy = Copy(collider.sharedMesh);
                        m_RuntimeEditMeshes.Add(collisionCopy); collider.sharedMesh = collisionCopy;
                    }
            m_RuntimeEditing = true;
            m_RegenerateQueued = m_FinalRegenerateQueued = m_RegenerateAsLivePreview = false;
            RemoveFromRegenerationQueue(this);
            SetGeneratedMesh(copy);
            m_RuntimeEditVisualMesh = copy;
            enabled = true;
            InvalidateVisualChunks();
        }

        public void RegenerateRuntimeEditing(bool preview = false)
        {
            PrepareRuntimeEditing();
            if (preview)
            {
                using (RuntimePreviewMarker.Auto())
                {
                    BeginRuntimePreview();
                    RegenerateScheduled(true);
                    m_RuntimePreviewRenderer.GetComponent<MeshFilter>().sharedMesh = m_GeneratedMesh;
                }
                return;
            }
            using (RuntimeFinalizeMarker.Auto())
            {
                EndRuntimePreview();
                Regenerate();
                // UVSpline may replace the rendered mesh. Only this verified local
                // output may be split while the old source renderer is still batched.
                m_RuntimeEditVisualMesh = GetComponent<MeshFilter>().sharedMesh;
                foreach (var collider in GetComponentsInChildren<MeshCollider>(true))
                    if (collider.sharedMesh) m_RuntimeEditMeshes.Add(collider.sharedMesh);
                BuildVisualChunks(false);
                if (m_VisualBuildFailed) throw new InvalidOperationException($"Loft '{name}' could not refresh its visual mesh.");
            }
        }
        void BeginRuntimePreview()
        {
            if (m_RuntimePreviewActive) return;
            ClearRuntimeVisuals();
            var source = GetComponent<MeshRenderer>();
            m_RuntimeSourceForcedOff = source.forceRenderingOff;
            if (!m_RuntimePreviewRenderer)
            {
                var go = new GameObject("Runtime Loft Preview") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false); go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>(); m_RuntimePreviewRenderer = go.AddComponent<MeshRenderer>();
            }
            LoftVisualMeshBuilder.CopyRenderer(source, m_RuntimePreviewRenderer);
            m_RuntimePreviewRenderer.forceRenderingOff = m_RuntimeSourceForcedOff;
            m_RuntimePreviewRenderer.gameObject.SetActive(true);
            source.forceRenderingOff = true;
            m_RuntimePreviewActive = true;
        }
        void EndRuntimePreview()
        {
            if (!m_RuntimePreviewActive) return;
            m_RuntimePreviewRenderer.gameObject.SetActive(false);
            GetComponent<MeshRenderer>().forceRenderingOff = m_RuntimeSourceForcedOff;
            m_RuntimePreviewActive = false;
        }
        void OnDestroy()
        {
            ClearSourceEvaluations();
            foreach (var mesh in m_RuntimeEditMeshes) if (mesh) Destroy(mesh);
            m_RuntimeEditMeshes.Clear();
        }
    }
}
