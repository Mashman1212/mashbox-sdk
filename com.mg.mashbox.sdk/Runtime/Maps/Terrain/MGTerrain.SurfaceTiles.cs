using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        [SerializeField] bool m_UseSurfaceTiles = true;
        [SerializeField, Min(8f)] float m_SurfaceTileSize = 32f;
        [NonSerialized] readonly List<SurfaceTile> m_SurfaceTiles = new List<SurfaceTile>();
        [NonSerialized] GameObject m_SurfaceTileRoot;
        [NonSerialized] Mesh m_TiledSource;
        [NonSerialized] int m_TiledVertexCount;
        [NonSerialized] int m_TiledSubMeshCount;
        [NonSerialized] float m_BuiltTileSize;
        [NonSerialized] Vector3 m_BuiltTileScale;
        [NonSerialized] bool m_SurfaceTilesDirty = true;
        [NonSerialized] bool m_MasterRenderingSuppressed;
        [NonSerialized] bool m_MasterWasForceRenderingOff;
        [NonSerialized] readonly List<Material> m_SurfaceTileMaterials = new List<Material>();
        [NonSerialized] readonly List<Material> m_PreviousTileMaterials = new List<Material>();
        [NonSerialized] MaterialPropertyBlock m_SurfaceTileProperties;
        [NonSerialized] readonly List<MaterialPropertyBlock> m_SurfaceTileSubMeshProperties = new List<MaterialPropertyBlock>();
        [NonSerialized] int m_SurfaceRendererState;
        [NonSerialized] bool m_HasSurfaceRendererState;
        [Serializable]
        public sealed class SurfaceColliderVertexMap
        {
            public MeshCollider collider;
            public int[] sourceIndices;
            public int[] holeSourceTriangles;
            public Mesh holeMesh;
            public SurfaceColliderVertexMap(MeshCollider collider, int[] sourceIndices)
            { this.collider = collider; this.sourceIndices = sourceIndices; holeSourceTriangles = collider != null && collider.sharedMesh != null ? collider.sharedMesh.triangles : null; }
        }
        [SerializeField, HideInInspector] SurfaceColliderVertexMap[] m_SurfaceColliderVertexMaps = Array.Empty<SurfaceColliderVertexMap>();
        [SerializeField, HideInInspector] int m_ColliderSourceVertexCount;
#if UNITY_EDITOR
        [NonSerialized] int m_SurfaceMeshDirtyCount;
#if UNITY_6000_0_OR_NEWER
        static readonly HashSet<MGTerrain> s_PickableTerrains = new HashSet<MGTerrain>();
        static Material s_SurfacePickingMaterial;

        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterSurfacePicking()
        {
            UnityEditor.HandleUtility.RegisterRenderPickingCallback(RenderSurfacePicking);
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (s_SurfacePickingMaterial != null) DestroyImmediate(s_SurfacePickingMaterial);
            };
        }

        static UnityEditor.RenderPickingResult RenderSurfacePicking(in UnityEditor.RenderPickingArgs args)
        {
            Camera camera = Camera.current;
            if (camera == null) return UnityEditor.RenderPickingResult.NoOperation;
            if (s_SurfacePickingMaterial == null)
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(
                    "Packages/com.mg.mashbox.sdk/Editor/Maps/MGTerrainPicking.shader");
                if (shader == null) return UnityEditor.RenderPickingResult.NoOperation;
                s_SurfacePickingMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var owners = new List<GameObject>();
            foreach (MGTerrain terrain in s_PickableTerrains)
            {
                if (terrain == null || !terrain.isActiveAndEnabled || !terrain.m_MasterRenderingSuppressed
                    || terrain.m_MasterWasForceRenderingOff || terrain.MeshRenderer == null || !terrain.MeshRenderer.enabled)
                    continue;
                GameObject owner = terrain.gameObject;
                var visibility = UnityEditor.SceneVisibilityManager.instance;
                if (!args.NeedToRenderForPicking(owner) || visibility.IsHidden(owner) || visibility.IsPickingDisabled(owner)
                    || visibility.IsHidden(terrain.MeshFilter.gameObject) || visibility.IsPickingDisabled(terrain.MeshFilter.gameObject)
                    || (camera.cullingMask & (1 << terrain.MeshFilter.gameObject.layer)) == 0
                    || UnityEditor.SceneManagement.StageUtility.GetStageHandle(owner) != UnityEditor.SceneManagement.StageUtility.GetCurrentStageHandle())
                    continue;
                // Draw the current surface only into Unity's picking buffer. The native
                // depth test handles occlusion; the retained hole faces are never drawn.
                s_SurfacePickingMaterial.SetColor("_SelectionID", UnityEditor.HandleUtility.EncodeSelectionId(args.pickingIndex + owners.Count));
                if (!s_SurfacePickingMaterial.SetPass(0)) continue;
                Mesh mesh = terrain.MeshFilter.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMeshNow(mesh, terrain.MeshFilter.transform.localToWorldMatrix, sub);
                owners.Add(owner);
            }
            return owners.Count == 0 ? UnityEditor.RenderPickingResult.NoOperation
                : new UnityEditor.RenderPickingResult(owners.Count, index => owners[index]);
        }
