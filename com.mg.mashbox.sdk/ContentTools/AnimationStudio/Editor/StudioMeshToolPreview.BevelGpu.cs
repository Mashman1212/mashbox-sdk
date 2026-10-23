using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshToolPreview
    {
        internal sealed partial class Target
        {
            internal bool IsBevelPreview;
            private int PublicationSequence, gpuSequence;
            private BevelGpuMesh gpuPreview;
            private int[] gpuTriangles, gpuPolygons;
            private bool gpuUnavailable;
            internal Mesh RenderMesh => gpuPreview?.Mesh ?? Live;
            internal void UseGpuPreview(MeshFilter filter)
            {
                if (!filter || !IsBevelPreview || gpuUnavailable || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;
                try
                {
                    bool rebuild=gpuPreview==null || !ReferenceEquals(gpuTriangles,Work.cutTriangles) ||
                        !ReferenceEquals(gpuPolygons,Work.cutPolygons) || (gpuSequence!=PublicationSequence && VertexUpdate==null);
                    if (rebuild)
                    {
                        ReleaseGpuPreview();
                        gpuPreview=new BevelGpuMesh(Work.mesh,Live);
                        gpuTriangles=Work.cutTriangles; gpuPolygons=Work.cutPolygons; gpuSequence=PublicationSequence;
                    }
                    if (gpuSequence!=PublicationSequence)
                    { gpuPreview.Apply(VertexUpdate); gpuSequence=PublicationSequence; }
                    gpuPreview.Attach(filter);
                }
                catch
                {
                    ReleaseGpuPreview(); gpuUnavailable=true;
#if MASHBOX_STUDIO_TESTS
                    throw;
#endif
                    // Native CPU publication is already current. Unsupported GPU
                    // buffers retain that renderer path and normal tool semantics.
                }
            }
            private void ReleaseGpuPreview()
            { gpuPreview?.Dispose(); gpuPreview=null; gpuTriangles=gpuPolygons=null; }
        }
        // GPU-only render mirror. Its CPU attributes never change during a
        // stable Width drag, preventing Unity from uploading the untouched mesh.
        // CPU Work/Live meshes remain authoritative for editing, picking/history.
        private sealed class BevelGpuMesh : IDisposable
        {
            internal Mesh Mesh { get; private set; }
            private readonly Mesh live;
            private readonly List<MeshFilter> filters=new List<MeshFilter>();
            private GraphicsBuffer[] buffers;
            internal BevelGpuMesh(Mesh source,Mesh live)
            {
                this.live=live;
                try
                {
                    Mesh=new Mesh { name="Studio live bevel GPU preview", hideFlags=HideFlags.HideAndDontSave };
                    CopyMesh(source,Mesh); Mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
                    int streams=0; foreach (var attribute in Mesh.GetVertexAttributes()) streams=Math.Max(streams,attribute.stream+1);
                    buffers=new GraphicsBuffer[streams];
                    for (int stream=0;stream<streams;stream++) buffers[stream]=Mesh.GetVertexBuffer(stream);
                }
                catch { Dispose(); throw; }
            }
            internal void Attach(MeshFilter filter)
            {
                if (!filters.Contains(filter)) filters.Add(filter);
                if (filter.sharedMesh!=Mesh) filter.sharedMesh=Mesh;
            }
            internal void Apply(StudioMeshCuts.BevelVertexUpdate update)
            {
                if (update==null || update.Streams.Length!=buffers.Length || update.Count!=Mesh.vertexCount)
                    throw new InvalidOperationException("The bevel GPU layout changed without a topology rebuild.");
                for (int stream=0;stream<buffers.Length;stream++)
                    buffers[stream].SetData(update.Streams[stream],0,update.Start*update.Strides[stream],update.Streams[stream].Length);
                Mesh.bounds=update.Bounds;
            }
            public void Dispose()
            {
                foreach (var filter in filters) if (filter && filter.sharedMesh==Mesh) filter.sharedMesh=live ? live : null;
                filters.Clear();
                if (buffers!=null) foreach (var buffer in buffers) buffer?.Dispose();
                buffers=null;
                if (Mesh) UnityEngine.Object.DestroyImmediate(Mesh); Mesh=null;
            }
        }
    }
}
