using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MashBoxSDK.Maps.Spline
{
    public sealed partial class MultiSplineLoft
    {
        [SerializeField, Min(1f), Tooltip("Maximum draw distance from each visual chunk's bounding sphere, in metres. Visual sections use Collider Chopping Distance, even with colliders disabled.")]
        float m_VisualCullDistance = 150f;
        [SerializeField, HideInInspector] float m_RenderUvAlongPerMeter;
        [SerializeField, HideInInspector] bool m_VisualsBaked;
        [SerializeField, HideInInspector] LoftVisualChunks[] m_VisualChunks = Array.Empty<LoftVisualChunks>();
        int m_VisualGeneration = -1;
        Mesh m_LastVisualMesh;
        bool m_VisualBuildFailed;

        public float VisualCullDistance
        {
            get => m_VisualCullDistance;
            set { m_VisualCullDistance = Mathf.Max(1f, value); InvalidateVisualChunks(); }
        }
        public bool VisualsBaked => m_VisualsBaked;
        public void InvalidateVisualChunks() { m_VisualGeneration = -1; m_VisualBuildFailed = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallDisabledLoftVisuals()
        {
            SceneManager.sceneLoaded -= PrepareDisabledLofts;
            SceneManager.sceneLoaded += PrepareDisabledLofts;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                PrepareDisabledLofts(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        static void PrepareDisabledLofts(Scene scene, LoadSceneMode mode)
        {
            if (!scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MultiSplineLoft loft in root.GetComponentsInChildren<MultiSplineLoft>(true))
                    if (!loft.enabled && !loft.m_VisualsBaked)
                        try { loft.BuildVisualChunks(false); }
                        catch (Exception exception) { Debug.LogException(exception, loft); }
        }

#if UNITY_EDITOR
        public void ReleaseEditorVisualChunks()
        {
            if (m_VisualsBaked) return;
            foreach (LoftVisualChunks owner in m_VisualChunks)
                if (owner != null)
                {
                    owner.gameObject.SetActive(false);
                    foreach (MeshFilter filter in owner.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null) DestroyImmediate(filter.sharedMesh);
                    DestroyImmediate(owner.gameObject);
                }
            m_VisualChunks = Array.Empty<LoftVisualChunks>();
            InvalidateVisualChunks();
        }
#endif

        void LateUpdate()
        {
            if (!Application.isPlaying || m_VisualsBaked || m_VisualBuildFailed) return;
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            if (m_VisualGeneration == m_GenerationVersion && mesh == m_LastVisualMesh) return;
            try { BuildVisualChunks(false); }
            catch (Exception exception)
            {
                m_VisualBuildFailed = true;
                Debug.LogError($"Could not split loft visuals for '{name}': {exception.Message}", this);
            }
        }

        void ClearRuntimeVisuals()
        {
            if (m_VisualsBaked) return;
            foreach (LoftVisualChunks chunks in m_VisualChunks)
                if (chunks != null)
                {
                    chunks.gameObject.SetActive(false); // Restore source before the replacement is copied.
                    LoftVisualMeshBuilder.DestroyGenerated(chunks.gameObject);
                }
            m_VisualChunks = Array.Empty<LoftVisualChunks>();
            InvalidateVisualChunks();
        }

        /// <summary>Build only on the build pipeline's scene copy, or temporarily in Play Mode.</summary>
        public void BuildVisualChunks(bool bake)
        {
            if (m_VisualsBaked) return;
            ClearRuntimeVisuals();
            MeshRenderer rootRenderer = GetComponent<MeshRenderer>();
            var sources = new List<MeshRenderer> { rootRenderer };
            LoftHeightOverlayModifier overlay = HeightOverlayModifier;
            if (overlay != null)
                foreach (MeshRenderer renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.GetComponentInParent<LoftVisualChunks>() == null && renderer != rootRenderer)
                        sources.Add(renderer);

            var built = new List<LoftVisualChunks>();
            float uvPerMeter = ResolveVisualUvDensity();
            try
            {
                foreach (MeshRenderer source in sources)
                {
                    MeshFilter filter = source.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount == 0) continue;
                    float[] distances = GetVisualAlongDistances(mesh, source == rootRenderer, uvPerMeter);
                    List<Mesh> meshes = LoftVisualMeshBuilder.Split(mesh, distances, Mathf.Max(1f, m_ColliderChunkLength));
                    GameObject root = new GameObject("Visual Chunks");
                    root.SetActive(false);
                    root.transform.SetParent(source.transform, false);
                    root.layer = source.gameObject.layer;
                    root.hideFlags = bake ? HideFlags.None : HideFlags.DontSave;
                    LoftVisualChunks owner = root.AddComponent<LoftVisualChunks>();
                    built.Add(owner);
                    var renderers = new List<MeshRenderer>();
                    try
                    {
                        foreach (Mesh chunk in meshes)
                        {
                            chunk.hideFlags = bake ? HideFlags.None : HideFlags.DontSave;
                            var child = new GameObject(chunk.name);
                            child.transform.SetParent(root.transform, false);
                            child.layer = source.gameObject.layer;
                            child.tag = source.gameObject.tag;
                            child.AddComponent<MeshFilter>().sharedMesh = chunk;
                            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                            LoftVisualMeshBuilder.CopyRenderer(source, renderer);
                            renderers.Add(renderer);
                        }
                        owner.Configure(new[] { new LoftVisualChunks.Source
                        {
                            renderer = source, wasForcedOff = source.forceRenderingOff, wasEnabled = source.enabled, chunks = renderers.ToArray()
                        } }, m_VisualCullDistance, bake);
                        if (bake) source.enabled = false;
                    }
                    catch
                    {
                        foreach (Mesh chunk in meshes) LoftVisualMeshBuilder.DestroyGenerated(chunk);
                        throw;
                    }
                }
                m_VisualChunks = built.ToArray();
                foreach (LoftVisualChunks owner in built) owner.gameObject.SetActive(true);
                m_VisualsBaked = bake;
                m_VisualGeneration = m_GenerationVersion;
                m_LastVisualMesh = GetComponent<MeshFilter>().sharedMesh;
                if (bake)
                {
                    PreserveBakedColliderMeshes();
                    RemoveFromRegenerationQueue(this);
                }
            }
            catch
            {
                foreach (LoftVisualChunks owner in built)
                    if (owner != null)
                    {
                        owner.gameObject.SetActive(false);
                        // A failed build must also release meshes configured as baked.
                        foreach (MeshFilter filter in owner.GetComponentsInChildren<MeshFilter>(true))
                            LoftVisualMeshBuilder.DestroyGenerated(filter.sharedMesh);
                        LoftVisualMeshBuilder.DestroyGenerated(owner.gameObject);
                    }
                throw;
            }
        }

        // Baked lofts do not regenerate at runtime. Their collision meshes must be
        // serialized too; authoring chunks use DontSave and would otherwise vanish.
        void PreserveBakedColliderMeshes()
        {
            var copies = new Dictionary<Mesh, Mesh>();
            foreach (MeshCollider collider in GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.GetComponentInParent<MultiSplineLoft>() != this) continue;
                Mesh source = collider.sharedMesh;
                if (source == null || (source.hideFlags &
                    (HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild)) == 0) continue;
                if (!copies.TryGetValue(source, out Mesh baked))
                {
                    baked = Instantiate(source);
                    baked.name = source.name;
                    baked.hideFlags = HideFlags.None;
                    copies.Add(source, baked);
                }
                // Clone instead of changing authoring mesh flags on the shared source.
                // No collider or GameObject activation depends on visual visibility.
                collider.sharedMesh = baked;
            }
        }
        float ResolveVisualUvDensity()
        {
            if (m_AlongDistances.Count > 1) return Mathf.Max(0.0001f, CurrentUvAlongPerMeter);
            if (m_RenderUvAlongPerMeter > 0f) return m_RenderUvAlongPerMeter;
            // Migration for scenes saved before the density snapshot existed. Sample their
            // authored centreline without regenerating or modifying their saved mesh.
            float length = 0f;
            if (TryEvaluateResolutionCenterline(0f, out Vector3 previous))
                for (int i = 1; i <= 512; i++)
                    if (TryEvaluateResolutionCenterline(i / 512f, out Vector3 next))
                    {
                        length += Vector3.Distance(transform.InverseTransformPoint(previous), transform.InverseTransformPoint(next));
                        previous = next;
                    }
            Vector2[] uv = m_GeneratedMesh != null ? m_GeneratedMesh.uv : Array.Empty<Vector2>();
            float maximum = 0f;
            foreach (Vector2 value in uv) maximum = Mathf.Max(maximum, value.y);
            if (length > 0f && maximum > 0f) return maximum / length;
            if (m_GeneratedMesh == null || m_GeneratedMesh.vertexCount == 0) return 1f;
            throw new InvalidOperationException("Rebuild this loft once to record its visual section distances.");
        }

        float[] GetVisualAlongDistances(Mesh mesh, bool mainVisual, float uvPerMeter)
        {
            Vector3[] vertices = mesh.vertices;
            var result = new float[vertices.Length];
            if (!mainVisual || mesh == m_GeneratedMesh)
            {
                Vector2[] uv = mesh.uv;
                if (uv.Length != vertices.Length) throw new InvalidOperationException("Loft source UV0 is missing.");
                for (int i = 0; i < result.Length; i++) result[i] = uv[i].y / uvPerMeter;
                return result;
            }
            // UVSpline may duplicate seam vertices and replace UV0. Recover the original
            // along coordinates by position so edited texture UVs cannot change partitions.
            Vector3[] originalVertices = m_GeneratedMesh.vertices;
            Vector2[] originalUv = m_GeneratedMesh.uv;
            if (originalUv.Length != originalVertices.Length)
                throw new InvalidOperationException("Loft source UV0 is missing.");
            var byPosition = new Dictionary<Vector3, float>();
            for (int i = 0; i < originalVertices.Length; i++)
                if (!byPosition.ContainsKey(originalVertices[i])) byPosition.Add(originalVertices[i], originalUv[i].y / uvPerMeter);
            for (int i = 0; i < vertices.Length; i++)
            {
                // Unchanged prefix indices preserve closed-loop seam coordinates.
                if (i < originalVertices.Length && vertices[i] == originalVertices[i]) result[i] = originalUv[i].y / uvPerMeter;
                else if (!byPosition.TryGetValue(vertices[i], out result[i]))
                    throw new InvalidOperationException("The rendered UV mesh is stale. Rebuild the loft before exporting.");
            }
            return result;
        }
    }
}
