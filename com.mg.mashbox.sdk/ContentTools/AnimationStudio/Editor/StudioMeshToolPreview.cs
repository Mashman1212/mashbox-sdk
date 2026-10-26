using Undo = MashBoxSDK.AnimationStudio.StudioUndo;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    // A transaction owns only the touched meshes. Geometry is evaluated off-scene
    // from an immutable source, then published atomically into existing Mesh objects.
    internal sealed partial class StudioMeshToolPreview : IDisposable
    {
        internal sealed partial class Target
        {
            internal readonly Mesh Live, Source;
            internal readonly StudioModelDocument.Node Work;
            internal readonly StudioMeshCuts Topology;
            private readonly StudioModelDocument.Node[] nodes;
            private readonly int[][] polygons, triangles, groups;
            internal int PublishedVersion;
            internal bool StructureChanged;
            internal int VertexUpdateStart;
            internal IList<Vector3> PublishedPositions;
            internal StudioMeshCuts.BevelVertexMap Welds;
            internal StudioMeshCuts.BevelVertexUpdate VertexUpdate;
            private readonly bool wasDirty;
            internal Target(StudioModelDocument document, StudioModelDocument.Node node)
            {
                Live = node.mesh; wasDirty = EditorUtility.IsDirty(Live); Source = StudioWorkspaceText.ReadableSnapshot(Live);
                Source.hideFlags = HideFlags.HideAndDontSave;
                Work = new StudioModelDocument.Node { mesh = StudioWorkspaceText.ReadableSnapshot(Live), skinned = node.skinned, blendShape = node.blendShape };
                Work.mesh.hideFlags = HideFlags.HideAndDontSave;
                nodes = document.nodes.Where(n => n.mesh == Live).ToArray();
                polygons = nodes.Select(n => n.cutPolygons?.ToArray()).ToArray();
                triangles = nodes.Select(n => n.cutTriangles?.ToArray()).ToArray();
                groups = nodes.Select(n => n.normalGroups?.ToArray()).ToArray();
                Reset(); Work.mesh.MarkDynamic(); Live.MarkDynamic(); Topology = new StudioMeshCuts(Work);
                PublishedVersion = EditorUtility.GetDirtyCount(Live);
            }
            internal void Reset()
            {
                ReleaseGpuPreview(); IsBevelPreview=false;
                VertexUpdateStart = 0; PublishedPositions = null; Welds = null; VertexUpdate=null;
                CopyMesh(Source, Work.mesh);
                Work.cutPolygons = polygons[0]; Work.cutTriangles = triangles[0]; Work.normalGroups = groups[0];
            }
            internal void Publish()
            {
                StudioMeshCuts.ProfileBevelUpload("publish.start");
                StructureChanged = Live.vertexCount != Work.mesh.vertexCount || !SameArray(nodes[0].cutPolygons, Work.cutPolygons) || !SameArray(nodes[0].cutTriangles, Work.cutTriangles);
                StudioMeshCuts.ProfileBevelUpload("publish.structure");
                int start = !StructureChanged ? VertexUpdateStart : 0;
                // Reuse the validated affected-range packet. Taking a fresh native
                // read-only snapshot here can copy every source vertex even when
                // only a few corners changed.
                bool updated=!StructureChanged && VertexUpdate!=null && VertexUpdate.Apply(Live,true);
                if (!updated && !CopyVertexStreams(Work.mesh, Live, start)) CopyMesh(Work.mesh, Live);
                foreach (var node in nodes)
                { node.cutPolygons = Work.cutPolygons; node.cutTriangles = Work.cutTriangles; node.normalGroups = Work.normalGroups; }
                StudioMeshCuts.ProfileBevelUpload("publish.finish");
                EditorUtility.SetDirty(Live); PublishedVersion = EditorUtility.GetDirtyCount(Live);
                StudioMeshCuts.ProfileBevelUpload("publish.dirty");
                PublicationSequence++;
            }
            internal void Restore(bool afterUndo = false)
            {
                ReleaseGpuPreview(); IsBevelPreview=false;
                if (!Live) return; // Native Undo may have removed the created mesh.
                // Native Undo may already have restored older geometry. Never overwrite it.
                if (afterUndo && PublishedVersion != EditorUtility.GetDirtyCount(Live)) return;
                StructureChanged = true;
                CopyMesh(Source, Live);
                for (int i = 0; i < nodes.Length; i++)
                { nodes[i].cutPolygons = polygons[i]; nodes[i].cutTriangles = triangles[i]; nodes[i].normalGroups = groups[i]; }
                if (wasDirty) EditorUtility.SetDirty(Live); else EditorUtility.ClearDirty(Live);
            }
            internal void Dispose()
            { ReleaseGpuPreview(); UnityEngine.Object.DestroyImmediate(Source); UnityEngine.Object.DestroyImmediate(Work.mesh); }
        }
        internal readonly Target[] Targets;
        private readonly StudioModelDocument document;
        private readonly bool documentWasDirty;
        internal bool HasChanges;
        internal StudioMeshToolPreview(StudioModelDocument document, IEnumerable<int> indices)
        {
            this.document = document; documentWasDirty = EditorUtility.IsDirty(document);
            var targets = new List<Target>(); var seen = new HashSet<Mesh>();
            try { foreach (int index in indices) if (seen.Add(document.nodes[index].mesh)) targets.Add(new Target(document, document.nodes[index])); }
            catch { foreach (var target in targets) target.Dispose(); throw; }
            Targets = targets.ToArray();
        }
        private static bool SameArray(int[] a, int[] b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b));
        private static bool CopyVertexStreams(Mesh source, Mesh destination, int vertexStart = 0)
        {
            if (source.vertexCount != destination.vertexCount || source.vertexBufferCount != destination.vertexBufferCount ||
                source.subMeshCount != destination.subMeshCount || source.indexFormat != destination.indexFormat) return false;
            using var data = MeshUtility.AcquireReadOnlyMeshData(source);
            using (var previous = MeshUtility.AcquireReadOnlyMeshData(destination))
            {
                var a = data[0]; var b = previous[0];
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                    if (a.GetVertexBufferStride(stream) != b.GetVertexBufferStride(stream)) return false;
                for (int attribute = 0; attribute <= (int)UnityEngine.Rendering.VertexAttribute.BlendIndices; attribute++)
                {
                    var type = (UnityEngine.Rendering.VertexAttribute)attribute;
                    if (a.HasVertexAttribute(type) != b.HasVertexAttribute(type)) return false;
                    if (a.HasVertexAttribute(type) && (a.GetVertexAttributeFormat(type) != b.GetVertexAttributeFormat(type) ||
                        a.GetVertexAttributeDimension(type) != b.GetVertexAttributeDimension(type) ||
                        a.GetVertexAttributeStream(type) != b.GetVertexAttributeStream(type) || a.GetVertexAttributeOffset(type) != b.GetVertexAttributeOffset(type))) return false;
                }
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var x = a.GetSubMesh(sub); var y = b.GetSubMesh(sub);
                    if (x.topology != y.topology || x.indexStart != y.indexStart || x.indexCount != y.indexCount || x.baseVertex != y.baseVertex) return false;
                }
                // A bevel's stable metadata token proves its indices unchanged.
                // Other tools retain the full native comparison.
                if (vertexStart == 0)
                {
                    var indices = a.GetIndexData<ushort>(); var oldIndices = b.GetIndexData<ushort>();
                    if (indices.Length != oldIndices.Length) return false;
                    for (int i = 0; i < indices.Length; i++) if (indices[i] != oldIndices[i]) return false;
                }
            }
            const UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;
            for (int stream = 0; stream < source.vertexBufferCount; stream++)
            {
                var bytes = data[0].GetVertexData<byte>(stream); int offset = vertexStart * source.GetVertexBufferStride(stream);
                destination.SetVertexBufferData(bytes, offset, offset, bytes.Length - offset, stream, flags);
            }
            destination.bounds = source.bounds;
            destination.MarkModified();
            return true;
        }
        internal static void CopyMesh(Mesh source, Mesh destination)
        {
            // CopySerialized changes CPU geometry but can leave an already-bound
            // renderer using the old GPU buffers. Apply native mesh data so topology
            // replacement invalidates its buffers through the normal Mesh API.
            using var sourceData = MeshUtility.AcquireReadOnlyMeshData(source);
            var writable = Mesh.AllocateWritableMeshData(1);
            try
            {
                var input = sourceData[0]; var output = writable[0];
                output.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < input.vertexBufferCount; stream++)
                    input.GetVertexData<byte>(stream).CopyTo(output.GetVertexData<byte>(stream));
                if (source.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
                {
                    var indices = input.GetIndexData<int>(); output.SetIndexBufferParams(indices.Length, source.indexFormat);
                    indices.CopyTo(output.GetIndexData<int>());
                }
                else
                {
                    var indices = input.GetIndexData<ushort>(); output.SetIndexBufferParams(indices.Length, source.indexFormat);
                    indices.CopyTo(output.GetIndexData<ushort>());
                }
                output.subMeshCount = source.subMeshCount;
                const UnityEngine.Rendering.MeshUpdateFlags flags = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;
                for (int sub = 0; sub < source.subMeshCount; sub++) output.SetSubMesh(sub, input.GetSubMesh(sub), flags);
                Mesh.ApplyAndDisposeWritableMeshData(writable, destination, flags);
                writable = default;
                destination.bounds = source.bounds;
            }
            finally { if (writable.Length != 0) writable.Dispose(); }
        }
        internal void Publish(bool changed)
        { foreach (var target in Targets) target.Publish(); HasChanges = changed; }
        internal void Restore(bool afterUndo = false)
        { foreach (var target in Targets) target.Restore(afterUndo); if (!afterUndo && !documentWasDirty) EditorUtility.ClearDirty(document); }
        internal void Commit(string label, Action recordSelection = null)
        {
            if (!HasChanges) return;
            Restore();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            var undoObjects = new List<UnityEngine.Object> { document };
            undoObjects.AddRange(Targets.Select(t => (UnityEngine.Object)t.Live));
            Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), label);
            foreach (var target in Targets) target.Publish();
            EditorUtility.SetDirty(document); recordSelection?.Invoke(); Undo.CollapseUndoOperations(group);
            // Do not let the next unrelated action join this transaction.
            Undo.IncrementCurrentGroup();
        }
        public void Dispose() { foreach (var target in Targets) target.Dispose(); }
    }
}
