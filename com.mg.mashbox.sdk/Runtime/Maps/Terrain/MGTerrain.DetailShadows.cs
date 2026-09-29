#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
namespace MashBoxSDK.Maps.TerrainSystem
{
    public sealed partial class MGTerrain
    {
        readonly struct ShadowCell
        {
            internal readonly ResidentGpuCell cell;
            internal readonly int count;
            internal ShadowCell(ResidentGpuCell cell, int count) { this.cell = cell; this.count = count; }
        }
        readonly List<ShadowCell> m_ShadowCells = new List<ShadowCell>();
        readonly List<ShadowCell> m_NextShadowCells = new List<ShadowCell>();
        readonly List<BrgPreparedGroup> m_DetailShadowGroups = new List<BrgPreparedGroup>();
        readonly List<int> m_ShadowIndexScratch = new List<int>();
        NativeArray<int> m_DetailShadowIndices;
        bool m_DetailShadowSelectionReady;
        int m_ShadowSelectionVersion, m_ShadowBuiltVersion = -1, m_DetailShadowCount;
        Vector3 m_ShadowCameraPosition;
        float m_ShadowDistance;
        static readonly Unity.Profiling.ProfilerMarker s_ShadowSelectionMarker = new Unity.Profiling.ProfilerMarker("MGTerrain.ShadowSelection");
        public int LastDetailShadowInstances => m_DetailShadowSelectionReady ? m_DetailShadowCount : -1;

        // Shadow prefixes fade per density texel, keeping the same deterministic instance identities.
        internal static int SelectShadowPopulation(int visible, bool tree, float distance, float limit)
        {
            if (tree || limit <= 0f) return visible;
            float t = Mathf.Clamp01((distance - limit * .5f) / (limit * .5f));
            float coverage = 1f - t * t * (3f - 2f * t);
            return Mathf.Clamp(Mathf.RoundToInt(visible * coverage), 0, visible);
        }

