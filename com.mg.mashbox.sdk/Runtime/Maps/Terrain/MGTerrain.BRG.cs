#if UNITY_6000_0_OR_NEWER

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        const int BrgPackedMatrixBytes = 48;
        const int BrgZeroPrefixBytes = 96;
        int m_DetailDefinitionCapacity;
        int DetailDataAddress => BrgZeroPrefixBytes + m_DetailBrgCapacity * BrgPackedMatrixBytes * 2;
        int DetailTableAddress => DetailDataAddress + m_DetailBrgCapacity * 16;
        Vector4[] m_DetailInstanceShaderData;

        void UploadDetailDefinitions()
        {
            var data = new Vector4[1 + m_DensityDetailLayers.Count * 2];
            data[0] = new Vector4(m_DensityDetailLayers.Count, 0, 0, 0);
            for (int i = 0; i < m_DensityDetailLayers.Count; i++)
            {
                var layer = m_DensityDetailLayers[i];
                data[1 + i * 2] = layer != null ? (Vector4)layer.ShaderTint : Vector4.one;
                data[2 + i * 2] = layer != null ? layer.ShaderDefinition : new Vector4(0, 1, 0, 0);
            }
            m_DetailBrgInstanceBuffer.SetData(data, 0, DetailTableAddress / 16, data.Length);
        }
        static readonly Unity.Profiling.ProfilerMarker s_GpuPrepareMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.GpuPrepare");
        static readonly Unity.Profiling.ProfilerMarker s_BrgCullingMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.BrgCulling");

        [StructLayout(LayoutKind.Sequential)]
        struct BrgPackedMatrix
        {
            internal float c0x, c0y, c0z;
            internal float c1x, c1y, c1z;
            internal float c2x, c2y, c2z;
            internal float c3x, c3y, c3z;

            internal BrgPackedMatrix(Matrix4x4 matrix)
            {
                c0x = matrix.m00;
                c0y = matrix.m10;
                c0z = matrix.m20;
                c1x = matrix.m01;
                c1y = matrix.m11;
                c1z = matrix.m21;
                c2x = matrix.m02;
                c2y = matrix.m12;
                c2z = matrix.m22;
                c3x = matrix.m03;
                c3y = matrix.m13;
                c3z = matrix.m23;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GpuDetailSpawnCommand
        {
            internal uint x;
            internal uint z;
            internal uint count;
            internal uint outputStart;
            internal uint layerIndex;
            internal uint padding0;
            internal uint padding1;
            internal uint padding2;
            internal Vector4 sizes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GpuDetailLayerParameters
        {
            internal uint width;
            internal uint height;
            internal uint seed;
            internal uint padding0;
            internal float minWidth;
            internal float maxWidth;
            internal float minHeight;
            internal float maxHeight;
            internal float yOffset;
            internal float padding1;
            internal float padding2;
            internal float padding3;
        }

        sealed class GpuProceduralBuildGroup
        {
            internal DrawBatch batch;
            internal ShadowCastingMode shadowCasting;
            internal Matrix4x4 relativeMatrix;
            internal readonly List<GpuDetailSpawnCommand> commands = new List<GpuDetailSpawnCommand>();
            internal int outputCount;
            internal int outputBase;
            internal int commandOffset;
            internal bool active;
            internal readonly List<GpuCellRange> ranges = new List<GpuCellRange>();
            internal readonly List<GpuCellRange> previousRanges = new List<GpuCellRange>();
            internal int previousOutputBase;
            internal int generation;
            internal Matrix4x4 previousRelativeMatrix;
        }

        readonly struct GpuCellRange
        {
            internal readonly DensityDetailChunk chunk;
            internal readonly int count;
            internal readonly int start;

            internal GpuCellRange(DensityDetailChunk chunk, int count, int start)
            {
                this.chunk = chunk;
                this.count = count;
                this.start = start;
            }
        }

        sealed class BrgBuildGroup
        {
            internal DrawBatch batch;
            internal ShadowCastingMode shadowCasting;
            internal readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
            internal readonly List<Vector4> shaderData = new List<Vector4>();
            internal bool active;
        }

        readonly struct BrgPreparedGroup
        {
            internal readonly BatchMeshID meshId;
            internal readonly BatchMaterialID materialId;
            internal readonly ushort subMesh;
            internal readonly uint visibleOffset;
            internal readonly uint visibleCount;
            internal readonly ShadowCastingMode shadowCasting;
            internal readonly bool receiveShadows;

            internal BrgPreparedGroup(
                BatchMeshID meshId,
                BatchMaterialID materialId,
                int subMesh,
                int visibleOffset,
                int visibleCount,
                ShadowCastingMode shadowCasting,
                bool receiveShadows)
            {
                this.meshId = meshId;
                this.materialId = materialId;
                this.subMesh = (ushort)Mathf.Clamp(subMesh, 0, ushort.MaxValue);
                this.visibleOffset = (uint)visibleOffset;
                this.visibleCount = (uint)visibleCount;
                this.shadowCasting = shadowCasting;
                this.receiveShadows = receiveShadows;
            }
        }

        [NonSerialized] BatchRendererGroup m_DetailBrg;
        [NonSerialized] GraphicsBuffer m_DetailBrgInstanceBuffer;
        [NonSerialized] BatchID m_DetailBrgBatchId;
        [NonSerialized] int m_DetailBrgCapacity;
        [NonSerialized] int m_DetailBrgVisibleCount;
        [NonSerialized] int m_DetailBrgLogicalCount;
        [NonSerialized] int m_DetailBrgSignature;
        [NonSerialized] bool m_DetailBrgHasSignature;
        [NonSerialized] bool m_DetailBrgUnavailable;
        [NonSerialized] bool m_DetailBrgFailureReported;
        [NonSerialized] BrgPackedMatrix[] m_DetailBrgObjectToWorld;
        [NonSerialized] BrgPackedMatrix[] m_DetailBrgWorldToObject;
        [NonSerialized] NativeArray<int> m_DetailBrgSequentialVisibleIndices;
        [NonSerialized] readonly Matrix4x4[] m_DetailBrgZero = { Matrix4x4.zero };
        [NonSerialized] readonly Dictionary<Mesh, BatchMeshID> m_DetailBrgMeshIds = new Dictionary<Mesh, BatchMeshID>();
        [NonSerialized] readonly Dictionary<Material, BatchMaterialID> m_DetailBrgMaterialIds = new Dictionary<Material, BatchMaterialID>();
        [NonSerialized] readonly Dictionary<DenseDetailBatchKey, BrgBuildGroup> m_DetailBrgBuildGroups = new Dictionary<DenseDetailBatchKey, BrgBuildGroup>();
        [NonSerialized] readonly List<BrgBuildGroup> m_ActiveDetailBrgBuildGroups = new List<BrgBuildGroup>();
        [NonSerialized] readonly List<BrgPreparedGroup> m_DetailBrgPreparedGroups = new List<BrgPreparedGroup>();
        [NonSerialized] ComputeShader m_DetailGpuGenerator;
        [NonSerialized] int m_DetailGpuGeneratorKernel = -1;
        [NonSerialized] bool m_DetailGpuGenerationUnavailable;
        [NonSerialized] bool m_DetailBrgUsesGpuGeneration;
        [NonSerialized] int m_DetailGpuGeneration;
        public int LastRegeneratedDetailInstances { get; private set; }
        [NonSerialized] GraphicsBuffer m_DetailGpuCommandBuffer;
        [NonSerialized] GraphicsBuffer m_DetailGpuLayerBuffer;
        [NonSerialized] GraphicsBuffer m_DetailGpuSurfaceHeightBuffer;
        [NonSerialized] Mesh m_DetailGpuSurfaceMesh;
        [NonSerialized] int m_DetailGpuCommandCapacity;
        [NonSerialized] int m_DetailGpuLayerCapacity;
        [NonSerialized] int m_DetailGpuSurfaceWidth;
        [NonSerialized] int m_DetailGpuSurfaceHeight;
        [NonSerialized] readonly List<GpuDetailSpawnCommand> m_DetailGpuCommands = new List<GpuDetailSpawnCommand>();
        [NonSerialized] readonly Dictionary<DenseDetailBatchKey, GpuProceduralBuildGroup> m_DetailGpuBuildGroups = new Dictionary<DenseDetailBatchKey, GpuProceduralBuildGroup>();
        [NonSerialized] readonly List<GpuProceduralBuildGroup> m_ActiveDetailGpuBuildGroups = new List<GpuProceduralBuildGroup>();

        public bool IsDensityDetailBrgActive => m_DetailBrg != null && m_DetailBrgVisibleCount > 0;
        public bool IsGpuProceduralDensityDetailActive => IsDensityDetailBrgActive && m_DetailBrgUsesGpuGeneration;

        long GetDensityDetailBrgMemoryBytes()
        {
            long bytes = m_DetailBrgInstanceBuffer != null
                ? (long)m_DetailBrgInstanceBuffer.count * m_DetailBrgInstanceBuffer.stride
                    + (long)m_DetailBrgCapacity * sizeof(int)
                : 0L;
            if (m_DetailGpuCommandBuffer != null)
                bytes += (long)m_DetailGpuCommandBuffer.count * m_DetailGpuCommandBuffer.stride;
            if (m_DetailGpuLayerBuffer != null)
                bytes += (long)m_DetailGpuLayerBuffer.count * m_DetailGpuLayerBuffer.stride;
            if (m_DetailGpuSurfaceHeightBuffer != null)
                bytes += (long)m_DetailGpuSurfaceHeightBuffer.count * m_DetailGpuSurfaceHeightBuffer.stride;
            return bytes;
        }

        bool TryPrepareDensityDetailBrg(
            Camera camera,
            int budget,
            float nearScale,
            float distantScale)
        {
            if (!m_UseBatchRendererGroup
                || !Application.isPlaying
                || GraphicsSettings.currentRenderPipeline == null
                || !SystemInfo.supportsInstancing
                || m_DetailBrgUnavailable)
            {
                ReleaseDensityDetailBrg();
                return false;
            }

            bool allProcedural = m_VisibleDensityDetails.Count > 0;
            for (int index = 0; index < m_VisibleDensityDetails.Count && allProcedural; index++)
                allProcedural = m_VisibleDensityDetails[index].chunk.gpuProcedural;
            if (allProcedural && CanUseGpuGeneratedDensityDetailBrg())
                return TryPrepareGpuGeneratedDensityDetailBrg(camera, budget, nearScale, distantScale);

            int remaining = budget;
            int submitted = 0;
            int signature = 17;
            bool hasInstancedBatches = false;
            for (int visibleIndex = 0; visibleIndex < m_VisibleDensityDetails.Count && remaining > 0; visibleIndex++)
            {
                VisibleDensityDetail visible = m_VisibleDensityDetails[visibleIndex];
                if (visible.chunk.combinedDraws.Count > 0)
                {
                    ReleaseDensityDetailBrg();
                    return false;
                }

                float scale = visible.densityLod == 0 ? nearScale : distantScale;
                int allowed = Mathf.Min(
                    remaining,
                    Mathf.FloorToInt(GetVisibleDensityDetailInstanceCount(visible) * scale));
                if (allowed <= 0)
                    continue;
                signature = unchecked(signature * 31 + RuntimeHelpers.GetHashCode(visible.chunk));
                signature = unchecked(signature * 31 + allowed);
                signature = unchecked(signature * 31 + visible.chunk.batches.Count);
                for (int batchIndex = 0; batchIndex < visible.chunk.batches.Count; batchIndex++)
                {
                    Material material = visible.chunk.batches[batchIndex].material;
                    if (!SupportsDotsInstancing(material))
                    {
                        FailDensityDetailBrg(
                            $"Material '{(material != null ? material.name : "<missing>")}' uses shader "
                            + $"'{(material != null && material.shader != null ? material.shader.name : "<missing>")}', "
                            + "which has no DOTS_INSTANCING_ON keyword. Enable DOTS Instancing on the Shader Graph "
                            + "and rebuild the map/content bundle.");
                        return false;
                    }
                }
                hasInstancedBatches |= visible.chunk.batches.Count > 0;
                submitted += allowed;
                remaining -= allowed;
            }

            if (!hasInstancedBatches || submitted <= 1)
            {
                ReleaseDensityDetailBrg();
                return false;
            }

            if (!EnsureDensityDetailBrg())
                return false;

            m_LastSubmittedDensityDetailInstances = submitted;
            if (m_DetailBrgHasSignature
                && signature == m_DetailBrgSignature
                && submitted == m_DetailBrgLogicalCount)
            {
                m_LastDensityDetailDrawCalls = m_DetailBrgPreparedGroups.Count;
                return true;
            }

            ResetBrgBuildGroups();
            remaining = budget;
            for (int visibleIndex = 0; visibleIndex < m_VisibleDensityDetails.Count && remaining > 0; visibleIndex++)
            {
                VisibleDensityDetail visible = m_VisibleDensityDetails[visibleIndex];
                float scale = visible.densityLod == 0 ? nearScale : distantScale;
                int allowed = Mathf.Min(
                    remaining,
                    Mathf.FloorToInt(GetVisibleDensityDetailInstanceCount(visible) * scale));
                if (allowed <= 0)
                    continue;

                ShadowCastingMode shadowCasting = m_DenseDetailShadows
                    ? visible.prototype.ShadowCasting
                    : ShadowCastingMode.Off;
                for (int batchIndex = 0; batchIndex < visible.chunk.batches.Count; batchIndex++)
                    AppendBrgMatrices(visible.chunk.batches[batchIndex], shadowCasting, allowed, visible.chunk.layerIndex);
                remaining -= allowed;
            }

            int gpuInstanceCount = 0;
            for (int groupIndex = 0; groupIndex < m_ActiveDetailBrgBuildGroups.Count; groupIndex++)
                gpuInstanceCount += m_ActiveDetailBrgBuildGroups[groupIndex].matrices.Count;
            if (gpuInstanceCount <= 1 || !EnsureDensityDetailBrgBuffer(gpuInstanceCount))
                return false;

            m_DetailBrgPreparedGroups.Clear();
            int destinationIndex = 0;
            if (m_DetailInstanceShaderData == null || m_DetailInstanceShaderData.Length < m_DetailBrgCapacity)
                m_DetailInstanceShaderData = new Vector4[m_DetailBrgCapacity];
            for (int groupIndex = 0; groupIndex < m_ActiveDetailBrgBuildGroups.Count; groupIndex++)
            {
                BrgBuildGroup group = m_ActiveDetailBrgBuildGroups[groupIndex];
                if (group.matrices.Count == 0)
                    continue;
                DrawBatch batch = group.batch;
                for (int matrixIndex = 0; matrixIndex < group.matrices.Count; matrixIndex++)
                {
                    Matrix4x4 matrix = group.matrices[matrixIndex];
                    m_DetailBrgObjectToWorld[destinationIndex] = new BrgPackedMatrix(matrix);
                    m_DetailBrgWorldToObject[destinationIndex] = new BrgPackedMatrix(matrix.inverse);
                    m_DetailInstanceShaderData[destinationIndex] = group.shaderData[matrixIndex];
                    destinationIndex++;
                }
                m_DetailBrgPreparedGroups.Add(new BrgPreparedGroup(
                    GetOrRegisterBrgMesh(batch.mesh),
                    GetOrRegisterBrgMaterial(batch.material),
                    batch.subMesh,
                    destinationIndex - group.matrices.Count,
                    group.matrices.Count,
                    group.shadowCasting,
                    batch.prototype.ReceiveShadows));
            }

            uint objectToWorldAddress = BrgZeroPrefixBytes;
            uint worldToObjectAddress = (uint)(BrgZeroPrefixBytes + m_DetailBrgCapacity * BrgPackedMatrixBytes);
            UploadDetailDefinitions();
            m_DetailBrgInstanceBuffer.SetData(m_DetailInstanceShaderData, 0, DetailDataAddress / 16, destinationIndex);
            m_DetailBrgInstanceBuffer.SetData(
                m_DetailBrgObjectToWorld,
                0,
                (int)(objectToWorldAddress / BrgPackedMatrixBytes),
                destinationIndex);
            m_DetailBrgInstanceBuffer.SetData(
                m_DetailBrgWorldToObject,
                0,
                (int)(worldToObjectAddress / BrgPackedMatrixBytes),
                destinationIndex);

            Bounds bounds = MeshRenderer != null
                ? MeshRenderer.bounds
                : new Bounds(transform.position, Vector3.one);
            bounds.Expand(Mathf.Max(2f, GetMaximumDenseDetailHeight() * 2f));
            m_DetailBrg.SetGlobalBounds(bounds);
            m_DetailBrgVisibleCount = destinationIndex;
            m_DetailBrgLogicalCount = submitted;
            m_DetailBrgUsesGpuGeneration = false;
            m_DetailBrgSignature = signature;
            m_DetailBrgHasSignature = true;
            m_LastDensityDetailDrawCalls = m_DetailBrgPreparedGroups.Count;
            return true;
        }

        bool CanUseGpuGeneratedDensityDetailBrg()
        {
            if (!m_UseGpuProceduralDetailGeneration
                || m_DetailGpuGenerationUnavailable
                || !SystemInfo.supportsComputeShaders)
            {
                return false;
            }
            if (m_DetailGpuGenerator != null && m_DetailGpuGeneratorKernel >= 0)
                return true;
            m_DetailGpuGenerator = Resources.Load<ComputeShader>("MGTerrainDetailInstances");
            if (m_DetailGpuGenerator == null)
                return false;
            try
            {
                m_DetailGpuGeneratorKernel = m_DetailGpuGenerator.FindKernel("GenerateDetailInstances");
                return m_DetailGpuGeneratorKernel >= 0;
            }
            catch (Exception exception)
            {
                FailGpuDensityDetailGeneration(exception.Message);
                return false;
            }
        }

        bool TryPrepareGpuGeneratedDensityDetailBrg(
            Camera camera,
            int budget,
            float nearScale,
            float distantScale)
        {
            using var profile = s_GpuPrepareMarker.Auto();
            try
            {
                ResetGpuProceduralBuildGroups();
                int remaining = budget;
                int submitted = 0;
                int signature = 23;
                for (int visibleIndex = 0; visibleIndex < m_VisibleDensityDetails.Count && remaining > 0; visibleIndex++)
                {
                    VisibleDensityDetail visible = m_VisibleDensityDetails[visibleIndex];
                    float budgetScale = visible.densityLod == 0 ? nearScale : distantScale;
                    int allowed = Mathf.Min(
                        remaining,
                        Mathf.FloorToInt(GetVisibleDensityDetailInstanceCount(visible) * budgetScale));
                    if (allowed <= 0)
                        continue;

                    DenseDetailPrototypeParts prototypeParts = GetDenseDetailRenderParts(visible.prototype);
                    if (prototypeParts.parts.Count == 0)
                        continue;
                    signature = unchecked(signature * 31 + RuntimeHelpers.GetHashCode(visible.chunk));
                    signature = unchecked(signature * 31 + allowed);
                    signature = unchecked(signature * 31 + prototypeParts.parts.Count);

                    for (int partIndex = 0; partIndex < prototypeParts.parts.Count; partIndex++)
                    {
                        RenderPart part = prototypeParts.parts[partIndex];
                        Material material = GetInstancedMaterial(part.material);
                        if (!SupportsDotsInstancing(material))
                        {
                            FailDensityDetailBrg(
                                $"Material '{(material != null ? material.name : "<missing>")}' uses shader "
                                + $"'{(material != null && material.shader != null ? material.shader.name : "<missing>")}', "
                                + "which has no DOTS_INSTANCING_ON keyword. Enable DOTS Instancing on the Shader Graph "
                                + "and rebuild the map/content bundle.");
                            return false;
                        }

                        var batch = new DrawBatch
                        {
                            mesh = part.mesh,
                            subMesh = part.subMesh,
                            material = material,
                            prototype = visible.prototype,
                            lightProbeUsage = LightProbeUsage.Off,
                            shadowCastingOverride = m_DenseDetailShadows
                                ? visible.prototype.ShadowCasting
                                : ShadowCastingMode.Off
                        };
                        ShadowCastingMode shadowCasting = m_DenseDetailShadows
                            ? visible.prototype.ShadowCasting
                            : ShadowCastingMode.Off;
                        GpuProceduralBuildGroup group = GetGpuProceduralBuildGroup(
                            batch,
                            shadowCasting,
                            part.relativeMatrix);
                        group.ranges.Add(new GpuCellRange(visible.chunk, allowed, group.outputCount));
                        group.outputCount += allowed;
                    }
                    submitted += allowed;
                    remaining -= allowed;
                }

                int gpuInstanceCount = 0;
                int commandCount = 0;
                for (int groupIndex = 0; groupIndex < m_ActiveDetailGpuBuildGroups.Count; groupIndex++)
                {
                    gpuInstanceCount += m_ActiveDetailGpuBuildGroups[groupIndex].outputCount;
                }
                if (submitted <= 1 || gpuInstanceCount <= 1)
                {
                    ReleaseDensityDetailBrg();
                    return false;
                }
                if (!EnsureDensityDetailBrg()
                    || !EnsureDensityDetailBrgBuffer(gpuInstanceCount, false)
                    || !EnsureGpuSurfaceHeightBuffer())
                {
                    return false;
                }

                // Compare exact cell ranges before expanding any density texels into
                // commands. Buffer recreation invalidates all previous addresses.
                bool reuseRanges = m_DetailBrgHasSignature && m_DetailBrgUsesGpuGeneration;
                int rangeBase = 0;
                LastRegeneratedDetailInstances = 0;
                for (int groupIndex = 0; groupIndex < m_ActiveDetailGpuBuildGroups.Count; groupIndex++)
                {
                    GpuProceduralBuildGroup group = m_ActiveDetailGpuBuildGroups[groupIndex];
                    bool reuseGroup = reuseRanges && group.generation == m_DetailGpuGeneration
                        && group.previousRelativeMatrix == group.relativeMatrix;
                    for (int index = 0; index < group.ranges.Count; index++)
                    {
                        GpuCellRange range = group.ranges[index];
                        if (reuseGroup && index < group.previousRanges.Count)
                        {
                            GpuCellRange previous = group.previousRanges[index];
                            if (ReferenceEquals(previous.chunk, range.chunk) && previous.count == range.count
                                && group.previousOutputBase + previous.start == rangeBase + range.start)
                                continue;
                        }
                        AppendGpuProceduralSpawns(group, range.chunk, range.count, range.start);
                        LastRegeneratedDetailInstances += range.count;
                    }
                    commandCount += group.commands.Count;
                    rangeBase += group.outputCount;
                }

                if (!EnsureGpuProceduralInputBuffers(commandCount))
                    return false;
                if (commandCount > 0)
                    UploadGpuLayerParameters();
                m_DetailGpuCommands.Clear();
                int outputBase = 0;
                for (int groupIndex = 0; groupIndex < m_ActiveDetailGpuBuildGroups.Count; groupIndex++)
                {
                    GpuProceduralBuildGroup group = m_ActiveDetailGpuBuildGroups[groupIndex];
                    group.commandOffset = m_DetailGpuCommands.Count;
                    group.outputBase = outputBase;
                    m_DetailGpuCommands.AddRange(group.commands);
                    outputBase += group.outputCount;
                }
                if (commandCount > 0)
                    m_DetailGpuCommandBuffer.SetData(m_DetailGpuCommands);

                Bounds surfaceBounds = MeshFilter.sharedMesh.bounds;
                uint objectToWorldAddress = BrgZeroPrefixBytes;
                uint worldToObjectAddress = (uint)(BrgZeroPrefixBytes + m_DetailBrgCapacity * BrgPackedMatrixBytes);
                int kernel = m_DetailGpuGeneratorKernel;
                m_DetailGpuGenerator.SetBuffer(kernel, "_Commands", m_DetailGpuCommandBuffer);
                m_DetailGpuGenerator.SetBuffer(kernel, "_Layers", m_DetailGpuLayerBuffer);
                m_DetailGpuGenerator.SetBuffer(kernel, "_SurfaceHeights", m_DetailGpuSurfaceHeightBuffer);
                m_DetailGpuGenerator.SetBuffer(kernel, "_InstanceData", m_DetailBrgInstanceBuffer);
                m_DetailGpuGenerator.SetInt("_ObjectToWorldAddress", (int)objectToWorldAddress);
                m_DetailGpuGenerator.SetInt("_WorldToObjectAddress", (int)worldToObjectAddress);
                m_DetailGpuGenerator.SetInt("_DetailDataAddress", DetailDataAddress);
                UploadDetailDefinitions();
                m_DetailGpuGenerator.SetInt("_SurfaceGridWidth", m_DetailGpuSurfaceWidth);
                m_DetailGpuGenerator.SetInt("_SurfaceGridHeight", m_DetailGpuSurfaceHeight);
                m_DetailGpuGenerator.SetVector(
                    "_SurfaceMinMax",
                    new Vector4(surfaceBounds.min.x, surfaceBounds.min.z, surfaceBounds.max.x, surfaceBounds.max.z));
                m_DetailGpuGenerator.SetMatrix("_TerrainLocalToWorld", transform.localToWorldMatrix);

                m_DetailBrgPreparedGroups.Clear();
                const int maximumDispatchGroups = 65535;
                for (int groupIndex = 0; groupIndex < m_ActiveDetailGpuBuildGroups.Count; groupIndex++)
                {
                    GpuProceduralBuildGroup group = m_ActiveDetailGpuBuildGroups[groupIndex];
                    if (group.outputCount <= 0)
                        continue;
                    m_DetailGpuGenerator.SetMatrix("_PartRelative", group.relativeMatrix);
                    m_DetailGpuGenerator.SetInt("_GroupOutputBase", group.outputBase);
                    for (int start = 0; start < group.commands.Count; start += maximumDispatchGroups)
                    {
                        int count = Mathf.Min(maximumDispatchGroups, group.commands.Count - start);
                        m_DetailGpuGenerator.SetInt("_CommandOffset", group.commandOffset + start);
                        m_DetailGpuGenerator.Dispatch(kernel, count, 1, 1);
                    }
                    m_DetailBrgPreparedGroups.Add(new BrgPreparedGroup(
                        GetOrRegisterBrgMesh(group.batch.mesh),
                        GetOrRegisterBrgMaterial(group.batch.material),
                        group.batch.subMesh,
                        group.outputBase,
                        group.outputCount,
                        group.shadowCasting,
                        group.batch.prototype.ReceiveShadows));
                }

                // Commit reuse metadata only after every dispatch succeeds. Inactive
                // groups must not reuse addresses overwritten by another group.
                m_DetailGpuGeneration++;
                for (int groupIndex = 0; groupIndex < m_ActiveDetailGpuBuildGroups.Count; groupIndex++)
                {
                    GpuProceduralBuildGroup group = m_ActiveDetailGpuBuildGroups[groupIndex];
                    group.previousRanges.Clear();
                    group.previousRanges.AddRange(group.ranges);
                    group.previousOutputBase = group.outputBase;
                    group.previousRelativeMatrix = group.relativeMatrix;
                    group.generation = m_DetailGpuGeneration;
                }

                Bounds bounds = MeshRenderer != null
                    ? MeshRenderer.bounds
                    : new Bounds(transform.position, Vector3.one);
                bounds.Expand(Mathf.Max(2f, GetMaximumDenseDetailHeight() * 2f));
                m_DetailBrg.SetGlobalBounds(bounds);
                m_DetailBrgVisibleCount = gpuInstanceCount;
                m_DetailBrgLogicalCount = submitted;
                m_DetailBrgSignature = signature;
                m_DetailBrgHasSignature = true;
                m_DetailBrgUsesGpuGeneration = true;
                m_LastSubmittedDensityDetailInstances = submitted;
                m_LastDensityDetailDrawCalls = m_DetailBrgPreparedGroups.Count;
                return true;
            }
            catch (Exception exception)
            {
                FailGpuDensityDetailGeneration(exception.Message);
                return false;
            }
        }

        void ResetGpuProceduralBuildGroups()
        {
            for (int index = 0; index < m_ActiveDetailGpuBuildGroups.Count; index++)
            {
                GpuProceduralBuildGroup group = m_ActiveDetailGpuBuildGroups[index];
                group.commands.Clear();
                group.ranges.Clear();
                group.outputCount = 0;
                group.outputBase = 0;
                group.commandOffset = 0;
                group.active = false;
            }
            m_ActiveDetailGpuBuildGroups.Clear();
        }

        GpuProceduralBuildGroup GetGpuProceduralBuildGroup(
            DrawBatch batch,
            ShadowCastingMode shadowCasting,
            Matrix4x4 relativeMatrix)
        {
            var key = new DenseDetailBatchKey(batch, shadowCasting, true, relativeMatrix);
            if (!m_DetailGpuBuildGroups.TryGetValue(key, out GpuProceduralBuildGroup group))
            {
                group = new GpuProceduralBuildGroup();
                m_DetailGpuBuildGroups.Add(key, group);
            }
            if (!group.active)
            {
                group.active = true;
                group.batch = batch;
                group.shadowCasting = shadowCasting;
                group.relativeMatrix = relativeMatrix;
                m_ActiveDetailGpuBuildGroups.Add(group);
            }
            return group;
        }

        static void AppendGpuProceduralSpawns(
            GpuProceduralBuildGroup group,
            DensityDetailChunk chunk,
            int selectedCount,
            int outputStart)
        {
            int sourceCount = Mathf.Max(1, chunk.instanceCount);
            int sourceCursor = 0;
            int selectedCursor = 0;
            for (int index = 0; index < chunk.proceduralSpawns.Count; index++)
            {
                DensityDetailSpawn spawn = chunk.proceduralSpawns[index];
                int nextSource = sourceCursor + spawn.count;
                int nextSelected = (int)((long)nextSource * selectedCount / sourceCount);
                int count = nextSelected - selectedCursor;
                if (count > 0)
                {
                    group.commands.Add(new GpuDetailSpawnCommand
                    {
                        x = (uint)spawn.x,
                        z = (uint)spawn.z,
                        count = (uint)count,
                        outputStart = (uint)(outputStart + selectedCursor),
                        layerIndex = (uint)chunk.layerIndex,
                        sizes = spawn.sizes
                    });
                }
                sourceCursor = nextSource;
                selectedCursor = nextSelected;
            }
        }

        bool EnsureGpuProceduralInputBuffers(int requiredCommands)
        {
            int commandCapacity = Mathf.NextPowerOfTwo(Mathf.Max(64, requiredCommands));
            if (m_DetailGpuCommandBuffer == null || commandCapacity > m_DetailGpuCommandCapacity)
            {
                m_DetailGpuCommandBuffer?.Dispose();
                m_DetailGpuCommandCapacity = commandCapacity;
                m_DetailGpuCommandBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    commandCapacity,
                    Marshal.SizeOf<GpuDetailSpawnCommand>());
            }

            int requiredLayers = Mathf.Max(1, m_DensityDetailLayers.Count);
            int layerCapacity = Mathf.NextPowerOfTwo(requiredLayers);
            if (m_DetailGpuLayerBuffer == null || layerCapacity > m_DetailGpuLayerCapacity)
            {
                m_DetailGpuLayerBuffer?.Dispose();
                m_DetailGpuLayerCapacity = layerCapacity;
                m_DetailGpuLayerBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    layerCapacity,
                    Marshal.SizeOf<GpuDetailLayerParameters>());
            }
            return true;
        }

        void UploadGpuLayerParameters()
        {
            var parameters = new GpuDetailLayerParameters[m_DensityDetailLayers.Count];
            for (int index = 0; index < parameters.Length; index++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[index];
                Texture2D densityMap = layer != null ? layer.DensityMap : null;
                parameters[index] = new GpuDetailLayerParameters
                {
                    width = (uint)Mathf.Max(1, densityMap != null ? densityMap.width : 1),
                    height = (uint)Mathf.Max(1, densityMap != null ? densityMap.height : 1),
                    seed = (uint)(layer != null ? layer.Seed : 0),
                    minWidth = layer != null ? layer.MinWidth : 1f,
                    maxWidth = layer != null ? layer.MaxWidth : 1f,
                    minHeight = layer != null ? layer.MinHeight : 1f,
                    maxHeight = layer != null ? layer.MaxHeight : 1f,
                    yOffset = layer != null ? layer.YOffset : 0f
                };
            }
            if (parameters.Length > 0)
                m_DetailGpuLayerBuffer.SetData(parameters, 0, 0, parameters.Length);
        }

        bool EnsureGpuSurfaceHeightBuffer()
        {
            Mesh mesh = MeshFilter != null ? MeshFilter.sharedMesh : null;
            if (mesh == null)
                return false;
            if (m_DetailGpuSurfaceHeightBuffer != null && m_DetailGpuSurfaceMesh == mesh)
                return true;

            Vector3[] vertices = mesh.vertices;
            int width = Mathf.Max(2, m_SurfaceGridWidth);
            int height = Mathf.Max(2, m_SurfaceGridHeight);
            if (vertices.Length != width * height)
            {
                int square = Mathf.RoundToInt(Mathf.Sqrt(vertices.Length));
                if (square * square != vertices.Length)
                    throw new InvalidOperationException(
                        $"MG Terrain GPU details require a regular surface grid; mesh has {vertices.Length:N0} vertices.");
                width = height = square;
            }

            var heights = new float[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
                heights[index] = vertices[index].y;
            m_DetailGpuSurfaceHeightBuffer?.Dispose();
            m_DetailGpuSurfaceHeightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                Mathf.Max(1, heights.Length),
                sizeof(float));
            m_DetailGpuSurfaceHeightBuffer.SetData(heights);
            m_DetailGpuSurfaceMesh = mesh;
            m_DetailGpuSurfaceWidth = width;
            m_DetailGpuSurfaceHeight = height;
            return true;
        }

        void FailGpuDensityDetailGeneration(string reason)
        {
            m_DetailGpuGenerationUnavailable = true;
            m_DetailRenderCacheDirty = true;
            ReleaseDensityDetailBrg();
            if (m_DetailBrgFailureReported)
                return;
            m_DetailBrgFailureReported = true;
            Debug.LogWarning(
                $"MG Terrain GPU detail generation unavailable; rebuilding with the packed fallback. {reason}",
                this);
        }

        float GetMaximumDenseDetailHeight()
        {
            float maximum = 1f;
            for (int layerIndex = 0; layerIndex < m_DensityDetailLayers.Count; layerIndex++)
            {
                DensityDetailLayer layer = m_DensityDetailLayers[layerIndex];
                if (layer != null)
                    maximum = Mathf.Max(maximum, layer.MaxHeight);
            }
            return maximum;
        }

        void ResetBrgBuildGroups()
        {
            for (int index = 0; index < m_ActiveDetailBrgBuildGroups.Count; index++)
            {
                BrgBuildGroup group = m_ActiveDetailBrgBuildGroups[index];
                group.matrices.Clear();
                group.shaderData.Clear();
                group.active = false;
            }
            m_ActiveDetailBrgBuildGroups.Clear();
        }

        void AppendBrgMatrices(DrawBatch batch, ShadowCastingMode shadowCasting, int maximumInstances, int layerIndex)
        {
            var key = new DenseDetailBatchKey(batch, shadowCasting, true);
            if (!m_DetailBrgBuildGroups.TryGetValue(key, out BrgBuildGroup group))
            {
                group = new BrgBuildGroup { batch = batch, shadowCasting = shadowCasting };
                m_DetailBrgBuildGroups.Add(key, group);
            }
            if (!group.active)
            {
                group.active = true;
                group.batch = batch;
                group.shadowCasting = shadowCasting;
                m_ActiveDetailBrgBuildGroups.Add(group);
            }

            int remaining = maximumInstances;
            for (int chunkIndex = 0; chunkIndex < batch.matrixChunks.Count && remaining > 0; chunkIndex++)
            {
                Matrix4x4[] matrices = batch.matrixChunks[chunkIndex];
                if (matrices == null)
                    continue;
                int count = Mathf.Min(matrices.Length, remaining);
                for (int matrixIndex = 0; matrixIndex < count; matrixIndex++)
                {
                    group.matrices.Add(matrices[matrixIndex]);
                    var matrix = matrices[matrixIndex];
                    uint seed = Hash((uint)matrix.m03.GetHashCode() ^ (uint)matrix.m23.GetHashCode());
                    group.shaderData.Add(new Vector4(layerIndex, Hash01(seed), 0, 0));
                }
                remaining -= count;
            }
        }

        bool EnsureDensityDetailBrg()
        {
            if (m_DetailBrg != null)
                return true;
            try
            {
                m_DetailBrg = new BatchRendererGroup(OnPerformDensityDetailBrgCulling, IntPtr.Zero);
                return true;
            }
            catch (Exception exception)
            {
                FailDensityDetailBrg(exception);
                return false;
            }
        }

        bool EnsureDensityDetailBrgBuffer(int requiredInstances, bool needsCpuUploadArrays = true)
        {
            if (m_DetailBrgInstanceBuffer != null && requiredInstances <= m_DetailBrgCapacity
                && m_DensityDetailLayers.Count <= m_DetailDefinitionCapacity)
            {
                if (needsCpuUploadArrays && (m_DetailBrgObjectToWorld == null || m_DetailBrgWorldToObject == null))
                {
                    m_DetailBrgObjectToWorld = new BrgPackedMatrix[m_DetailBrgCapacity];
                    m_DetailBrgWorldToObject = new BrgPackedMatrix[m_DetailBrgCapacity];
                }
                return true;
            }

            try
            {
                if (m_DetailBrgInstanceBuffer != null)
                {
                    m_DetailBrg.RemoveBatch(m_DetailBrgBatchId);
                    m_DetailBrgInstanceBuffer.Dispose();
                }

                m_DetailBrgCapacity = Mathf.NextPowerOfTwo(Mathf.Max(256, requiredInstances));
                m_DetailDefinitionCapacity = Mathf.Max(1, m_DensityDetailLayers.Count);
                long bytes = BrgZeroPrefixBytes + (long)m_DetailBrgCapacity * (BrgPackedMatrixBytes * 2L + 16L)
                    + (1L + m_DetailDefinitionCapacity * 2L) * 16L;
                if (bytes > int.MaxValue)
                    throw new InvalidOperationException("The requested MG Terrain BRG buffer exceeds the 2 GB GraphicsBuffer limit.");
                m_DetailBrgInstanceBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Raw,
                    (int)((bytes + sizeof(int) - 1L) / sizeof(int)),
                    sizeof(int));
                m_DetailBrgObjectToWorld = needsCpuUploadArrays
                    ? new BrgPackedMatrix[m_DetailBrgCapacity]
                    : null;
                m_DetailBrgWorldToObject = needsCpuUploadArrays
                    ? new BrgPackedMatrix[m_DetailBrgCapacity]
                    : null;
                if (m_DetailBrgSequentialVisibleIndices.IsCreated)
                    m_DetailBrgSequentialVisibleIndices.Dispose();
                m_DetailBrgSequentialVisibleIndices = new NativeArray<int>(
                    m_DetailBrgCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                for (int index = 0; index < m_DetailBrgCapacity; index++)
                    m_DetailBrgSequentialVisibleIndices[index] = index;
                m_DetailBrgInstanceBuffer.SetData(m_DetailBrgZero, 0, 0, 1);

                uint objectToWorldAddress = BrgZeroPrefixBytes;
                uint worldToObjectAddress = (uint)(BrgZeroPrefixBytes + m_DetailBrgCapacity * BrgPackedMatrixBytes);
                var metadata = new NativeArray<MetadataValue>(4, Allocator.Temp);
                try
                {
                    metadata[0] = new MetadataValue
                    {
                        NameID = Shader.PropertyToID("unity_ObjectToWorld"),
                        Value = 0x80000000u | objectToWorldAddress
                    };
                    metadata[1] = new MetadataValue
                    {
                        NameID = Shader.PropertyToID("unity_WorldToObject"),
                        Value = 0x80000000u | worldToObjectAddress
                    };
                    metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("_MGDetailInstance"), Value = 0x80000000u | (uint)DetailDataAddress };
                    metadata[3] = new MetadataValue { NameID = Shader.PropertyToID("_MGDetailTable"), Value = (uint)DetailTableAddress };
                    m_DetailBrgBatchId = m_DetailBrg.AddBatch(metadata, m_DetailBrgInstanceBuffer.bufferHandle);
                }
                finally
                {
                    if (metadata.IsCreated)
                        metadata.Dispose();
                }
                m_DetailBrgHasSignature = false;
                return true;
            }
            catch (Exception exception)
            {
                FailDensityDetailBrg(exception);
                return false;
            }
        }

        BatchMeshID GetOrRegisterBrgMesh(Mesh mesh)
        {
            if (!m_DetailBrgMeshIds.TryGetValue(mesh, out BatchMeshID id))
            {
                id = m_DetailBrg.RegisterMesh(mesh);
                m_DetailBrgMeshIds.Add(mesh, id);
            }
            return id;
        }

        BatchMaterialID GetOrRegisterBrgMaterial(Material material)
        {
            if (!m_DetailBrgMaterialIds.TryGetValue(material, out BatchMaterialID id))
            {
                id = m_DetailBrg.RegisterMaterial(material);
                m_DetailBrgMaterialIds.Add(material, id);
            }
            return id;
        }

        static bool SupportsDotsInstancing(Material material)
        {
            Shader shader = material != null ? material.shader : null;
            return shader != null
                && shader.keywordSpace.FindKeyword("DOTS_INSTANCING_ON").isValid;
        }

        unsafe JobHandle OnPerformDensityDetailBrgCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            using var profile = s_BrgCullingMarker.Auto();
            var output = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();
            *output = default;
            bool cameraView = cullingContext.viewType == BatchCullingViewType.Camera;
            bool lightView = cullingContext.viewType == BatchCullingViewType.Light;
            if (!cameraView && !lightView)
                return default;

            int commandCount = 0;
            for (int groupIndex = 0; groupIndex < m_DetailBrgPreparedGroups.Count; groupIndex++)
            {
                BrgPreparedGroup group = m_DetailBrgPreparedGroups[groupIndex];
                if ((cameraView && group.shadowCasting != ShadowCastingMode.ShadowsOnly)
                    || (lightView && group.shadowCasting != ShadowCastingMode.Off))
                {
                    commandCount++;
                }
            }
            int visibleCount = m_DetailBrgVisibleCount;
            if (commandCount == 0
                || visibleCount == 0
                || !m_DetailBrgSequentialVisibleIndices.IsCreated)
                return default;

            int alignment = UnsafeUtility.AlignOf<long>();
            output->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount,
                alignment,
                Allocator.TempJob);
            output->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawRange>() * commandCount,
                alignment,
                Allocator.TempJob);
            output->visibleInstances = (int*)UnsafeUtility.Malloc(
                sizeof(int) * visibleCount,
                alignment,
                Allocator.TempJob);
            output->drawCommandCount = commandCount;
            output->drawRangeCount = commandCount;
            output->visibleInstanceCount = visibleCount;

            UnsafeUtility.MemCpy(
                output->visibleInstances,
                m_DetailBrgSequentialVisibleIndices.GetUnsafeReadOnlyPtr(),
                sizeof(int) * (long)visibleCount);

            int commandIndex = 0;
            for (int groupIndex = 0; groupIndex < m_DetailBrgPreparedGroups.Count; groupIndex++)
            {
                BrgPreparedGroup group = m_DetailBrgPreparedGroups[groupIndex];
                if ((cameraView && group.shadowCasting == ShadowCastingMode.ShadowsOnly)
                    || (lightView && group.shadowCasting == ShadowCastingMode.Off))
                {
                    continue;
                }
                output->drawCommands[commandIndex] = new BatchDrawCommand
                {
                    visibleOffset = group.visibleOffset,
                    visibleCount = group.visibleCount,
                    batchID = m_DetailBrgBatchId,
                    materialID = group.materialId,
                    meshID = group.meshId,
                    submeshIndex = group.subMesh,
                    splitVisibilityMask = ushort.MaxValue,
                    flags = BatchDrawCommandFlags.None,
                    sortingPosition = 0
                };
                output->drawRanges[commandIndex] = new BatchDrawRange
                {
                    drawCommandsType = BatchDrawCommandType.Direct,
                    drawCommandsBegin = (uint)commandIndex,
                    drawCommandsCount = 1,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = uint.MaxValue,
                        layer = (byte)gameObject.layer,
                        shadowCastingMode = group.shadowCasting,
                        receiveShadows = group.receiveShadows
                    }
                };
                commandIndex++;
            }
            return default;
        }

        void FailDensityDetailBrg(Exception exception)
        {
            FailDensityDetailBrg(exception.Message);
        }

        void FailDensityDetailBrg(string reason)
        {
            ReleaseDensityDetailBrg();
            m_DetailBrgUnavailable = true;
            if (m_DetailBrgFailureReported)
                return;
            m_DetailBrgFailureReported = true;
            Debug.LogWarning(
                $"MG Terrain BatchRendererGroup unavailable; using packed 1,023-instance fallback. {reason}",
                this);
        }

        void ClearDensityDetailBrgVisibility()
        {
            m_DetailBrgPreparedGroups.Clear();
            m_DetailBrgVisibleCount = 0;
            m_DetailBrgLogicalCount = 0;
            m_DetailBrgHasSignature = false;
            LastRegeneratedDetailInstances = 0;
        }

        void ReleaseDensityDetailBrg()
        {
            LastRegeneratedDetailInstances = 0;
            if (m_DetailBrg != null)
            {
                m_DetailBrg.Dispose();
                m_DetailBrg = null;
            }
            if (m_DetailBrgInstanceBuffer != null)
            {
                m_DetailBrgInstanceBuffer.Dispose();
                m_DetailBrgInstanceBuffer = null;
            }
            m_DetailBrgMeshIds.Clear();
            m_DetailBrgMaterialIds.Clear();
            m_DetailBrgPreparedGroups.Clear();
            ResetBrgBuildGroups();
            ResetGpuProceduralBuildGroups();
            m_DetailGpuBuildGroups.Clear();
            m_DetailGpuCommands.Clear();
            if (m_DetailGpuCommandBuffer != null)
            {
                m_DetailGpuCommandBuffer.Dispose();
                m_DetailGpuCommandBuffer = null;
            }
            if (m_DetailGpuLayerBuffer != null)
            {
                m_DetailGpuLayerBuffer.Dispose();
                m_DetailGpuLayerBuffer = null;
            }
            if (m_DetailGpuSurfaceHeightBuffer != null)
            {
                m_DetailGpuSurfaceHeightBuffer.Dispose();
                m_DetailGpuSurfaceHeightBuffer = null;
            }
            m_DetailGpuCommandCapacity = 0;
            m_DetailGpuLayerCapacity = 0;
            m_DetailGpuSurfaceMesh = null;
            m_DetailGpuSurfaceWidth = 0;
            m_DetailGpuSurfaceHeight = 0;
            m_DetailBrgCapacity = 0;
            m_DetailBrgVisibleCount = 0;
            m_DetailBrgLogicalCount = 0;
            m_DetailBrgHasSignature = false;
            m_DetailBrgUsesGpuGeneration = false;
            m_DetailBrgObjectToWorld = null;
            m_DetailBrgWorldToObject = null;
            m_DetailInstanceShaderData = null;
            m_DetailDefinitionCapacity = 0;
            if (m_DetailBrgSequentialVisibleIndices.IsCreated)
                m_DetailBrgSequentialVisibleIndices.Dispose();
            m_DetailBrgSequentialVisibleIndices = default;
        }
    }
}

#else

using UnityEngine;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        public bool IsDensityDetailBrgActive => false;
        public bool IsGpuProceduralDensityDetailActive => false;
        public int LastRegeneratedDetailInstances => 0;
        void ClearDensityDetailBrgVisibility() { }
        long GetDensityDetailBrgMemoryBytes() => 0L;
        bool TryPrepareDensityDetailBrg(Camera camera, int budget, float nearScale, float distantScale) => false;
        void ReleaseDensityDetailBrg() { }
    }
}

#endif
