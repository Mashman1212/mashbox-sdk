#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrainWorld
    {
        BatchRendererGroup m_Renderer;
        readonly List<MGTerrain> m_BrgChunks = new List<MGTerrain>();
        readonly Dictionary<MGTerrain, Bounds> m_ChunkBounds = new Dictionary<MGTerrain, Bounds>();
        readonly Dictionary<Mesh, (BatchMeshID id, int users)> m_Meshes = new Dictionary<Mesh, (BatchMeshID, int)>();
        readonly Dictionary<Material, (BatchMaterialID id, int users)> m_Materials = new Dictionary<Material, (BatchMaterialID, int)>();
        public int SharedRendererCount => m_Renderer == null ? 0 : 1;
        public int RegisteredMeshCount => m_Meshes.Count;
        public int RegisteredMaterialCount => m_Materials.Count;

        internal BatchRendererGroup AcquireRenderer(MGTerrain chunk)
        {
            m_Renderer ??= new BatchRendererGroup(Cull, IntPtr.Zero);
            if (!m_BrgChunks.Contains(chunk)) m_BrgChunks.Add(chunk);
            return m_Renderer;
        }
        internal BatchMeshID RegisterMesh(Mesh mesh)
        {
            if (!m_Meshes.TryGetValue(mesh, out var item)) item = (m_Renderer.RegisterMesh(mesh), 0);
            m_Meshes[mesh] = (item.id, item.users + 1);
            return item.id;
        }
        internal BatchMaterialID RegisterMaterial(Material material)
        {
            if (!m_Materials.TryGetValue(material, out var item)) item = (m_Renderer.RegisterMaterial(material), 0);
            m_Materials[material] = (item.id, item.users + 1);
            return item.id;
        }
        internal void ReleaseRenderer(MGTerrain chunk, BatchID batch, bool hasBatch,
            IEnumerable<Mesh> meshes, IEnumerable<Material> materials)
        {
            if (m_Renderer == null) return;
            if (hasBatch) m_Renderer.RemoveBatch(batch);
            foreach (var mesh in meshes)
                if (m_Meshes.TryGetValue(mesh, out var item))
                {
                    if (item.users == 1) { m_Renderer.UnregisterMesh(item.id); m_Meshes.Remove(mesh); }
                    else m_Meshes[mesh] = (item.id, item.users - 1);
                }
            foreach (var material in materials)
                if (m_Materials.TryGetValue(material, out var item))
                {
                    if (item.users == 1) { m_Renderer.UnregisterMaterial(item.id); m_Materials.Remove(material); }
                    else m_Materials[material] = (item.id, item.users - 1);
                }
            m_BrgChunks.Remove(chunk);
            m_ChunkBounds.Remove(chunk);
            UpdateBounds();
        }
        internal void SetChunkBounds(MGTerrain chunk, Bounds bounds)
        {
            m_ChunkBounds[chunk] = bounds;
            UpdateBounds();
        }
        void UpdateBounds()
        {
            bool first = true;
            Bounds combined = default;
            foreach (var bounds in m_ChunkBounds.Values)
                if (first) { combined = bounds; first = false; } else combined.Encapsulate(bounds);
            m_Renderer?.SetGlobalBounds(combined);
        }
        void DisposeWorldRenderer()
        {
            m_Renderer?.Dispose(); m_Renderer = null;
            m_BrgChunks.Clear(); m_ChunkBounds.Clear(); m_Meshes.Clear(); m_Materials.Clear();
        }

        unsafe JobHandle Cull(BatchRendererGroup renderer, BatchCullingContext context,
            BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            var output = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();
            *output = default;
            int direct = 0, indirect = 0, visible = 0;
            foreach (var chunk in m_BrgChunks) chunk.CountWorldCommands(context, ref direct, ref indirect, ref visible);
            int ranges = direct + indirect;
            if (ranges == 0) return default;
            output->drawCommandCount = direct; output->indirectDrawCommandCount = indirect;
            output->drawRangeCount = ranges; output->visibleInstanceCount = visible;
            output->drawCommands = Allocate<BatchDrawCommand>(direct);
            output->indirectDrawCommands = Allocate<BatchDrawCommandIndirect>(indirect);
            output->drawRanges = Allocate<BatchDrawRange>(ranges);
            output->visibleInstances = Allocate<int>(visible);
            direct = indirect = visible = ranges = 0;
            foreach (var chunk in m_BrgChunks)
                chunk.WriteWorldCommands(context, output, ref direct, ref indirect, ref visible, ref ranges);
            return default;
        }
        static unsafe T* Allocate<T>(int count) where T : unmanaged => count == 0 ? null :
            (T*)UnsafeUtility.Malloc((long)UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<long>(), Allocator.TempJob);
    }

    public sealed partial class MGTerrain
    {
        bool WorldHasCommands(BatchCullingContext context) =>
            (context.viewType == BatchCullingViewType.Camera || context.viewType == BatchCullingViewType.Light)
            && m_DetailBrgVisibleCount > 0 && m_DetailBrgSequentialVisibleIndices.IsCreated;
        static bool WorldGroupVisible(BrgPreparedGroup group, bool camera) => camera
            ? group.shadowCasting != ShadowCastingMode.ShadowsOnly : group.shadowCasting != ShadowCastingMode.Off;

        internal void CountWorldCommands(BatchCullingContext context, ref int direct, ref int indirect, ref int visible)
        {
            if (!WorldHasCommands(context)) return;
            bool useIndirect = m_IndirectDetailDrawsReady && m_DetailBrgUsesGpuGeneration;
            int count = 0;
            foreach (var group in m_DetailBrgPreparedGroups)
                if (WorldGroupVisible(group, context.viewType == BatchCullingViewType.Camera)) count++;
            if (useIndirect) indirect += count;
            else if (count > 0) { direct += count; visible += m_DetailBrgVisibleCount; }
        }

        internal unsafe void WriteWorldCommands(BatchCullingContext context, BatchCullingOutputDrawCommands* output,
            ref int direct, ref int indirect, ref int visible, ref int range)
        {
            if (!WorldHasCommands(context)) return;
            bool camera = context.viewType == BatchCullingViewType.Camera;
            bool useIndirect = m_IndirectDetailDrawsReady && m_DetailBrgUsesGpuGeneration;
            bool occlusion = camera && m_OcclusionReady && m_OcclusionCamera != null
                && context.viewID.GetInstanceID() == m_OcclusionCamera.GetInstanceID();
            bool copied = false;
            for (int i = 0; i < m_DetailBrgPreparedGroups.Count; i++)
            {
                var group = m_DetailBrgPreparedGroups[i];
                if (!WorldGroupVisible(group, camera)) continue;
                uint commandIndex = (uint)(useIndirect ? indirect : direct);
                if (useIndirect)
                    output->indirectDrawCommands[indirect++] = new BatchDrawCommandIndirect
                    {
                        flags = BatchDrawCommandFlags.None, visibleOffset = group.visibleOffset,
                        batchID = m_DetailBrgBatchId, materialID = group.materialId, meshID = group.meshId,
                        splitVisibilityMask = ushort.MaxValue, sortingPosition = 0, topology = m_IndirectTopologies[i],
                        visibleInstancesBufferHandle = (occlusion ? m_OcclusionIndices : m_IndirectVisibleIndices).bufferHandle,
                        indirectArgsBufferHandle = (occlusion ? m_OcclusionArgs : m_IndirectArgs).bufferHandle,
                        indirectArgsBufferOffset = (uint)(i * GraphicsBuffer.IndirectDrawIndexedArgs.size)
                    };
                else
                {
                    if (!copied)
                    {
                        UnsafeUtility.MemCpy(output->visibleInstances + visible,
                            m_DetailBrgSequentialVisibleIndices.GetUnsafeReadOnlyPtr(), sizeof(int) * (long)m_DetailBrgVisibleCount);
                        copied = true;
                    }
                    output->drawCommands[direct++] = new BatchDrawCommand
                    {
                        visibleOffset = group.visibleOffset + (uint)visible, visibleCount = group.visibleCount,
                        batchID = m_DetailBrgBatchId, materialID = group.materialId, meshID = group.meshId,
                        submeshIndex = group.subMesh, splitVisibilityMask = ushort.MaxValue,
                        flags = BatchDrawCommandFlags.None, sortingPosition = 0
                    };
                }
                output->drawRanges[range++] = new BatchDrawRange
                {
                    drawCommandsType = useIndirect ? BatchDrawCommandType.Indirect : BatchDrawCommandType.Direct,
                    drawCommandsBegin = commandIndex, drawCommandsCount = 1,
                    filterSettings = new BatchFilterSettings { batchLayer = DetailBatchLayer,
                        renderingLayerMask = uint.MaxValue, layer = (byte)gameObject.layer,
                        shadowCastingMode = group.shadowCasting, receiveShadows = group.receiveShadows }
                };
            }
            if (copied) visible += m_DetailBrgVisibleCount;
        }
    }
}
#endif
