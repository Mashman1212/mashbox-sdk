using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace MG.GodSurface
{
    public sealed partial class MGSurfaceWorld
    {
#if UNITY_EDITOR
        // Editor validation observes its own camera; Scene view rendering can interleave render requests.
        internal Action<int, LODParameters, int, int> cameraDiagnostics;
#endif
        static readonly Unity.Profiling.ProfilerMarker cullMarker = new Unity.Profiling.ProfilerMarker("MGSurface.Cull");
        BatchRendererGroup brg;
        GraphicsBuffer buffer;
        BatchID[] batches;
        // 96-byte zero prefix is aligned for both 48-byte matrices and 16-byte vectors.
        const int Prefix = 96;
        readonly float[] matrixScratch = new float[12];
        int MatrixAddress(int stream) => Prefix + entries.Count * 48 * stream;
        int LightingAddress => Prefix + entries.Count * 96;
        int SurfaceAddress(int submesh) => GeometryAddress + entries.Count * 4 * (submesh + 1);

        int GeometryAddress => LightingAddress + entries.Count * 128;

        void CreateBuffer()
        {
            int streams = 1; foreach (var e in entries) streams = Mathf.Max(streams, e.surfaceIDs.Length);
            batches = new BatchID[streams];
            InstanceBufferBytes = Prefix + (long)entries.Count * (96 + 128 + 4 * (streams + 1));
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, checked((int)(InstanceBufferBytes / 4)), 4);
            var values = new float[InstanceBufferBytes / 4];
            for (int i = 0; i < entries.Count; i++)
            {
                values[GeometryAddress / 4 + i] = entries[i].geometryID;
                Pack(entries[i].matrix, values, MatrixAddress(0) / 4 + i * 12);
                Pack(entries[i].matrix.inverse, values, MatrixAddress(1) / 4 + i * 12);
                for (int part = 0; part < entries[i].surfaceIDs.Length; part++) values[SurfaceAddress(part) / 4 + i] = entries[i].surfaceIDs[part];
            }
            buffer.SetData(values); UpdateLighting();
            using (var metadataStorage = new NativeArray<MetadataValue>(7, Allocator.Temp))
            {
                var metadata = metadataStorage;
                metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = 0x80000000u | (uint)MatrixAddress(0) };
                metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = 0x80000000u | (uint)MatrixAddress(1) };
                // The pilot is static rendering: camera motion vectors use the same world matrices.
                metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousM"), Value = 0x80000000u | (uint)MatrixAddress(0) };
                metadata[3] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousMI"), Value = 0x80000000u | (uint)MatrixAddress(1) };
                metadata[4] = new MetadataValue { NameID = Shader.PropertyToID("unity_SHCoefficients"), Value = 0x80000000u | (uint)LightingAddress };
                metadata[6] = new MetadataValue { NameID = Shader.PropertyToID("_MGGeometryID"), Value = 0x80000000u | (uint)GeometryAddress };
                // Each submesh gets its own ID stream. Matrix and lighting streams stay shared.
                for (int part = 0; part < streams; part++)
                {
                    metadata[5] = new MetadataValue { NameID = Shader.PropertyToID("_MGSurfaceID"), Value = 0x80000000u | (uint)SurfaceAddress(part) };
                    batches[part] = brg.AddBatch(metadata, buffer.bufferHandle);
                }
            }
        }
        static void Pack(Matrix4x4 m, float[] values, int offset)
        {
            for (int column = 0; column < 4; column++) for (int row = 0; row < 3; row++) values[offset + column * 3 + row] = m[row, column];
        }
        void UploadMatrix(int index, Matrix4x4 m)
        {
            Pack(m, matrixScratch, 0); buffer.SetData(matrixScratch, 0, MatrixAddress(0) / 4 + index * 12, 12);
            Pack(m.inverse, matrixScratch, 0); buffer.SetData(matrixScratch, 0, MatrixAddress(1) / 4 + index * 12, 12);
        }
        public void UpdateLighting()
        {
            if (buffer == null || entries.Count == 0) return;
            var positions = new Vector3[registeredProps]; var probes = new SphericalHarmonicsL2[registeredProps]; var occlusion = new Vector4[registeredProps];
            var useProbe = new bool[registeredProps];
            foreach (int i in drawEntries) { var e = entries[i]; positions[e.propIndex] = e.bounds.center; useProbe[e.propIndex] = e.renderer.lightProbeUsage != LightProbeUsage.Off; }
            if (LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, probes, occlusion);
            else for (int i = 0; i < registeredProps; i++) { probes[i] = RenderSettings.ambientProbe; occlusion[i] = Vector4.one; }
            var values = new Vector4[entries.Count * 8];
            for (int stream = 0; stream < 8; stream++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    int prop = entries[i].propIndex;
                    var p = useProbe[prop] ? probes[prop] : RenderSettings.ambientProbe;
                    int channel = stream % 3;
                    int offset = i * 8 + stream;
                    if (stream < 3) values[offset] = new Vector4(p[channel, 3], p[channel, 1], p[channel, 2], p[channel, 0] - p[channel, 6]);
                    else if (stream < 6) values[offset] = new Vector4(p[channel, 4], p[channel, 5], 3 * p[channel, 6], p[channel, 7]);
                    else if (stream == 6) values[offset] = new Vector4(p[0, 8], p[1, 8], p[2, 8], 1);
                    else values[offset] = occlusion[prop];
                }
            }
            buffer.SetData(values, 0, LightingAddress / 16, values.Length);
        }

        static unsafe T* Allocate<T>(int count) where T : unmanaged => count == 0 ? null :
            (T*)UnsafeUtility.Malloc((long)UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<long>(), Allocator.TempJob);

        unsafe JobHandle Cull(BatchRendererGroup renderer, BatchCullingContext context, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            using var sample = cullMarker.Auto(); using var timing = new MeasuredScope(this, false);
            var output = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr(); *output = default;
            if (entries.Count == 0 || (context.viewType != BatchCullingViewType.Camera && context.viewType != BatchCullingViewType.Light)) return default;
            bool camera = context.viewType == BatchCullingViewType.Camera;
            int splitCount = camera || context.cullingSplits.Length == 0 ? 1 : Mathf.Min(16, context.cullingSplits.Length);
            int bucketCount = groups.Count * splitCount;
            bool sharedLods = lodSelection == LodSelection.PerCellFamily;
            int lodCount = sharedLods ? cellLods.Count : lods.Count;
            using var masks = new NativeArray<ushort>(drawEntries.Count, Allocator.TempJob);
            using var counts = new NativeArray<int>(bucketCount, Allocator.TempJob);
            using var selected = new NativeArray<int>(lodCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var seen = new NativeArray<byte>(registeredProps, Allocator.TempJob);
            using var visibleDraws = new NativeArray<int>(drawEntries.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            using var totals = new NativeArray<int>(3, Allocator.TempJob);
            using var allowedStorage = new NativeArray<byte>(groups.Count, Allocator.TempJob); var allowed = allowedStorage;
            for(int g=0;g<groups.Count;g++)
            {
                var key=groups[g].key;
                allowed[g]=(byte)(((context.cullingLayerMask & (1u << key.layer)) != 0 &&
                    (camera ? key.shadows != ShadowCastingMode.ShadowsOnly : key.shadows != ShadowCastingMode.Off)) ? 1 : 0);
            }
            int debugCount=0;
#if UNITY_EDITOR
            CellDebugState[] debugStates=null;
            if(camera && drawCellBounds && cellDebugViews.TryGetValue(context.viewID.GetInstanceID(),out debugStates))debugCount=cells.Count;
#endif
            using var debug = new NativeArray<byte>(debugCount, Allocator.TempJob);
            var view=context.lodParameters;
            new CullJob { entries=cullEntries, cells=cullCells, lods=sharedLods?cullCellLods:cullLods,
                members=cullCellEntries, groups=cullGroups, thresholds=cullThresholds, planes=context.cullingPlanes,
                splits=context.cullingSplits, allowed=allowed, selected=selected, counts=counts, visible=visibleDraws, totals=totals,
                masks=masks, seen=seen, debug=debug, cameraPosition=view.cameraPosition, drawDistanceSquared=Mathf.Max(1,cellDrawDistance)*Mathf.Max(1,cellDrawDistance),
                denominatorScale=2*Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f), orthoDenominator=2*view.orthoSize,
                lodBias=lodBias, splitCount=splitCount, exclusionMask=context.splitExclusionMask,
                camera=camera, sharedLods=sharedLods, orthographic=view.isOrthographic }.Run();
            int visibleProps=totals[1], visibleCells=totals[2];
            if(camera)LastLodEvaluations=lodCount;
#if UNITY_EDITOR
            if(debugStates!=null)for(int i=0;i<debugCount;i++)debugStates[i]=(CellDebugState)debug[i];
#endif
            int commands = 0, visible = 0;
            for (int i = 0; i < bucketCount; i++) if (counts[i] > 0) { commands++; visible += counts[i]; }
            if (camera) { LastVisibleInstances = visibleProps; LastVisibleCells = visibleCells; LastDrawCommands = commands; }
#if UNITY_EDITOR
            if (camera) cameraDiagnostics?.Invoke(context.viewID.GetInstanceID(), context.lodParameters, visibleProps, commands);
#endif
            if (commands == 0) return default;
            output->drawCommandCount = commands; output->drawRangeCount = commands; output->visibleInstanceCount = visible;
            output->drawCommands = Allocate<BatchDrawCommand>(commands); output->drawRanges = Allocate<BatchDrawRange>(commands); output->visibleInstances = Allocate<int>(visible);
            using var offsetsStorage = new NativeArray<int>(bucketCount, Allocator.TempJob);
            var offsets = offsetsStorage;
            int command = 0, offset = 0;
            for (int bucket = 0; bucket < bucketCount; bucket++)
            {
                if (counts[bucket] == 0) continue;
                int g = bucket / splitCount, split = bucket % splitCount;
                var group = groups[g]; var key = group.key; offsets[bucket] = offset;
                output->drawCommands[command] = new BatchDrawCommand
                {
                    batchID = batches[key.submesh], meshID = group.mesh, materialID = group.material, submeshIndex = (ushort)key.submesh,
                    visibleOffset = (uint)offset, visibleCount = (uint)counts[bucket], splitVisibilityMask = camera || context.cullingSplits.Length == 0 ? ushort.MaxValue : (ushort)(1 << split),
                    flags = key.flipped ? BatchDrawCommandFlags.FlipWinding : BatchDrawCommandFlags.None, sortingPosition = 0
                };
                output->drawRanges[command] = new BatchDrawRange
                {
                    drawCommandsType = BatchDrawCommandType.Direct, drawCommandsBegin = (uint)command, drawCommandsCount = 1,
                    filterSettings = new BatchFilterSettings { batchLayer = 2, layer = (byte)key.layer, renderingLayerMask = key.renderingLayers,
                        shadowCastingMode = key.shadows, receiveShadows = key.receive, motionMode = MotionVectorGenerationMode.Camera }
                };
                offset += counts[bucket]; command++;
            }
            new FillVisibleJob { entries=cullEntries, visible=visibleDraws, groups=cullGroups, masks=masks,
                allowed=allowed, offsets=offsets, output=output->visibleInstances, count=totals[0], splitCount=splitCount }.Run();
            return default;
        }
    }
}

