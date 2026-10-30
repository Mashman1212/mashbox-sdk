using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMeshCuts
    {
        // Immutable source attributes and untouched-face publication data belong
        // to the captured tool source, not to a mutable preview Mesh.
        private sealed class BevelSourceData
        {
            internal readonly Color[] Colors;
            internal readonly Vector3[] Normals, RetainedNormals;
            internal readonly Vector4[] Tangents;
            internal readonly Vector4[][] UV = new Vector4[8][];
            internal readonly int[] Dimensions = new int[8];
            internal readonly int Submeshes;
            internal BevelSourceData(Mesh mesh, Vector3[] positions)
            {
                Colors = mesh.colors; Normals = mesh.normals; Tangents = mesh.tangents; Submeshes = mesh.subMeshCount;
                var values = new List<Vector4>();
                for (int c = 0; c < 8; c++)
                {
                    values.Clear(); mesh.GetUVs(c, values);
                    if (values.Count == 0) continue;
                    UV[c] = values.ToArray(); Dimensions[c] = mesh.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + c));
                }
                RetainedNormals = Normals;
                if (Normals.Length == positions.Length) return;
                RetainedNormals = new Vector3[positions.Length];
                for (int sub = 0; sub < Submeshes; sub++)
                {
                    var indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], d = indices[i + 2];
                        var normal = Vector3.Cross(positions[b] - positions[a], positions[d] - positions[a]);
                        RetainedNormals[a] += normal; RetainedNormals[b] += normal; RetainedNormals[d] += normal;
                    }
                }
                for (int i = 0; i < RetainedNormals.Length; i++) RetainedNormals[i] = BevelUnit(RetainedNormals[i]);
            }
        }
        private BevelSourceData bevelSourceData;
        private BevelSourceData BevelSource(Mesh mesh) => bevelSourceData ?? (bevelSourceData = new BevelSourceData(mesh, Positions));
        internal sealed class BevelVertexMap
        {
            private readonly List<int> ids=new List<int>(),next=new List<int>();
            private readonly Dictionary<int,int> staticHeads=new Dictionary<int,int>(),dynamicHeads=new Dictionary<int,int>();
            private int prefix;
            internal int[] CopyGeometry()=>ids.ToArray();
            internal void AddStatic(int geo)
            { int index=ids.Count; next.Add(staticHeads.TryGetValue(geo,out int head) ? head : -1); ids.Add(geo); staticHeads[geo]=index; prefix=ids.Count; }
            internal void ResetDynamic()
            { TrimBevelTail(ids,prefix); TrimBevelTail(next,prefix); dynamicHeads.Clear(); }
            internal void AddDynamic(int geo)
            {
                int index=ids.Count;
                if (!dynamicHeads.TryGetValue(geo,out int head) && !staticHeads.TryGetValue(geo,out head)) head=-1;
                next.Add(head); ids.Add(geo); dynamicHeads[geo]=index;
            }
            internal void Expand(HashSet<int> selection)
            {
                var groups=new HashSet<int>(); foreach (int index in selection) if (index>=0 && index<ids.Count) groups.Add(ids[index]);
                foreach (int geo in groups)
                {
                    if (!dynamicHeads.TryGetValue(geo,out int head) && !staticHeads.TryGetValue(geo,out head)) continue;
                    for (int i=head;i>=0;i=next[i]) selection.Add(i);
                }
            }
        }
#if MASHBOX_STUDIO_TESTS
        internal static Action<string> BevelUploadStage;