        void UpdateDetailShadowSelection(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game || !Application.isPlaying
                || m_AppearanceCaptureCamera != null) return;
            if (!m_DetailBrgUsesGpuGeneration || m_DenseDetailShadowDistance <= 0f)
            { m_DetailShadowSelectionReady = false; return; }
            Vector3 position = camera.transform.position;
            if (m_DetailShadowSelectionReady && m_ShadowBuiltVersion == m_ShadowSelectionVersion
                && position.Equals(m_ShadowCameraPosition) && m_ShadowDistance == m_DenseDetailShadowDistance) return;
            using var profile = s_ShadowSelectionMarker.Auto();
            m_NextShadowCells.Clear();
            foreach (var group in m_ActiveDetailGpuBuildGroups)
            {
                if (group.shadowCasting == ShadowCastingMode.Off) continue;
                foreach (var cell in group.residentCells.Values)
                {
                    if (cell.tick != m_ResidentGpuTick || !m_ResidentVisibleCounts.TryGetValue(cell.chunk, out int visible)) continue;
                    m_ResidentTreeLods.TryGetValue(cell.chunk, out int lod);
                    if (!TreeBatchVisible(group.batch, lod)) continue;
                    int count = SelectShadowPopulation(visible, group.batch.prototype.Kind == InstanceKind.Tree,
                        Mathf.Sqrt(cell.chunk.worldBounds.SqrDistance(position)), m_DenseDetailShadowDistance);
                    if (count > 0) m_NextShadowCells.Add(new ShadowCell(cell, count));
                }
            }
            bool same = m_DetailShadowSelectionReady && m_ShadowBuiltVersion == m_ShadowSelectionVersion
                && m_NextShadowCells.Count == m_ShadowCells.Count;
            if (same) for (int i = 0; i < m_NextShadowCells.Count; i++)
                if (m_NextShadowCells[i].cell != m_ShadowCells[i].cell || m_NextShadowCells[i].count != m_ShadowCells[i].count)
                { same = false; break; }
            m_ShadowCameraPosition = position; m_ShadowDistance = m_DenseDetailShadowDistance;
            if (same) return;
            m_ShadowCells.Clear(); m_ShadowCells.AddRange(m_NextShadowCells);
            m_ShadowIndexScratch.Clear(); m_DetailShadowGroups.Clear();
            GpuProceduralBuildGroup current = null;
            int offset = 0;
            foreach (var selection in m_ShadowCells)
            {
                var cell = selection.cell;
                if (current != cell.group)
                {
                    AddShadowGroup(current, offset);
                    current = cell.group; offset = m_ShadowIndexScratch.Count;
                }
                AppendShadowIndices(cell, selection.count);
            }
            AddShadowGroup(current, offset);
            int total = m_ShadowIndexScratch.Count;
            if (total > 0 && (!m_DetailShadowIndices.IsCreated || m_DetailShadowIndices.Length < total))
            {
                if (m_DetailShadowIndices.IsCreated) m_DetailShadowIndices.Dispose();
                m_DetailShadowIndices = new NativeArray<int>(Mathf.NextPowerOfTwo(Mathf.Max(64, total)), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            for (int i = 0; i < total; i++) m_DetailShadowIndices[i] = m_ShadowIndexScratch[i];
            m_DetailShadowCount = total; m_ShadowBuiltVersion = m_ShadowSelectionVersion;
            m_DetailShadowSelectionReady = true;
        }
        void AddShadowGroup(GpuProceduralBuildGroup group, int offset)
        {
            int count = m_ShadowIndexScratch.Count - offset;
            if (group == null || count <= 0) return;
            m_DetailShadowGroups.Add(new BrgPreparedGroup(GetOrRegisterBrgMesh(group.batch.mesh),
                GetOrRegisterBrgMaterial(group.batch.material), group.batch.subMesh, offset, count,
                group.shadowCasting, group.batch.prototype.ReceiveShadows));
        }
        void AppendShadowIndices(ResidentGpuCell cell, int selected)
        {
            if (selected == cell.population)
            {
                for (int i = 0; i < selected; i++) m_ShadowIndexScratch.Add(cell.start + i);
                return;
            }
            int source = 0, resident = 0, chosen = 0;
            int visible = m_ResidentVisibleCounts[cell.chunk];
            int sourceCount = Mathf.Max(1, cell.chunk.instanceCount);
            foreach (var spawn in cell.chunk.proceduralSpawns)
            {
                source += spawn.count;
                int residentEnd = (int)((long)source * cell.population / sourceCount);
                int visibleEnd = (int)((long)residentEnd * visible / Mathf.Max(1, cell.population));
                int chosenEnd = (int)((long)visibleEnd * selected / Mathf.Max(1, visible));
                for (int i = 0; i < chosenEnd - chosen; i++) m_ShadowIndexScratch.Add(cell.start + resident + i);
                resident = residentEnd; chosen = chosenEnd;
            }
        }
        bool UsesDetailShadowSelection(BatchCullingContext context) => m_DetailShadowSelectionReady
            && context.viewType == BatchCullingViewType.Light && m_AppearanceCaptureCamera == null;

        internal unsafe void WriteDetailShadowCommands(BatchCullingOutputDrawCommands* output,
            ref int direct, ref int visible, ref int range)
        {
            if (m_DetailShadowCount == 0) return;
            UnsafeUtility.MemCpy(output->visibleInstances + visible, m_DetailShadowIndices.GetUnsafeReadOnlyPtr(), sizeof(int) * (long)m_DetailShadowCount);
            foreach (var group in m_DetailShadowGroups)
            {
                uint index = (uint)direct;
                output->drawCommands[direct++] = new BatchDrawCommand { visibleOffset = group.visibleOffset + (uint)visible,
                    visibleCount = group.visibleCount, batchID = m_DetailBrgBatchId, materialID = group.materialId,
                    meshID = group.meshId, submeshIndex = group.subMesh, splitVisibilityMask = ushort.MaxValue };
                output->drawRanges[range++] = new BatchDrawRange { drawCommandsType = BatchDrawCommandType.Direct,
                    drawCommandsBegin = index, drawCommandsCount = 1, filterSettings = new BatchFilterSettings {
                        batchLayer = DetailBatchLayer, renderingLayerMask = uint.MaxValue, layer = (byte)gameObject.layer,
                        shadowCastingMode = group.shadowCasting, receiveShadows = group.receiveShadows } };
            }
            visible += m_DetailShadowCount;
        }
        void ReleaseDetailShadowSelection()
        {
            m_DetailShadowSelectionReady = false; m_DetailShadowCount = 0; m_ShadowBuiltVersion = -1;
            m_ShadowCells.Clear(); m_NextShadowCells.Clear(); m_DetailShadowGroups.Clear(); m_ShadowIndexScratch.Clear();
            if (m_DetailShadowIndices.IsCreated) m_DetailShadowIndices.Dispose();
        }
    }
}
#endif