#endif
#endif

        sealed class SurfaceTile
        {
            internal Mesh mesh;
            internal MeshRenderer renderer;
            internal int[] sourceIndices;
        }

        sealed class SurfaceTileGroup
        {
            internal readonly Dictionary<int, int> remap = new Dictionary<int, int>();
            internal readonly List<int> sourceIndices = new List<int>();
            internal readonly List<int>[] triangles;
            internal SurfaceTileGroup(int subMeshes)
            {
                triangles = new List<int>[subMeshes];
                for (int i = 0; i < subMeshes; i++) triangles[i] = new List<int>();
            }
            internal void Add(int sourceIndex, int subMesh)
            {
                if (!remap.TryGetValue(sourceIndex, out int index))
                {
                    index = sourceIndices.Count;
                    remap.Add(sourceIndex, index);
                    sourceIndices.Add(sourceIndex);
                }
                triangles[subMesh].Add(index);
            }
        }

        public int SurfaceTileCount => m_SurfaceTiles.Count;
        public bool UsesSurfaceTiles => m_UseSurfaceTiles;
        public bool IsSurfaceRenderTile(MeshFilter filter) => filter != null && m_SurfaceTileRoot != null
            && filter.transform.parent == m_SurfaceTileRoot.transform;

        void LateUpdate() => RefreshSurfaceTiles();

        // The original MeshFilter stays the sole editable surface. These meshes are
        // disposable render caches, regenerated after reload and never painted directly.
        public void NotifySurfaceMeshChanged(bool topologyChanged = false)
        {
            m_HolePickTree = null;
            m_SurfaceTilesDirty = true;
            if (topologyChanged) m_TiledSource = null;
        }

        public void RefreshSurfaceTiles()
        {
            if (!isActiveAndEnabled) return;
            MeshFilter filter = MeshFilter;
            MeshRenderer master = MeshRenderer;
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (!m_UseSurfaceTiles || source == null || !source.isReadable || master == null)
            {
                ReleaseSurfaceTiles();
                return;
            }

            float size = Mathf.Max(8f, m_SurfaceTileSize);
            Vector3 scale = filter.transform.lossyScale;
            bool rebuild = source != m_TiledSource || source.vertexCount != m_TiledVertexCount
                || source.subMeshCount != m_TiledSubMeshCount || size != m_BuiltTileSize
                || scale != m_BuiltTileScale || m_SurfaceTileRoot == null;
#if UNITY_EDITOR
            int dirtyCount = UnityEditor.EditorUtility.GetDirtyCount(source);
            if (dirtyCount != m_SurfaceMeshDirtyCount) m_SurfaceTilesDirty = true;
#endif
            if (rebuild)
            {
                CacheSurfaceColliderVertexMaps(source);
                BuildSurfaceTiles(source, size, scale);
            }
            else if (m_SurfaceTilesDirty)
                UpdateSurfaceTileVertices(source);

            if (m_SurfaceTiles.Count == 0) return;
            if (!m_MasterRenderingSuppressed)
            {
                m_MasterWasForceRenderingOff = master.forceRenderingOff;
                m_MasterRenderingSuppressed = true;
            }
            master.forceRenderingOff = true;
            SyncSurfaceTileRenderers(master);
#if UNITY_EDITOR
            m_SurfaceMeshDirtyCount = dirtyCount;
#endif
        }

        void BuildSurfaceTiles(Mesh source, float size, Vector3 scale)
        {
            ReleaseSurfaceTiles();
            Vector3[] vertices = source.vertices;
            var groups = new Dictionary<Vector2Int, SurfaceTileGroup>();
            Vector3 origin = source.bounds.min;
            float sx = Mathf.Max(.0001f, Mathf.Abs(scale.x));
            float sz = Mathf.Max(.0001f, Mathf.Abs(scale.z));
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                if (source.GetTopology(subMesh) != MeshTopology.Triangles)
                    throw new InvalidOperationException("MG terrain surface tiles require triangle meshes.");
                int[] indices = source.GetTriangles(subMesh);
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 center = (vertices[indices[t]] + vertices[indices[t + 1]] + vertices[indices[t + 2]]) / 3f - origin;
                    var key = new Vector2Int(Mathf.FloorToInt(center.x * sx / size), Mathf.FloorToInt(center.z * sz / size));
                    if (!groups.TryGetValue(key, out SurfaceTileGroup group))
                        groups.Add(key, group = new SurfaceTileGroup(source.subMeshCount));
                    group.Add(indices[t], subMesh);
                    group.Add(indices[t + 1], subMesh);
                    group.Add(indices[t + 2], subMesh);
                }
            }
            if (groups.Count == 0) return;
            m_SurfaceTileRoot = new GameObject("MG Terrain Render Tiles") { hideFlags = HideFlags.HideAndDontSave };
            m_SurfaceTileRoot.transform.SetParent(MeshFilter.transform, false);
            foreach (var entry in groups)
            {
                SurfaceTileGroup group = entry.Value;
                var child = new GameObject($"Surface Tile {entry.Key.x},{entry.Key.y}") { hideFlags = HideFlags.HideAndDontSave };
                child.transform.SetParent(m_SurfaceTileRoot.transform, false);
                child.layer = MeshFilter.gameObject.layer;
                var mesh = new Mesh
                {
                    name = child.name,
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = group.sourceIndices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.MarkDynamic();
                mesh.vertices = Gather(vertices, group.sourceIndices.ToArray());
                mesh.subMeshCount = source.subMeshCount;
                for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                    mesh.SetTriangles(group.triangles[subMesh], subMesh, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var tile = new SurfaceTile { mesh = mesh, renderer = child.AddComponent<MeshRenderer>(), sourceIndices = group.sourceIndices.ToArray() };
                m_SurfaceTiles.Add(tile);
#if UNITY_EDITOR
                UnityEditor.SceneVisibilityManager.instance.DisablePicking(child, true);
#if UNITY_6000_0_OR_NEWER
                s_PickableTerrains.Add(this);
#endif
#endif
            }
            m_TiledSource = source;
            m_TiledVertexCount = source.vertexCount;
            m_TiledSubMeshCount = source.subMeshCount;
            m_BuiltTileSize = size;
            m_BuiltTileScale = scale;
            UpdateSurfaceTileVertices(source);
        }

        static T[] Gather<T>(T[] source, int[] indices)
        {
            if (source.Length == 0) return Array.Empty<T>();
            var result = new T[indices.Length];
            for (int i = 0; i < indices.Length; i++) result[i] = source[indices[i]];
            return result;
        }

        void UpdateSurfaceTileVertices(Mesh source)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Color[] colors = source.colors;
            var uvs = new Vector4[8][];
            var uvBuffer = new List<Vector4>();
            for (int channel = 0; channel < 8; channel++)
            {
                source.GetUVs(channel, uvBuffer);
                uvs[channel] = uvBuffer.ToArray();
            }
            foreach (SurfaceTile tile in m_SurfaceTiles)
            {
                tile.mesh.vertices = Gather(vertices, tile.sourceIndices);
                // Copy master normals/tangents rather than recalculating per tile:
                // shared boundary vertices must retain exactly the same shading.
                tile.mesh.normals = Gather(normals, tile.sourceIndices);
                tile.mesh.tangents = Gather(tangents, tile.sourceIndices);
                tile.mesh.colors = Gather(colors, tile.sourceIndices);
                for (int channel = 0; channel < 8; channel++)
                    tile.mesh.SetUVs(channel, Gather(uvs[channel], tile.sourceIndices));
                tile.mesh.RecalculateBounds();
                tile.mesh.UploadMeshData(false);
            }
            m_SurfaceTilesDirty = false;
            m_HasSurfaceRendererState = false;
        }

        void SyncSurfaceTileRenderers(MeshRenderer master)
        {
            master.GetSharedMaterials(m_SurfaceTileMaterials);
            bool materialsChanged = m_SurfaceTileMaterials.Count != m_PreviousTileMaterials.Count;
            for (int i = 0; !materialsChanged && i < m_SurfaceTileMaterials.Count; i++)
                materialsChanged = m_SurfaceTileMaterials[i] != m_PreviousTileMaterials[i];
            m_SurfaceTileProperties ??= new MaterialPropertyBlock();
            master.GetPropertyBlock(m_SurfaceTileProperties);
            bool hasPropertyBlocks = master.HasPropertyBlock();
            if (hasPropertyBlocks)
            {
                while (m_SurfaceTileSubMeshProperties.Count < m_SurfaceTileMaterials.Count)
                    m_SurfaceTileSubMeshProperties.Add(new MaterialPropertyBlock());
                for (int i = 0; i < m_SurfaceTileMaterials.Count; i++)
                    master.GetPropertyBlock(m_SurfaceTileSubMeshProperties[i], i);
            }
            float padding = 0f;
            foreach (Material material in m_SurfaceTileMaterials)
                if (material != null && material.HasProperty("_TessellationMaxDisplacement"))
                    padding = Mathf.Max(padding, Mathf.Abs(material.GetFloat("_TessellationMaxDisplacement")));
            int state = 17;
            AddSurfaceState(ref state, master.gameObject.layer);
            AddSurfaceState(ref state, master.enabled);
            AddSurfaceState(ref state, master.shadowCastingMode);
            AddSurfaceState(ref state, master.receiveShadows);
            AddSurfaceState(ref state, master.lightProbeUsage);
            AddSurfaceState(ref state, master.reflectionProbeUsage);
            AddSurfaceState(ref state, master.probeAnchor != null ? master.probeAnchor.GetInstanceID() : 0);
            AddSurfaceState(ref state, master.lightProbeProxyVolumeOverride != null ? master.lightProbeProxyVolumeOverride.GetInstanceID() : 0);
            AddSurfaceState(ref state, master.renderingLayerMask);
            AddSurfaceState(ref state, master.motionVectorGenerationMode);
            AddSurfaceState(ref state, master.allowOcclusionWhenDynamic);
            AddSurfaceState(ref state, master.rayTracingMode);
            AddSurfaceState(ref state, master.lightmapIndex);
            AddSurfaceState(ref state, master.lightmapScaleOffset);
            AddSurfaceState(ref state, master.realtimeLightmapIndex);
            AddSurfaceState(ref state, master.realtimeLightmapScaleOffset);
            AddSurfaceState(ref state, master.sortingLayerID);
            AddSurfaceState(ref state, master.sortingOrder);
            AddSurfaceState(ref state, padding);
            AddSurfaceState(ref state, m_SurfaceTileProperties.isEmpty);
            AddSurfaceState(ref state, hasPropertyBlocks);
            AddSurfaceState(ref state, m_DistantMorphTop);
            bool settingsChanged = !m_HasSurfaceRendererState || state != m_SurfaceRendererState;
            if (!settingsChanged && !materialsChanged && !hasPropertyBlocks) return;
            Vector3 scale = MeshFilter.transform.lossyScale;
            Vector3 localPadding = new Vector3(padding / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                padding / Mathf.Max(.0001f, Mathf.Abs(scale.y)), padding / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            foreach (SurfaceTile tile in m_SurfaceTiles)
            {
                MeshRenderer renderer = tile.renderer;
                if (renderer == null) continue;
                if (materialsChanged) renderer.SetSharedMaterials(m_SurfaceTileMaterials);
                renderer.SetPropertyBlock(m_SurfaceTileProperties.isEmpty ? null : m_SurfaceTileProperties);
                for (int i = 0; i < m_SurfaceTileMaterials.Count; i++)
                    renderer.SetPropertyBlock(hasPropertyBlocks && !m_SurfaceTileSubMeshProperties[i].isEmpty
                        ? m_SurfaceTileSubMeshProperties[i] : null, i);
                if (!settingsChanged) continue;
                renderer.gameObject.layer = master.gameObject.layer;
                renderer.enabled = master.enabled;
                renderer.forceRenderingOff = m_MasterWasForceRenderingOff;
                renderer.shadowCastingMode = master.shadowCastingMode;
                renderer.receiveShadows = master.receiveShadows;
                renderer.lightProbeUsage = master.lightProbeUsage;
                renderer.reflectionProbeUsage = master.reflectionProbeUsage;
                renderer.probeAnchor = master.probeAnchor;
                renderer.lightProbeProxyVolumeOverride = master.lightProbeProxyVolumeOverride;
                renderer.renderingLayerMask = master.renderingLayerMask;
                renderer.motionVectorGenerationMode = master.motionVectorGenerationMode;
                renderer.allowOcclusionWhenDynamic = master.allowOcclusionWhenDynamic;
                renderer.rayTracingMode = master.rayTracingMode;
                renderer.lightmapIndex = master.lightmapIndex;
                renderer.lightmapScaleOffset = master.lightmapScaleOffset;
                renderer.realtimeLightmapIndex = master.realtimeLightmapIndex;
                renderer.realtimeLightmapScaleOffset = master.realtimeLightmapScaleOffset;
                renderer.sortingLayerID = master.sortingLayerID;
                renderer.sortingOrder = master.sortingOrder;
                Bounds bounds = tile.mesh.bounds;
                bounds.Expand(localPadding * 2f);
                renderer.localBounds = ExpandDistantMorphBounds(bounds);
            }
            m_SurfaceRendererState = state;
            m_HasSurfaceRendererState = true;
            if (materialsChanged)
            {
                m_PreviousTileMaterials.Clear();
                m_PreviousTileMaterials.AddRange(m_SurfaceTileMaterials);
            }
        }

        static void AddSurfaceState<T>(ref int hash, T value) where T : struct
        {
            unchecked { hash = hash * 31 + value.GetHashCode(); }
        }

        public void SetSurfaceColliderVertexMaps(SurfaceColliderVertexMap[] maps, int sourceVertexCount)
        {
            m_SurfaceColliderVertexMaps = maps;
            m_ColliderSourceVertexCount = sourceVertexCount;
        }

        void CacheSurfaceColliderVertexMaps(Mesh source)
        {
            if (m_SurfaceColliderChunks.Length == 0 || m_SurfaceColliderVertexMaps.Length != 0) return;
            // Older conversions did not save source indices. Recover them once from
            // the unchanged surface, before the first sculpt stroke replaces its mesh.
            var lookup = new Dictionary<Vector3, int>();
            Vector3[] vertices = source.vertices;
            for (int i = 0; i < vertices.Length; i++)
                lookup[MeshFilter.transform.TransformPoint(vertices[i])] = i;
            var maps = new List<SurfaceColliderVertexMap>();
            foreach (MeshCollider collider in m_SurfaceColliderChunks)
            {
                if (collider == null || collider.sharedMesh == null || !collider.sharedMesh.isReadable) return;
                Vector3[] colliderVertices = collider.sharedMesh.vertices;
                var indices = new int[colliderVertices.Length];
                for (int i = 0; i < indices.Length; i++)
                    if (!lookup.TryGetValue(collider.transform.TransformPoint(colliderVertices[i]), out indices[i])) return;
                maps.Add(new SurfaceColliderVertexMap(collider, indices));
            }
            SetSurfaceColliderVertexMaps(maps.ToArray(), source.vertexCount);
        }

        public void RefreshSurfaceCollidersFromMesh()
        {
            Mesh source = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (source == null || !source.isReadable || m_SurfaceColliderChunks.Length == 0) return;
            CacheSurfaceColliderVertexMaps(source);
            if (m_SurfaceColliderVertexMaps.Length != m_SurfaceColliderChunks.Length || m_ColliderSourceVertexCount != source.vertexCount)
            {
                // A topology edit invalidates old index maps. Keep collision correct
                // using the master until the user rebuilds the child colliders.
                if (MeshCollider != null)
                {
                    foreach (MeshCollider chunk in m_SurfaceColliderChunks) if (chunk != null) chunk.enabled = false;
                    MeshCollider.sharedMesh = null;
                    MeshCollider.sharedMesh = source;
                    MeshCollider.enabled = true;
                }
                return;
            }
            Vector3[] vertices = source.vertices;
            foreach (SurfaceColliderVertexMap map in m_SurfaceColliderVertexMaps)
            {
                if (map.collider == null || map.collider.sharedMesh == null) continue;
                Mesh mesh = map.collider.sharedMesh;
                Vector3[] previous = mesh.vertices;
                Vector3[] updated = new Vector3[map.sourceIndices.Length];
                bool changed = previous.Length != updated.Length;
                for (int i = 0; i < updated.Length; i++)
                {
                    updated[i] = map.collider.transform.InverseTransformPoint(MeshFilter.transform.TransformPoint(vertices[map.sourceIndices[i]]));
                    changed |= i >= previous.Length || !updated[i].Equals(previous[i]);
                }
                if (!changed) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.Undo.RecordObject(mesh, "Update Terrain Child Collider");
#endif
                mesh.vertices = updated;
                mesh.RecalculateBounds();
                map.collider.sharedMesh = null;
                map.collider.sharedMesh = mesh;
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(mesh);
#endif
            }
            // Seam sculpting temporarily uses the master. Once the saved chunks
            // have been refreshed, hand collision back to them (including holes).
            TryUseSurfaceColliderChunks();
            Physics.SyncTransforms();
        }

        void ReleaseSurfaceTiles()
        {
            if (m_MasterRenderingSuppressed && m_MeshRenderer != null)
                m_MeshRenderer.forceRenderingOff = m_MasterWasForceRenderingOff;
            m_MasterRenderingSuppressed = false;
            foreach (SurfaceTile tile in m_SurfaceTiles)
            {
                if (tile.renderer != null)
                {
#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
                    s_PickableTerrains.Remove(this);
#endif
                    tile.renderer.enabled = false;
                }
                if (tile.mesh == null) continue;
                if (Application.isPlaying) Destroy(tile.mesh); else DestroyImmediate(tile.mesh);
            }
            m_SurfaceTiles.Clear();
            if (m_SurfaceTileRoot != null)
            {
                m_SurfaceTileRoot.SetActive(false);
                if (Application.isPlaying) Destroy(m_SurfaceTileRoot); else DestroyImmediate(m_SurfaceTileRoot);
            }
            m_SurfaceTileRoot = null;
            m_TiledSource = null;
            m_PreviousTileMaterials.Clear();
            m_HasSurfaceRendererState = false;
        }

#if UNITY_EDITOR
        void OnSurfaceTilesUndoRedo()
        {
            NotifySurfaceMeshChanged(true);
        }
#endif
    }
}