#endif
        [System.Diagnostics.Conditional("MASHBOX_STUDIO_TESTS")]
        internal static void ProfileBevelUpload(string stage)
        {
#if MASHBOX_STUDIO_TESTS
            BevelUploadStage?.Invoke(stage);
#endif
        }
        internal sealed class BevelVertexUpdate
        {
            internal int Start, Count;
            internal float[][] Streams;
            internal int[] Strides;
            internal VertexAttributeDescriptor[] Attributes;
            internal Bounds Bounds;
            internal bool Apply(Mesh mesh, bool live=false)
            {
                ProfileBevelUpload(live?"live.start":"work.start");
                if (!mesh.isReadable || mesh.vertexCount!=Count) return false;
                // vertexBufferCount/GetVertexBufferStride can force dirty CPU
                // data to the GPU, including the off-scene working mesh. The
                // stable descriptor list already proves the stream layout.
                ProfileBevelUpload(live?"live.basic":"work.basic");
                var attributes=mesh.GetVertexAttributes(); if (attributes.Length!=Attributes.Length) return false;
                for (int i=0;i<attributes.Length;i++) if (!attributes[i].Equals(Attributes[i])) return false;
                ProfileBevelUpload(live?"live.layout":"work.layout");
                const MeshUpdateFlags flags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
                for (int stream=0;stream<Streams.Length;stream++)
                    mesh.SetVertexBufferData(Streams[stream],0,Start*Strides[stream],Streams[stream].Length,stream,flags);
                ProfileBevelUpload(live?"live.data":"work.data");
                mesh.bounds=Bounds; ProfileBevelUpload(live?"live.bounds":"work.bounds");
                mesh.MarkModified(); ProfileBevelUpload(live?"live.modified":"work.modified"); return true;
            }
        }
        private sealed class BevelPublication
        {
            internal BevelWavefrontPlan Plan;
            internal readonly BevelVertexMap Welds=new BevelVertexMap();
            internal readonly BevelVertexUpdate Update=new BevelVertexUpdate();
            internal Vector3[] StaticPositions, StaticNormals;
            internal Color[] StaticColors;
            internal Vector4[] StaticTangents;
            internal readonly Vector4[][] StaticUV = new Vector4[8][];
            internal int[] StaticGroups, StaticMetadata, CachedGeometry;
            internal int[][] StaticTriangles;
            internal readonly Dictionary<(int, Vector3), int> StaticSmoothing = new Dictionary<(int, Vector3), int>();
            internal readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<Vector4> Tangents = new List<Vector4>();
            internal readonly List<int> Groups = new List<int>(), Metadata = new List<int>();
            internal List<int>[] Triangles;
            internal readonly List<Vector4>[] UV = new List<Vector4>[8];
            internal readonly List<Vector2>[] UV2 = new List<Vector2>[8];
            internal readonly List<Vector3>[] UV3 = new List<Vector3>[8];
            internal int[] CachedMetadata, CachedIndices, CachedGroups;
            internal int[][] CachedTriangles;
            internal readonly Dictionary<(int, Vector3), int> DynamicSmoothing = new Dictionary<(int, Vector3), int>();
            internal readonly Vector4[] Values = new Vector4[8];
            internal readonly List<Vector3> PatchPositions = new List<Vector3>(), PatchNormals = new List<Vector3>();
            internal readonly List<Vector2> PatchUV = new List<Vector2>();
            internal readonly List<Vector4> PatchTangents = new List<Vector4>();
            internal readonly List<int> PatchIndices = new List<int>();
            internal readonly object TangentOwner=new object();
            internal int[] TangentIndices;
            internal Bounds StaticBounds;
            internal VertexAttributeDescriptor[] Attributes;
            internal float[][] StreamValues;
            internal int[] StreamStrides, AttributeOffsets;
        }
        // Unity's own tangent generator operates on the disconnected dynamic
        // render corners. Its inputs contain no static prefix vertices, so there
        // is no need to recalculate the untouched mesh during every Width edit.
        private static class BevelTangentWorkspace
        {
            internal static Mesh Mesh;
            internal static object Owner;
            static BevelTangentWorkspace()
            {
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
                UnityEditor.EditorApplication.quitting += Release;
            }
            private static void Release()
            { if (Mesh) UnityEngine.Object.DestroyImmediate(Mesh); Mesh = null; Owner = null; }
        }
        private static Mesh BevelTangentMesh()
        {
            if (!BevelTangentWorkspace.Mesh)
                BevelTangentWorkspace.Mesh=new Mesh { name="Studio bevel tangent workspace", hideFlags=HideFlags.HideAndDontSave };
            return BevelTangentWorkspace.Mesh;
        }
        private static void PrepareStaticBevelTangents(BevelPublication output)
        {
            // A mesh can have UVs without an authored tangent stream. The
            // untouched render vertices then need native tangents once, not a
            // full-mesh recalculation on every sample. Static and patch indices
            // are disjoint, so Unity's tangent accumulation is independent.
            var mesh=BevelTangentMesh(); mesh.Clear();
            mesh.indexFormat=output.StaticPositions.Length>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(output.StaticPositions); mesh.SetNormals(output.StaticNormals);
            mesh.SetUVs(0,output.StaticUV[0]); mesh.subMeshCount=output.StaticTriangles.Length;
            for (int sub=0;sub<output.StaticTriangles.Length;sub++) mesh.SetTriangles(output.StaticTriangles[sub],sub,false);
            mesh.RecalculateTangents(); output.StaticTangents=mesh.tangents;
            output.Tangents.Clear(); output.Tangents.AddRange(output.StaticTangents);
            BevelTangentWorkspace.Owner=null;
        }
        private static void RecalculateBevelPatchTangents(BevelPublication output)
        {
            int start=output.StaticPositions.Length;
            output.PatchPositions.Clear(); output.PatchNormals.Clear(); output.PatchUV.Clear();
            for (int i=start;i<output.Positions.Count;i++)
            {
                output.PatchPositions.Add(output.Positions[i]); output.PatchNormals.Add(output.Normals[i]);
                var uv=output.UV[0][i]; output.PatchUV.Add(new Vector2(uv.x,uv.y));
            }
            bool indicesChanged=!ReferenceEquals(output.TangentIndices,output.CachedIndices);
            if (indicesChanged)
            {
                output.PatchIndices.Clear();
                for (int sub=0;sub<output.Triangles.Length;sub++)
                    for (int i=output.StaticTriangles[sub].Length;i<output.Triangles[sub].Count;i++)
                        output.PatchIndices.Add(output.Triangles[sub][i]-start);
                output.TangentIndices=output.CachedIndices;
            }
            var mesh=BevelTangentMesh();
            bool rebuild=indicesChanged || !ReferenceEquals(BevelTangentWorkspace.Owner,output.TangentOwner) || mesh.vertexCount!=output.PatchPositions.Count;
            if (rebuild) { mesh.Clear(); mesh.indexFormat=output.PatchPositions.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16; }
            mesh.SetVertices(output.PatchPositions); mesh.SetNormals(output.PatchNormals); mesh.SetUVs(0,output.PatchUV);
            if (rebuild) mesh.SetTriangles(output.PatchIndices,0,false);
            BevelTangentWorkspace.Owner=output.TangentOwner;
            mesh.RecalculateTangents(); mesh.GetTangents(output.PatchTangents);
            for (int i=0;i<output.PatchTangents.Count;i++) output.Tangents[start+i]=output.PatchTangents[i];
        }
        private BevelPublication bevelPublication;
        private BevelPublication PrepareBevelPublication(Mesh mesh, BevelDraft draft, BevelSourceData source)
        {
            if (bevelPublication != null && ReferenceEquals(bevelPublication.Plan, draft.Plan)) return bevelPublication;
            var result = new BevelPublication { Plan = draft.Plan, StaticTriangles = new int[source.Submeshes][], Triangles = new List<int>[source.Submeshes], CachedTriangles = new int[source.Submeshes][] };
            for (int sub = 0; sub < source.Submeshes; sub++) result.Triangles[sub] = new List<int>();
            var originalRender = new Dictionary<int, int>();
            int Original(int index)
            {
                if (originalRender.TryGetValue(index, out int output)) return output;
                output = result.Positions.Count; originalRender.Add(index, output);
                result.Welds.AddStatic(welded[index]);
                result.Positions.Add(Positions[index]); result.Normals.Add(source.RetainedNormals[index]);
                var key = (welded[index], source.RetainedNormals[index].normalized);
                if (!result.StaticSmoothing.TryGetValue(key, out int group)) { group = result.StaticSmoothing.Count; result.StaticSmoothing.Add(key, group); }
                result.Groups.Add(group);
                if (source.Colors.Length > 0) result.Colors.Add(source.Colors[index]);
                if (source.Tangents.Length > 0) result.Tangents.Add(source.Tangents[index]);
                for (int c = 0; c < 8; c++) if (source.UV[c] != null) result.UV[c].Add(source.UV[c][index]);
                return output;
            }
            for (int c = 0; c < 8; c++) if (source.UV[c] != null)
            {
                result.UV[c] = new List<Vector4>();
                if (source.Dimensions[c] == 2) result.UV2[c] = new List<Vector2>();
                if (source.Dimensions[c] == 3) result.UV3[c] = new List<Vector3>();
            }
            var sourceTriangles = BevelSourceTriangles(mesh);
            foreach (int retained in draft.Plan.StaticFaces)
            {
                var face = Faces[retained]; var triangles = sourceTriangles[retained];
                if (triangles.Count != (face.Vertices.Count - 2) * 3) throw new BevelOverlap("An untouched source face has no matching render triangulation.");
                result.Metadata.Add(face.Submesh); result.Metadata.Add(face.Vertices.Count);
                foreach (int vertex in face.Vertices) result.Metadata.Add(Original(vertex));
                foreach (int vertex in triangles) result.Triangles[face.Submesh].Add(Original(vertex));
            }
            result.StaticPositions = result.Positions.ToArray(); result.StaticNormals = result.Normals.ToArray();
            if (result.StaticPositions.Length>0)
            { result.StaticBounds=new Bounds(result.StaticPositions[0],Vector3.zero); foreach (var point in result.StaticPositions) result.StaticBounds.Encapsulate(point); }
            result.StaticColors = result.Colors.ToArray(); result.StaticTangents = result.Tangents.ToArray();
            result.StaticGroups = result.Groups.ToArray(); result.StaticMetadata = result.Metadata.ToArray();
            for (int c = 0; c < 8; c++) if (result.UV[c] != null) result.StaticUV[c] = result.UV[c].ToArray();
            for (int sub = 0; sub < source.Submeshes; sub++) result.StaticTriangles[sub] = result.Triangles[sub].ToArray();
            if (source.UV[0]!=null && source.Tangents.Length==0 && result.StaticPositions.Length>0) PrepareStaticBevelTangents(result);
            return bevelPublication = result;
        }
        private static Bounds BevelOutputBounds(BevelPublication output)
        {
            int start=output.StaticPositions.Length;
            var bounds=start>0 ? output.StaticBounds : new Bounds(output.Positions[0],Vector3.zero);
            for (int i=start;i<output.Positions.Count;i++) bounds.Encapsulate(output.Positions[i]);
            return bounds;
        }
        private static bool UpdateBevelVertexSuffix(Mesh mesh, BevelPublication output)
        {
            if (output.Attributes==null)
            {
                var attributes=mesh.GetVertexAttributes();
                foreach (var attribute in attributes)
                    if (attribute.format!=VertexAttributeFormat.Float32 || attribute.dimension>4 ||
                        attribute.attribute>VertexAttribute.TexCoord7) return false;
                output.Attributes=attributes;
                int streams=0; foreach (var attribute in attributes) streams=Math.Max(streams,attribute.stream+1);
                output.StreamValues=new float[streams][];
                output.StreamStrides=new int[streams];
                output.AttributeOffsets=new int[attributes.Length];
                for (int i=0;i<attributes.Length;i++) output.AttributeOffsets[i]=mesh.GetVertexAttributeOffset(attributes[i].attribute)/4;
                for (int i=0;i<streams;i++) output.StreamStrides[i]=mesh.GetVertexBufferStride(i)/4;
            }
            int start=output.StaticPositions.Length,count=output.Positions.Count-start;
            for (int stream=0;stream<output.StreamValues.Length;stream++)
            {
                int length=count*output.StreamStrides[stream];
                if (output.StreamValues[stream]==null || output.StreamValues[stream].Length!=length)
                    output.StreamValues[stream]=new float[length];
            }
            for (int a=0;a<output.Attributes.Length;a++)
            {
                var attribute=output.Attributes[a]; var values=output.StreamValues[attribute.stream];
                int stride=output.StreamStrides[attribute.stream],offset=output.AttributeOffsets[a];
                for (int v=0;v<count;v++)
                {
                    int i=start+v; Vector4 value;
                    switch (attribute.attribute)
                    {
                        case VertexAttribute.Position: value=output.Positions[i]; break;
                        case VertexAttribute.Normal: value=output.Normals[i]; break;
                        case VertexAttribute.Tangent: value=output.Tangents[i]; break;
                        case VertexAttribute.Color: value=output.Colors[i]; break;
                        default: value=output.UV[(int)attribute.attribute-(int)VertexAttribute.TexCoord0][i]; break;
                    }
                    int target=v*stride+offset;
                    for (int component=0;component<attribute.dimension;component++) values[target+component]=value[component];
                }
            }
            var update=output.Update; update.Start=start; update.Count=output.Positions.Count;
            update.Streams=output.StreamValues; update.Strides=output.StreamStrides; update.Attributes=output.Attributes;
            update.Bounds=BevelOutputBounds(output);
            return update.Apply(mesh);
        }
        private static void TrimBevelTail<T>(List<T> list, int prefix)
        { if (list.Count > prefix) list.RemoveRange(prefix,list.Count-prefix); }
        private static bool SameBevelIndices(int[] array, List<int> list, int start = 0)
        {
            if (array == null || array.Length != list.Count) return false;
            for (int i = start; i < array.Length; i++) if (array[i] != list[i]) return false;
            return true;
        }
        private BevelResult WriteBevelDraft(StudioModelDocument.Node node, BevelDraft draft, int edgeCount, Action beforeWrite, double epsilon)
        {
            var mesh = node.mesh; var source = BevelSource(mesh); var output = PrepareBevelPublication(mesh, draft, source);
            // Immutable prefixes remain in these reusable buffers throughout a
            // gesture. Only remove and regenerate the affected suffix.
            TrimBevelTail(output.Positions, output.StaticPositions.Length);
            TrimBevelTail(output.Normals, output.StaticNormals.Length);
            TrimBevelTail(output.Colors, output.StaticColors.Length);
            TrimBevelTail(output.Tangents, output.StaticTangents.Length);
            TrimBevelTail(output.Groups, output.StaticGroups.Length);
            TrimBevelTail(output.Metadata, output.StaticMetadata.Length);
            output.DynamicSmoothing.Clear(); output.Welds.ResetDynamic();
            for (int c=0;c<8;c++) if (output.UV[c]!=null) TrimBevelTail(output.UV[c],output.StaticUV[c].Length);
            for (int sub=0;sub<source.Submeshes;sub++) TrimBevelTail(output.Triangles[sub],output.StaticTriangles[sub].Length);
            var render = new int[draft.Vertices.Count]; var used = new bool[render.Length];
            foreach (var polygon in draft.Polygons) if (polygon.OriginalFace < 0) foreach (int vertex in polygon.Vertices) used[vertex] = true;
            for (int v = 0; v < render.Length; v++)
            {
                if (!used[v]) continue;
                var vertex = draft.Vertices[v]; var sample = vertex.Sample; var normal = vertex.Normal.normalized;
                if (!BevelFinite(draft.Positions[vertex.Geo]) || !BevelFinite(normal)) throw new BevelOverlap("The bevel produced non-finite coordinates.");
                var key = (vertex.Geo, normal);
                if (!output.StaticSmoothing.TryGetValue(key, out int group) && !output.DynamicSmoothing.TryGetValue(key, out group))
                { group = output.StaticSmoothing.Count + output.DynamicSmoothing.Count; output.DynamicSmoothing.Add(key, group); }
                Color color = source.Colors.Length == Positions.Length ? source.Colors[sample.A] * sample.Weights.x + source.Colors[sample.B] * sample.Weights.y + source.Colors[sample.C] * sample.Weights.z : Color.white;
                Vector4 tangent = source.Tangents.Length == Positions.Length ? source.Tangents[sample.A] * sample.Weights.x + source.Tangents[sample.B] * sample.Weights.y + source.Tangents[sample.C] * sample.Weights.z : Vector4.zero;
                for (int c = 0; c < 8; c++) if (output.UV[c] != null)
                { output.Values[c] = source.UV[c][sample.A] * sample.Weights.x + source.UV[c][sample.B] * sample.Weights.y + source.UV[c][sample.C] * sample.Weights.z; }
                // Render slots are polygon corners, not hashes of evaluated
                // normals/UVs. Float equality can change during a Width drag and
                // must never add/remove vertices or invalidate the whole mesh.
                // Geometric IDs and normal groups still share editable seams.
                int match = output.Positions.Count; output.Welds.AddDynamic(vertex.Geo);
                output.Positions.Add(draft.Positions[vertex.Geo]); output.Normals.Add(normal); output.Groups.Add(group);
                if (source.Colors.Length > 0) output.Colors.Add(color); if (source.Tangents.Length > 0 || source.UV[0]!=null) output.Tangents.Add(tangent);
                for (int c = 0; c < 8; c++) if (output.UV[c] != null) output.UV[c].Add(output.Values[c]);
                render[v] = match;
            }
            foreach (var polygon in draft.Polygons)
            {
                if (polygon.OriginalFace >= 0) continue;
                var vertices = new List<int>(polygon.Vertices.Count); foreach (int vertex in polygon.Vertices) vertices.Add(render[vertex]);
                output.Metadata.Add(polygon.Material); output.Metadata.Add(vertices.Count); output.Metadata.AddRange(vertices);
                output.Triangles[polygon.Material].AddRange(BevelTriangulate(vertices, output.Positions, epsilon));
            }
            if (!SameBevelIndices(output.CachedMetadata, output.Metadata, output.StaticMetadata.Length)) output.CachedMetadata = output.Metadata.ToArray();
            if (!SameBevelIndices(output.CachedGroups, output.Groups, output.StaticGroups.Length)) output.CachedGroups = output.Groups.ToArray();
            bool newIndices = output.CachedIndices == null;
            for (int sub = 0; sub < source.Submeshes; sub++) if (!SameBevelIndices(output.CachedTriangles[sub], output.Triangles[sub], output.StaticTriangles[sub].Length))
            { output.CachedTriangles[sub] = output.Triangles[sub].ToArray(); newIndices = true; }
            if (newIndices)
            {
                int count = 0; foreach (var indices in output.CachedTriangles) count += indices.Length;
                output.CachedIndices = new int[count]; int offset = 0;
                foreach (var indices in output.CachedTriangles) { Array.Copy(indices, 0, output.CachedIndices, offset, indices.Length); offset += indices.Length; }
            }
            var format = output.Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            bool sameTopology = mesh.vertexCount == output.Positions.Count && mesh.subMeshCount == source.Submeshes && mesh.indexFormat == format &&
                ReferenceEquals(node.cutPolygons, output.CachedMetadata) && ReferenceEquals(node.cutTriangles, output.CachedIndices);
            bool patchTangents=source.UV[0]!=null;
            if (patchTangents) RecalculateBevelPatchTangents(output);
            beforeWrite?.Invoke();
            bool suffixPublished=sameTopology && UpdateBevelVertexSuffix(mesh,output);
            if (!suffixPublished)
            {
                // Validation and buffer preparation finish before touching the preview.
                if (!sameTopology) { mesh.Clear(); mesh.indexFormat = format; output.Attributes=null; }
                mesh.SetVertices(output.Positions); mesh.SetNormals(output.Normals);
                if (source.Colors.Length > 0) mesh.SetColors(output.Colors);
                for (int c = 0; c < 8; c++) if (output.UV[c] != null)
                {
                    if(source.Dimensions[c]==1)StudioSubdivisionData.SetUV1(mesh,c,output.UV[c]);
                    else if (source.Dimensions[c] == 2)
                    { var values = output.UV2[c]; values.Clear(); foreach (var value in output.UV[c]) values.Add(new Vector2(value.x, value.y)); mesh.SetUVs(c, values); }
                    else if (source.Dimensions[c] == 3)
                    { var values = output.UV3[c]; values.Clear(); foreach (var value in output.UV[c]) values.Add(new Vector3(value.x, value.y, value.z)); mesh.SetUVs(c, values); }
                    else mesh.SetUVs(c, output.UV[c]);
                }
                if (!sameTopology)
                { mesh.subMeshCount = source.Submeshes; for (int sub = 0; sub < source.Submeshes; sub++) mesh.SetTriangles(output.CachedTriangles[sub], sub, false); }
                if (patchTangents) mesh.SetTangents(output.Tangents);
                else if (source.UV[0] != null) mesh.RecalculateTangents();
                else if (source.Tangents.Length > 0) mesh.SetTangents(output.Tangents);
                mesh.bounds=BevelOutputBounds(output);
            }
            if(!sameTopology || output.CachedGeometry==null)output.CachedGeometry=GeometricIds(output.Welds.CopyGeometry());node.geometryVertices=output.CachedGeometry;
            node.cutPolygons = output.CachedMetadata; node.cutTriangles = output.CachedIndices; node.normalGroups = output.CachedGroups;
            var result = new BevelResult { VertexUpdate=suffixPublished ? output.Update : null, Welds = output.Welds, VertexUpdateStart = output.StaticPositions.Length, PublishedPositions = output.Positions, AppliedWidth = draft.Width, Edges = edgeCount, AddedFaces = draft.Polygons.Count + draft.Plan.StaticFaces.Count - (bevelInputFaceCount > 0 ? bevelInputFaceCount : Faces.Count) };
            foreach (var edge in draft.Selection) result.Selection.Add(NormalEdgePositionKeyForBevel(draft.Positions[edge.Item1], draft.Positions[edge.Item2]));
            foreach (var polygon in draft.Polygons)
            {
                if (polygon.OriginalFace >= 0) continue;
                for (int i=0;i<polygon.Vertices.Count;i++)
                {
                    int a=polygon.Vertices[i],b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                    if (draft.Selection.Contains(BevelKey(draft.Vertices[a].Geo,draft.Vertices[b].Geo)))
                        result.RenderSelection.Add(BevelKey(render[a],render[b]));
                }
            }
            return result;
        }
    }
}
