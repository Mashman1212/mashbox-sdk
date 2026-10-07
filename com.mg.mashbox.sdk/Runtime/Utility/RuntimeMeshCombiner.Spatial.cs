using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class RuntimeMeshCombiner
{
    [Tooltip("Combine nearby geometry per material, retaining spatial culling. Disable only for small objects that are always visible together.")]
    [SerializeField] private bool useSpatialChunks = true;
    [Tooltip("World-space cell size in metres. Large sources are split into triangle sections before combining.")]
    [SerializeField, Min(1f)] private float spatialChunkSize = 64f;
    [Tooltip("Maximum copied vertices per output mesh. Large sources are split to fit. Keeps output buffers bounded and compatible with 16-bit indices.")]
    [SerializeField, Range(1024, 65535)] private int maxVerticesPerChunk = 60000;

    public string LastSpatialCombineSummary { get; private set; }

    private readonly List<Mesh> spatialMeshes = new List<Mesh>();

    private sealed class SpatialBatch
    {
        public Material Material;
        public MeshRenderer Template;
        public readonly List<CombineInstance> Instances = new List<CombineInstance>();
        public int Vertices;
        public Bounds WorldBounds;
    }

    private void ClearSpatialMeshes()
    {
        foreach (Mesh mesh in spatialMeshes)
            DestroyObject(mesh);
        spatialMeshes.Clear();
    }

    private string SpatialRetentionReason(MeshFilter filter, MeshRenderer renderer)
    {
        Mesh mesh = filter.sharedMesh;
        if (!mesh.isReadable) return "unreadable meshes";
        if (mesh.vertexCount == 0 || mesh.subMeshCount == 0) return "empty meshes";
        if (renderer.isPartOfStaticBatch) return "already statically batched";
        if (renderer.HasPropertyBlock()) return "material property overrides";
        if ((renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534) ||
            (renderer.realtimeLightmapIndex >= 0 && renderer.realtimeLightmapIndex < 65534)) return "lightmapped sources";
        if (renderer.GetComponentInParent<LODGroup>() != null) return "LOD-controlled sources";
        if (mesh.blendShapeCount != 0 || mesh.HasVertexAttribute(VertexAttribute.BlendWeight)) return "deformed meshes";
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length != mesh.subMeshCount) return "mismatched material slots";
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (materials[i] == null) return "missing materials";
            if (materials[i].renderQueue >= (int)RenderQueue.Transparent) return "transparent materials";
            if (mesh.GetTopology(i) != MeshTopology.Triangles || mesh.GetIndexCount(i) == 0) return "non-triangle or empty submeshes";
        }
        return null;
    }

    private static bool SameRendererSettings(MeshRenderer a, MeshRenderer b)
    {
        return a.gameObject.layer == b.gameObject.layer &&
            a.shadowCastingMode == b.shadowCastingMode && a.receiveShadows == b.receiveShadows &&
            a.lightProbeUsage == b.lightProbeUsage && a.reflectionProbeUsage == b.reflectionProbeUsage &&
            a.probeAnchor == b.probeAnchor && a.lightProbeProxyVolumeOverride == b.lightProbeProxyVolumeOverride &&
            a.motionVectorGenerationMode == b.motionVectorGenerationMode && a.renderingLayerMask == b.renderingLayerMask &&
            a.allowOcclusionWhenDynamic == b.allowOcclusionWhenDynamic &&
            a.sortingLayerID == b.sortingLayerID && a.sortingOrder == b.sortingOrder &&
            a.rendererPriority == b.rendererPriority;
    }

    private void CombineSpatially()
    {
        float cellSize = float.IsNaN(spatialChunkSize) || float.IsInfinity(spatialChunkSize)
            ? 64f : Mathf.Max(1f, spatialChunkSize);
        int vertexLimit = Mathf.Clamp(maxVerticesPerChunk, 1024, 65535);
        var groups = new Dictionary<(Vector3Int, Material), List<SpatialBatch>>();
        var batches = new List<SpatialBatch>();
        int retained = 0;
        int splitSources = 0;
        var temporaryMeshes = new List<Mesh>();
        var retentionReasons = new Dictionary<string, int>();
        void Retain(string reason)
        {
            retained++;
            retentionReasons.TryGetValue(reason, out int count);
            retentionReasons[reason] = count + 1;
        }
        string RetainedSummary() => string.Join(", ", System.Linq.Enumerable.Select(retentionReasons, pair => $"{pair.Value} {pair.Key}"));
        long expectedIndices = 0;
        try
        {
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(includeInactiveChildren))
            {
                if (!ShouldUseMeshFilter(filter)) continue;
                Mesh mesh = filter.sharedMesh;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                string reason = SpatialRetentionReason(filter, renderer);
                if (reason != null) { Retain(reason); continue; }
                Material[] materials = renderer.sharedMaterials;
                List<SourceSection> sections;
                if (mesh.vertexCount > vertexLimit || !FitsSpatialBounds(renderer.bounds, cellSize))
                {
                    sections = SplitSpatialSource(mesh, filter.transform.localToWorldMatrix, cellSize, vertexLimit, temporaryMeshes);
                    if (sections == null) { Retain("individual triangles wider than a cell"); continue; }
                    splitSources++;
                }
                else
                {
                    sections = new List<SourceSection>();
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        sections.Add(new SourceSection { Mesh = mesh, Submesh = submesh,
                            Cell = SpatialCell(renderer.bounds.center, cellSize), WorldBounds = renderer.bounds });
                }
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    expectedIndices += mesh.GetIndexCount(submesh);
                foreach (SourceSection section in sections)
                {
                    bool split = section.Mesh != mesh;
                    Mesh input = section.Mesh;
                    int inputSubmesh = split ? 0 : section.Submesh;
                    var key = (section.Cell, materials[section.Submesh]);
                    if (!groups.TryGetValue(key, out var candidates))
                    {
                        candidates = new List<SpatialBatch>();
                        groups.Add(key, candidates);
                    }
                    SpatialBatch batch = null;
                    for (int i = candidates.Count - 1; i >= 0; i--)
                    {
                        SpatialBatch candidate = candidates[i];
                        Bounds joined = candidate.WorldBounds;
                        joined.Encapsulate(section.WorldBounds);
                        if (candidate.Vertices + input.vertexCount <= vertexLimit &&
                            FitsSpatialBounds(joined, cellSize * 2f) && SameRendererSettings(candidate.Template, renderer))
                        {
                            batch = candidate;
                            break;
                        }
                    }
                    if (batch == null)
                    {
                        batch = new SpatialBatch { Material = materials[section.Submesh], Template = renderer, WorldBounds = section.WorldBounds };
                        candidates.Add(batch);
                        batches.Add(batch);
                    }
                    batch.Instances.Add(new CombineInstance {
                        mesh = input, subMeshIndex = inputSubmesh,
                        transform = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    });
                    batch.Vertices += input.vertexCount;
                    batch.WorldBounds.Encapsulate(section.WorldBounds);
                }
                sourceRenderers.Add(renderer);
                sourceRendererEnabledStates.Add(renderer.enabled);
            }

            if (batches.Count == 0)
            {
                LastSpatialCombineSummary = $"No eligible spatial batches. Retained {retained} sources: {RetainedSummary()}.";
                Debug.Log($"RuntimeMeshCombiner on '{name}': {LastSpatialCombineSummary}", this);
                return;
            }
            combinedObject = new GameObject(combinedObjectName);
            combinedObject.transform.SetParent(transform, false);
            long actualIndices = 0;
            long vertices = 0;
            foreach (SpatialBatch batch in batches)
            {
                var mesh = new Mesh { name = $"{name} Chunk {spatialMeshes.Count}", indexFormat = IndexFormat.UInt16 };
                spatialMeshes.Add(mesh);
                // One material and one combine pass: no intermediate full-size
                // material meshes and no duplicated mountain-wide shadow proxy.
                mesh.CombineMeshes(batch.Instances.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                if (mesh.vertexCount == 0 || mesh.vertexCount > vertexLimit || mesh.GetIndexCount(0) == 0)
                    throw new InvalidOperationException("Spatial combine produced an empty or oversized mesh.");
                actualIndices += mesh.GetIndexCount(0);
                vertices += mesh.vertexCount;
                GameObject child = new GameObject($"Chunk {spatialMeshes.Count - 1} - {batch.Material.name}");
                child.transform.SetParent(combinedObject.transform, false);
                child.layer = batch.Template.gameObject.layer;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer output = child.AddComponent<MeshRenderer>();
                output.sharedMaterial = batch.Material;
                sourceRendererTemplate = batch.Template;
                ApplyRendererSettings(output);
                output.probeAnchor = batch.Template.probeAnchor;
                output.lightProbeProxyVolumeOverride = batch.Template.lightProbeProxyVolumeOverride;
                output.allowOcclusionWhenDynamic = batch.Template.allowOcclusionWhenDynamic;
                output.sortingLayerID = batch.Template.sortingLayerID;
                output.sortingOrder = batch.Template.sortingOrder;
                output.rendererPriority = batch.Template.rendererPriority;
                if (addMeshCollider) child.AddComponent<MeshCollider>().sharedMesh = mesh;
                else if (Application.isPlaying) mesh.UploadMeshData(true);
            }
            if (actualIndices != expectedIndices)
                throw new InvalidOperationException("Spatial combine changed the source index count.");
            if (disableSourceRenderers)
                foreach (MeshRenderer renderer in sourceRenderers) renderer.enabled = false;
            SetCombinedVisibility(UseCombinedRendering && enabled);
            LastSpatialCombineSummary = $"Combined {sourceRenderers.Count} sources into {batches.Count} chunks; split {splitSources} large sources. {vertices} vertices / {actualIndices / 3} triangles. Retained {retained} sources" +
                (retained > 0 ? $": {RetainedSummary()}." : ".");
            Debug.Log($"RuntimeMeshCombiner on '{name}': {LastSpatialCombineSummary}", this);
        }
        catch (Exception exception)
        {
            // Commit source visibility only after every chunk succeeds. Any failure
            // discards partial outputs and keeps the original mountain visible.
            ClearCombinedObject();
            RestoreSourceRenderers();
            LastSpatialCombineSummary = "Combine failed; original geometry retained: " + exception.Message;
            Debug.LogError($"RuntimeMeshCombiner on '{name}' retained original geometry after a spatial combine failure: {exception.Message}", this);
        }
        finally
        {
            sourceRendererTemplate = null;
            foreach (Mesh mesh in temporaryMeshes) DestroyObject(mesh);
        }
    }
}
